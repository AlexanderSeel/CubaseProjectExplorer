using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Reads narrowly typed metadata patterns described by public CPR research.
/// It records candidates only; bounded byte proximity is not Cubase object ownership.
/// </summary>
internal static class CprMetadataCandidateScanner
{
    private const int FieldWindowBytes = 512;
    private const int MaximumCandidates = 128;
    private static readonly byte[] SampleRate = "SampleRate"u8.ToArray();
    private static readonly byte[] SampleSize = "SampleSize"u8.ToArray();
    private static readonly byte[] TempoEvent = "MTempoEvent"u8.ToArray();
    private static readonly byte[] TempoTrackEvent = "MTempoTrackEvent"u8.ToArray();
    private static readonly byte[] MusicalTempo = "MusicalTempo"u8.ToArray();
    private static readonly byte[] SignatureEvent = "TimeSignatureEvent"u8.ToArray();
    private static readonly byte[] Float = [.. "Float"u8, 0, 0, 4];
    private static readonly byte[] Long = [.. "Long"u8, 0, 0, 1];
    private static readonly byte[] Bpm = [.. "BPM"u8, 0, 0, 4];
    private static readonly byte[] Numerator = [.. "Numerator"u8, 0, 0, 1];
    private static readonly byte[] Denominator = [.. "Denominator"u8, 0, 0, 1];
    private static readonly HashSet<int> KnownSampleRates = [44100, 48000, 88200, 96000, 176400, 192000];
    private static readonly HashSet<long> KnownSampleSizes = [16, 24, 32, 64];

    public static IReadOnlyList<CprMetadataCandidate> Scan(byte[] data, long archiveOffset,
        List<string>? readErrors = null)
    {
        var results = new List<CprMetadataCandidate>();
        ScanFloatingField(data, archiveOffset, SampleRate, Float, "Sample rate", IsKnownSampleRate, results, readErrors);
        ScanIntegerField(data, archiveOffset, SampleSize, Long, "Sample size", IsKnownSampleSize, results, readErrors);
        ScanTempo(data, archiveOffset, results, readErrors);
        ScanTempoTrackHeaders(data, archiveOffset, results, readErrors);
        ScanMusicalTempo(data, archiveOffset, results, readErrors);
        ScanSignatures(data, archiveOffset, results, readErrors);
        return results.OrderBy(candidate => candidate.ArchiveOffset).Take(MaximumCandidates).ToArray();
    }

    private static void ScanFloatingField(byte[] data, long archiveOffset, byte[] field, byte[] typedValue,
        string label, Func<double, bool> accept, List<CprMetadataCandidate> results, List<string>? readErrors)
    {
        var sawBinaryMarker = false;
        var decoded = 0;
        foreach (var position in FindAll(data, field))
        {
            var end = Math.Min(data.Length, position + FieldWindowBytes);
            var region = data.AsSpan(position, end - position);
            // Ignore the XML preset form; this pattern is only for binary named fields.
            if (region[..Math.Min(region.Length, field.Length + 8)].IndexOfAny((byte)'<', (byte)'>') >= 0) continue;
            sawBinaryMarker = true;
            var relative = region.IndexOf(typedValue);
            if (relative < 0 || relative + typedValue.Length > region.Length - 8) continue;
            var valueOffset = relative + typedValue.Length;
            var value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(region.Slice(valueOffset, 8)));
            if (!double.IsFinite(value) || !accept(value)) continue;
            Add(results, label, value.ToString("0.##", CultureInfo.InvariantCulture),
                archiveOffset + position, Encoding.ASCII.GetString(field) + "/Float BE-double signature");
            decoded++;
        }
        if (sawBinaryMarker && decoded == 0) AddReadError(readErrors, label, archiveOffset);
    }

    private static void ScanIntegerField(byte[] data, long archiveOffset, byte[] field, byte[] typedValue,
        string label, Func<long, bool> accept, List<CprMetadataCandidate> results, List<string>? readErrors)
    {
        var sawBinaryMarker = false;
        var decoded = 0;
        foreach (var position in FindAll(data, field))
        {
            var end = Math.Min(data.Length, position + FieldWindowBytes);
            var region = data.AsSpan(position, end - position);
            if (region[..Math.Min(region.Length, field.Length + 8)].IndexOfAny((byte)'<', (byte)'>') >= 0) continue;
            sawBinaryMarker = true;
            var relative = region.IndexOf(typedValue);
            if (relative < 0 || relative + typedValue.Length > region.Length - 8) continue;
            var value = BinaryPrimitives.ReadInt64BigEndian(region.Slice(relative + typedValue.Length, 8));
            if (!accept(value)) continue;
            Add(results, label, value.ToString(CultureInfo.InvariantCulture), archiveOffset + position,
                Encoding.ASCII.GetString(field) + "/Long BE-int64 signature");
            decoded++;
        }
        if (sawBinaryMarker && decoded == 0) AddReadError(readErrors, label, archiveOffset);
    }

    private static void ScanTempo(byte[] data, long archiveOffset, List<CprMetadataCandidate> results,
        List<string>? readErrors)
    {
        var found = false;
        foreach (var position in FindAll(data, TempoEvent))
        {
            var region = data.AsSpan(position, Math.Min(data.Length - position, FieldWindowBytes));
            var relative = region.IndexOf(Bpm);
            if (relative < 0 || relative + Bpm.Length > region.Length - 8) continue;
            var value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(region.Slice(relative + Bpm.Length, 8)));
            if (!double.IsFinite(value) || value is < 1 or > 1000) continue;
            Add(results, "Tempo event BPM", value.ToString("0.##", CultureInfo.InvariantCulture),
                archiveOffset + position, "MTempoEvent/BPM BE-double signature");
            found = true;
        }
        if (!found && data.AsSpan().IndexOf(TempoEvent) >= 0) AddReadError(readErrors, "Tempo event BPM", archiveOffset);
    }

    private static void ScanTempoTrackHeaders(byte[] data, long archiveOffset,
        List<CprMetadataCandidate> results, List<string>? readErrors)
    {
        // CubaseTools documents a first-event header scan here. Preserve its values as
        // candidates: nearby doubles may be event positions or unrelated header fields.
        var markers = 0;
        var decoded = 0;
        foreach (var position in FindAll(data, TempoTrackEvent))
        {
            markers++;
            var start = Math.Min(data.Length, position + 17);
            var end = Math.Min(data.Length, position + FieldWindowBytes);
            for (var valueOffset = start; valueOffset <= end - 8; valueOffset++)
            {
                var value = BitConverter.Int64BitsToDouble(
                    BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(valueOffset, 8)));
                if (!double.IsFinite(value) || value is < 20 or > 400) continue;
                Add(results, "Tempo track BPM", value.ToString("0.##", CultureInfo.InvariantCulture),
                    archiveOffset + position, $"MTempoTrackEvent first in-range BE-double at +{valueOffset - position}");
                decoded++;
                break;
            }
        }
        if (markers > 0 && decoded == 0) AddReadError(readErrors, "Tempo track BPM", archiveOffset);
    }

    private static void ScanMusicalTempo(byte[] data, long archiveOffset,
        List<CprMetadataCandidate> results, List<string>? readErrors)
    {
        ScanFloatingField(data, archiveOffset, MusicalTempo, Float,
            "Musical tempo BPM", value => value is > 1 and < 1000, results, readErrors);
    }

    private static void ScanSignatures(byte[] data, long archiveOffset, List<CprMetadataCandidate> results,
        List<string>? readErrors)
    {
        var markers = 0;
        var decoded = 0;
        foreach (var position in FindAll(data, SignatureEvent))
        {
            markers++;
            var region = data.AsSpan(position, Math.Min(data.Length - position, FieldWindowBytes));
            if (!TryReadInt64(region, Numerator, out var numerator) ||
                !TryReadInt64(region, Denominator, out var denominator) ||
                numerator is < 1 or > 32 || denominator is not (1 or 2 or 4 or 8 or 16 or 32)) continue;
            Add(results, "Time signature event", $"{numerator}/{denominator}", archiveOffset + position,
                "TimeSignatureEvent/Numerator+Denominator BE-int64 signatures");
            decoded++;
        }
        if (markers > 0 && decoded == 0) AddReadError(readErrors, "Time signature", archiveOffset);
    }

    private static bool TryReadInt64(ReadOnlySpan<byte> region, byte[] field, out long value)
    {
        value = 0;
        var relative = region.IndexOf(field);
        if (relative < 0 || relative + field.Length > region.Length - 8) return false;
        value = BinaryPrimitives.ReadInt64BigEndian(region.Slice(relative + field.Length, 8));
        return true;
    }

    private static IEnumerable<int> FindAll(byte[] data, byte[] marker)
    {
        var position = 0;
        while (position <= data.Length - marker.Length)
        {
            var found = data.AsSpan(position).IndexOf(marker);
            if (found < 0) yield break;
            position += found;
            yield return position;
            position += marker.Length;
        }
    }

    private static void Add(List<CprMetadataCandidate> output, string field, string value,
        long offset, string pattern)
    {
        if (output.Count >= MaximumCandidates) return;
        output.Add(new CprMetadataCandidate(field, value, offset, pattern,
            "Candidate only; field proximity and semantics are not independently verified"));
    }

    private static void AddReadError(List<string>? output, string field, long archiveOffset)
    {
        if (output is not null && output.Count < 16)
            output.Add($"{field} marker was present, but its typed value could not be decoded within the bounded ARCH scan beginning at file offset {archiveOffset}.");
    }

    private static bool IsKnownSampleRate(double value) =>
        value >= int.MinValue && value <= int.MaxValue && KnownSampleRates.Contains((int)Math.Round(value));
    private static bool IsKnownSampleSize(long value) => KnownSampleSizes.Contains(value);
}
