namespace CubaseInspector.Core;

/// <summary>Evidence counts over references in the visible group, not installed bundle counts.</summary>
public sealed record PluginGroupLocalEvidence(int ExactCidReferences,
    int FilenameHintReferences, int UnverifiedReferences)
{
    public int Total => ExactCidReferences + FilenameHintReferences + UnverifiedReferences;

    public string Summary
    {
        get
        {
            if (Total == 0) return "Local evidence: no displayed references";
            var parts = new List<string>();
            if (ExactCidReferences > 0)
                parts.Add(ExactCidReferences + " unique-CID local class reference matches");
            if (FilenameHintReferences > 0)
                parts.Add(FilenameHintReferences + " filename-only hints");
            if (UnverifiedReferences > 0)
                parts.Add(UnverifiedReferences + " unverified references");
            return "Local evidence: " + string.Join("; ", parts) +
                ". Not a plugin loadability or last-used test.";
        }
    }
}

/// <summary>Counts only provenance-eligible reference checks, without loading DLLs.</summary>
public static class PluginGroupLocalEvidenceService
{
    public static PluginGroupLocalEvidence Summarize(IEnumerable<PluginCheck> checks)
    {
        int cid = 0, filename = 0, unknown = 0;
        foreach (var check in checks)
        {
            if (check.Reference.Evidence != EvidenceLevel.Confirmed)
            {
                unknown++;
                continue;
            }
            switch (check.Status)
            {
                case PluginStatus.InstalledByClassId when !string.IsNullOrWhiteSpace(check.MatchedPath):
                    cid++;
                    break;
                case PluginStatus.InstalledByName when !string.IsNullOrWhiteSpace(check.MatchedPath):
                    filename++;
                    break;
                default:
                    unknown++;
                    break;
            }
        }
        return new(cid, filename, unknown);
    }
}
