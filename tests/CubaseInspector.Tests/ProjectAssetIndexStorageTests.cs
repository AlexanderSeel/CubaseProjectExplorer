using System.Text.Json;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectAssetIndexStorageTests
{
    private static ProjectAssetIndex Index(int count) => new("C:/Sessions/α.cpr", "Session α", EvidenceLevel.Candidate,
        [], [], Enumerable.Range(0, count).Select(i => new IndexedMidiNoteCandidate(
            i % 7 == 0 ? "C:/Other/source.cpr" : "C:/Sessions/α.cpr", "Session α", i + 1, 9_000_000_000L + i,
            -12.250000000000002 + i, i == 0 ? -0d : 131.92000000000007, 60, 0,
            EvidenceLevel.Candidate, new string('w', 400), "RIFF MMidiPart/counted-events", "Keys", "Verse",
            5_000_000_000, null, i % 2 == 0 ? 2 : null, i % 3 == 0 ? ushort.MaxValue : null)).ToArray());

    [Fact]
    public void PackedIndexRetainsAllNoteValuesProvenanceAndNullableFields()
    {
        var index = Index(100);
        var json = JsonSerializer.Serialize(new LibraryProjectAssetIndexDocument { Index = index });
        var restored = JsonSerializer.Deserialize<LibraryProjectAssetIndexDocument>(json)!.Index!;
        Assert.Equal(index.ProjectPath, restored.ProjectPath);
        Assert.Equal(index.NativeMidiNoteCandidates, restored.NativeMidiNoteCandidates);
        Assert.Equal(BitConverter.DoubleToInt64Bits(index.NativeMidiNoteCandidates![0].DurationTicks),
            BitConverter.DoubleToInt64Bits(restored.NativeMidiNoteCandidates![0].DurationTicks));
        Assert.Contains("\"v\":2", json);
    }

    [Fact]
    public void LegacyIndexesRemainReadableAndNewPayloadDeduplicatesRepeatedContext()
    {
        var index = Index(10_000);
        var legacy = JsonSerializer.Serialize(new { Index = index });
        var restored = JsonSerializer.Deserialize<LibraryProjectAssetIndexDocument>(legacy)!.Index!;
        Assert.Equal(index.NativeMidiNoteCandidates, restored.NativeMidiNoteCandidates);
        var packed = JsonSerializer.Serialize(new LibraryProjectAssetIndexDocument { Index = index });
        Assert.True(packed.Length < legacy.Length / 5, $"Legacy {legacy.Length}; packed {packed.Length}.");
        var roundTrip = JsonSerializer.Deserialize<LibraryProjectAssetIndexDocument>(packed)!.Index!;
        Assert.Equal(index.NativeMidiNoteCandidates, roundTrip.NativeMidiNoteCandidates);
    }

    [Fact]
    public void UnsupportedStorageVersionsAndInvalidContextIdsAreRejected()
    {
        var json = JsonSerializer.Serialize(new LibraryProjectAssetIndexDocument { Index = Index(1) });
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LibraryProjectAssetIndexDocument>(json.Replace("\"v\":2", "\"v\":99")));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LibraryProjectAssetIndexDocument>(json.Replace("\"notes\":[[0,", "\"notes\":[[-1,")));
    }

    [Theory]
    [InlineData("\"v\":2", "\"v\":2.5")]
    [InlineData("\"contexts\":[[", "\"contexts\":[[true,")]
    [InlineData("\"notes\":[[0,1,", "\"notes\":[[0,\"bad\",")]
    public void CorruptStoredValuesProduceJsonErrors(string oldValue, string newValue)
    {
        var json = JsonSerializer.Serialize(new LibraryProjectAssetIndexDocument { Index = Index(1) });
        Assert.Contains(oldValue, json);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LibraryProjectAssetIndexDocument>(json.Replace(oldValue, newValue)));
    }

    [Fact]
    public async Task DatabaseQueriesAndStreamingReadsUseTheSamePackedCodec()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-packed-index-");
        try
        {
            using var store = new LibraryDocumentStore(Path.Combine(directory.FullName, "library.db"));
            var project = new ProjectSnapshot("C:/Sessions/packed.cpr", "Packed", "CPR", "15", null,
                EvidenceLevel.Candidate, [], [], [], [], NativeMidiNoteCandidates:
                [new(42, -12.25, 131.92000000000007, 60, 0, "wire evidence", Channel: 2)]);
            await store.SaveProjectInspectionAsync(project);
            var expected = ProjectAssetIndexer.Build(project);
            var queried = Assert.Single(await store.GetProjectAssetIndexesAsync());
            Assert.Equal(expected.NativeMidiNoteCandidates, queried.Index!.NativeMidiNoteCandidates);
            var seen = new List<ProjectAssetIndex>();
            await store.VisitProjectAssetIndexesAsync(item => seen.Add(item.Index!));
            Assert.Equal(expected.NativeMidiNoteCandidates, Assert.Single(seen).NativeMidiNoteCandidates);
        }
        finally { directory.Delete(true); }
    }
}
