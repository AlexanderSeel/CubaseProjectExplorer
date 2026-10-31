using System.Security.Cryptography;

namespace CubaseInspector.Infrastructure;

/// <summary>Computes a raw-file checksum for exact duplicate detection; it is not semantic project identity.</summary>
public static class ProjectContentChecksum
{
    public static async Task<string> ComputeAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(digest);
    }
}
