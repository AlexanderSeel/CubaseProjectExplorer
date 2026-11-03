namespace CubaseInspector.Core;

/// <summary>One searchable row projected from a saved project or asset index.</summary>
public sealed record LibrarySearchDocument(string Category, string Title, string Detail,
    string ProjectName, string ProjectPath, string Evidence, string SearchTerms,
    ProjectSnapshot? Project = null, string? Target = null);

public sealed record LibrarySearchResponse(int MatchCount, bool Limited,
    IReadOnlyList<LibrarySearchDocument> Results);

/// <summary>Bounded, deterministic search over saved library projections; never opens source projects.</summary>
public static class LibrarySearchEngine
{
    public const int MaximumResults = 2000;

    public static LibrarySearchResponse Search(IEnumerable<LibrarySearchDocument> documents,
        string query, string scope = "All", int limit = MaximumResults)
    {
        if (limit is < 1 or > MaximumResults) throw new ArgumentOutOfRangeException(nameof(limit));
        var tokens = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return new LibrarySearchResponse(0, false, []);

        var matches = documents.Where(document => scope == "All" ||
                string.Equals(scope, document.Category, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(scope, "MIDI", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(document.Category, "MIDI candidate", StringComparison.OrdinalIgnoreCase)))
            .Select(document => (Document: document, Score: Score(document, tokens)))
            .Where(match => match.Score >= 0)
            .OrderBy(match => match.Score)
            .ThenBy(match => match.Document.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.Document.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new LibrarySearchResponse(matches.Length, matches.Length > limit,
            matches.Take(limit).Select(match => match.Document).ToArray());
    }

    private static int Score(LibrarySearchDocument document, IReadOnlyList<string> tokens)
    {
        var title = document.Title ?? "";
        var fields = string.Join('\n', title, document.Detail, document.ProjectName,
            document.ProjectPath, document.Evidence, document.SearchTerms);
        if (tokens.Any(token => !fields.Contains(token, StringComparison.OrdinalIgnoreCase))) return -1;
        var full = string.Join(' ', tokens);
        if (title.Equals(full, StringComparison.OrdinalIgnoreCase)) return 0;
        if (title.StartsWith(full, StringComparison.OrdinalIgnoreCase)) return 1;
        if (title.Contains(full, StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }
}
