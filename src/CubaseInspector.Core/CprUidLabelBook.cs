using System.Text.Json;

namespace CubaseInspector.Core;

/// <summary>
/// User-maintained display names for observed CPR UIDs. A manual name is NOT
/// source data and never proves VST format, class identity, or installation.
/// This store only operates on the app's own user-local configuration.
/// </summary>
public static class CprUidLabelBook
{
    private const int MaxEntries = 4096;
    private const int MaxJsonLength = 600_000;

    public static IReadOnlyDictionary<string, string> ParseOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        if (json.Length > MaxJsonLength)
            throw new JsonException("CPR UID label file exceeds maximum supported size.");
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !HasExactlyProperties(root, "schemaVersion", "labels") ||
            !root.TryGetProperty("schemaVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schemaVersion) || schemaVersion != 1 ||
            !root.TryGetProperty("labels", out var labels) ||
            labels.ValueKind != JsonValueKind.Array)
            throw new JsonException("Unrecognized CPR UID label file structure.");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in labels.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !HasExactlyProperties(item, "uid", "label") ||
                !item.TryGetProperty("uid", out var uidField) ||
                !item.TryGetProperty("label", out var labelField) ||
                uidField.ValueKind != JsonValueKind.String ||
                labelField.ValueKind != JsonValueKind.String)
                throw new JsonException("Invalid CPR UID label entry.");
            var uid = Vst3ClassIdentity.Canonical(uidField.GetString());
            var text = labelField.GetString();
            if (uid is null || !ValidLabel(text) || result.ContainsKey(uid))
                throw new JsonException("Invalid or duplicate CPR UID label.");
            if (result.Count >= MaxEntries)
                throw new JsonException("CPR UID label count exceeds the supported limit.");
            result[uid] = text!.Trim();
        }
        return result;
    }

    public static IReadOnlyDictionary<string, string> Assign(
        IReadOnlyDictionary<string, string> existing, string uid, string label)
    {
        var canonical = Vst3ClassIdentity.Canonical(uid)
            ?? throw new ArgumentException("A stored 32-digit CPR UID is required.", nameof(uid));
        if (!ValidLabel(label))
            throw new ArgumentException("Display label must be 1–120 printable characters.", nameof(label));
        var copy = new Dictionary<string, string>(existing, StringComparer.Ordinal);
        if (copy.Count >= MaxEntries && !copy.ContainsKey(canonical))
            throw new InvalidOperationException("Maximum number of user labels reached.");
        copy[canonical] = label.Trim();
        return copy;
    }

    public static IReadOnlyDictionary<string, string> Remove(
        IReadOnlyDictionary<string, string> existing, string uid)
    {
        var canonical = Vst3ClassIdentity.Canonical(uid)
            ?? throw new ArgumentException("A stored 32-digit CPR UID is required.", nameof(uid));
        var copy = new Dictionary<string, string>(existing, StringComparer.Ordinal);
        copy.Remove(canonical);
        return copy;
    }

    public static string? Find(IReadOnlyDictionary<string, string> labels, PluginReference reference)
    {
        if (reference.Evidence != EvidenceLevel.Candidate ||
            !reference.Format.StartsWith("CPR", StringComparison.OrdinalIgnoreCase) ||
            Vst3ClassIdentity.Canonical(reference.Id) is not { } uid)
            return null;
        return labels.TryGetValue(uid, out var name) ? name : null;
    }

    public static string ToJson(IReadOnlyDictionary<string, string> labels)
    {
        if (labels.Count > MaxEntries) throw new ArgumentException("Too many user labels.");
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            labels = labels.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new { uid = p.Key, label = p.Value }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool ValidLabel(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 120 &&
        !name.Any(char.IsControl);

    private static bool HasExactlyProperties(JsonElement element, params string[] expected)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!expected.Contains(property.Name, StringComparer.Ordinal) || !names.Add(property.Name))
                return false;
        return names.Count == expected.Length;
    }
}
