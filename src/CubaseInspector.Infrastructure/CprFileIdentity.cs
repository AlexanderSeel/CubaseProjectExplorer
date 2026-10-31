namespace CubaseInspector.Infrastructure;

/// <summary>Identifies a Cubase/ Nuendo RIFF/NUND backup by bytes, never by extension alone.</summary>
public static class CprFileIdentity
{
    public static bool HasCprHeader(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 16, FileOptions.SequentialScan);
            // Recognizes either header width; this does not prove semantic decoding.
            return Cubase15HeaderProbe.Read(file).IsCubaseOrNuendoProject;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static bool IsCprOrValidBackup(string path) =>
        Path.GetExtension(path).Equals(".cpr", StringComparison.OrdinalIgnoreCase) ||
        (Path.GetExtension(path).Equals(".bak", StringComparison.OrdinalIgnoreCase) &&
            HasCprHeader(path));
}
