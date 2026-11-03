using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class LibraryDatabaseValidatorTests
{
    [Fact]
    public async Task ExistingLibraryValidatesWithoutChangingEitherDatabaseAndCanBeReopened()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cpe-existing-db-" + Guid.NewGuid().ToString("N"));
        var currentPath = Path.Combine(directory, "current.db");
        var selectedPath = Path.Combine(directory, "selected.db");
        try
        {
            using var current = new LibraryDocumentStore(currentPath);
            using (var selected = new LibraryDocumentStore(selectedPath))
            {
                var project = new ProjectSnapshot("example.dawproject", "Stored example", "DAWproject", "15", 120,
                    EvidenceLevel.Confirmed, [], [], [], []);
                await selected.SaveProjectInspectionAsync(project);
            }
            var bytes = await File.ReadAllBytesAsync(selectedPath);

            var validated = await LibraryDatabaseValidator.ValidateAsync(selectedPath);

            Assert.Equal(Path.GetFullPath(selectedPath), validated);
            Assert.Equal(Path.GetFullPath(currentPath), current.DatabasePath);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(selectedPath));
            using var reopened = new LibraryDocumentStore(validated);
            Assert.Equal("Stored example", Assert.Single(await reopened.GetProjectsAsync()).Name);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("text")]
    [InlineData("unrelated")]
    [InlineData("wrong-columns")]
    public async Task RejectsIncompatibleFileWithoutChangingIt(string kind)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cpe-existing-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "selected.db");
        try
        {
            if (kind is "empty" or "text") await File.WriteAllTextAsync(path, kind == "empty" ? "" : "Not a database");
            else
            {
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = kind == "unrelated" ? "CREATE TABLE unrelated (Id TEXT);" : "CREATE TABLE documents (Id TEXT, Data TEXT);";
                command.ExecuteNonQuery();
            }
            var bytes = await File.ReadAllBytesAsync(path);
            await Assert.ThrowsAsync<InvalidDataException>(() => LibraryDatabaseValidator.ValidateAsync(path));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task MissingDatabaseIsRejectedWithoutCreatingIt()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db");
        await Assert.ThrowsAsync<FileNotFoundException>(() => LibraryDatabaseValidator.ValidateAsync(path));
        Assert.False(File.Exists(path));
    }
}
