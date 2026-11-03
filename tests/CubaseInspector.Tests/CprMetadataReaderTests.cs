using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprMetadataReaderTests
{
    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Chunk(string name, byte[] payload) =>
        [.. Encoding.ASCII.GetBytes(name), .. U32((uint)payload.Length), .. payload];

    private static byte[] ProjectWithArchive(byte[] payload)
    {
        var stream = Encoding.ASCII.GetBytes("Main");
        var type = Encoding.ASCII.GetBytes("PProject");
        var root = Chunk("ROOT", [.. U32((uint)stream.Length), .. stream, .. U32((uint)type.Length), .. type]);
        var archive = Chunk("ARCH", payload);
        var chunks = root.Concat(archive).ToArray();
        return [.. Encoding.ASCII.GetBytes("RIFF"), .. U32((uint)chunks.Length), .. Encoding.ASCII.GetBytes("NUND"), .. chunks];
    }

    [Fact]
    public async Task TypedCubaseToolsMetadataPatternsFlowThroughReaderAsCandidates()
    {
        var payload = new List<byte>();
        Append(payload, "SampleRate"); Append(payload, "Float\0\0\x04"); AppendDouble(payload, 48000);
        Append(payload, "SampleSize"); Append(payload, "Long\0\0\x01"); AppendInt64(payload, 24);
        Append(payload, "MTempoEvent"); Append(payload, "BPM\0\0\x04"); AppendDouble(payload, 97.5);
        Append(payload, "TimeSignatureEvent"); Append(payload, "Numerator\0\0\x01"); AppendInt64(payload, 7);
        Append(payload, "Denominator\0\0\x01"); AppendInt64(payload, 8);
        var path = await WriteProjectAsync(payload.ToArray());
        try
        {
            var result = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var project = result.Value!;
            Assert.Equal(EvidenceLevel.Candidate, project.Evidence);
            Assert.Null(project.Bpm);
            Assert.Null(project.TimeSignatureNumerator);
            Assert.Null(project.TimeSignatureDenominator);
            Assert.Equal(4, project.NativeMetadataCandidates!.Count);
            Assert.Contains(project.Warnings, warning => warning.Contains("Sample rate candidate: 48000", StringComparison.Ordinal));
            var coverage = ProjectFieldCoverage.Evaluate(project);
            Assert.Equal(ProjectFieldState.Candidate, coverage["Tempo"]);
            Assert.Equal(ProjectFieldState.Candidate, coverage["Time signature"]);
            Assert.Equal(ProjectFieldState.Candidate, coverage["Sample rate"]);
            Assert.Equal(ProjectFieldState.Candidate, coverage["Sample size"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ConflictingEventsRemainAvailableWithoutInventingASingleTempoOrSignature()
    {
        var payload = new List<byte>();
        AppendTempo(payload, 90);
        AppendTempo(payload, 120);
        AppendSignature(payload, 3, 4);
        AppendSignature(payload, 4, 4);
        var path = await WriteProjectAsync(payload.ToArray());
        try
        {
            var result = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Null(result.Value!.Bpm);
            Assert.Null(result.Value.TimeSignatureNumerator);
            Assert.Equal(4, result.Value.NativeMetadataCandidates!.Count);
            Assert.Equal(ProjectFieldState.Candidate, ProjectFieldCoverage.Evaluate(result.Value)["Tempo"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReadsAdditionalCubaseToolsTempoPatternsWithoutPromotingThemToProjectTempo()
    {
        var payload = new List<byte>();
        Append(payload, "MTempoTrackEvent");
        payload.AddRange(new byte[17]);
        AppendDouble(payload, 123.25);
        Append(payload, "MusicalTempo"); Append(payload, "Float\0\0\x04"); AppendDouble(payload, 111.5);
        var path = await WriteProjectAsync(payload.ToArray());
        try
        {
            var result = await new CprEvidenceReader().ReadAsync(path);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Null(result.Value!.Bpm);
            Assert.Contains(result.Value.NativeMetadataCandidates!, candidate =>
                candidate.Field == "Tempo track BPM" && candidate.Value == "123.25");
            Assert.Contains(result.Value.NativeMetadataCandidates!, candidate =>
                candidate.Field == "Musical tempo BPM" && candidate.Value == "111.5");
            Assert.Equal(ProjectFieldState.Candidate, ProjectFieldCoverage.Evaluate(result.Value)["Tempo"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task IgnoresXmlSampleRateAndInvalidValues()
    {
        var payload = new List<byte>();
        Append(payload, "SampleRate\">48000</Descriptor>");
        Append(payload, "SampleRate"); Append(payload, "Float\0\0\x04"); AppendDouble(payload, 12345);
        AppendTempo(payload, 0);
        var path = await WriteProjectAsync(payload.ToArray());
        try
        {
            var result = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Empty(result.Value!.NativeMetadataCandidates ?? []);
            Assert.Null(result.Value.Bpm);
            Assert.Contains(result.Value.Warnings, warning => warning.Contains(
                "Sample rate marker was present", StringComparison.Ordinal));
            Assert.Contains(result.Value.Warnings, warning => warning.Contains(
                "Tempo event BPM marker was present", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    private static async Task<string> WriteProjectAsync(byte[] payload)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cpr");
        await File.WriteAllBytesAsync(path, ProjectWithArchive(payload));
        return path;
    }

    private static void AppendTempo(List<byte> bytes, double value)
    { Append(bytes, "MTempoEvent"); Append(bytes, "BPM\0\0\x04"); AppendDouble(bytes, value); }
    private static void AppendSignature(List<byte> bytes, long numerator, long denominator)
    {
        Append(bytes, "TimeSignatureEvent"); Append(bytes, "Numerator\0\0\x01"); AppendInt64(bytes, numerator);
        Append(bytes, "Denominator\0\0\x01"); AppendInt64(bytes, denominator);
    }
    private static void Append(List<byte> bytes, string value) => bytes.AddRange(Encoding.ASCII.GetBytes(value));
    private static void AppendDouble(List<byte> bytes, double value)
    {
        Span<byte> raw = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(raw, BitConverter.DoubleToInt64Bits(value));
        bytes.AddRange(raw.ToArray());
    }
    private static void AppendInt64(List<byte> bytes, long value)
    {
        Span<byte> raw = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(raw, value);
        bytes.AddRange(raw.ToArray());
    }
}
