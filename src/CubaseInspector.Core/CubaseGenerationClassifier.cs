using System.Text.RegularExpressions;

namespace CubaseInspector.Core;

/// <summary>
/// Bounded descriptive classification of an on-disk PAppVersion string.
/// It is NOT proof of the version which originally created the project, nor a decoding contract.
/// </summary>
public sealed record CubaseGenerationInfo(string Product, string Generation,
    int? SavedByMajorVersion, bool IsBefore11, string Evidence)
{
    public string Display => Product + " · " + Generation + " · " + Evidence;
}

public static partial class CubaseGenerationClassifier
{
    [GeneratedRegex(@"^\s*(Cubase(?:\s+(?:SX|SL|Pro|Artist|Elements|LE|AI|VST))?|Nuendo)\b[\s,]*(?:Version\s*)?(\d{1,2})(?:\.(\d{1,2}))?(?:\.\d{1,3})?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionHint();

    [GeneratedRegex(@"^\s*(Cubase(?:\s+(?:SX|SL|Pro|Artist|Elements|LE|AI|VST))?|Nuendo)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProductHint();

    public static CubaseGenerationInfo Classify(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 256)
            return new("Unknown Cubase family", "Unknown generation", null, false,
                "No reliable on-disk PAppVersion hint");
        var product = ProductHint().Match(raw);
        if (!product.Success)
            return new("Unknown Cubase family", "Unknown generation", null, false,
                "On-disk version text not recognized");
        var name = product.Groups[1].Value.Trim();
        var match = VersionHint().Match(raw);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out var major))
            return new(name, "Unknown generation", null, false,
                "Application name only; saved-by version not decoded");
        var isSx = name.Equals("Cubase SX", StringComparison.OrdinalIgnoreCase);
        var isSl = name.Equals("Cubase SL", StringComparison.OrdinalIgnoreCase);
        var isVst = name.Equals("Cubase VST", StringComparison.OrdinalIgnoreCase);
        var isNuendo = name.Equals("Nuendo", StringComparison.OrdinalIgnoreCase);
        var era = isSx || isSl
            ? "SX / SL era (legacy CPR)"
            : isVst
                ? "Cubase VST era (file type must be verified)"
                : isNuendo
                    ? major < 11 ? "Nuendo pre-11 era" : "Nuendo 11+ era"
                    : major is >= 4 and <= 10
                        ? "Cubase 4–10.x era (legacy CPR)"
                        : major >= 11
                            ? "Cubase 11+ era"
                            : "Early Cubase version (ambiguous; not assumed SX)";
        var isBefore11 = isSx || isSl || isVst || major is >= 4 and <= 10 ||
            (isNuendo && major < 11);
        return new(name, era, major, isBefore11,
            "Candidate saved-by PAppVersion text; not original project creation version");
    }
}
