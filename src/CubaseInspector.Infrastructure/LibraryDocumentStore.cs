using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CubaseInspector.Core;
using Microsoft.Data.Sqlite;
using Shiny.DocumentDb;
using Shiny.DocumentDb.Sqlite;

namespace CubaseInspector.Infrastructure;

/// <summary>Local document types for the user's project library and plugin inventory.</summary>
public sealed class LibraryProjectDocument
{
    public string Id { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Name { get; set; } = "";
    public string Format { get; set; } = "";
    public string? CreatorVersion { get; set; }
    public DateTime LastScannedUtc { get; set; }
    public string? Description { get; set; }
    public string? ArtworkPath { get; set; }
    public string? SourceUrl { get; set; }
    public bool IsManual { get; set; }
    public long? SizeBytes { get; set; }
    public DateTimeOffset? LastModifiedUtc { get; set; }
    public string? SourceSha256 { get; set; }
    public ProjectSnapshot? Snapshot { get; set; }
}

public sealed class LibraryPluginDocument
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Format { get; set; } = "";
    public string? Manufacturer { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? ArtworkPath { get; set; }
    public string? SourceUrl { get; set; }
    public bool IsManual { get; set; }
    public DateTime LastScannedUtc { get; set; }
    public IReadOnlyList<InstalledPluginClass>? Classes { get; set; }
}

public sealed class LibraryCatalogProductDocument
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Subcategory { get; set; }
    public string? Description { get; set; }
    public string? Website { get; set; }
    public string? ImageUrl { get; set; }
    public byte[]? ImageBytes { get; set; }
    public string? ImageMediaType { get; set; }
    public string? ManualUrl { get; set; }
    public string? VideoUrl { get; set; }
    public IReadOnlyList<string> Formats { get; set; } = [];
    public IReadOnlyList<string> Tags { get; set; } = [];
    public IReadOnlyList<string> Aliases { get; set; } = [];
    public string Source { get; set; } = "";
    public DateTime LastImportedUtc { get; set; }
}

public sealed class LibraryScanFolderDocument
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTime LastScannedUtc { get; set; }
    public int ProjectCount { get; set; }
    public int FailureCount { get; set; }
}

/// <summary>Search-oriented media and MIDI summaries derived from the persisted project snapshot.</summary>
public sealed class LibraryProjectAssetIndexDocument
{
    public string Id { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public DateTime IndexedUtc { get; set; }
    public ProjectAssetIndex? Index { get; set; }
}

public sealed record LibraryDatabaseOverview(
    int Projects, int Plugins, int CatalogProducts, int ScanFolders, int ProjectAssetIndexes,
    long DatabaseBytes, long JournalBytes);

public sealed record LibraryDatabaseCleanupResult(int DuplicateProjectsRemoved, int OrphanedAssetIndexesRemoved);
public sealed record LibraryDatabaseTableInfo(string Name, long RowCount, IReadOnlyList<string> Columns)
{
    public string ColumnsSummary => string.Join(", ", Columns);
}
public sealed record LibraryDatabaseRecord(string TypeName, string Id, string UpdatedAt, string Data);
public sealed record LibraryDatabaseQueryResult(IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows, bool IsTruncated);

/// <summary>
/// Local-first persistence for rescan results and editable catalog metadata. IDs are stable
/// path hashes, so rescans replace matching entries instead of duplicating them.
/// </summary>
public sealed partial class LibraryDocumentStore : IDisposable
{
    private static readonly JsonSerializerOptions AdminDocumentJsonOptions = new()
    { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private SqliteDocumentStore _store;

    public string DatabasePath { get; private set; }

    public LibraryDocumentStore(string? databasePath = null)
    {
        var path = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CubaseProjectExplorer", "library.db");
        DatabasePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        _store = new SqliteDocumentStore($"Data Source={DatabasePath};Pooling=False");
    }

    public async Task SaveScanAsync(string folder, FolderReport report, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var folderId = StableId(folder);
        var existingProjects = await GetProjectsAsync(ct);
        var existingDocumentsByPath = existingProjects
            .ToDictionary(project => NormalizePath(project.FilePath), StringComparer.OrdinalIgnoreCase);
        var existingEntries = new List<ProjectLibraryEntry>(existingDocumentsByPath.Count);
        foreach (var existing in existingDocumentsByPath.Values.Where(project => project.Snapshot is not null))
        {
            var snapshot = existing.Snapshot!;
            var checksum = ProjectLibraryBrowser.NormalizeSha256(existing.SourceSha256) ??
                ProjectLibraryBrowser.NormalizeSha256(snapshot.SourceSha256);
            if (checksum is null && File.Exists(existing.FilePath))
            {
                try { checksum = await ProjectContentChecksum.ComputeAsync(existing.FilePath, ct); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or ArgumentException or NotSupportedException or System.Security.SecurityException) { }
            }
            if (checksum is not null)
            {
                existing.SourceSha256 = checksum;
                existing.Snapshot = snapshot = snapshot with { SourceSha256 = checksum };
            }
            existingEntries.Add(ProjectLibraryBrowser.FromFile(snapshot, existing.SizeBytes,
                existing.LastModifiedUtc, checksum));
        }

        var scannedProjects = new List<ProjectSnapshot>(report.Projects.Count);
        foreach (var project in report.Projects)
        {
            var checksum = ProjectLibraryBrowser.NormalizeSha256(project.SourceSha256);
            if (checksum is null)
            {
                try { checksum = await ProjectContentChecksum.ComputeAsync(project.FilePath, ct); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or ArgumentException or NotSupportedException or System.Security.SecurityException) { }
            }
            scannedProjects.Add(checksum is null ? project : project with { SourceSha256 = checksum });
        }
        var scannedEntries = scannedProjects.Select(project => ProjectLibraryBrowser.FromFile(project)).ToArray();
        var uniqueEntries = ProjectLibraryBrowser.MergeScanResults(existingEntries, scannedEntries);
        var uniquePaths = uniqueEntries.Select(entry => NormalizePath(entry.Project.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scannedByPath = scannedProjects.ToDictionary(project => NormalizePath(project.FilePath), StringComparer.OrdinalIgnoreCase);
        var projectsByPath = existingDocumentsByPath;
        var documentsToSave = new List<LibraryProjectDocument>(uniqueEntries.Count);
        var scannedWinners = new List<ProjectSnapshot>();
        foreach (var entry in uniqueEntries)
        {
            var key = NormalizePath(entry.Project.FilePath);
            projectsByPath.TryGetValue(key, out var existing);
            if (scannedByPath.TryGetValue(key, out var scanned))
            {
                var document = new LibraryProjectDocument
                {
                    Id = StableId(scanned.FilePath), FilePath = scanned.FilePath,
                    Name = existing is { IsManual: true } ? existing.Name : scanned.Name,
                    Format = scanned.Format, CreatorVersion = scanned.CreatorVersion, LastScannedUtc = now,
                    Description = existing?.Description, ArtworkPath = existing?.ArtworkPath,
                    SourceUrl = existing?.SourceUrl, IsManual = existing?.IsManual ?? false,
                    SizeBytes = GetFileSize(scanned.FilePath), LastModifiedUtc = GetLastModified(scanned.FilePath),
                    SourceSha256 = ProjectLibraryBrowser.NormalizeSha256(scanned.SourceSha256),
                    Snapshot = scanned
                };
                documentsToSave.Add(document);
                scannedWinners.Add(scanned);
            }
            else if (existing is not null)
                documentsToSave.Add(existing);
        }

        foreach (var duplicate in existingDocumentsByPath.Values.Where(document => document.Snapshot is not null &&
                     !uniquePaths.Contains(NormalizePath(document.FilePath))))
        {
            await DeleteProjectAsync(duplicate.Id, ct);
        }
        await _store.BatchUpsert(documentsToSave, cancellationToken: ct);
        await _store.BatchUpsert(scannedWinners.Select(BuildAssetIndexDocument), cancellationToken: ct);
        await _store.Upsert(new LibraryScanFolderDocument
        {
            Id = folderId, Path = folder, LastScannedUtc = now,
            ProjectCount = ProjectLibraryBrowser.MergeScanResults([], scannedEntries).Count,
            FailureCount = report.Failures.Count
        }, patchIfUpdate: false, cancellationToken: ct);
    }

    public async Task SavePluginScanAsync(IEnumerable<InstalledPlugin> plugins, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var scan = plugins.ToArray();
        var existingPlugins = (await GetPluginsAsync(ct))
            .ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        var documents = new List<LibraryPluginDocument>(scan.Length);
        foreach (var plugin in scan)
        {
            var id = StableId(plugin.Path);
            existingPlugins.TryGetValue(id, out var existing);
            var isManual = existing?.IsManual ?? false;
            documents.Add(new LibraryPluginDocument
            {
                Id = id, Name = isManual ? existing!.Name : plugin.Name, Path = plugin.Path,
                Format = plugin.Format, Manufacturer = existing?.Manufacturer ?? plugin.Classes?.Select(c => c.Manufacturer)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                Version = existing?.Version ?? plugin.Classes?.Select(c => c.Version)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                Description = existing?.Description, ArtworkPath = existing?.IsManual == true
                    ? existing.ArtworkPath : FindBundleArtwork(plugin),
                SourceUrl = existing?.SourceUrl, IsManual = isManual, LastScannedUtc = now,
                Classes = plugin.Classes
            });
        }
        await _store.BatchUpsert(documents, cancellationToken: ct);
    }

    public async Task SaveProjectInspectionAsync(ProjectSnapshot project, CancellationToken ct = default)
    {
        if (ProjectLibraryBrowser.NormalizeSha256(project.SourceSha256) is null)
        {
            try { project = project with { SourceSha256 = await ProjectContentChecksum.ComputeAsync(project.FilePath, ct) }; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException or System.Security.SecurityException) { }
        }
        var id = StableId(project.FilePath);
        var existing = await _store.Get<LibraryProjectDocument>(id, cancellationToken: ct);
        await _store.Upsert(new LibraryProjectDocument
        {
            Id = id,
            FilePath = project.FilePath,
            Name = existing?.IsManual == true ? existing.Name : project.Name,
            Format = project.Format,
            CreatorVersion = project.CreatorVersion,
            LastScannedUtc = DateTime.UtcNow,
            Description = existing?.Description,
            ArtworkPath = existing?.ArtworkPath,
            SourceUrl = existing?.SourceUrl,
            IsManual = existing?.IsManual ?? false,
            SizeBytes = GetFileSize(project.FilePath),
            LastModifiedUtc = GetLastModified(project.FilePath),
            SourceSha256 = ProjectLibraryBrowser.NormalizeSha256(project.SourceSha256),
            Snapshot = project
        }, patchIfUpdate: false, cancellationToken: ct);
        await _store.Upsert(BuildAssetIndexDocument(project), patchIfUpdate: false, cancellationToken: ct);
        await DeduplicateStoredProjectsAsync(ct);
    }

    private async Task DeduplicateStoredProjectsAsync(CancellationToken ct)
    {
        var documents = await GetProjectsAsync(ct);
        var entries = new List<ProjectLibraryEntry>();
        var updates = new List<LibraryProjectDocument>();
        foreach (var document in documents.Where(item => item.Snapshot is not null))
        {
            var snapshot = document.Snapshot!;
            var checksum = ProjectLibraryBrowser.NormalizeSha256(document.SourceSha256) ??
                ProjectLibraryBrowser.NormalizeSha256(snapshot.SourceSha256);
            if (checksum is null && File.Exists(document.FilePath))
            {
                try { checksum = await ProjectContentChecksum.ComputeAsync(document.FilePath, ct); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or ArgumentException or NotSupportedException or System.Security.SecurityException) { }
            }
            if (checksum is not null &&
                (ProjectLibraryBrowser.NormalizeSha256(document.SourceSha256) != checksum ||
                 ProjectLibraryBrowser.NormalizeSha256(snapshot.SourceSha256) != checksum))
            {
                document.SourceSha256 = checksum;
                document.Snapshot = snapshot = snapshot with { SourceSha256 = checksum };
                updates.Add(document);
            }
            entries.Add(ProjectLibraryBrowser.FromFile(snapshot, document.SizeBytes,
                document.LastModifiedUtc, checksum));
        }

        var winners = ProjectLibraryBrowser.MergeScanResults(entries, []);
        var winnerPaths = winners.Select(entry => NormalizePath(entry.Project.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var duplicate in documents.Where(document => document.Snapshot is not null &&
                     !winnerPaths.Contains(NormalizePath(document.FilePath))))
            await DeleteProjectAsync(duplicate.Id, ct);
        if (updates.Count > 0) await _store.BatchUpsert(updates, cancellationToken: ct);
    }

    public Task<IReadOnlyList<LibraryProjectAssetIndexDocument>> GetProjectAssetIndexesAsync(CancellationToken ct = default) =>
        _store.Query<LibraryProjectAssetIndexDocument>().OrderBy(document => document.ProjectPath).ToList(ct);

    /// <summary>Rebuilds search indexes from project snapshots already stored in this database, without reopening source files.</summary>
    public async Task<int> RebuildProjectAssetIndexesAsync(CancellationToken ct = default)
    {
        var projects = await GetProjectsAsync(ct);
        var indexes = projects.Where(project => project.Snapshot is not null)
            .Select(project => BuildAssetIndexDocument(project.Snapshot!)).ToArray();
        await _store.BatchUpsert(indexes, cancellationToken: ct);
        return indexes.Length;
    }

    public Task<IReadOnlyList<LibraryProjectDocument>> GetProjectsAsync(CancellationToken ct = default) =>
        _store.Query<LibraryProjectDocument>().OrderBy(x => x.Name).ToList(ct);

    /// <summary>Reads document counts from SQLite metadata without hydrating large JSON payloads or catalog images.</summary>
    public Task<LibraryDatabaseOverview> GetDatabaseOverviewAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private, Pooling = false
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TypeName, COUNT(*) FROM documents GROUP BY TypeName;";
        using var reader = command.ExecuteReader();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }
        static int Count<T>(IReadOnlyDictionary<string, int> values)
        {
            var expected = typeof(T).FullName ?? typeof(T).Name;
            return values.Where(pair =>
            {
                var persistedType = pair.Key.Split(',', 2)[0].Trim();
                return persistedType.Equals(expected, StringComparison.Ordinal) ||
                    persistedType.Equals(typeof(T).Name, StringComparison.Ordinal);
            }).Sum(pair => pair.Value);
        }
        var databaseBytes = GetFileSize(DatabasePath) ?? 0;
        var journalBytes = GetFileSize(DatabasePath + "-wal") ?? 0;
        return new LibraryDatabaseOverview(Count<LibraryProjectDocument>(counts), Count<LibraryPluginDocument>(counts),
            Count<LibraryCatalogProductDocument>(counts), Count<LibraryScanFolderDocument>(counts),
            Count<LibraryProjectAssetIndexDocument>(counts), databaseBytes, journalBytes);
    }, ct);

    public Task<IReadOnlyList<LibraryDatabaseTableInfo>> GetDatabaseTablesAsync(CancellationToken ct = default) => Task.Run<IReadOnlyList<LibraryDatabaseTableInfo>>(() =>
    {
        var tables = new List<LibraryDatabaseTableInfo>();
        using var connection = OpenReadOnlyDatabase();
        using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var tableReader = tableCommand.ExecuteReader();
        var names = new List<string>();
        while (tableReader.Read()) { ct.ThrowIfCancellationRequested(); names.Add(tableReader.GetString(0)); }
        tableReader.Close();
        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            var columns = new List<string>();
            using (var columnsCommand = connection.CreateCommand())
            {
                columnsCommand.CommandText = $"PRAGMA table_info(\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\")";
                using var reader = columnsCommand.ExecuteReader();
                while (reader.Read()) columns.Add(reader.GetString(1));
            }
            long count;
            using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = $"SELECT COUNT(*) FROM \"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
                count = (long)(countCommand.ExecuteScalar() ?? 0L);
            }
            tables.Add(new LibraryDatabaseTableInfo(name, count, columns));
        }
        return tables;
    }, ct);

    public Task<IReadOnlyList<string>> GetDatabaseRecordTypesAsync(CancellationToken ct = default) => Task.Run<IReadOnlyList<string>>(() =>
    {
        using var connection = OpenReadOnlyDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT TypeName FROM documents ORDER BY TypeName";
        using var reader = command.ExecuteReader();
        var types = new List<string>();
        while (reader.Read()) { ct.ThrowIfCancellationRequested(); types.Add(reader.GetString(0)); }
        return types;
    }, ct);

    public Task<IReadOnlyList<LibraryDatabaseRecord>> GetDatabaseRecordsAsync(string typeName,
        string search = "", int offset = 0, int limit = 100, CancellationToken ct = default) => Task.Run<IReadOnlyList<LibraryDatabaseRecord>>(() =>
    {
        ValidateAdminType(typeName);
        if (offset < 0 || limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        using var connection = OpenReadOnlyDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TypeName, Id, UpdatedAt, Data FROM documents WHERE TypeName=$type AND " +
            "($search='' OR instr(lower(Id), lower($search))>0 OR instr(lower(Data), lower($search))>0) " +
            "ORDER BY UpdatedAt DESC, Id LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$type", typeName);
        command.Parameters.AddWithValue("$search", search.Trim());
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var rows = new List<LibraryDatabaseRecord>();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(new LibraryDatabaseRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }
        return rows;
    }, ct);

    /// <summary>Runs one bounded read-only SELECT against the active library database.</summary>
    public Task<LibraryDatabaseQueryResult> QueryDatabaseAsync(string sql, CancellationToken ct = default) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > 16_384)
            throw new ArgumentException("Enter a query between 1 and 16,384 characters.", nameof(sql));
        var trimmed = sql.Trim();
        if (!(trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
              trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)) || trimmed.Contains(';'))
            throw new ArgumentException("The query tool accepts one read-only SELECT or WITH statement without semicolons.", nameof(sql));
        using var connection = OpenReadOnlyDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = trimmed;
        command.CommandTimeout = 5;
        using var reader = command.ExecuteReader();
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = new List<IReadOnlyList<string>>();
        while (rows.Count < 500 && reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            var cells = new string[reader.FieldCount];
            for (var index = 0; index < cells.Length; index++)
            {
                if (reader.IsDBNull(index)) { cells[index] = "NULL"; continue; }
                var value = reader.GetValue(index);
                cells[index] = value switch
                {
                    byte[] bytes => $"[BLOB · {bytes.Length:N0} bytes]",
                    _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""
                };
                if (cells[index].Length > 2048) cells[index] = cells[index][..2048] + "… [truncated]";
            }
            rows.Add(cells);
        }
        return new LibraryDatabaseQueryResult(columns, rows, rows.Count == 500 && reader.Read());
    }, ct);

    public async Task SaveAdminDatabaseRecordAsync(string typeName, string json, bool create,
        CancellationToken ct = default)
    {
        ValidateAdminType(typeName);
        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
                !parsed.RootElement.TryGetProperty("Id", out var idValue) && !parsed.RootElement.TryGetProperty("id", out idValue) ||
                idValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(idValue.GetString()))
                throw new ArgumentException("The JSON document must be an object with a non-empty Id.", nameof(json));
        }
        catch (JsonException exception) { throw new ArgumentException("The record is not valid JSON: " + exception.Message, nameof(json)); }

        switch (ShortTypeName(typeName))
        {
            case nameof(LibraryProjectDocument): await SaveAdminDocumentAsync<LibraryProjectDocument>(json, create, ct); break;
            case nameof(LibraryPluginDocument): await SaveAdminDocumentAsync<LibraryPluginDocument>(json, create, ct); break;
            case nameof(LibraryCatalogProductDocument): await SaveAdminDocumentAsync<LibraryCatalogProductDocument>(json, create, ct); break;
            case nameof(LibraryScanFolderDocument): await SaveAdminDocumentAsync<LibraryScanFolderDocument>(json, create, ct); break;
            case nameof(LibraryProjectAssetIndexDocument): await SaveAdminDocumentAsync<LibraryProjectAssetIndexDocument>(json, create, ct); break;
            default: throw new ArgumentException("This record type is not supported for CRUD.", nameof(typeName));
        }
    }

    public async Task<bool> DeleteAdminDatabaseRecordAsync(string typeName, string id, CancellationToken ct = default)
    {
        ValidateAdminType(typeName);
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A record ID is required.", nameof(id));
        return ShortTypeName(typeName) switch
        {
            nameof(LibraryProjectDocument) => await DeleteProjectAsync(id, ct),
            nameof(LibraryPluginDocument) => await _store.Remove<LibraryPluginDocument>(id, ct),
            nameof(LibraryCatalogProductDocument) => await _store.Remove<LibraryCatalogProductDocument>(id, ct),
            nameof(LibraryScanFolderDocument) => await _store.Remove<LibraryScanFolderDocument>(id, ct),
            nameof(LibraryProjectAssetIndexDocument) => await _store.Remove<LibraryProjectAssetIndexDocument>(id, ct),
            _ => throw new ArgumentException("This record type is not supported for CRUD.", nameof(typeName))
        };
    }

    private async Task SaveAdminDocumentAsync<T>(string json, bool create, CancellationToken ct) where T : class
    {
        var document = JsonSerializer.Deserialize<T>(json, AdminDocumentJsonOptions)
            ?? throw new ArgumentException("The record could not be deserialized.", nameof(json));
        var id = GetAdminDocumentId(document);
        var exists = await _store.Get<T>(id, cancellationToken: ct) is not null;
        if (create && exists) throw new InvalidOperationException("A record with this ID already exists.");
        if (!create && !exists) throw new InvalidOperationException("The record no longer exists. Reload before editing.");
        await _store.Upsert(document, patchIfUpdate: false, cancellationToken: ct);
    }

    private static string GetAdminDocumentId<T>(T document) where T : class => document switch
    {
        LibraryProjectDocument item => item.Id,
        LibraryPluginDocument item => item.Id,
        LibraryCatalogProductDocument item => item.Id,
        LibraryScanFolderDocument item => item.Id,
        LibraryProjectAssetIndexDocument item => item.Id,
        _ => throw new ArgumentException("Unsupported library document.", nameof(document))
    };

    private static void ValidateAdminType(string typeName)
    {
        var shortName = ShortTypeName(typeName);
        if (shortName is not (nameof(LibraryProjectDocument) or nameof(LibraryPluginDocument) or
            nameof(LibraryCatalogProductDocument) or nameof(LibraryScanFolderDocument) or
            nameof(LibraryProjectAssetIndexDocument)))
            throw new ArgumentException("Unsupported library record type.", nameof(typeName));
    }

    private static string ShortTypeName(string typeName) => typeName.Split(',', 2)[0].Split('.').Last().Trim();

    private SqliteConnection OpenReadOnlyDatabase()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 5
        }.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>Merges known library document types from a validated source database, retaining target records on ID conflicts.</summary>
    public async Task<int> ImportDatabaseAsync(string sourcePath, CancellationToken ct = default)
    {
        var source = await LibraryDatabaseValidator.ValidateAsync(sourcePath, ct);
        if (string.Equals(source, DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a different database to import.", nameof(sourcePath));
        using var import = new LibraryDocumentStore(source);
        var added = 0;
        added += await ImportMissingByIdAsync(await import.GetProjectsAsync(ct), await GetProjectsAsync(ct), ct);
        added += await ImportMissingByIdAsync(await import.GetPluginsAsync(ct), await GetPluginsAsync(ct), ct);
        added += await ImportMissingByIdAsync(await import.GetCatalogProductsAsync(ct), await GetCatalogProductsAsync(ct), ct);
        added += await ImportMissingByIdAsync(await import.GetScanFoldersAsync(ct), await GetScanFoldersAsync(ct), ct);
        added += await ImportMissingByIdAsync(await import.GetProjectAssetIndexesAsync(ct), await GetProjectAssetIndexesAsync(ct), ct);
        await DeduplicateStoredProjectsAsync(ct);
        await RemoveOrphanedProjectAssetIndexesAsync(ct);
        return added;
    }

    /// <summary>Removes checksum-identical project records and indexes with no stored project; source files remain untouched.</summary>
    public async Task<LibraryDatabaseCleanupResult> CleanDatabaseAsync(CancellationToken ct = default)
    {
        var beforeProjects = (await GetProjectsAsync(ct)).Count;
        await DeduplicateStoredProjectsAsync(ct);
        var afterProjects = (await GetProjectsAsync(ct)).Count;
        var orphanedIndexes = await RemoveOrphanedProjectAssetIndexesAsync(ct);
        return new LibraryDatabaseCleanupResult(beforeProjects - afterProjects, orphanedIndexes);
    }

    public Task OptimizeDatabaseAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private, Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA optimize; VACUUM;";
        command.ExecuteNonQuery();
    }, ct);

    private async Task<int> RemoveOrphanedProjectAssetIndexesAsync(CancellationToken ct)
    {
        var projectIds = (await GetProjectsAsync(ct)).Select(project => project.Id).ToHashSet(StringComparer.Ordinal);
        var indexes = await GetProjectAssetIndexesAsync(ct);
        var orphans = indexes.Where(index => !projectIds.Contains(index.Id)).ToArray();
        foreach (var orphan in orphans)
        {
            ct.ThrowIfCancellationRequested();
            await _store.Remove<LibraryProjectAssetIndexDocument>(orphan.Id, ct);
        }
        return orphans.Length;
    }

    private async Task<int> ImportMissingByIdAsync<T>(IReadOnlyList<T> source, IReadOnlyList<T> target, CancellationToken ct)
        where T : class
    {
        var targetIds = target.Select(GetDocumentId).ToHashSet(StringComparer.Ordinal);
        var additions = source.Where(document => !targetIds.Contains(GetDocumentId(document))).ToArray();
        if (additions.Length > 0) await _store.BatchUpsert(additions, cancellationToken: ct);
        return additions.Length;
    }

    private static string GetDocumentId<T>(T document) where T : class => document switch
    {
        LibraryProjectDocument item => item.Id,
        LibraryPluginDocument item => item.Id,
        LibraryCatalogProductDocument item => item.Id,
        LibraryScanFolderDocument item => item.Id,
        LibraryProjectAssetIndexDocument item => item.Id,
        _ => throw new ArgumentException("Unsupported library document type.", nameof(document))
    };

    public Task<LibraryProjectDocument?> GetProjectAsync(string id, CancellationToken ct = default) =>
        _store.Get<LibraryProjectDocument>(id, cancellationToken: ct);

    public Task<IReadOnlyList<LibraryPluginDocument>> GetPluginsAsync(CancellationToken ct = default) =>
        _store.Query<LibraryPluginDocument>().OrderBy(x => x.Name).ToList(ct);

    public Task<IReadOnlyList<LibraryCatalogProductDocument>> GetCatalogProductsAsync(CancellationToken ct = default) =>
        _store.Query<LibraryCatalogProductDocument>().OrderBy(x => x.Name).ToList(ct);

    public async Task SaveCatalogProductsAsync(IEnumerable<PluginMetadataEntry> products, CancellationToken ct = default)
    {
        var entries = products.Where(product => !string.IsNullOrWhiteSpace(product.Name)).ToArray();
        if (entries.Any(product => product.ImageBytes is { Length: > CatalogImageFetcher.MaximumImageBytes } ||
            product.ImageBytes is not null && product.ImageMediaType is not ("image/png" or "image/jpeg" or "image/webp" or "image/gif")))
            throw new ArgumentException("Catalog image data exceeds its size limit or has an unsupported media type.", nameof(products));
        var existing = (await GetCatalogProductsAsync(ct)).ToDictionary(product => product.Id, StringComparer.OrdinalIgnoreCase);
        var imported = entries
            .Select(product => new LibraryCatalogProductDocument
            {
                Id = StableId($"{product.Source}|{product.Manufacturer}|{product.Name}"),
                Name = product.Name.Trim(), Manufacturer = product.Manufacturer.Trim(),
                Category = product.Category, Subcategory = product.Subcategory,
                Description = product.Description, Website = product.Website, ImageUrl = product.ImageUrl,
                ImageBytes = product.ImageBytes,
                ImageMediaType = product.ImageMediaType,
                ManualUrl = product.ManualUrl, VideoUrl = product.VideoUrl, Formats = product.Formats,
                Tags = product.Tags, Aliases = product.Aliases, Source = product.Source,
                LastImportedUtc = DateTime.UtcNow
            })
            .Select(document =>
            {
                if (existing.TryGetValue(document.Id, out var old))
                {
                    document.Subcategory ??= old.Subcategory;
                    document.Description ??= old.Description;
                    document.Website ??= old.Website;
                    document.ImageUrl ??= old.ImageUrl;
                    document.ManualUrl ??= old.ManualUrl;
                    document.VideoUrl ??= old.VideoUrl;
                    if (document.Category.Equals("Uncategorized", StringComparison.OrdinalIgnoreCase))
                        document.Category = old.Category;
                    document.Formats = MergeCatalogLabels(old.Formats, document.Formats);
                    document.Tags = MergeCatalogLabels(old.Tags, document.Tags);
                    document.Aliases = MergeCatalogLabels(old.Aliases, document.Aliases);
                    document.ImageBytes ??= old.ImageBytes;
                    document.ImageMediaType ??= old.ImageMediaType;
                }
                return document;
            }).ToArray();
        await _store.BatchUpsert(imported, cancellationToken: ct);
    }

    private static IReadOnlyList<string> MergeCatalogLabels(IEnumerable<string> oldValues, IEnumerable<string> newValues) =>
        oldValues.Concat(newValues).Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray();

    public Task<IReadOnlyList<LibraryScanFolderDocument>> GetScanFoldersAsync(CancellationToken ct = default) =>
        _store.Query<LibraryScanFolderDocument>().OrderByDescending(x => x.LastScannedUtc).ToList(ct);

    public Task BackupAsync(string destinationPath, CancellationToken ct = default)
    {
        var destination = Path.GetFullPath(destinationPath);
        if (string.Equals(destination, DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a different path for the database backup.", nameof(destinationPath));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        return _store.Backup(destination, ct);
    }

    /// <summary>Copies the current library database to a new path for use on the next application launch.</summary>
    public async Task CopyDatabaseToNewPathAsync(string destinationPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Choose a database file path.", nameof(destinationPath));

        var destination = Path.GetFullPath(destinationPath);
        if (string.Equals(destination, DatabasePath, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(Path.GetExtension(destination), ".db", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The library database file must use the .db extension.", nameof(destinationPath));
        if (File.Exists(destination) && new FileInfo(destination).Length > 0)
            throw new IOException("The selected database file already contains data. Choose a new .db file path.");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var placeholder = File.Exists(destination);
        if (placeholder) File.Delete(destination); // Empty placeholder created by the save picker.
        try
        {
            await Task.Run(() =>
            {
                var sourceConnectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = DatabasePath,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false
                }.ToString();
                var destinationConnectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = destination,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false
                }.ToString();
                using var source = new SqliteConnection(sourceConnectionString);
                using var target = new SqliteConnection(destinationConnectionString);
                source.Open();
                target.Open();
                source.BackupDatabase(target);
            }, ct);
        }
        catch
        {
            if (File.Exists(destination)) File.Delete(destination);
            throw;
        }
    }

    public async Task SaveProjectAsync(LibraryProjectDocument project, CancellationToken ct = default)
    {
        Validate(project.Id, project.Name, project.Description, project.ArtworkPath, project.SourceUrl);
        await EnsureUniquePathAsync(project.Id, project.FilePath, GetProjectsAsync, "project", ct);
        await _store.Upsert(project, patchIfUpdate: false, cancellationToken: ct);
    }

    public async Task SavePluginAsync(LibraryPluginDocument plugin, CancellationToken ct = default)
    {
        Validate(plugin.Id, plugin.Name, plugin.Description, plugin.ArtworkPath, plugin.SourceUrl);
        await EnsureUniquePathAsync(plugin.Id, plugin.Path, GetPluginsAsync, "VST", ct);
        await _store.Upsert(plugin, patchIfUpdate: false, cancellationToken: ct);
    }

    private static async Task EnsureUniquePathAsync<T>(string id, string path,
        Func<CancellationToken, Task<IReadOnlyList<T>>> load, string kind, CancellationToken ct)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var records = await load(ct);
        var duplicate = records.FirstOrDefault(record => record switch
        {
            LibraryProjectDocument project => project.Id != id && string.Equals(project.FilePath, path, StringComparison.OrdinalIgnoreCase),
            LibraryPluginDocument plugin => plugin.Id != id && string.Equals(plugin.Path, path, StringComparison.OrdinalIgnoreCase),
            _ => false
        });
        if (duplicate is not null) throw new InvalidOperationException($"A {kind} record already uses this path.");
    }

    public async Task<bool> DeleteProjectAsync(string id, CancellationToken ct = default)
    {
        var removed = await _store.Remove<LibraryProjectDocument>(id, ct);
        await _store.Remove<LibraryProjectAssetIndexDocument>(id, ct);
        return removed;
    }
    public Task<bool> DeletePluginAsync(string id, CancellationToken ct = default) => _store.Remove<LibraryPluginDocument>(id, ct);

    public void Dispose() => _store.Dispose();

    private static string StableId(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant())));

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or IOException or
            NotSupportedException or System.Security.SecurityException) { return path.Trim(); }
    }

    private static LibraryProjectAssetIndexDocument BuildAssetIndexDocument(ProjectSnapshot project) => new()
    {
        Id = StableId(project.FilePath), ProjectPath = project.FilePath, IndexedUtc = DateTime.UtcNow,
        Index = ProjectAssetIndexer.Build(project)
    };

    private static long? GetFileSize(string path)
    {
        try { var file = new FileInfo(path); return file.Exists ? file.Length : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static DateTimeOffset? GetLastModified(string path)
    {
        try { var file = new FileInfo(path); return file.Exists ? new DateTimeOffset(file.LastWriteTimeUtc) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static string? FindBundleArtwork(InstalledPlugin plugin)
    {
        if (!plugin.Format.Equals("VST3", StringComparison.OrdinalIgnoreCase) || plugin.Classes is null)
            return null;
        var images = plugin.Classes.Select(klass => VerifiedVst3Artwork.FindClassSnapshot(plugin.Path, klass))
            .Where(path => path is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return images.Length == 1 ? images[0] : null;
    }

    private static void Validate(string id, string name, string? description, string? artwork, string? source)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || string.IsNullOrWhiteSpace(name) || name.Length > 512)
            throw new ArgumentException("Catalog entry requires a bounded ID and name.");
        if (description?.Length > 16_384 || artwork?.Length > 2048 || source?.Length > 2048)
            throw new ArgumentException("Catalog metadata exceeds its storage limit.");
        if (!string.IsNullOrWhiteSpace(source) &&
            (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Catalog source URLs must use HTTPS.");
    }
}
