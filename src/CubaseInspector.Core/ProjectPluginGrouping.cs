namespace CubaseInspector.Core;

/// <summary>A project location for one observed plug-in reference.</summary>
public sealed record ProjectPluginUse(PluginReference Reference, string Location, string? DeclaredTrackId = null)
{
    public bool CanNavigateToTrack => Reference.Evidence == EvidenceLevel.Confirmed &&
        !string.IsNullOrWhiteSpace(DeclaredTrackId);
}

/// <summary>Same-name references are grouped for browsing; grouping does not assert instance identity.</summary>
public sealed record ProjectPluginGroup(string Identity, string Name, string? Vendor,
    string Format, EvidenceLevel Evidence, IReadOnlyList<ProjectPluginUse> Uses)
{
    public int Observations => Uses.Count;
    public string CountLabel => Observations == 1 ? "1 project reference" : $"{Observations:N0} project references";
    public string LocationSummary => string.Join(" · ", Uses.Select(x => x.Location)
        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4));
}

public static class ProjectPluginGrouping
{
    public static IReadOnlyList<ProjectPluginGroup> Group(ProjectSnapshot project)
    {
        var trackNames = project.Tracks.Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .GroupBy(t => t.Id, StringComparer.Ordinal).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        return project.Plugins.GroupBy(Identity, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var uses = g.Select(plugin =>
                {
                    var location = "Location not found in this project file";
                    string? trackId = null;
                    if (project.Evidence == EvidenceLevel.Confirmed && plugin.Evidence == EvidenceLevel.Confirmed &&
                        plugin.TrackId is { } id && trackNames.TryGetValue(id, out var track))
                    {
                        location = track;
                        trackId = id;
                        if (plugin.Slot.HasValue) location += $" · insert slot {plugin.Slot.Value}";
                    }
                    else if (!string.IsNullOrWhiteSpace(plugin.CandidateOwnerName))
                    {
                        location = $"Nearby CPR record (owner unverified): {plugin.CandidateOwnerName}";
                        if (!string.IsNullOrWhiteSpace(plugin.CandidateLocationPath))
                            location += " · " + plugin.CandidateLocationPath;
                    }
                    else if (plugin.Evidence == EvidenceLevel.Candidate && plugin.SourceByteOffset.HasValue)
                        location = $"Found near byte {plugin.SourceByteOffset.Value:N0} (track unknown)";
                    return new ProjectPluginUse(plugin, location, trackId);
                }).ToArray();

                var levels = uses.Select(use => use.Reference.Evidence).Distinct().ToArray();
                var evidence = levels.Length == 1 ? levels[0]
                    : levels.Contains(EvidenceLevel.Candidate) ? EvidenceLevel.Candidate : EvidenceLevel.Unknown;
                var formats = uses.Select(use => use.Reference.Format).Distinct(StringComparer.OrdinalIgnoreCase);
                var vendor = uses.Select(use => use.Reference.Manufacturer)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                return new ProjectPluginGroup(g.Key, first.Name, vendor,
                    string.Join(", ", formats), evidence, uses);
            })
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Identity, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string Identity(PluginReference plugin)
    {
        // Group same displayed product names even when one source omits the vendor; preserve anonymous UID-only tokens individually.
        var name = Normalize(plugin.Name);
        if (name.Length < 2 || name is "namenotdecoded" or "unresolvedpluginuid")
            return "TOKEN:" + plugin.Id + "|" + plugin.SourceByteOffset;
        return "NAME:" + name;
    }

    private static string Normalize(string? value) => new((value ?? "")
        .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
