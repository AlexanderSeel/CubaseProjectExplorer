using System.Text.Json;

namespace CubaseInspector.Core;

/// <summary>A user-reviewed catalog description, never a verified VST3 module identity.</summary>
public sealed record PluginCatalogDecision(string GroupIdentity, string? CatalogName,
    string? CatalogManufacturer, string? CatalogSource, bool Rejected);

public sealed class PluginCatalogAssociations
{
    private const int Limit = 2000;
    private const int MaxBytes = 512_000;
    private readonly Dictionary<string, PluginCatalogDecision> _decisions = new(StringComparer.OrdinalIgnoreCase);

    public bool IsWritable { get; private set; } = true;
    public string? LoadWarning { get; private set; }
    public int Count => _decisions.Count;
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CubaseProjectExplorer", "plugin-catalog-associations.json");

    public PluginCatalogDecision? Find(string identity) =>
        _decisions.TryGetValue(identity, out var value) ? value : null;

    private void CheckEdit(string identity)
    {
        if (!IsWritable)
            throw new InvalidOperationException("Catalog decisions are read-only: " + LoadWarning);
        if (string.IsNullOrWhiteSpace(identity) || identity.Length > 1024)
            throw new ArgumentException("Invalid group identity.", nameof(identity));
        if (_decisions.Count >= Limit && !_decisions.ContainsKey(identity))
            throw new InvalidOperationException("Catalog association limit reached.");
    }

    public void Confirm(string identity, PluginMetadataEntry catalog)
    {
        CheckEdit(identity);
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(catalog.Name) || catalog.Name.Length > 256 ||
            string.IsNullOrWhiteSpace(catalog.Manufacturer) || catalog.Manufacturer.Length > 256 ||
            string.IsNullOrWhiteSpace(catalog.Source) || catalog.Source.Length > 128)
            throw new ArgumentException("Invalid catalog record.", nameof(catalog));
        _decisions[identity] = new(identity, catalog.Name, catalog.Manufacturer, catalog.Source, false);
    }

    public void Reject(string identity)
    {
        CheckEdit(identity);
        _decisions[identity] = new(identity, null, null, null, true);
    }

    public void Clear(string identity)
    {
        CheckEdit(identity);
        _decisions.Remove(identity);
    }

    public PluginMetadataEntry? Resolve(string identity, PluginMetadataCatalog? catalog)
    {
        var decision = Find(identity);
        if (decision is null || decision.Rejected || catalog is null) return null;
        // Reconcile against the CURRENT loaded catalog, never trust stale saved metadata.
        return catalog.FindExactCatalogEntry(decision.CatalogName, decision.CatalogManufacturer,
            decision.CatalogSource);
    }

    public static PluginCatalogAssociations Load(string? path = null)
    {
        var result = new PluginCatalogAssociations();
        var file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file)) return result;
            if (new FileInfo(file).Length > MaxBytes)
                throw new InvalidDataException("Association file exceeds size budget.");
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1 ||
                !root.TryGetProperty("decisions", out var entries) ||
                entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > Limit)
                throw new InvalidDataException("Unknown association schema or record count.");
            foreach (var item in entries.EnumerateArray())
            {
                var d = item.Deserialize<PluginCatalogDecision>();
                if (d is null || string.IsNullOrWhiteSpace(d.GroupIdentity) || d.GroupIdentity.Length > 1024 ||
                    (!d.Rejected && (string.IsNullOrWhiteSpace(d.CatalogName) ||
                        string.IsNullOrWhiteSpace(d.CatalogManufacturer) || string.IsNullOrWhiteSpace(d.CatalogSource) ||
                        d.CatalogName.Length > 256 || d.CatalogManufacturer.Length > 256 ||
                        d.CatalogSource.Length > 128)) ||
                    !result._decisions.TryAdd(d.GroupIdentity, d))
                    throw new InvalidDataException("Invalid or duplicated association decision.");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or ArgumentException or System.Security.SecurityException)
        {
            result._decisions.Clear();
            result.IsWritable = false;
            result.LoadWarning = ex.GetType().Name +
                ": existing catalog association file cannot be safely read; it will not be overwritten.";
        }
        return result;
    }

    public void Save(string? path = null)
    {
        if (!IsWritable)
            throw new InvalidOperationException("Refusing to overwrite unreadable association settings.");
        var file = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new { version = 1,
                decisions = _decisions.Values.OrderBy(x => x.GroupIdentity, StringComparer.OrdinalIgnoreCase).ToArray() }));
            File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
