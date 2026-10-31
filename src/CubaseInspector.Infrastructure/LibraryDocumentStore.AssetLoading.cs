using System.Text.Json;

namespace CubaseInspector.Infrastructure;

public sealed record LibraryAssetIndexProgress(string Stage, int Processed, int Total,
    string? CurrentProject = null, int Skipped = 0, string? Detail = null);

public sealed partial class LibraryDocumentStore
{
    /// <summary>Reads one saved index at a time on a worker, releasing the SQLite reader every 16 records.</summary>
    public Task<int> VisitProjectAssetIndexesAsync(Action<LibraryProjectAssetIndexDocument> visit,
        IProgress<LibraryAssetIndexProgress>? progress = null, string? projectPath = null,
        CancellationToken ct = default) => Task.Run(() =>
    {
        ArgumentNullException.ThrowIfNull(visit);
        if (!File.Exists(DatabasePath)) return 0;
        var total = CountAssetDocuments(nameof(LibraryProjectAssetIndexDocument), projectPath);
        if (total == 0) return 0;
        var processed = 0;
        long cursor = 0;
        progress?.Report(new("Reading saved asset indexes", 0, total));
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using var connection = OpenReadOnlyDatabase();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT rowid, COALESCE(json_extract(Data,'$.projectPath'),json_extract(Data,'$.ProjectPath')), Data FROM documents " +
                "WHERE TypeName=$type AND rowid>$after " +
                (string.IsNullOrWhiteSpace(projectPath) ? "" : "AND Id=$id ") +
                "ORDER BY rowid LIMIT 16";
            command.Parameters.AddWithValue("$type", nameof(LibraryProjectAssetIndexDocument));
            command.Parameters.AddWithValue("$after", cursor);
            if (!string.IsNullOrWhiteSpace(projectPath)) command.Parameters.AddWithValue("$id", StableId(projectPath));
            using var reader = command.ExecuteReader();
            var found = false;
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                found = true;
                cursor = reader.GetInt64(0);
                var path = reader.IsDBNull(1) ? null : reader.GetString(1);
                progress?.Report(new("Reading saved asset index", processed, total, path));
                var document = JsonSerializer.Deserialize<LibraryProjectAssetIndexDocument>(reader.GetString(2), AdminDocumentJsonOptions);
                ct.ThrowIfCancellationRequested();
                if (document is not null) visit(document);
                processed++;
                progress?.Report(new("Reading saved asset indexes", processed, total, path));
            }
            if (!found) break;
        }
        return processed;
    }, ct);

    private int CountAssetDocuments(string type, string? projectPath = null)
    {
        if (!File.Exists(DatabasePath)) return 0;
        using var connection = OpenReadOnlyDatabase();
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='documents'";
            if (Convert.ToInt32(schema.ExecuteScalar()) == 0) return 0;
        }
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM documents WHERE TypeName=$type" +
            (string.IsNullOrWhiteSpace(projectPath) ? "" : " AND Id=$id");
        command.Parameters.AddWithValue("$type", type);
        if (!string.IsNullOrWhiteSpace(projectPath)) command.Parameters.AddWithValue("$id", StableId(projectPath));
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
