using System.Buffers.Binary;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Reports competing interpretations of the RIFF/NUND header size field.
/// These calculations are diagnostics only; they do not establish a CPR grammar.
/// </summary>
public static class CprEnvelopeProbe
{
    public static CprEnvelopeSizeDiagnostics Inspect(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("A readable, seekable stream is required.", nameof(source));

        var originalPosition = source.Position;
        try
        {
            source.Position = 0;
            Span<byte> header = stackalloc byte[12];
            source.ReadExactly(header);
            var bigEndian = BinaryPrimitives.ReadUInt32BigEndian(header[4..8]);
            var littleEndian = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
            var bigEndianLength = (ulong)bigEndian + 12UL;
            var littleEndianLength = (ulong)littleEndian + 12UL;
            var actualLength = source.Length;
            var signature = header[..4].SequenceEqual("RIFF"u8) &&
                            header[8..12].SequenceEqual("NUND"u8);

            return new CprEnvelopeSizeDiagnostics(signature, bigEndian, littleEndian, actualLength,
                bigEndianLength, bigEndianLength == (ulong)actualLength,
                littleEndianLength, littleEndianLength == (ulong)actualLength);
        }
        finally
        {
            source.Position = originalPosition;
        }
    }
}
