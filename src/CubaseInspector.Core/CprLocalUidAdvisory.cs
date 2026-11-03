namespace CubaseInspector.Core;

/// <summary>
/// Advisory comparison of a candidate native CPR UID with locally declared VST3
/// class IDs. A matching 128-bit string is NOT proof that the native CPR token
/// means a VST3 plug-in, that a plug-in is installed/loadable or that an insert
/// exists. This service does not upgrade the source evidence.
/// </summary>
public static class CprLocalUidAdvisory
{
    public static CprLocalUidEvidence Analyze(PluginReference reference,
        IEnumerable<InstalledPlugin> installed, bool inventoryScanned)
    {
        if (reference.Evidence != EvidenceLevel.Candidate ||
            !reference.Format.StartsWith("CPR", StringComparison.OrdinalIgnoreCase) ||
            Vst3ClassIdentity.Canonical(reference.Id) is not { } uid)
            return new CprLocalUidEvidence(CprLocalUidEvidenceKind.NotApplicable,
                "No supported candidate native CPR UID on this record.", null, null);

        if (!inventoryScanned)
            return new CprLocalUidEvidence(CprLocalUidEvidenceKind.NotScanned,
                "Discover local plugins to compare this candidate CPR UID with local VST3 declarations. " +
                "Any equality would be advisory only.", null, null);

        var matches = installed.SelectMany(bundle => (bundle.Classes ?? [])
                .Where(c => c.Cid == uid).Select(c => (Bundle: bundle, Class: c)))
            .ToArray();

        if (matches.Length == 0)
            return new CprLocalUidEvidence(CprLocalUidEvidenceKind.NoDeclaration,
                "No local moduleinfo.json VST3 class with this stored CPR UID was found. " +
                "This does NOT prove the plugin is missing: native CPR format and VST2/VST3 identity are unverified.",
                null, null);
        if (matches.Length != 1)
            return new CprLocalUidEvidence(CprLocalUidEvidenceKind.Ambiguous,
                matches.Length + " local module class declarations share this UID. " +
                "The match is ambiguous; no local plugin is selected or verified.", null, null);

        var match = matches[0];
        return new CprLocalUidEvidence(CprLocalUidEvidenceKind.UniqueUidCoincidence,
            "Advisory UID coincidence: CPR candidate token " + uid +
            " matches a local VST3 moduleinfo.json class declaration (" + match.Class.Name +
            ", " + (match.Class.Manufacturer ?? "manufacturer unknown") + "). " +
            "The binary CPR property's VST format, actual plugin instance and loadability remain UNKNOWN.",
            match.Bundle.Path, match.Class);
    }
}

public enum CprLocalUidEvidenceKind
{
    NotApplicable, NotScanned, NoDeclaration, Ambiguous, UniqueUidCoincidence
}

public sealed record CprLocalUidEvidence(CprLocalUidEvidenceKind Kind,
    string Description, string? BundlePath, InstalledPluginClass? MatchedClass);
