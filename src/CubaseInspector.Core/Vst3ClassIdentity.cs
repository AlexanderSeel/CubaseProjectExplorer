namespace CubaseInspector.Core;

/// <summary>Canonical VST3 component CID syntax, never an alias for a DAW's instance identifier.</summary>
public static class Vst3ClassIdentity
{
    public static string? Canonical(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var value = id.Trim();
        if (value.Length == 36 && Guid.TryParseExact(value, "D", out var guid))
            value = guid.ToString("N");
        else if (value.Length == 38 && Guid.TryParseExact(value, "B", out guid))
            value = guid.ToString("N");
        if (value.Length != 32 || !value.All(Uri.IsHexDigit)) return null;
        return value.ToUpperInvariant();
    }
}
