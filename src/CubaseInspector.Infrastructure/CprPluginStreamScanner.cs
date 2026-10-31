using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Bounded read-only streaming extraction of CPR plugin property tokens, including
/// records located beyond the initial project prefix. The overlap is larger than
/// the per-record parser window so no token is lost at a read boundary.
/// </summary>
public static class CprPluginStreamScanner
{
    private const int ChunkSize = 256 * 1024;
    private const int LookAhead = CprPluginMetadataReader.MaxWindow + 32;
    public const long DefaultMaxBytes = 2L * 1024 * 1024 * 1024;

    public static async Task<CprPluginStreamResult> ScanAsync(Stream stream,
        CancellationToken cancellationToken = default, long maxBytes = DefaultMaxBytes)
    {
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("A seekable, readable file stream is required.", nameof(stream));
        if (maxBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        stream.Position = 0;
        var bytes = new byte[ChunkSize + LookAhead];
        var tokens = new List<PluginReference>();
        long totalRead = 0, processed = 0;
        var carry = 0;
        var eof = false;
        var tokenCapReached = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (!eof && carry < bytes.Length && totalRead < maxBytes)
            {
                var allowed = (int)Math.Min(bytes.Length - carry, maxBytes - totalRead);
                var size = await stream.ReadAsync(bytes.AsMemory(carry, allowed), cancellationToken);
                if (size == 0) eof = true;
                else
                {
                    carry += size;
                    totalRead += size;
                }
            }

            var lastBlock = eof || totalRead >= maxBytes;
            var processBytes = lastBlock ? carry : Math.Max(0, carry - LookAhead);
            if (processBytes <= 0 && !lastBlock) continue;

            var remaining = CprPluginMetadataReader.MaxInstances - tokens.Count;
            if (remaining > 0)
                tokens.AddRange(CprPluginMetadataReader.Extract(bytes.AsSpan(0, carry),
                    processed, processBytes, remaining));

            processed += processBytes;
            if (tokens.Count >= CprPluginMetadataReader.MaxInstances)
            {
                tokenCapReached = true;
                break;
            }
            if (lastBlock) break;

            var rest = carry - processBytes;
            Buffer.BlockCopy(bytes, processBytes, bytes, 0, rest);
            carry = rest;
        }

        return new CprPluginStreamResult(tokens, totalRead,
            tokenCapReached || totalRead < stream.Length, tokenCapReached,
            maxBytes);
    }
}

public sealed record CprPluginStreamResult(
    IReadOnlyList<PluginReference> Plugins, long BytesRead, bool Partial,
    bool TokenCapReached, long ScanBudget);
