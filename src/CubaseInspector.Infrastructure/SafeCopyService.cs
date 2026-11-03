using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>Copies bytes only; deliberately does not attempt proprietary CPR state rewriting.</summary>
public sealed class SafeCopyService : ISafeCopyService
{
    public async Task<Result<string>> CopyUnmodifiedAsync(string source, string destination, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(source)) return Result<string>.Fail("FILE_NOT_FOUND", "Source file not found.");
        if (!source.EndsWith(".cpr", StringComparison.OrdinalIgnoreCase) &&
            !(source.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) &&
              CprFileIdentity.HasCprHeader(source)))
            return Result<string>.Fail("NOT_CPR",
                "Source must be a CPR file or a RIFF/NUND Cubase backup (.bak).");
        if (!destination.EndsWith(".cpr", StringComparison.OrdinalIgnoreCase))
            return Result<string>.Fail("DESTINATION_EXTENSION", "Destination must have .cpr extension.");
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            return Result<string>.Fail("SAME_PATH", "Destination must differ from original.");
        try
        {
            await using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            await using var dst = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            try { await src.CopyToAsync(dst, cancellationToken); }
            catch
            {
                await dst.DisposeAsync();
                File.Delete(destination); // Never leave an incomplete copy.
                throw;
            }
            return Result<string>.Ok(destination);
        }
        catch (OperationCanceledException) { return Result<string>.Fail("CANCELLED", "Copy cancelled."); }
        catch (IOException e) { return Result<string>.Fail("IO_ERROR", e.Message); }
        catch (UnauthorizedAccessException e) { return Result<string>.Fail("ACCESS_DENIED", e.Message); }
    }

    public Task<Result<string>> CopyWithDisabledPluginsAsync(string source, string destination,
        IReadOnlyList<string> pluginIds, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<string>.Fail("NOT_IMPLEMENTED_SAFELY",
            "Disabling plugins in native CPR files is unavailable until the binary serialization has been validated against Cubase."));
}
