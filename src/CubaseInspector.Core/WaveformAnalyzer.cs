using System.Buffers.Binary;

namespace CubaseInspector.Core;

public sealed record AudioWaveform(int Channels, int SampleRate, long FrameCount,
    IReadOnlyList<float> PeakLevels, string Sparkline)
{
    public double DurationSeconds => SampleRate > 0 ? FrameCount / (double)SampleRate : 0;
}

/// <summary>Builds a small peak envelope from bounded PCM/IEEE-float WAV data; it does not decode compressed audio.</summary>
public static class WaveformAnalyzer
{
    public const long MaximumInputBytes = 150L * 1024 * 1024;
    public const int MaximumFramesAnalyzed = 2_000_000;
    private const int DefaultBins = 128;
    private static readonly char[] Levels = ['▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];

    public static Result<AudioWaveform> Analyze(Stream stream, int bins = DefaultBins)
    {
        if (!stream.CanRead || !stream.CanSeek)
            return Result<AudioWaveform>.Fail("WAVEFORM_STREAM_UNSUPPORTED", "Waveform analysis needs a readable, seekable audio stream.");
        if (stream.Length is < 44 or > MaximumInputBytes)
            return Result<AudioWaveform>.Fail("WAVEFORM_SIZE_LIMIT", "WAV files must be 44 bytes to 150 MiB for waveform analysis.");
        if (bins is < 16 or > 512)
            return Result<AudioWaveform>.Fail("WAVEFORM_BIN_LIMIT", "Waveform bins must be between 16 and 512.");

        try
        {
            stream.Position = 0;
            Span<byte> header = stackalloc byte[12];
            if (!ReadExactly(stream, header) || !header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8))
                return Result<AudioWaveform>.Fail("WAVEFORM_FORMAT_UNSUPPORTED", "Only RIFF/WAVE files are supported for waveform analysis.");
            var declaredRiffSize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
            var riffEnd = 8L + declaredRiffSize;
            if (riffEnd > stream.Length || riffEnd < 12)
                return Result<AudioWaveform>.Fail("WAVEFORM_WAV_INVALID", "The RIFF length is outside the source file.");

            ushort encoding = 0, channels = 0, blockAlign = 0, bitDepth = 0;
            uint sampleRate = 0, byteRate = 0;
            long dataOffset = -1, dataLength = 0;
            var formatSeen = false;
            var chunkHeaderBuffer = new byte[8];
            var formatBuffer = new byte[16];
            while (stream.Position + 8 <= riffEnd)
            {
                var chunkHeader = chunkHeaderBuffer.AsSpan();
                if (!ReadExactly(stream, chunkHeader)) break;
                var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
                var chunkStart = stream.Position;
                var chunkEnd = checked(chunkStart + chunkLength);
                if (chunkEnd > riffEnd || chunkEnd > stream.Length)
                    return Result<AudioWaveform>.Fail("WAVEFORM_WAV_INVALID", "A WAV chunk exceeds the declared RIFF bounds.");
                var nextChunk = checked(chunkEnd + (chunkLength & 1));
                if (nextChunk > riffEnd || nextChunk > stream.Length)
                    return Result<AudioWaveform>.Fail("WAVEFORM_WAV_INVALID", "A WAV chunk is missing its required padding byte.");

                if (chunkHeader[..4].SequenceEqual("fmt "u8))
                {
                    if (formatSeen)
                        return Result<AudioWaveform>.Fail("WAVEFORM_WAV_INVALID", "Multiple WAV format chunks are ambiguous.");
                    if (chunkLength < 16)
                        return Result<AudioWaveform>.Fail("WAVEFORM_WAV_INVALID", "The WAV format chunk is truncated.");
                    var format = formatBuffer.AsSpan();
                    if (!ReadExactly(stream, format))
                        return Result<AudioWaveform>.Fail("WAVEFORM_WAV_INVALID", "The WAV format chunk could not be read.");
                    encoding = BinaryPrimitives.ReadUInt16LittleEndian(format[..2]);
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..4]);
                    sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(format[4..8]);
                    byteRate = BinaryPrimitives.ReadUInt32LittleEndian(format[8..12]);
                    blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(format[12..14]);
                    bitDepth = BinaryPrimitives.ReadUInt16LittleEndian(format[14..16]);
                    formatSeen = true;
                }
                else if (chunkHeader[..4].SequenceEqual("data"u8))
                {
                    if (dataOffset >= 0)
                        return Result<AudioWaveform>.Fail("WAVEFORM_WAV_INVALID", "Multiple WAV data chunks are ambiguous.");
                    dataOffset = chunkStart;
                    dataLength = chunkLength;
                }
                stream.Position = checked(chunkEnd + (chunkLength & 1));
            }

            if (dataOffset < 0 || channels is < 1 or > 8 || sampleRate is < 8_000 or > 384_000 ||
                encoding is not (1 or 3) || (encoding == 1 && bitDepth is not (8 or 16 or 24 or 32)) ||
                (encoding == 3 && bitDepth != 32))
                return Result<AudioWaveform>.Fail("WAVEFORM_FORMAT_UNSUPPORTED", "The WAV encoding, channel count, or sample rate is not supported for a waveform.");
            var bytesPerSample = bitDepth / 8;
            var expectedAlign = channels * bytesPerSample;
            if (blockAlign != expectedAlign || blockAlign == 0 || byteRate != sampleRate * blockAlign ||
                dataLength == 0 || dataLength % blockAlign != 0)
                return Result<AudioWaveform>.Fail("WAVEFORM_WAV_INVALID", "The WAV data does not align to complete sample frames.");

            var frameCount = dataLength / blockAlign;
            var stride = Math.Max(1L, (frameCount + MaximumFramesAnalyzed - 1) / MaximumFramesAnalyzed);
            var peaks = new float[bins];
            var bytesPerFrame = blockAlign;
            var framesPerBuffer = Math.Max(1, 64 * 1024 / bytesPerFrame);
            var buffer = new byte[framesPerBuffer * bytesPerFrame];
            long processed = 0;
            long frameIndex = 0;
            stream.Position = dataOffset;
            while (processed < dataLength)
            {
                var desired = (int)Math.Min(buffer.Length, dataLength - processed);
                desired -= desired % bytesPerFrame;
                var read = ReadUpTo(stream, buffer.AsSpan(0, desired));
                if (read == 0 || read % bytesPerFrame != 0)
                    return Result<AudioWaveform>.Fail("WAVEFORM_WAV_INVALID", "The WAV data chunk ended before its declared length.");
                var frames = read / bytesPerFrame;
                for (var local = 0; local < frames; local++, frameIndex++)
                {
                    if (frameIndex % stride != 0) continue;
                    var frame = buffer.AsSpan(local * bytesPerFrame, bytesPerFrame);
                    float peak = 0;
                    for (var channel = 0; channel < channels; channel++)
                    {
                        var sampleBytes = frame.Slice(channel * bytesPerSample, bytesPerSample);
                        var value = SampleMagnitude(sampleBytes, encoding, bitDepth);
                        if (float.IsFinite(value)) peak = Math.Max(peak, Math.Abs(value));
                    }
                    var bin = (int)Math.Min(bins - 1L, frameIndex * bins / frameCount);
                    peaks[bin] = Math.Max(peaks[bin], Math.Clamp(peak, 0f, 1f));
                }
                processed += read;
            }
            return Result<AudioWaveform>.Ok(new AudioWaveform(channels, (int)sampleRate, frameCount,
                peaks, ToSparkline(peaks)));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OverflowException)
        { return Result<AudioWaveform>.Fail("WAVEFORM_WAV_INVALID", ex.Message); }
    }

    private static float SampleMagnitude(ReadOnlySpan<byte> value, ushort encoding, ushort bitDepth)
    {
        if (encoding == 3) return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(value));
        return bitDepth switch
        {
            8 => (value[0] - 128) / 128f,
            16 => BinaryPrimitives.ReadInt16LittleEndian(value) / 32768f,
            24 => ReadInt24(value) / 8388608f,
            32 => BinaryPrimitives.ReadInt32LittleEndian(value) / 2147483648f,
            _ => 0
        };
    }

    private static int ReadInt24(ReadOnlySpan<byte> value)
    {
        var result = value[0] | value[1] << 8 | value[2] << 16;
        return (result & 0x800000) != 0 ? result | unchecked((int)0xFF000000) : result;
    }

    private static string ToSparkline(IReadOnlyList<float> peaks) => new(peaks.Select(peak =>
        Levels[(int)Math.Clamp(Math.Round(peak * (Levels.Length - 1)), 0, Levels.Length - 1)]).ToArray());

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var next = stream.Read(buffer[read..]);
            if (next == 0) return false;
            read += next;
        }
        return true;
    }

    private static int ReadUpTo(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}
