using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text.Json;
using CubaseInspector.App.Services;
using CubaseInspector.Infrastructure;

namespace CubaseInspector.App.ViewModels;

public sealed partial record LibraryDatabaseRecordRow(LibraryDatabaseRecord Record)
{
    public string Id => Record.Id;
    public string Type => Record.TypeName.Split('.').Last();
    public string UpdatedAt => Record.UpdatedAt;
    public string Preview => Record.Data.Length > 180 ? Record.Data[..180] + "…" : Record.Data;
}

public sealed record LibraryDatabaseQueryRow(string Values);

public sealed partial class LibraryAdminViewModel(LibraryDocumentStore store, IProjectPathPicker picker,
    AppPreferencesService preferences, ProjectLibraryViewModel library) : ObservableObject
{
    [ObservableProperty] private string _status = "Local library database status has not been loaded.";
    [ObservableProperty] private string _projects = "—";
    [ObservableProperty] private string _plugins = "—";
    [ObservableProperty] private string _catalogProducts = "—";
    [ObservableProperty] private string _folders = "—";
    [ObservableProperty] private string _assetIndexes = "—";
    [ObservableProperty] private string _databaseSize = "—";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _selectedDatabaseRecordType = "";
    [ObservableProperty] private string _databaseRecordSearch = "";
    [ObservableProperty] private string _recordJson = "";
    [ObservableProperty] private string _recordEditorStatus = "Select a database record to inspect or edit its JSON.";
    [ObservableProperty] private bool _isRecordBrowserBusy;
    [ObservableProperty] private bool _isQueryBusy;
    [ObservableProperty] private string _databaseQuery = "SELECT TypeName, COUNT(*) AS Records FROM documents GROUP BY TypeName ORDER BY Records DESC";
    [ObservableProperty] private string _databaseQueryStatus = "Read-only query results are limited to 500 rows.";

    public string DatabasePath => store.DatabasePath;
    public string NextLaunchDatabasePath => preferences.DatabasePath ?? store.DatabasePath;
    public string JournalSize { get; private set; } = "—";
    public ObservableCollection<LibraryDatabaseTableInfo> DatabaseTables { get; } = [];
    public ObservableCollection<string> DatabaseRecordTypes { get; } = [];
    public ObservableCollection<LibraryDatabaseRecordRow> DatabaseRecords { get; } = [];
    public ObservableCollection<LibraryDatabaseQueryRow> DatabaseQueryRows { get; } = [];
    public IReadOnlyList<string> DatabaseQueryColumns { get; private set; } = [];
    public LibraryDatabaseRecordRow? SelectedDatabaseRecord { get; private set; }
    public int DatabaseRecordPage { get; private set; }
    private const int DatabaseRecordsPageSize = 100;

    [RelayCommand]
    private async Task LoadDatabaseBrowserAsync()
    {
        if (IsRecordBrowserBusy) return;
        IsRecordBrowserBusy = true;
        try
        {
            var tables = await store.GetDatabaseTablesAsync();
            var types = await store.GetDatabaseRecordTypesAsync();
            DatabaseTables.Clear(); foreach (var table in tables) DatabaseTables.Add(table);
            DatabaseRecordTypes.Clear(); foreach (var type in types) DatabaseRecordTypes.Add(type);
            if (!DatabaseRecordTypes.Contains(SelectedDatabaseRecordType))
                SelectedDatabaseRecordType = DatabaseRecordTypes.FirstOrDefault(type => type.EndsWith(nameof(LibraryProjectDocument), StringComparison.Ordinal))
                    ?? DatabaseRecordTypes.FirstOrDefault() ?? "";
            DatabaseRecordPage = 0;
            await LoadDatabaseRecordsCoreAsync();
            RecordEditorStatus = $"Read-only table discovery loaded {tables.Count:N0} table(s); record edits use validated document types.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        { RecordEditorStatus = "Database browser could not be loaded: " + ex.Message; }
        finally { IsRecordBrowserBusy = false; }
    }

    [RelayCommand]
    private async Task LoadDatabaseRecordsAsync()
    {
        if (IsRecordBrowserBusy) return;
        IsRecordBrowserBusy = true;
        try { DatabaseRecordPage = 0; await LoadDatabaseRecordsCoreAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Data.Common.DbException)
        { RecordEditorStatus = "Database records could not be read: " + ex.Message; }
        finally { IsRecordBrowserBusy = false; }
    }

    private async Task LoadDatabaseRecordsCoreAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedDatabaseRecordType)) { DatabaseRecords.Clear(); return; }
        var records = await store.GetDatabaseRecordsAsync(SelectedDatabaseRecordType, DatabaseRecordSearch,
            DatabaseRecordPage * DatabaseRecordsPageSize, DatabaseRecordsPageSize);
        DatabaseRecords.Clear(); foreach (var record in records) DatabaseRecords.Add(new LibraryDatabaseRecordRow(record));
        SelectedDatabaseRecord = null;
        RecordJson = "";
        RecordEditorStatus = $"Page {DatabaseRecordPage + 1} · {records.Count:N0} rows loaded for {SelectedDatabaseRecordType}.";
        OnPropertyChanged(nameof(SelectedDatabaseRecord));
        OnPropertyChanged(nameof(DatabaseRecordPage));
    }

    public void SelectDatabaseRecord(LibraryDatabaseRecordRow? row)
    {
        SelectedDatabaseRecord = row;
        if (row is null) RecordJson = "";
        else
        {
            using var document = JsonDocument.Parse(row.Record.Data);
            RecordJson = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        RecordEditorStatus = row is null ? "Select a database record to inspect or edit its JSON." : $"Editing {row.Type} · {row.Id}";
        OnPropertyChanged(nameof(SelectedDatabaseRecord));
    }

    public void NewDatabaseRecord()
    {
        if (string.IsNullOrWhiteSpace(SelectedDatabaseRecordType)) return;
        var id = Guid.NewGuid().ToString("N");
        object template = SelectedDatabaseRecordType.Split('.').Last() switch
        {
            nameof(LibraryProjectDocument) => new LibraryProjectDocument { Id = id, Name = "New project record", Format = "Unknown", FilePath = "", LastScannedUtc = DateTime.UtcNow, IsManual = true },
            nameof(LibraryPluginDocument) => new LibraryPluginDocument { Id = id, Name = "New plug-in record", Path = "", Format = "Unknown", IsManual = true, LastScannedUtc = DateTime.UtcNow },
            nameof(LibraryCatalogProductDocument) => new LibraryCatalogProductDocument { Id = id, Name = "New catalog record", Manufacturer = "Unknown", Category = "Unknown", Source = "Manual" },
            nameof(LibraryScanFolderDocument) => new LibraryScanFolderDocument { Id = id, Path = "", LastScannedUtc = DateTime.UtcNow },
            nameof(LibraryProjectAssetIndexDocument) => new LibraryProjectAssetIndexDocument { Id = id, ProjectPath = "", IndexedUtc = DateTime.UtcNow },
            _ => throw new InvalidOperationException("This record type cannot be created here.")
        };
        SelectedDatabaseRecord = null;
        RecordJson = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
        RecordEditorStatus = "New unsaved record · set required fields, then choose Create record.";
        OnPropertyChanged(nameof(SelectedDatabaseRecord));
    }

    public void PrepareDatabaseTableQuery(LibraryDatabaseTableInfo? table)
    {
        if (table is null || !DatabaseTables.Any(candidate =>
                candidate.Name.Equals(table.Name, StringComparison.Ordinal))) return;
        var quotedName = table.Name.Replace("\"", "\"\"", StringComparison.Ordinal);
        DatabaseQuery = $"SELECT * FROM \"{quotedName}\" LIMIT 500";
        DatabaseQueryStatus = $"Prepared a read-only preview of table {table.Name}. Run the query to load up to 500 rows.";
    }

    [RelayCommand]
    private async Task SaveDatabaseRecordAsync(bool create)
    {
        if (IsRecordBrowserBusy) return;
        IsRecordBrowserBusy = true;
        try
        {
            if (!create)
            {
                if (SelectedDatabaseRecord is not { } selected)
                    throw new InvalidOperationException("Select an existing record before updating it.");
                using var edited = JsonDocument.Parse(RecordJson);
                var id = edited.RootElement.TryGetProperty("Id", out var idValue) ||
                         edited.RootElement.TryGetProperty("id", out idValue) ? idValue.GetString() : null;
                if (!string.Equals(id, selected.Id, StringComparison.Ordinal))
                    throw new InvalidOperationException("Record IDs cannot be changed in the editor. Create a separate record instead.");
            }
            await store.SaveAdminDatabaseRecordAsync(SelectedDatabaseRecordType, RecordJson, create);
            RecordEditorStatus = create ? "Record created." : "Record updated.";
            await LoadDatabaseRecordsCoreAsync();
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or System.Data.Common.DbException)
        { RecordEditorStatus = "Database record was not saved: " + ex.Message; }
        finally { IsRecordBrowserBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteDatabaseRecordAsync()
    {
        if (IsRecordBrowserBusy || SelectedDatabaseRecord is not { } selected) return;
        IsRecordBrowserBusy = true;
        try
        {
            var removed = await store.DeleteAdminDatabaseRecordAsync(selected.Record.TypeName, selected.Id);
            RecordEditorStatus = removed ? "Record deleted." : "The record was already removed.";
            await LoadDatabaseRecordsCoreAsync();
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Data.Common.DbException)
        { RecordEditorStatus = "Database record was not deleted: " + ex.Message; }
        finally { IsRecordBrowserBusy = false; }
    }

    [RelayCommand]
    private async Task ChangeDatabaseRecordPageAsync(int offset)
    {
        if (IsRecordBrowserBusy) return;
        IsRecordBrowserBusy = true;
        try
        {
            DatabaseRecordPage = Math.Max(0, DatabaseRecordPage + offset);
            await LoadDatabaseRecordsCoreAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Data.Common.DbException)
        { RecordEditorStatus = "Database records could not be read: " + ex.Message; }
        finally { IsRecordBrowserBusy = false; }
    }

    [RelayCommand]
    private async Task RunDatabaseQueryAsync()
    {
        if (IsQueryBusy) return;
        IsQueryBusy = true;
        try
        {
            var result = await store.QueryDatabaseAsync(DatabaseQuery);
            DatabaseQueryColumns = result.Columns;
            DatabaseQueryRows.Clear();
            foreach (var row in result.Rows) DatabaseQueryRows.Add(new LibraryDatabaseQueryRow(string.Join("  │  ", row)));
            DatabaseQueryStatus = $"{result.Rows.Count:N0} result rows" + (result.IsTruncated ? " · capped at 500" : "");
            OnPropertyChanged(nameof(DatabaseQueryColumns));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Data.Common.DbException)
        { DatabaseQueryStatus = "Query failed: " + ex.Message; }
        finally { IsQueryBusy = false; }
    }

    [RelayCommand]
    private async Task SelectExistingDatabaseAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "Choose an existing library database…";
        try
        {
            var path = await picker.PickDatabaseAsync();
            if (path is null)
            {
                Status = "Database selection cancelled.";
                return;
            }
            Status = "Checking the selected database…";
            var validatedPath = await LibraryDatabaseValidator.ValidateAsync(path);
            preferences.SetDatabasePath(validatedPath);
            OnPropertyChanged(nameof(NextLaunchDatabasePath));
            Status = "Existing database selected. Restart the app to use it. Current session: " + DatabasePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or System.Security.SecurityException)
        {
            Status = "Existing database was not selected: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var overview = await store.GetDatabaseOverviewAsync();
            Projects = overview.Projects.ToString("N0");
            Plugins = overview.Plugins.ToString("N0");
            CatalogProducts = overview.CatalogProducts.ToString("N0");
            Folders = overview.ScanFolders.ToString("N0");
            AssetIndexes = overview.ProjectAssetIndexes.ToString("N0");
            DatabaseSize = FormatBytes(overview.DatabaseBytes);
            JournalSize = FormatBytes(overview.JournalBytes);
            OnPropertyChanged(nameof(JournalSize));
            Status = "Database summary loaded. Counts are read from SQLite metadata; stored project metadata and catalog images stay on disk.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        {
            Status = "Database could not be read: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ImportDatabaseAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Status = "Choose a library database to merge…";
            var path = await picker.PickDatabaseAsync();
            if (path is null) { Status = "Database import cancelled."; return; }
            Status = "Validating and merging supported library records…";
            var added = await store.ImportDatabaseAsync(path);
            await RefreshAsync();
            Status = $"Database merged: {added:N0} new records added. Existing records with matching IDs were kept.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or InvalidDataException or System.Data.Common.DbException or System.Security.SecurityException)
        {
            Status = "Database import failed: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CleanDatabaseAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Status = "Removing duplicate project records and orphaned media indexes…";
            var result = await store.CleanDatabaseAsync();
            await RefreshAsync();
            Status = $"Cleanup complete: {result.DuplicateProjectsRemoved:N0} duplicate project records and " +
                $"{result.OrphanedAssetIndexesRemoved:N0} orphaned asset indexes removed. Source files were not changed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException or System.Security.SecurityException)
        {
            Status = "Database cleanup failed: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task OptimizeDatabaseAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Status = "Checking database integrity and compacting SQLite storage…";
            await LibraryDatabaseValidator.ValidateAsync(DatabasePath);
            await store.OptimizeDatabaseAsync();
            await RefreshAsync();
            Status = "Database integrity check passed and storage was compacted.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or System.Data.Common.DbException or System.Security.SecurityException)
        {
            Status = "Database maintenance failed: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824d:N1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576d:N1} MB",
        >= 1_024 => $"{bytes / 1_024d:N1} KB",
        _ => $"{bytes:N0} bytes"
    };

    [RelayCommand]
    private async Task BackupAsync()
    {
        IsBusy = true;
        Status = "Choose where to save the SQLite backup…";
        try
        {
            var path = await picker.PickSaveFileAsync("cubase-library-backup", ".db", "SQLite database backup");
            if (path is null) return;
            Status = "Creating a consistent SQLite backup…";
            await store.BackupAsync(path);
            Status = "Database backup created at " + path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Data.Common.DbException)
        {
            Status = "Database backup failed: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ChangeDatabasePathAsync()
    {
        if (library.IsBusy)
        {
            Status = "Wait for the current project scan or read to finish before moving the library database.";
            return;
        }

        IsBusy = true;
        Status = "Choose the new library database path…";
        try
        {
            var path = await picker.PickSaveFileAsync("cubase-library", ".db", "SQLite library database");
            if (path is null) return;
            await store.CopyDatabaseToNewPathAsync(path);
            try { preferences.SetDatabasePath(Path.GetFullPath(path)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Status = $"Database copy created at {path}, but the preference could not be saved: {ex.Message}";
                return;
            }
            OnPropertyChanged(nameof(NextLaunchDatabasePath));
            await RefreshAsync();
            Status = "Database copied. The new location will be used after restarting the app; the current database remains intact.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Data.Common.DbException or System.Security.SecurityException)
        {
            Status = "Database location was not changed: " + ex.Message;
        }
        finally { IsBusy = false; }
    }
}
