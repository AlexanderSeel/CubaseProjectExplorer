using System.Globalization;
using System.Text;

namespace CubaseInspector.Core;

/// <summary>A confirmed project export reference tied to one exact local module class.</summary>
public sealed record ProjectBundleCheck(string ProjectPath, PluginCheck Check);
public sealed record LocalBundleProjectUse(string ProjectPath, string PluginName,
    string ClassId, int ExportedReferences)
{
    public string Description => PluginName + " · " + ExportedReferences +
        " confirmed exported reference(s) · class " + ClassId;
}
public sealed record LocalBundleAttribution(LocalBundleFootprint Bundle,
    IReadOnlyList<LocalBundleProjectUse> Uses)
{
    public int ProjectCount => Uses.Select(x => x.ProjectPath)
        .Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public int ClassCount => Uses.Select(x => x.ClassId)
        .Distinct(StringComparer.Ordinal).Count();
    public string Display => Path.GetFileName(Bundle.Path) +
        (Bundle.Complete ? " · " + (Bundle.LogicalBytes / 1048576d).ToString("N2",
            CultureInfo.InvariantCulture) + " MiB" : " · size unknown") +
        " · " + ProjectCount + " project(s), " + ClassCount + " class(es)";
}

/// <summary>
/// Correlates already-measured shared bundles with exact VST3 class references.
/// Storage is NEVER multiplied by project or reference count.
/// </summary>
public static class LocalBundleAttributionService
{
    public static IReadOnlyList<LocalBundleAttribution> Build(
        LocalPluginFootprintReport report, IEnumerable<ProjectBundleCheck> source)
    {
        var knownBundles = report.Bundles.ToDictionary(b => b.Path,
            StringComparer.OrdinalIgnoreCase);
        var eligible = new List<(string Bundle, string Project, string Name, string Cid)>();
        foreach (var item in source)
        {
            var check = item.Check;
            if (string.IsNullOrWhiteSpace(item.ProjectPath) ||
                check.Reference.Evidence != EvidenceLevel.Confirmed ||
                check.Status != PluginStatus.InstalledByClassId ||
                !check.Reference.Format.Contains("vst3", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(check.MatchedPath) || check.MatchedClass is null)
                continue;
            var referenceCid = Vst3ClassIdentity.Canonical(check.Reference.Id);
            var moduleCid = Vst3ClassIdentity.Canonical(check.MatchedClass.Cid);
            if (referenceCid is null || moduleCid != referenceCid) continue;
            try
            {
                var bundle = Path.GetFullPath(check.MatchedPath);
                if (knownBundles.ContainsKey(bundle))
                    eligible.Add((bundle, item.ProjectPath, check.Reference.Name, referenceCid));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or
                PathTooLongException or IOException) { }
        }
        var byBundle = eligible.GroupBy(x => x.Bundle, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group =>
                (IReadOnlyList<LocalBundleProjectUse>)group
                    .GroupBy(x => (Path: x.Project.ToUpperInvariant(),
                        ClassId: x.Cid, Plugin: x.Name.ToUpperInvariant()))
                    .Select(x => new LocalBundleProjectUse(x.First().Project, x.First().Name,
                        x.First().Cid, x.Count()))
                    .OrderBy(x => x.ProjectPath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.PluginName, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        return report.Bundles.Select(bundle =>
            new LocalBundleAttribution(bundle, byBundle.TryGetValue(bundle.Path, out var uses)
                ? uses : [])).ToArray();
    }

    public static string Csv(IEnumerable<LocalBundleAttribution> rows)
    {
        var sb = new StringBuilder(
            "BundlePath,Complete,LogicalBytes,Projects,Classes,ProjectPath,PluginName,ClassId,ExportedReferences").AppendLine();
        foreach (var item in rows)
        {
            // Logical bytes appear ONLY once per bundle, even with multiple project uses.
            // Otherwise spreadsheet SUM(LogicalBytes) would overstate shared disk usage.
            var uses = item.Uses.Count == 0
                ? new LocalBundleProjectUse?[] { null }
                : item.Uses.Cast<LocalBundleProjectUse?>().ToArray();
            for (int i = 0; i < uses.Length; i++)
            {
                var use = uses[i];
                sb.AppendLine(string.Join(",", [
                    Cell(item.Bundle.Path), item.Bundle.Complete ? "true" : "false",
                    i == 0 && item.Bundle.Complete
                        ? item.Bundle.LogicalBytes.ToString(CultureInfo.InvariantCulture) : "",
                    item.ProjectCount.ToString(CultureInfo.InvariantCulture),
                    item.ClassCount.ToString(CultureInfo.InvariantCulture),
                    Cell(use?.ProjectPath), Cell(use?.PluginName), Cell(use?.ClassId),
                    use is null ? "" : use.ExportedReferences.ToString(CultureInfo.InvariantCulture)
                ]));
            }
        }
        return sb.ToString();
    }

    private static string Cell(string? raw)
    {
        var s = raw ?? "";
        if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
