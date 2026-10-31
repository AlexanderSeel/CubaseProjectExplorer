using Microsoft.Data.Sqlite;

namespace CubaseInspector.Infrastructure;

/// <summary>Checks compatibility without creating a file or initializing a document store.</summary>
public static class LibraryDatabaseValidator
{
    public static Task<string> ValidateAsync(string path, CancellationToken ct = default) => Task.Run(() =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Choose an existing library database.", fullPath);
        if (new FileInfo(fullPath).Length == 0) throw new InvalidDataException("The selected database file is empty.");
        ct.ThrowIfCancellationRequested();
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = fullPath, Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private, Pooling = false
            }.ToString());
            connection.Open();
            using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA quick_check(1);";
            if (!string.Equals(integrity.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The selected database did not pass SQLite's integrity check.");
            ct.ThrowIfCancellationRequested();
            using var schema = connection.CreateCommand();
            schema.CommandText = "PRAGMA table_info('documents');";
            using var reader = schema.ExecuteReader();
            var columns = new Dictionary<string, (string Type, bool Required, int Key)>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                columns[reader.GetString(1)] = (reader.GetString(2), reader.GetInt32(3) != 0, reader.GetInt32(5));
            }
            foreach (var name in new[] { "Id", "TypeName", "Data", "CreatedAt", "UpdatedAt" })
                if (!columns.TryGetValue(name, out var column) || !column.Required ||
                    !column.Type.Equals("TEXT", StringComparison.OrdinalIgnoreCase) ||
                    column.Key != (name == "Id" ? 1 : name == "TypeName" ? 2 : 0))
                    throw new InvalidDataException("The selected SQLite file does not have a compatible library document schema.");
            return fullPath;
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException("The selected file could not be read as a SQLite library database: " + ex.Message, ex);
        }
    }, ct);
}
