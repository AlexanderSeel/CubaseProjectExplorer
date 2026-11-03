using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>Connects a CPR plugin token to a parsed named record only when their byte ranges overlap.</summary>
public static class CprPluginLocationCorrelator
{
    public static IReadOnlyList<PluginReference> AttachCandidateLocations(
        IReadOnlyList<PluginReference> plugins, IReadOnlyList<CprNamedRecordCandidate>? records)
    {
        if (records is null || records.Count == 0) return plugins;
        var indexed = records.Select(record => new
        {
            Record = record,
            Start = record.Values.Count == 0 ? record.ArchiveOffset : record.Values.Min(value => value.ArchiveOffset),
            End = record.Values.Count == 0 ? record.ArchiveOffset : record.Values.Max(value => value.ArchiveOffset)
        }).ToArray();

        return plugins.Select(plugin =>
        {
            if (plugin.SourceByteOffset is not { } offset ||
                string.IsNullOrWhiteSpace(plugin.Name) || plugin.Name.StartsWith("Name not decoded", StringComparison.Ordinal))
                return plugin;
            var containingRecords = indexed.Where(item => offset >= item.Start && offset <= item.End).ToArray();
            if (containingRecords.Length != 1) return plugin;
            var record = containingRecords[0];
            var matchingNames = record.Record.Values.Where(value =>
                value.Path.EndsWith("Plugin Name", StringComparison.OrdinalIgnoreCase) &&
                value.Value.Equals(plugin.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matchingNames.Length != 1) return plugin;
            var trackNames = record.Record.Values.Where(value =>
                value.Path.Equals("Name/String", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (trackNames.Length != 1 || string.IsNullOrWhiteSpace(trackNames[0].Value)) return plugin;
            var matchingName = matchingNames[0];
            var trackName = trackNames[0].Value;
            var locationPath = matchingName.Path[..^"/Plugin Name".Length];
            return plugin with { CandidateOwnerName = trackName, CandidateLocationPath = locationPath };
        }).ToArray();
    }
}
