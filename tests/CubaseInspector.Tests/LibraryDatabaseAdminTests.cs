using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class LibraryDatabaseAdminTests
{
    [Fact]
    public async Task BrowsesQueriesCreatesUpdatesAndDeletesSupportedRecords()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpe-db-admin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "library.db");
        try
        {
            using var store = new LibraryDocumentStore(database);
            await store.SaveProjectAsync(new LibraryProjectDocument
            {
                Id = "project-1", Name = "Initial", Format = "CPR", FilePath = "", IsManual = true
            });

            var tables = await store.GetDatabaseTablesAsync();
            var documents = Assert.Single(tables);
            Assert.Equal("documents", documents.Name);
            Assert.Contains("Data", documents.Columns);
            Assert.Equal(1, documents.RowCount);

            var projectType = Assert.Single(await store.GetDatabaseRecordTypesAsync(),
                type => type.EndsWith(nameof(LibraryProjectDocument), StringComparison.Ordinal));
            var rows = await store.GetDatabaseRecordsAsync(projectType, "Initial");
            var row = Assert.Single(rows);
            var updated = row.Data.Replace("Initial", "Updated", StringComparison.Ordinal);
            await store.SaveAdminDatabaseRecordAsync(projectType, updated, create: false);
            Assert.Equal("Updated", (await store.GetProjectAsync("project-1"))!.Name);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.SaveAdminDatabaseRecordAsync(projectType, updated, create: true));
            var query = await store.QueryDatabaseAsync("SELECT TypeName, COUNT(*) AS Records FROM documents GROUP BY TypeName");
            Assert.Single(query.Rows);
            await Assert.ThrowsAsync<ArgumentException>(() => store.QueryDatabaseAsync("DELETE FROM documents"));

            Assert.True(await store.DeleteAdminDatabaseRecordAsync(projectType, "project-1"));
            Assert.Null(await store.GetProjectAsync("project-1"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
