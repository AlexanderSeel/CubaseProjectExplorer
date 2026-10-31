using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;

namespace CubaseInspector.Infrastructure;

/// <summary>Computes a raw-file checksum for exact duplicate detection; it is not semantic project identity.</summary>
public static class ProjectContentChecksum
{
    public static async Task<string> ComputeAsync(string path, CancellationToken cancellationToken = default,
        Action<long, long>? progress = null)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var totalBytes = stream.Length;
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var processedBytes = 0L;
        var lastProgress = Stopwatch.GetTimestamp();
        try
        {
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0) break;
                hash.AppendData(buffer, 0, count);
                processedBytes += count;
                var now = Stopwatch.GetTimestamp();
                if (progress is not null && (processedBytes == totalBytes ||
                    Stopwatch.GetElapsedTime(lastProgress, now) >= TimeSpan.FromMilliseconds(100)))
                {
                    progress(processedBytes, totalBytes);
                    lastProgress = now;
                }
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
