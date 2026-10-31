using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

/// <summary>
/// Synthetic public fixtures plus opt-in checks of the two user-supplied
/// Cubase 15.0.40 projects, kept outside the public Git repository.
/// </summary>
public sealed class CprRif2FixtureTests
{
    private static byte[] U32(uint value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        return b;
    }

    private static byte[] U64(ulong value)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(b, value);
        return b;
    }

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] Chunk(string id, byte[] payload) =>
        Join(Encoding.ASCII.GetBytes(id), U64((ulong)payload.Length), payload);

    private static byte[] Root(string name, string cls)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var c = Encoding.ASCII.GetBytes(cls);
        return Chunk("ROOT", Join(U32((uint)n.Length), n, U32((uint)c.Length), c));
    }

    private static byte[] NamedClass(string name, byte[] body)
    {
        var n = Encoding.ASCII.GetBytes(name + "\0");
        return Join(U64(ulong.MaxValue), U32((uint)n.Length), n,
            [0, 1], U64((ulong)body.Length), body);
    }

    private static byte[] Note(double position, int pitch, int velocity)
    {
        var body = new byte[112];
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(0, 8),
            BitConverter.DoubleToInt64Bits(position));
        body[9] = checked((byte)pitch);
        body[10] = checked((byte)velocity);
        Encoding.ASCII.GetBytes("adcn").CopyTo(body, 73);
        body[78] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(79, 4), 32);
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(87, 8),
            BitConverter.DoubleToInt64Bits(120));
        return body;
    }

    private static byte[] Project(byte[] arrangement)
    {
        var contents = Join(Root("Arrangement1", "PArrangement"), Chunk("ARCH", arrangement));
        return Join(Encoding.ASCII.GetBytes("RIF2"), U64((ulong)contents.Length),
            Encoding.ASCII.GetBytes("NUND"), contents);
    }

    private static byte[] ProjectWithVersion(byte[] arrangement)
    {
        var version = Encoding.ASCII.GetBytes("Cubase\0Version 15.0.40\0");
        var contents = Join(Root("Application", "PAppVersion"), Chunk("ARCH", version),
            Root("Arrangement1", "PArrangement"), Chunk("ARCH", arrangement));
        return Join(Encoding.ASCII.GetBytes("RIF2"), U64((ulong)contents.Length),
            Encoding.ASCII.GetBytes("NUND"), contents);
    }

    private static async Task<string> TemporaryFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cpr");
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    [Fact]
    public async Task ParsesGeneratedRif2ChunksAndScopedMidiNotesWithoutGuessingOwnership()
    {
        var arrangement = Join(NamedClass("MMidiNote", Note(0, 36, 100)),
            NamedClass("MMidiNote", Note(120, 37, 100)));
        var path = await TemporaryFile(Project(arrangement));
        try
        {
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var structure = result.Value!;
            Assert.Equal(2, structure.Chunks.Count);
            Assert.Equal("PArrangement", structure.Chunks[1].ArchiveClass);
            Assert.Equal(2, structure.Arrangement!.RecordsFound);
            var notes = Assert.IsAssignableFrom<IReadOnlyList<CubaseInspector.Core.CprRif2MidiNoteCandidate>>(
                structure.Arrangement.MidiNoteCandidates);
            Assert.Collection(notes,
                first =>
                {
                    Assert.Equal(36, first.MidiPitch);
                    Assert.Equal(0d, first.PositionTicks);
                    Assert.Equal(120d, first.DurationTicks);
                    Assert.Equal(100, first.Velocity);
                },
                second =>
                {
                    Assert.Equal(37, second.MidiPitch);
                    Assert.Equal(120d, second.PositionTicks);
                    Assert.Equal(120d, second.DurationTicks);
                    Assert.Equal(100, second.Velocity);
                });
            Assert.Contains("candidate", CprStructureInspector.ToMarkdown(structure),
                StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MainProjectReaderUsesRif2StructureAndKeepsNotesAsCandidates()
    {
        var arrangement = Join(NamedClass("MAudioTrackEvent", []),
            NamedClass("MMidiNote", Note(240, 60, 97)));
        var path = await TemporaryFile(ProjectWithVersion(arrangement));
        try
        {
            var result = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var project = result.Value!;
            Assert.Equal(EvidenceLevel.Candidate, project.Evidence);
            Assert.Equal("Cubase Version 15.0.40", project.CreatorVersion);
            Assert.Single(project.Tracks);
            Assert.Equal(EvidenceLevel.Candidate, project.Tracks[0].Evidence);
            Assert.Equal("Audio", project.Tracks[0].Kind);
            Assert.Empty(project.MidiNotes ?? []);
            var note = Assert.Single(project.NativeMidiNoteCandidates!);
            Assert.Equal(240d, note.PositionTicks);
            Assert.Equal(120d, note.DurationTicks);
            Assert.Equal(60, note.MidiPitch);
            Assert.Contains(project.Warnings, warning => warning.Contains("RIF2 MIDI note observations", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RejectsTruncatedRif2ChunkSizeWithoutAllocation()
    {
        var bytes = Project(Array.Empty<byte>());
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(64, 8), ulong.MaxValue);
        var path = await TemporaryFile(bytes);
        try
        {
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.False(result.IsSuccess);
            Assert.Equal("TRUNCATED_CHUNK", result.Error?.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OptionalUserProvidedCubase15ProjectsHaveExactSixteenthNoteSequence()
    {
        var dir = Environment.GetEnvironmentVariable("CPE_CPR_FIXTURE_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return; // Private files are never committed or fetched by CI.
        var fixtures = new[]
        {
            ("CPETestEmpty.cpr", "cf0d6f5f1828d7da6857506b5973c5dcd7c6c1712ec60683862077be22a1bcdf"),
            ("CPETestMidiC1_UpTo16Steps.cpr", "77fa9d77ea911ed161eea13c7b099e3943f6753b42c81b5c41f31be79a1b1804")
        };
        foreach (var (filename, hash) in fixtures)
        {
            var path = Path.Combine(dir, filename);
            Assert.True(File.Exists(path), $"Missing private fixture: {filename}");
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, $"{filename}: {result.Error?.Message}");
            Assert.Equal(14, result.Value!.Chunks.Count);
            Assert.Equal("Cubase Version 15.0.40", result.Value.VersionTextCandidate);
            var notes = result.Value.Arrangement!.MidiNoteCandidates!;
            if (filename == "CPETestEmpty.cpr")
                Assert.Empty(notes);
            else
            {
                Assert.Equal(16, notes.Count);
                for (var i = 0; i < 16; i++)
                {
                    Assert.Equal(i + 36, notes[i].MidiPitch);
                    Assert.Equal(i * 120d, notes[i].PositionTicks);
                    Assert.Equal(120d, notes[i].DurationTicks);
                    Assert.Equal(100, notes[i].Velocity);
                }
            }
        }
    }
}
