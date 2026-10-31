using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprArchiveInventoryIntegrationTests
{
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Number(ulong value, bool wide)
    {
        if (!wide) return U32(checked((uint)value));
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Chunk(string id, byte[] body, bool wide) =>
        Join(Encoding.ASCII.GetBytes(id), Number((ulong)body.Length, wide), body);

    private static byte[] Root(string stream, string cls, bool wide)
    {
        var streamBytes = Encoding.ASCII.GetBytes(stream);
        var classBytes = Encoding.ASCII.GetBytes(cls);
        return Chunk("ROOT", Join(U32((uint)streamBytes.Length), streamBytes,
            U32((uint)classBytes.Length), classBytes), wide);
    }

    private static byte[] Record(string cls, byte[] body, bool wide, ushort version = 7)
    {
        var name = Encoding.ASCII.GetBytes(cls + "\0");
        var versionBytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(versionBytes, version);
        return Join(Number(wide ? ulong.MaxValue : uint.MaxValue, wide),
            U32((uint)name.Length), name, versionBytes, Number((ulong)body.Length, wide), body);
    }

    private static byte[] Project(bool wide, params byte[][] chunks)
    {
        var body = Join(chunks);
        return Join(Encoding.ASCII.GetBytes(wide ? "RIF2" : "RIFF"),
            Number((ulong)body.Length, wide), Encoding.ASCII.GetBytes("NUND"), body);
    }

    private static async Task<string> WriteTemporary(byte[] data)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cpr");
        await File.WriteAllBytesAsync(path, data);
        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InventoriesEveryArchiveWithAbsoluteOffsetsAndCandidateContainment(bool wide)
    {
        var child = Record("UnknownChild", [0x11, 0x22, 0x33], wide);
        var outer = Record("UnknownParent", child, wide);
        var reference = Join(Number(wide ? 0x8000000000000000UL : 0x80000000UL, wide),
            Number(0, wide));
        var project = Project(wide,
            Root("Other|Stream", "PUnknownArchive", wide), Chunk("ARCH", Join(outer, reference), wide),
            Root("Arrangement1", "PArrangement", wide), Chunk("ARCH", [], wide),
            Chunk("ARCH", Record("OrphanClass", [], wide), wide));
        var path = await WriteTemporary(project);
        try
        {
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var report = result.Value!;
            Assert.Equal(wide ? "RIF2" : "RIFF", report.ContainerMagic);
            Assert.Equal(3, report.ArchiveInventories!.Count);
            var first = report.ArchiveInventories[0];
            Assert.Equal("Other|Stream", first.StreamName);
            Assert.Equal("PUnknownArchive", first.ArchiveClass);
            Assert.Equal(3, first.RecordsFound);
            Assert.Equal(2, first.ClassCounts["UnknownParent"]);
            Assert.False(first.Partial);
            var parent = first.Records[0];
            var nested = first.Records[1];
            var reused = first.Records[2];
            Assert.Equal(first.PayloadFileOffset, parent.HeaderFileOffset);
            Assert.Equal(parent.BodyFileOffset, nested.HeaderFileOffset);
            Assert.Equal(parent.HeaderFileOffset, nested.ParentHeaderFileOffset);
            Assert.Equal(0, parent.Depth);
            Assert.Equal(1, nested.Depth);
            Assert.Equal((ushort)7, parent.ClassVersion);
            Assert.Equal((ushort)7, reused.ClassVersion);
            Assert.Equal(0UL, reused.ClassSymbolArchiveOffset);
            Assert.Equal(parent.BodyFileOffset + parent.BodyBytes, reused.HeaderFileOffset);
            Assert.Equal("interned-class-reference", reused.ClassEncoding);
            Assert.Null(reused.ParentHeaderFileOffset);
            Assert.Equal(3, first.UninterpretedBytes);
            Assert.Empty(report.ArchiveInventories[1].Records);
            Assert.False(report.ArchiveInventories[1].Partial);
            Assert.Equal("Unnamed", report.ArchiveInventories[2].StreamName);
            Assert.Null(report.ArchiveInventories[2].ArchiveClass);
            Assert.Equal("OrphanClass", Assert.Single(report.ArchiveInventories[2].Records).ClassName);
            AssertInventoryBounds(report);

            var markdown = CprStructureInspector.ToMarkdown(report);
            Assert.Contains("ARCH-relative symbol offset", markdown);
            Assert.Contains("not verified objects, plugin identities or ownership", markdown);
            Assert.Contains("Other\\|Stream", markdown);
            Assert.Contains("Class version", markdown);
            Assert.Contains("interned-class-reference", markdown);
            Assert.Contains($"- Container: {(wide ? "RIF2" : "RIFF")}", markdown);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownAndMusicalClassNamesDoNotCreateConfirmedMusicalFields(bool wide)
    {
        var data = Join(Record("MUnrecognizedFutureObject", [1, 2, 3], wide),
            Record("MTempoEvent", new byte[24], wide), Record("UnknownPluginInstance", [], wide));
        var path = await WriteTemporary(Project(wide,
            Root("Arrangement1", "PArrangement", wide), Chunk("ARCH", data, wide)));
        try
        {
            var structure = await new CprStructureInspector().InspectAsync(path);
            Assert.True(structure.IsSuccess, structure.Error?.Message);
            var inventory = Assert.Single(structure.Value!.ArchiveInventories!);
            Assert.Equal(3, inventory.RecordsFound);
            Assert.All(inventory.Records, record => Assert.Equal(EvidenceLevel.Candidate, record.Evidence));

            var result = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var snapshot = result.Value!;
            Assert.Equal(EvidenceLevel.Candidate, snapshot.Evidence);
            Assert.Empty(snapshot.Tracks);
            Assert.Empty(snapshot.Plugins);
            Assert.Empty(snapshot.MidiNotes ?? []);
            Assert.Null(snapshot.Bpm);
            Assert.Contains(snapshot.Warnings, warning => warning.Contains(
                (wide ? "RIF2" : "RIFF") + "/NUND chunk structure parsed", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BinaryDerivedTempoFieldsRetainRawBitsWithoutBecomingProjectBpm(bool wide)
    {
        var body = new byte[24];
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(0, 4), BitConverter.SingleToInt32Bits(125.5f));
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(4, 8), BitConverter.DoubleToInt64Bits(240d));
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(12, 8), BitConverter.DoubleToInt64Bits(0.5d));
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(20, 4), 3);
        var path = await WriteTemporary(Project(wide,
            Root("Arrangement1", "PArrangement", wide), Chunk("ARCH", Record("MTempoEvent", body, wide, 0), wide)));
        try
        {
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var record = Assert.Single(Assert.Single(result.Value!.ArchiveInventories!).Records);
            Assert.Equal((ushort)0, record.ClassVersion);
            var fields = record.RawFields!;
            Assert.Equal(new[] { "125.5", "240", "0.5", "3" }, fields.Select(field => field.Value));
            Assert.Equal(new long[] { 0, 4, 12, 20 }, fields.Select(field => field.FileOffset - record.BodyFileOffset));
            Assert.Equal(Convert.ToHexString(body), string.Concat(fields.Select(field => field.RawHex)));
            Assert.All(fields, field =>
            {
                Assert.Equal(EvidenceLevel.Candidate, field.Evidence);
                Assert.Contains("semantics unverified", field.Source);
            });
            var snapshot = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
            Assert.Null(snapshot.Value!.Bpm);
            Assert.Empty(snapshot.Value.Tracks);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DetailRetentionBudgetAppliesAcrossArchivesWhileClassTotalsRemainAvailable()
    {
        const int perArchive = 6000;
        var records = Join(Enumerable.Range(0, perArchive).Select(_ => Record("OpaqueClass", [], true)).ToArray());
        var path = await WriteTemporary(Project(true,
            Root("One", "Unknown", true), Chunk("ARCH", records, true),
            Root("Two", "Unknown", true), Chunk("ARCH", records, true)));
        try
        {
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var inventories = result.Value!.ArchiveInventories!;
            Assert.Equal(2, inventories.Count);
            Assert.Equal(12_000, inventories.Sum(item => item.RecordsFound));
            Assert.Equal(10_000, inventories.Sum(item => item.Records.Count));
            Assert.False(inventories[0].Partial);
            Assert.True(inventories[1].Partial);
            Assert.Contains(inventories[1].LimitReasons, reason => reason.Contains("retention", StringComparison.Ordinal));
            Assert.Equal(perArchive, inventories[1].ClassCounts["OpaqueClass"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ArchiveLimitPreservesContainerEntriesAndExplainsOmittedDetailedEvidence()
    {
        var chunks = Enumerable.Range(0, 129).SelectMany(index => new[]
        {
            Root("Archive" + index, "Unknown", true), Chunk("ARCH", Record("UnknownClass", [], true), true)
        }).ToArray();
        var path = await WriteTemporary(Project(true, chunks));
        try
        {
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(258, result.Value!.Chunks.Count);
            Assert.Equal(128, result.Value.ArchiveInventories!.Count);
            Assert.Contains(result.Value.Warnings, warning => warning.Contains("omitted 1 archive", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DeepArrangementReportsBothInventoryAndArrangementLimitReasons()
    {
        byte[] nested = Record("UnknownDeepClass", [], true);
        for (var index = 0; index < 52; index++) nested = Record("UnknownDeepClass", nested, true);
        var path = await WriteTemporary(Project(true,
            Root("Arrangement1", "PArrangement", true), Chunk("ARCH", nested, true)));
        try
        {
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var report = result.Value!;
            var inventory = Assert.Single(report.ArchiveInventories!);
            Assert.True(inventory.Partial);
            Assert.Contains(inventory.LimitReasons, reason => reason.Contains("depth", StringComparison.Ordinal));
            Assert.True(report.Arrangement!.Partial);
            Assert.Contains(report.Arrangement.LimitReasons!, reason => reason.Contains("nesting", StringComparison.Ordinal));
            AssertInventoryBounds(report);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OptionalPrivateFixturesRetainCandidateRecordBoundsAndUnchangedSourceHashes()
    {
        var directory = Environment.GetEnvironmentVariable("CPE_CPR_FIXTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var fixtures = new[]
        {
            ("CPETestEmpty.cpr", "cf0d6f5f1828d7da6857506b5973c5dcd7c6c1712ec60683862077be22a1bcdf"),
            ("CPETestMidiC1_UpTo16Steps.cpr", "77fa9d77ea911ed161eea13c7b099e3943f6753b42c81b5c41f31be79a1b1804")
        };
        foreach (var (filename, hash) in fixtures)
        {
            var path = Path.Combine(directory, filename);
            Assert.True(File.Exists(path), $"Missing private fixture: {filename}");
            var sourceBytes = await File.ReadAllBytesAsync(path);
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant());
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, $"{filename}: {result.Error?.Message}");
            var report = result.Value!;
            Assert.Equal("RIF2", report.ContainerMagic);
            Assert.Equal(report.Chunks.Count(chunk => chunk.Id == "ARCH"), report.ArchiveInventories!.Count);
            Assert.True(report.ArchiveInventories.Sum(inventory => inventory.RecordsFound) > 0);
            AssertInventoryBounds(report);
            var tempo = Assert.Single(report.ArchiveInventories.SelectMany(inventory => inventory.Records),
                record => record.ClassName == "MTempoTrackEvent");
            Assert.Equal((ushort)3, tempo.ClassVersion);
            Assert.Equal(54, tempo.BodyBytes);
            var rawFields = tempo.RawFields!;
            Assert.Equal(7, rawFields.Count);
            Assert.Equal(new long[] { 0, 4, 8, 16, 24, 26, 30 },
                rawFields.Select(field => field.FileOffset - tempo.BodyFileOffset));
            Assert.Equal(new[] { 4, 4, 8, 8, 2, 4, 1 }, rawFields.Select(field => field.ByteCount));
            Assert.Equal(new[] { "1", "0.5", "0", "0", "0", "120", "0" },
                rawFields.Select(field => field.Value));
            Assert.All(rawFields, field =>
            {
                Assert.Equal(EvidenceLevel.Candidate, field.Evidence);
                Assert.Equal(Convert.ToHexString(sourceBytes.AsSpan(checked((int)field.FileOffset), field.ByteCount)), field.RawHex);
                Assert.Contains("semantics unverified", field.Source);
            });
            Assert.Equal(23, tempo.BodyBytes - rawFields.Sum(field => field.ByteCount));
            var snapshot = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
            Assert.Null(snapshot.Value!.Bpm);
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant());
        }
    }

    private static void AssertInventoryBounds(CprStructureReport report)
    {
        foreach (var inventory in report.ArchiveInventories!)
        {
            var archiveEnd = inventory.PayloadFileOffset + inventory.BytesScanned;
            var byOffset = inventory.Records.ToDictionary(record => record.HeaderFileOffset);
            foreach (var record in inventory.Records)
            {
                Assert.Equal(EvidenceLevel.Candidate, record.Evidence);
                Assert.InRange(record.HeaderFileOffset, inventory.PayloadFileOffset, archiveEnd);
                Assert.InRange(record.BodyFileOffset, record.HeaderFileOffset, archiveEnd);
                Assert.InRange(record.BodyBytes, 0, archiveEnd - record.BodyFileOffset);
                if (record.ParentHeaderFileOffset is not long parentOffset || !byOffset.TryGetValue(parentOffset, out var parent)) continue;
                Assert.Equal(parent.Depth + 1, record.Depth);
                Assert.InRange(record.HeaderFileOffset, parent.BodyFileOffset, parent.BodyFileOffset + parent.BodyBytes);
                Assert.True(record.BodyFileOffset + record.BodyBytes <= parent.BodyFileOffset + parent.BodyBytes);
            }
            long previousEnd = inventory.PayloadFileOffset;
            foreach (var range in inventory.UninterpretedRanges)
            {
                Assert.InRange(range.FileOffset, previousEnd, archiveEnd);
                Assert.InRange(range.ByteCount, 1, archiveEnd - range.FileOffset);
                previousEnd = range.FileOffset + range.ByteCount;
            }
            if (!inventory.Partial)
                Assert.Equal(inventory.BytesScanned, inventory.UninterpretedBytes +
                    inventory.Records.Sum(record => record.BodyFileOffset - record.HeaderFileOffset));
        }
    }
}
