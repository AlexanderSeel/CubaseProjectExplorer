using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class LibraryDocumentStoreTests
{
    [Fact]
    public async Task DatabaseOverviewCountsRecordsWithoutChangingTheStoredDocuments()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-db-overview-");
        var database = Path.Combine(directory.FullName, "library.db");
        try
        {
            using var store = new LibraryDocumentStore(database);
            await store.SaveScanAsync("C:/Sessions", new FolderReport([
                new ProjectSnapshot("C:/Sessions/song.dawproject", "song", "DAWproject", "15", 120,
                    EvidenceLevel.Confirmed, [], [], [], [])], [], false, 1, 1));
            await store.SavePluginScanAsync([new InstalledPlugin("Synth", "C:/VST3/Synth.vst3", "VST3", [])]);
            await store.SaveCatalogProductsAsync([new PluginMetadataEntry("EQ", "Vendor", "Effect", null,
                null, null, null, null, null, [], [], [], "test")]);

            var overview = await store.GetDatabaseOverviewAsync();

            Assert.Equal(1, overview.Projects);
            Assert.Equal(1, overview.Plugins);
            Assert.Equal(1, overview.CatalogProducts);
            Assert.Equal(1, overview.ScanFolders);
            Assert.Equal(1, overview.ProjectAssetIndexes);
            Assert.True(overview.DatabaseBytes > 0);
            Assert.Single(await store.GetProjectsAsync());
            await store.OptimizeDatabaseAsync();
            Assert.Equal(1, (await store.GetDatabaseOverviewAsync()).Projects);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task LibraryProjectionReadsFullDocumentsIndividuallyAndRetainsBrowserEvidence()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-library-projection-");
        var database = Path.Combine(directory.FullName, "library.db");
        try
        {
            using var store = new LibraryDocumentStore(database);
            var project = new ProjectSnapshot("C:/Sessions/song.cpr", "song", "CPR candidate", "Cubase 14", null,
                EvidenceLevel.Candidate,
                [new TrackInfo("track", "Bass", "Instrument", null, null, null, null, null)],
                [new PluginReference("uid", "Bass Engine", null, "VST3", "track", EvidenceLevel.Candidate)],
                [], ["Native structure is partial"], SourceSha256: new string('B', 64));
            await store.SaveScanAsync("C:/Sessions", new FolderReport([project], [], false, 1, 1));

            var summary = Assert.Single(await store.GetProjectLibraryEntriesAsync());
            var persisted = Assert.Single(await store.GetProjectsAsync());

            Assert.True(summary.IsSummary);
            Assert.Equal(1, summary.TrackRecordCount);
            Assert.Equal(1, summary.PluginReferenceCount);
            Assert.Equal(1, summary.ParserWarningCount);
            Assert.Contains(summary.SearchablePlugins!, plugin => plugin.Name == "Bass Engine");
            Assert.Single(persisted.Snapshot!.Tracks);
            Assert.Single(persisted.Snapshot.Plugins);
            Assert.Single(persisted.Snapshot.Warnings);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task LibraryProjectionDoesNotHydrateTypesDisabledInScanSettings()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-library-type-filter-");
        try
        {
            using var store = new LibraryDocumentStore(Path.Combine(directory.FullName, "library.db"));
            await store.SaveScanAsync("C:/Sessions", new FolderReport([
                new ProjectSnapshot("C:/Sessions/song.cpr", "song", "CPR candidate", null, null,
                    EvidenceLevel.Candidate, [], [], [], []),
                new ProjectSnapshot("C:/Sessions/export.dawproject", "export", "DAWproject", null, null,
                    EvidenceLevel.Confirmed, [], [], [], [])
            ], [], false, 2, 2));

            var enabled = await store.GetProjectLibraryEntriesAsync(
                projectTypes: new ProjectScanTypes(Cpr: true, Bak: true, Dawproject: false));
            var all = await store.GetProjectLibraryEntriesAsync();

            Assert.Equal("song.cpr", Path.GetFileName(Assert.Single(enabled).Project.FilePath));
            Assert.Equal(2, all.Count);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task LargeScanUsesSmallWriteBatchesAndTruncatesItsWalWhenIdle()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-scan-wal-");
        var database = Path.Combine(directory.FullName, "library.db");
        try
        {
            using var store = new LibraryDocumentStore(database);
            var projects = Enumerable.Range(0, 70).Select(index => new ProjectSnapshot(
                $"C:/Sessions/song-{index}.cpr", $"song-{index}", "CPR candidate", null, null,
                EvidenceLevel.Candidate, [], [], [], [])).ToArray();

            await store.SaveScanAsync("C:/Sessions", new FolderReport(projects, [], false, projects.Length, projects.Length));

            Assert.Equal(projects.Length, (await store.GetDatabaseOverviewAsync()).Projects);
            Assert.True(new FileInfo(database + "-wal").Length < 1024 * 1024);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task CompactScanPersistenceKeepsBrowserCountsAndOmitsFullRoutingDetails()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-compact-persist-");
        try
        {
            using var store = new LibraryDocumentStore(Path.Combine(directory.FullName, "library.db"));
            var full = new ProjectSnapshot("C:/Sessions/song.cpr", "song", "CPR candidate", null, null,
                EvidenceLevel.Candidate,
                [new TrackInfo("track", "Bass", "Audio", null, null, null, null, null)],
                [new PluginReference("uid", "Bass Engine", null, "VST3", "track", EvidenceLevel.Candidate)],
                [new RoutingEdge("channel", "output", "Output")], ["candidate warning"]);
            var entry = ProjectLibraryBrowser.ToSummary(ProjectLibraryBrowser.FromFile(full));
            var compact = full with { Tracks = [], Routing = [], Warnings = [] };
            var assets = ProjectAssetIndexer.Build(full);
            var report = new FolderReport([compact], [], false, 1, 1, 0, [entry], [assets]);

            await store.SaveScanAsync("C:/Sessions", report);

            var browser = Assert.Single(await store.GetProjectLibraryEntriesAsync());
            var persisted = Assert.Single(await store.GetProjectsAsync());
            Assert.Equal(1, browser.TrackRecordCount);
            Assert.Equal(1, browser.RoutingCount);
            Assert.Empty(persisted.Snapshot!.Routing);
            Assert.Empty(persisted.Snapshot.Tracks);
            Assert.Single(persisted.Snapshot.Plugins);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task LibraryPluginUsageStreamsPersistedProjectsAndKeepsEvidenceCounts()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-library-plugin-usage-");
        try
        {
            using var store = new LibraryDocumentStore(Path.Combine(directory.FullName, "library.db"));
            var confirmed = new ProjectSnapshot("C:/Sessions/confirmed.dawproject", "Confirmed", "DAWproject", "15", null,
                EvidenceLevel.Confirmed, [],
                [new("local-1", "Synth", "Vendor", "VST3", null, EvidenceLevel.Confirmed),
                 new("local-2", "Synth", "Vendor", "VST3", null, EvidenceLevel.Confirmed)], [], []);
            var candidate = new ProjectSnapshot("C:/Sessions/candidate.cpr", "Candidate", "CPR candidate", "14", null,
                EvidenceLevel.Candidate, [],
                [new("offset-1", "Possible Synth", null, "VST3", null, EvidenceLevel.Candidate),
                 new("offset-1", "Possible Synth", null, "VST3", null, EvidenceLevel.Candidate),
                 new("", "No token", null, "VST3", null, EvidenceLevel.Candidate),
                 new("unknown", "Unknown", null, "VST3", null, EvidenceLevel.Unknown)], [], []);
            await store.SaveScanAsync("C:/Sessions", new FolderReport([confirmed, candidate], [], false, 2, 2));

            var index = await store.GetLibraryPluginUsageIndexAsync();

            Assert.Equal(2, index.StoredProjectCount);
            Assert.Equal(1, index.ConfirmedProjectCount);
            Assert.Equal(3, index.CandidateReferenceCount);
            Assert.Equal(1, index.CandidateWithoutTokenCount);
            Assert.Equal(1, index.UnknownReferenceCount);
            var plugin = Assert.Single(index.ConfirmedReferences);
            Assert.Equal(2, plugin.ReferenceCount);
            Assert.Equal(2, plugin.DistinctExportedIds);
            var marker = Assert.Single(index.CandidateMarkers);
            Assert.Equal(2, marker.OccurrenceCount);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task ScanningAnotherFolderDoesNotRewriteStoredFullSnapshotsAsHeaders()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-scan-header-preservation-");
        var database = Path.Combine(directory.FullName, "library.db");
        try
        {
            using var store = new LibraryDocumentStore(database);
            var original = new ProjectSnapshot("C:/One/full.cpr", "Full", "CPR candidate", "Cubase 14", null,
                EvidenceLevel.Candidate,
                [new TrackInfo("track", "Bass", "Instrument", null, null, null, null, null)],
                [new PluginReference("uid", "Bass Engine", null, "VST3", "track", EvidenceLevel.Candidate)],
                [], ["Keep the source warning"], SourceSha256: new string('C', 64));
            await store.SaveScanAsync("C:/One", new FolderReport([original], [], false, 1, 1));
            var added = new ProjectSnapshot("C:/Two/new.dawproject", "New", "DAWproject", "1", null,
                EvidenceLevel.Confirmed, [], [], [], [], SourceSha256: new string('D', 64));

            await store.SaveScanAsync("C:/Two", new FolderReport([added], [], false, 1, 1));

            var stored = (await store.GetProjectsAsync()).Single(project => project.Name == "Full").Snapshot!;
            Assert.Single(stored.Tracks);
            Assert.Single(stored.Plugins);
            Assert.Equal("Keep the source warning", Assert.Single(stored.Warnings));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task ImportMergesNewRecordsAndPreservesMatchingTargetIds()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-db-import-");
        var sourcePath = Path.Combine(directory.FullName, "source.db");
        var targetPath = Path.Combine(directory.FullName, "target.db");
        try
        {
            using (var source = new LibraryDocumentStore(sourcePath))
            {
                await source.SaveScanAsync("C:/Sessions", new FolderReport([
                    new ProjectSnapshot("C:/Sessions/one.dawproject", "One", "DAWproject", "15", null,
                        EvidenceLevel.Unknown, [], [], [], []),
                    new ProjectSnapshot("C:/Sessions/two.dawproject", "Two", "DAWproject", "15", null,
                        EvidenceLevel.Unknown, [], [], [], [])], [], false, 2, 2));
            }
            using var target = new LibraryDocumentStore(targetPath);
            var existing = new LibraryProjectDocument
            {
                Id = "kept", FilePath = "C:/existing.cpr", Name = "Curated", IsManual = true
            };
            await target.SaveProjectAsync(existing);

            var added = await target.ImportDatabaseAsync(sourcePath);

            Assert.Equal(5, added); // Two projects, their indexes, and one scan-folder record.
            Assert.Equal(3, (await target.GetDatabaseOverviewAsync()).Projects);
            Assert.Equal("Curated", (await target.GetProjectAsync("kept"))!.Name);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task ScanPersistsSearchableMediaAndMidiIndexesAndRebuildsThemFromStoredSnapshots()
    {
        var database = Path.Combine(Path.GetTempPath(), "cpe-assets-" + Guid.NewGuid().ToString("N"), "library.db");
        try
        {
            using var store = new LibraryDocumentStore(database);
            var project = new ProjectSnapshot("C:/Sessions/song.dawproject", "song", "DAWproject", "15", 120,
                EvidenceLevel.Confirmed,
                [new TrackInfo("audio", "Drums", "Audio", null, null, null, null, null),
                 new TrackInfo("midi", "Bass", "MIDI", null, null, null, null, null)], [], [], [],
                MediaReferences: [new ProjectMediaReference("Samples/kick.wav", false, 2, 48000, 1.2, "audio")],
                MidiNotes: [new ProjectMidiNote("midi", 0, .5, 0, 36, .9, .4, "beats")]);

            await store.SaveScanAsync("C:/Sessions", new FolderReport([project], [], false, 1, 1));
            var index = Assert.Single(await store.GetProjectAssetIndexesAsync());
            Assert.Equal("WAV", Assert.Single(index.Index!.MediaUses).FileType);
            Assert.Equal("Drums", index.Index.MediaUses[0].TrackName);
            Assert.Equal(1, Assert.Single(index.Index.MidiTracks).NoteCount);

            Assert.Equal(1, await store.RebuildProjectAssetIndexesAsync());
            Assert.Single(await store.GetProjectAssetIndexesAsync());
            var storedProject = Assert.Single(await store.GetProjectsAsync());
            Assert.True(await store.DeleteProjectAsync(storedProject.Id));
            Assert.Empty(await store.GetProjectAssetIndexesAsync());
        }
        finally
        {
            var directory = Path.GetDirectoryName(database)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AssetIndexRebuildPagesThroughLibrariesLargerThanOneBatch()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-assets-batches-");
        try
        {
            using var store = new LibraryDocumentStore(Path.Combine(directory.FullName, "library.db"));
            var projects = Enumerable.Range(0, 130).Select(index => new ProjectSnapshot(
                $"C:/Sessions/song-{index:D3}.dawproject", $"song-{index:D3}", "DAWproject", "15", 120,
                EvidenceLevel.Confirmed, [], [], [], [])).ToArray();
            await store.SaveScanAsync("C:/Sessions", new FolderReport(projects, [], false, projects.Length, projects.Length));
            foreach (var assetIndex in await store.GetProjectAssetIndexesAsync())
                await store.DeleteAdminDatabaseRecordAsync(nameof(LibraryProjectAssetIndexDocument), assetIndex.Id);

            var rebuilt = await store.RebuildProjectAssetIndexesAsync();

            Assert.Equal(130, rebuilt);
            Assert.Equal(130, (await store.GetProjectAssetIndexesAsync()).Count);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task CopyingDatabaseToNewPathPreservesIndexAndLeavesCurrentLocationActive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cpe-move-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(directory, "source.db");
        var destination = Path.Combine(directory, "moved", "library.db");
        Directory.CreateDirectory(directory);
        try
        {
            using (var store = new LibraryDocumentStore(source))
            {
                var project = new ProjectSnapshot("C:/sessions/song.cpr", "song", "CPR", "14", null,
                    EvidenceLevel.Unknown, [], [], [], []);
                await store.SaveScanAsync("C:/sessions", new FolderReport([project], [], false, 1, 1));

                await store.CopyDatabaseToNewPathAsync(destination);

                Assert.Equal(Path.GetFullPath(source), store.DatabasePath);
                Assert.True(File.Exists(source));
                Assert.Single(await store.GetProjectsAsync());
                Assert.True(new FileInfo(destination).Length > 0);
                using (var copiedStore = new LibraryDocumentStore(destination))
                    Assert.Equal("song", Assert.Single(await copiedStore.GetProjectsAsync()).Name);
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CopyingDatabaseToNewPathRejectsExistingNonemptyDestination()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cpe-move-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(directory, "source.db");
        var destination = Path.Combine(directory, "destination.db");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(destination, [1, 2, 3]);
        try
        {
            using var store = new LibraryDocumentStore(source);
            await Assert.ThrowsAsync<IOException>(() => store.CopyDatabaseToNewPathAsync(destination));
            Assert.Equal(Path.GetFullPath(source), store.DatabasePath);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rescans_UpsertProjectAndPluginByPath_AndCrudPreservesEditableMetadata()
    {
        var database = Path.Combine(Path.GetTempPath(), "cpe-" + Guid.NewGuid().ToString("N"), "library.db");
        try
        {
            using (var store = new LibraryDocumentStore(database))
            {
                var project = new ProjectSnapshot("C:/sessions/song.cpr", "song", "CPR", "13", null,
                    EvidenceLevel.Unknown, [], [], [], []);
                var report = new FolderReport([project], [], false, 1, 1);
                await store.SaveScanAsync("C:/sessions", report);
                await store.SaveScanAsync("C:/sessions", report);
                var storedProject = Assert.Single(await store.GetProjectsAsync());
                Assert.Equal("13", storedProject.Snapshot?.CreatorVersion);
                Assert.Equal("C:/sessions", Assert.Single(await store.GetScanFoldersAsync()).Path);

                var plugin = new InstalledPlugin("Synth", "C:/VST3/Synth.vst3", "VST3",
                    [new InstalledPluginClass("0123456789abcdef0123456789abcdef", "Synth", "Vendor", "1.0", "Instrument")]);
                await store.SavePluginScanAsync([plugin]);
                await store.SavePluginScanAsync([plugin]);
                var plugins = await store.GetPluginsAsync();
                var saved = Assert.Single(plugins);
                saved.Description = "Manually reviewed";
                saved.IsManual = true;
                saved.Name = "Curated synth name";
                await store.SavePluginAsync(saved);
                Assert.Equal("Manually reviewed", Assert.Single(await store.GetPluginsAsync()).Description);
                await store.SavePluginScanAsync([plugin]);
                var restoredPlugin = Assert.Single(await store.GetPluginsAsync());
                Assert.Equal("Curated synth name", restoredPlugin.Name);
                Assert.Equal("0123456789abcdef0123456789abcdef", Assert.Single(restoredPlugin.Classes!).Cid);
                Assert.True(await store.DeletePluginAsync(saved.Id));
                Assert.Empty(await store.GetPluginsAsync());
            }
        }
        finally
        {
            var directory = Path.GetDirectoryName(database)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RescanAtDifferentPath_DeduplicatesByChecksumAndReplacesMissingCanonicalCopy()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-rescan-hash-");
        var database = Path.Combine(directory.FullName, "library.db");
        var originalPath = Path.Combine(directory.FullName, "old", "session.cpr");
        var movedPath = Path.Combine(directory.FullName, "new", "session-copy.cpr");
        Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
        var content = "same project file content"u8.ToArray();
        await File.WriteAllBytesAsync(originalPath, content);
        await File.WriteAllBytesAsync(movedPath, content);
        try
        {
            using var store = new LibraryDocumentStore(database);
            static ProjectSnapshot Project(string path) => new(path, "session", "CPR", "15", null,
                EvidenceLevel.Candidate, [], [], [], []);

            await store.SaveScanAsync(Path.GetDirectoryName(originalPath)!,
                new FolderReport([Project(originalPath)], [], false, 1, 1));
            var first = Assert.Single(await store.GetProjectsAsync());
            Assert.NotNull(first.SourceSha256);
            Assert.Equal(first.SourceSha256, first.Snapshot?.SourceSha256);

            await store.SaveScanAsync(Path.GetDirectoryName(movedPath)!,
                new FolderReport([Project(movedPath)], [], false, 1, 1));
            var whileOriginalExists = Assert.Single(await store.GetProjectsAsync());
            Assert.Equal(originalPath, whileOriginalExists.FilePath);

            File.Delete(originalPath);
            await store.SaveScanAsync(Path.GetDirectoryName(movedPath)!,
                new FolderReport([Project(movedPath)], [], false, 1, 1));
            var afterMove = Assert.Single(await store.GetProjectsAsync());
            Assert.Equal(movedPath, afterMove.FilePath);
            Assert.Single(await store.GetProjectAssetIndexesAsync());
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task CatalogImportsArePersistedBySourceAndRepeatedImportsUpdateRatherThanDuplicate()
    {
        var database = Path.Combine(Path.GetTempPath(), "cpe-catalog-" + Guid.NewGuid().ToString("N"), "library.db");
        try
        {
            using var store = new LibraryDocumentStore(database);
            var product = new PluginMetadataEntry("Pro-Q 4", "FabFilter", "EQ", null,
                "Description", "https://example.test/product", null, null, null,
                ["VST3"], ["EQ"], ["Pro Q"], "PluginDB CC0 catalog");
            await store.SaveCatalogProductsAsync([product]);
            await store.SaveCatalogProductsAsync([product with { Description = "Updated description" }]);
            var saved = Assert.Single(await store.GetCatalogProductsAsync());
            Assert.Equal(product.Name, saved.Name);
            Assert.Equal("Updated description", saved.Description);
            Assert.Equal("PluginDB CC0 catalog", saved.Source);
        }
        finally
        {
            var directory = Path.GetDirectoryName(database)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CatalogImportsAccumulateAcrossSourcesAndKeepPreviouslyDownloadedImages()
    {
        var database = Path.Combine(Path.GetTempPath(), "cpe-catalog-merge-" + Guid.NewGuid().ToString("N"), "library.db");
        try
        {
            using var store = new LibraryDocumentStore(database);
            var pluginDb = new PluginMetadataEntry("Glass Synth", "North Star", "Instrument", null,
                "Description", "https://example.test/product", "https://cdn.example.test/glass.png", null, null,
                ["VST3"], [], [], "PluginDB CC0 catalog", [1, 2, 3], "image/png");
            var directory = pluginDb with { Source = "VST3s public database", ImageUrl = null, ImageBytes = null, ImageMediaType = null };

            await store.SaveCatalogProductsAsync([pluginDb]);
            await store.SaveCatalogProductsAsync([pluginDb with
            {
                Description = null, Website = null, Formats = [], Aliases = [],
                ImageUrl = null, ImageBytes = null, ImageMediaType = null
            }]);
            await store.SaveCatalogProductsAsync([directory]);

            var saved = await store.GetCatalogProductsAsync();
            Assert.Equal(2, saved.Count);
            Assert.Contains(saved, item => item.Source == "PluginDB CC0 catalog" &&
                item.ImageBytes is { Length: 3 } bytes && bytes[0] == 1 && bytes[1] == 2 && bytes[2] == 3 &&
                item.ImageMediaType == "image/png" && item.Description == "Description" &&
                item.Website == "https://example.test/product" && item.Formats.Contains("VST3"));
            Assert.Contains(saved, item => item.Source == "VST3s public database");
        }
        finally
        {
            var directory = Path.GetDirectoryName(database)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ManualRecords_RejectDuplicatePathsAndNonHttpsSources()
    {
        var database = Path.Combine(Path.GetTempPath(), "cpe-" + Guid.NewGuid().ToString("N"), "library.db");
        try
        {
            using var store = new LibraryDocumentStore(database);
            var project = new LibraryProjectDocument { Id = "p1", Name = "One", FilePath = "C:/song.cpr" };
            await store.SaveProjectAsync(project);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveProjectAsync(
                new LibraryProjectDocument { Id = "p2", Name = "Two", FilePath = "c:/SONG.cpr" }));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SavePluginAsync(
                new LibraryPluginDocument { Id = "v1", Name = "Plugin", SourceUrl = "http://example.test/plugin" }));
        }
        finally
        {
            var directory = Path.GetDirectoryName(database)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

}
