namespace CubaseInspector.Core;

/// <summary>Classification uses only explicitly reported error-code prefixes.</summary>
public static class ScanIssueBrowser
{
    public const string All = "All";
    public const string Unclassified = "Unclassified";

    public static string Category(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return Unclassified;
        var colon = message.IndexOf(':');
        if (colon is < 2 or > 40) return Unclassified;
        var key = message[..colon].Trim();
        return key.Length is >= 2 and <= 40 &&
               key.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-')
            ? key : Unclassified;
    }

    public static IReadOnlyList<string> Categories(IEnumerable<string> messages) =>
        new[] { All }.Concat(messages.Select(Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)).ToArray();

    public static bool Matches(string path, string message, string? category, string? search)
    {
        if (!string.IsNullOrWhiteSpace(category) && category != All &&
            !Category(message).Equals(category, StringComparison.OrdinalIgnoreCase))
            return false;
        var query = (search ?? "").Trim();
        return query.Length == 0 ||
               path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               message.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
