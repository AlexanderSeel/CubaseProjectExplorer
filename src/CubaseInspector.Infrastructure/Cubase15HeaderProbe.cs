// Cubase15HeaderProbe.cs
// Read-only, minimal project container probe; intended for .NET 10.
// Based on static analysis of the user-supplied Cubase15.exe.
// Not a full project reader. The meaning of the declared-length field
// beyond its width and big-endian encoding must be fixture-validated.
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace CubaseInspector.Infrastructure;

public enum CprContainerVariant
{
    Riff32,
    Rif2_64
}

public sealed record CprContainerHeader(
    CprContainerVariant Variant,
    string Magic,
    string FormType,
    ulong DeclaredSize,
    int HeaderLength,
    long ActualFileLength)
{
    public bool IsCubaseOrNuendoProject => FormType == "NUND";
}

public static class Cubase15HeaderProbe
{
    /// <summary>
    /// Reads the project-container prefix without modifying the stream's final position.
    /// Supports legacy RIFF and the newer RIF2 64-bit wrapper.
    /// Validates the form-type tag (NUND), not the unverified length equation.
    /// </summary>
    public static CprContainerHeader Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek)
            throw new ArgumentException("A readable, seekable stream is required", nameof(input));

        long originalPosition = input.Position;
        try
        {
            input.Position = 0;
            Span<byte> magicBytes = stackalloc byte[4];
            input.ReadExactly(magicBytes);
            string magic = Encoding.ASCII.GetString(magicBytes);

            CprContainerVariant variant;
            int sizeFieldBytes;
            switch (magic)
            {
                case "RIFF":
                    variant = CprContainerVariant.Riff32;
                    sizeFieldBytes = 4;
                    break;
                case "RIF2":
                    variant = CprContainerVariant.Rif2_64;
                    sizeFieldBytes = 8;
                    break;
                default:
                    throw new InvalidDataException($"Unsupported project container magic '{magic}'");
            }

            Span<byte> buffer = stackalloc byte[8];
            input.ReadExactly(buffer[..sizeFieldBytes]);
            ulong declaredSize = sizeFieldBytes == 4
                ? BinaryPrimitives.ReadUInt32BigEndian(buffer[..4])
                : BinaryPrimitives.ReadUInt64BigEndian(buffer);

            Span<byte> formBytes = stackalloc byte[4];
            input.ReadExactly(formBytes);
            string form = Encoding.ASCII.GetString(formBytes);
            if (form != "NUND")
                throw new InvalidDataException($"Not a Cubase/Nuendo NUND project: form '{form}'");

            // Do not assume that declaredSize == input.Length - 8 or -12.
            // Some reverse-engineering tools report differing wrapper semantics.
            return new CprContainerHeader(
                variant, magic, form, declaredSize,
                4 + sizeFieldBytes + 4,
                input.Length);
        }
        finally
        {
            input.Position = originalPosition;
        }
    }

    public static CprContainerHeader ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }
}
