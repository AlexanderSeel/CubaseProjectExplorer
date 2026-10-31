using System.Buffers.Binary;
using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class WaveformAnalyzerTests
{
    [Fact]
    public void AnalyzesPcmWaveIntoBoundedPeakEnvelope()
    {
        using var stream = new MemoryStream(CreateWave(1, 8_000, 16,
            [short.MinValue, 0, short.MaxValue, 0, -16_384, 0, 8_192, 0]));

        var result = WaveformAnalyzer.Analyze(stream, bins: 16);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1, result.Value!.Channels);
        Assert.Equal(8_000, result.Value.SampleRate);
        Assert.Equal(8, result.Value.FrameCount);
        Assert.Equal(16, result.Value.PeakLevels.Count);
        Assert.Equal(1f, result.Value.PeakLevels.Max());
        Assert.Equal(16, result.Value.Sparkline.Length);
    }

    [Fact]
    public void RejectsUnsupportedEncodingAndMalformedRiffBounds()
    {
        using var unsupported = new MemoryStream(CreateWave(1, 44_100, 16, [1, 2], encoding: 6));
        using var malformed = new MemoryStream(CreateWave(1, 44_100, 16, [1, 2], riffSize: uint.MaxValue));

        Assert.Equal("WAVEFORM_FORMAT_UNSUPPORTED", WaveformAnalyzer.Analyze(unsupported).Error?.Code);
        Assert.Equal("WAVEFORM_WAV_INVALID", WaveformAnalyzer.Analyze(malformed).Error?.Code);
    }

    [Fact]
    public void AnalyzesIeeeFloatWav()
    {
        var bytes = CreateFloatWave([0.25f, -0.75f, 0f, 0.5f]);
        using var stream = new MemoryStream(bytes);

        var result = WaveformAnalyzer.Analyze(stream, bins: 16);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(48_000, result.Value!.SampleRate);
        Assert.Equal(4, result.Value.FrameCount);
        Assert.Equal(0.75f, result.Value.PeakLevels.Max());
    }

    [Fact]
    public void RejectsInvalidBinCount()
    {
        using var stream = new MemoryStream(CreateWave(1, 44_100, 16, [1, 2]));
        Assert.Equal("WAVEFORM_BIN_LIMIT", WaveformAnalyzer.Analyze(stream, 4).Error?.Code);
    }

    private static byte[] CreateWave(ushort channels, uint sampleRate, ushort bitDepth, short[] samples,
        ushort encoding = 1, uint? riffSize = null)
    {
        var bytesPerSample = bitDepth / 8;
        var dataBytes = samples.Length * bytesPerSample;
        var bytes = new byte[44 + dataBytes];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), riffSize ?? (uint)(bytes.Length - 8));
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), encoding);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), channels);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), sampleRate);
        var blockAlign = (ushort)(channels * bytesPerSample);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), sampleRate * blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), bitDepth);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), (uint)dataBytes);
        for (var i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44 + i * 2), samples[i]);
        return bytes;
    }

    private static byte[] CreateFloatWave(float[] samples)
    {
        var dataBytes = samples.Length * sizeof(float);
        var bytes = new byte[44 + dataBytes];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 48_000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 48_000 * sizeof(float));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), sizeof(float));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), 32);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), (uint)dataBytes);
        for (var i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(44 + i * sizeof(float)), BitConverter.SingleToInt32Bits(samples[i]));
        return bytes;
    }
}
