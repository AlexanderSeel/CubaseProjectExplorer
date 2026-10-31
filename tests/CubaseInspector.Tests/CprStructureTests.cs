using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprStructureTests
{
    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
    private static byte[] U16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }
    private static byte[] Concat(params byte[][] bytes) => bytes.SelectMany(x => x).ToArray();
    private static byte[] Chunk(string name, byte[] payload) =>
        Concat(Encoding.ASCII.GetBytes(name), U32((uint)payload.Length), payload);
    private static byte[] Root(string stream, string type)
    {
        var a = Encoding.ASCII.GetBytes(stream);
        var b = Encoding.ASCII.GetBytes(type);
        return Chunk("ROOT", Concat(U32((uint)a.Length), a, U32((uint)b.Length), b));
    }
    private static byte[] FullRecord(string name, byte[] body)
    {
        var cls = Encoding.ASCII.GetBytes(name + "\0");
        return Concat(U32(0xFFFFFFFF), U32((uint)cls.Length), cls, U16(1),
            U32((uint)body.Length), body);
    }
    private static byte[] Project(params byte[][] chunks)
    {
        var body = Concat(chunks);
        return Concat(Encoding.ASCII.GetBytes("RIFF"), U32((uint)body.Length),
            Encoding.ASCII.GetBytes("NUND"), body);
    }
    private static async Task<string> MakeFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cpr");
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    [Fact]
    public async Task Parses_BigEndianChunks_RootArchLinks_OddLength_And_InternedClass()
    {
        var first = FullRecord("MTrackList", Array.Empty<byte>());
        // First class-name length field at ARCH offset 4 -> interned reference id 0.
        var second = Concat(U32(0x80000000), U32(0));
        var arrangement = Concat(first, second); // deliberately odd number of bytes
        var version = FullRecord("PAppVersion", Encoding.ASCII.GetBytes("Cubase Version 13.0.0\0"));
        var file = await MakeFile(Project(Root("Arrangement1", "PArrangement"),
            Chunk("ARCH", arrangement), Root("Version", "PAppVersion"), Chunk("ARCH", version)));
        try
        {
            var parsed = await new CprStructureInspector().InspectAsync(file);
            Assert.True(parsed.IsSuccess, parsed.Error?.Message);
            var report = parsed.Value!;
            Assert.Equal(4, report.Chunks.Count);
            Assert.Equal("Arrangement1", report.Chunks[1].StreamName);
            Assert.Equal("PArrangement", report.Chunks[1].ArchiveClass);
            Assert.Equal(2, report.Arrangement!.ClassCounts["MTrackList"]);
            Assert.Equal(2, report.Arrangement.RecordsFound);
            Assert.False(report.Arrangement.Partial);
            Assert.StartsWith("Cubase Version 13", report.VersionTextCandidate);
            Assert.NotNull(report.SizeDiagnostics);
            Assert.True(report.SizeDiagnostics!.HasRiffNundSignature);
            Assert.True(report.SizeDiagnostics.BigEndianTotalLengthMatches);
            Assert.Contains("candidate parsing strategy", string.Join(" ", report.Warnings));
            Assert.Contains("competing header interpretations", CprStructureInspector.ToMarkdown(report));
            Assert.Contains("NOT decoded", CprStructureInspector.ToMarkdown(report));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task ReadsSeparatedApplicationAndVersionTokensFromVersionArchive()
    {
        var versionPayload = Concat(Encoding.ASCII.GetBytes("Cubase\0"), U32(14),
            Encoding.ASCII.GetBytes("Version 5.1.1\0"));
        var file = await MakeFile(Project(Root("Version", "PAppVersion"),
            Chunk("ARCH", FullRecord("PAppVersion", versionPayload))));
        try
        {
            var result = await new CprStructureInspector().InspectAsync(file);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("Cubase Version 5.1.1", result.Value!.VersionTextCandidate);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task ExtractsNestedTrackNameAsCandidateWithoutAssertingVerifiedTrack()
    {
        var name = Concat(U32(4), Encoding.UTF8.GetBytes("Vox\0"));
        var track = FullRecord("MAudioTrackEvent", FullRecord("MListNode", name));
        var file = await MakeFile(Project(Root("Arrangement1", "PArrangement"), Chunk("ARCH", track)));
        try
        {
            var structural = await new CprStructureInspector().InspectAsync(file);
            Assert.True(structural.IsSuccess, structural.Error?.Message);
            var hint = Assert.Single(structural.Value!.Arrangement!.TrackHints);
            Assert.Equal("MAudioTrackEvent", hint.RecordClass);
            Assert.Equal("Vox", hint.Name);
            Assert.True(hint.ArchiveOffset > 0);
            Assert.True(hint.ArchiveEndOffset > hint.ArchiveOffset);
            Assert.Equal((ushort)1, hint.ClassVersion);
            Assert.Equal((long)FullRecord("MListNode", name).Length, hint.BodyBytes);
            Assert.Equal("inline-class-name", hint.ClassEncoding);
            var snapshot = await new CprEvidenceReader().ReadAsync(file);
            Assert.Equal(EvidenceLevel.Candidate, snapshot.Value!.Evidence);
            var candidateTrack = Assert.Single(snapshot.Value.Tracks);
            Assert.Equal("Vox", candidateTrack.Name);
            Assert.Equal("Audio", candidateTrack.Kind);
            Assert.Equal(EvidenceLevel.Candidate, candidateTrack.Evidence);
            Assert.Equal(hint.ArchiveOffset, candidateTrack.SourceOffset);
            Assert.Equal(hint.ArchiveEndOffset, candidateTrack.SourceEndOffset);
            Assert.Equal(hint.ClassVersion, candidateTrack.SourceClassVersion);
            Assert.Equal(hint.BodyBytes, candidateTrack.SourceBodyBytes);
            Assert.Equal(hint.ClassEncoding, candidateTrack.SourceClassEncoding);
            Assert.Null(candidateTrack.Volume);
            Assert.Null(candidateTrack.ChannelId);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task RecognizesAsmBackedArrangementTrackAndLaneClassVocabulary()
    {
        var classes = CprTrackRecordKinds.SerializedClasses.Order(StringComparer.Ordinal).ToArray();
        var arrangement = Concat(classes.Select(name => FullRecord(name, Array.Empty<byte>())).ToArray());
        var file = await MakeFile(Project(Root("Arrangement1", "PArrangement"), Chunk("ARCH", arrangement)));
        try
        {
            var result = await new CprStructureInspector().InspectAsync(file);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var hints = result.Value!.Arrangement!.TrackHints;
            Assert.Equal(classes.Length, hints.Count);
            Assert.All(hints, hint => Assert.NotNull(CprTrackRecordKinds.KindFor(hint.RecordClass)));
            Assert.Contains(hints, hint => hint.RecordClass == "MTempoTrackEvent" &&
                CprTrackRecordKinds.KindFor(hint.RecordClass) == "Tempo");
            Assert.Contains(hints, hint => hint.RecordClass == "MVideoTrackEvent" &&
                CprTrackRecordKinds.KindFor(hint.RecordClass) == "Video");
            Assert.DoesNotContain(hints, hint => hint.RecordClass == "MTrackEvent");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task ReadsCubaseBakWithSameNativeTrackMarkersAsCpr()
    {
        var track = FullRecord("MAudioTrackEvent", FullRecord("MListNode",
            Concat(U32(4), Encoding.UTF8.GetBytes("Vox\0"))));
        var cpr = await MakeFile(Project(Root("Arrangement1", "PArrangement"), Chunk("ARCH", track)));
        var bak = Path.ChangeExtension(cpr, ".bak");
        try
        {
            File.Copy(cpr, bak);
            var parsed = await new CprEvidenceReader().ReadAsync(bak);
            Assert.True(parsed.IsSuccess, parsed.Error?.Message);
            var audio = Assert.Single(parsed.Value!.Tracks);
            Assert.Equal("Audio", audio.Kind);
            Assert.Equal("MAudioTrackEvent", audio.SourceRecordClass);
            Assert.Contains(parsed.Value.Warnings, warning => warning.Contains(
                "Cubase native track/lane class records found", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(cpr);
            if (File.Exists(bak)) File.Delete(bak);
        }
    }

    [Fact]
    public async Task InvalidUtf8CandidateTrackNameIsNotPromoted()
    {
        var invalidName = Concat(U32(4), [0xC3, 0x28, 0x41, 0x00]);
        var track = FullRecord("MAudioTrackEvent", FullRecord("MListNode", invalidName));
        var path = await MakeFile(Project(Root("Arrangement1", "PArrangement"), Chunk("ARCH", track)));
        try
        {
            var structure = await new CprStructureInspector().InspectAsync(path);
            Assert.True(structure.IsSuccess, structure.Error?.Message);
            var hint = Assert.Single(structure.Value!.Arrangement!.TrackHints);
            Assert.Null(hint.Name);
            var project = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(project.IsSuccess, project.Error?.Message);
            Assert.Equal(EvidenceLevel.Candidate, project.Value!.Evidence);
            Assert.Equal("(unnamed CPR record)", Assert.Single(project.Value.Tracks).Name);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DoesNotInventNamesWhenListNodeIsNotNestedUnderTrack()
    {
        var listNode = FullRecord("MListNode", Concat(U32(4), Encoding.ASCII.GetBytes("Bass\0")));
        var file = await MakeFile(Project(Root("Arrangement1", "PArrangement"), Chunk("ARCH", listNode)));
        try
        {
            var parsed = await new CprStructureInspector().InspectAsync(file);
            Assert.True(parsed.IsSuccess);
            Assert.Empty(parsed.Value!.Arrangement!.TrackHints);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task Rejects_TruncatedChunkAndWrongMagic()
    {
        var good = Project(Chunk("ARCH", [1, 2, 3]));
        var malformed = good.ToArray();
        // Change ARCH size to 200 while preserving actual file length and container header.
        BinaryPrimitives.WriteUInt32BigEndian(malformed.AsSpan(16, 4), 200);
        var file = await MakeFile(malformed);
        try
        {
            var parsed = await new CprStructureInspector().InspectAsync(file);
            Assert.Equal("TRUNCATED_CHUNK", parsed.Error?.Code);
        }
        finally { File.Delete(file); }
        var wav = good.ToArray();
        Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
        file = await MakeFile(wav);
        try
        {
            var parsed = await new CprStructureInspector().InspectAsync(file);
            Assert.Equal("NOT_CPR_CONTAINER", parsed.Error?.Code);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task CorruptContainerAndChunkLengthsRetainPartialSnapshotWithReadErrors()
    {
        var bytes = Project(Chunk("ARCH", Encoding.ASCII.GetBytes("PAppVersion\\0")));
        var path = await MakeFile(bytes);
        try
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), uint.MaxValue);
            await File.WriteAllBytesAsync(path, bytes);
            var result = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Contains(result.Value!.Warnings, warning => warning.Contains("TRUNCATED_CPR", StringComparison.Ordinal));
            bytes = Project(Chunk("ARCH", Encoding.ASCII.GetBytes("PAppVersion\\0")));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), 100_000);
            await File.WriteAllBytesAsync(path, bytes);
            result = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Contains(result.Value!.Warnings, warning => warning.Contains("TRUNCATED_CHUNK", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AdversarialChunkLengthMutationsFailClosedWithoutCrashing()
    {
        var valid = Project(Root("Arrangement1", "PArrangement"),
            Chunk("ARCH", FullRecord("MTrackList", [])));
        var path = await MakeFile(valid);
        try
        {
            var random = new Random(0xC0BA5E);
            // Deterministic mutations of the RIFF length and the ROOT/ARCH framing.
            for (var i = 0; i < 96; i++)
            {
                var bytes = valid.ToArray();
                var offset = i % 3 switch { 0 => 4, 1 => 16, _ => 16 + 8 + 4 };
                for (var b = 0; b < 4; b++) bytes[offset + b] = (byte)random.Next(256);
                await File.WriteAllBytesAsync(path, bytes);
                var structure = await new CprStructureInspector().InspectAsync(path);
                // Keep the partial snapshot available, but retain the structural failure
                // as a warning instead of representing the corrupt input as fully parsed.
                if (!structure.IsSuccess &&
                    structure.Error?.Code is "TRUNCATED_CPR" or "TRUNCATED_CHUNK" or "CHUNK_LIMIT")
                {
                    var snapshot = await new CprEvidenceReader().ReadAsync(path);
                    Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
                    Assert.Contains(snapshot.Value!.Warnings, warning =>
                        warning.Contains(structure.Error.Code, StringComparison.Ordinal));
                }
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ChunkCountAboveLimitIsReportedWhileEvidenceReaderReturnsPartialSnapshot()
    {
        const int maxChunks = 4096;
        var chunks = Enumerable.Range(0, maxChunks + 1)
            .Select(_ => Chunk("DATA", []))
            .ToArray();
        var path = await MakeFile(Project(chunks));
        try
        {
            var structure = await new CprStructureInspector().InspectAsync(path);
            Assert.False(structure.IsSuccess);
            Assert.Equal("CHUNK_LIMIT", structure.Error?.Code);

            var project = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(project.IsSuccess, project.Error?.Message);
            Assert.Contains(project.Value!.Warnings, warning => warning.Contains("CHUNK_LIMIT", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AlreadyCancelledStructureInspectionReturnsExplicitFailure()
    {
        var path = await MakeFile(Project(Chunk("ARCH", [])));
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var result = await new CprStructureInspector().InspectAsync(path, cancellation.Token);
            Assert.False(result.IsSuccess);
            Assert.Equal("CANCELLED", result.Error?.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task TruncatedCrashBackupReturnsPartialSnapshotAndRemainsUnmodified()
    {
        var bytes = Project(Chunk("ARCH", "Plugin UID\\0"u8.ToArray()));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), uint.MaxValue);
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bak");
        await File.WriteAllBytesAsync(file, bytes);
        try
        {
            var before = await File.ReadAllBytesAsync(file);
            var parsed = await new CprEvidenceReader().ReadAsync(file);
            Assert.True(parsed.IsSuccess, parsed.Error?.Message);
            Assert.Contains(parsed.Value!.Warnings, warning => warning.Contains("TRUNCATED_CPR", StringComparison.Ordinal));
            Assert.Equal(before, await File.ReadAllBytesAsync(file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task MalformedRif2FallsBackToCandidateEvidenceWithStructuralWarning()
    {
        var bytes = Project(Chunk("ARCH", []))
            .Concat(Encoding.ASCII.GetBytes("PAppVersion\0Cubase 15.0.40\0")).ToArray();
        Encoding.ASCII.GetBytes("RIF2").CopyTo(bytes, 0);
        var path = await MakeFile(bytes);
        try
        {
            var result = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(EvidenceLevel.Candidate, result.Value!.Evidence);
            Assert.Equal("Cubase 15.0.40", result.Value.CreatorVersion);
            Assert.Contains(result.Value.Warnings,
                warning => warning.Contains("structural read error [NOT_CPR_CONTAINER]", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ExistingInspector_AcceptsContainerWithoutArbitraryPluginMarkers_AsCandidates()
    {
        var file = await MakeFile(Project(Root("Arrangement1", "PArrangement"),
            Chunk("ARCH", FullRecord("MTrackList", []))));
        try
        {
            var parsed = await new CprEvidenceReader().ReadAsync(file);
            Assert.True(parsed.IsSuccess, parsed.Error?.Message);
            Assert.Equal(EvidenceLevel.Candidate, parsed.Value!.Evidence);
            Assert.Empty(parsed.Value.Tracks);
            Assert.Empty(parsed.Value.Plugins);
            Assert.Contains(parsed.Value.Warnings, x => x.Contains("RIFF/NUND"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task OversizedOrMalformedRoot_NeverCreatesInventedMetadata()
    {
        var root = Chunk("ROOT", Concat(U32(0xFFFFFFFF), [0]));
        var file = await MakeFile(Project(root, Chunk("ARCH", [])));
        try
        {
            var parsed = await new CprStructureInspector().InspectAsync(file);
            Assert.True(parsed.IsSuccess, parsed.Error?.Message);
            Assert.Null(parsed.Value!.Chunks[1].StreamName);
            Assert.Null(parsed.Value.Arrangement);
            Assert.Contains(parsed.Value.Warnings, x => x.Contains("ROOT"));
        }
        finally { File.Delete(file); }
    }
}
