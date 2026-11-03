using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprBackupTests
{
    private static byte[] Cpr(bool truncate = false, byte[]? trailing = null)
    {
        // Big-endian payload size for a RIFF/NUND containing an empty ARCH chunk.
        byte[] result = [
            (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 8,
            (byte)'N', (byte)'U', (byte)'N', (byte)'D',
            (byte)'A', (byte)'R', (byte)'C', (byte)'H', 0, 0, 0, 0
        ];
        if (truncate)
        {
            // Advertise 16 bytes of chunks, but only provide 8.
            result[7] = 16;
        }
        return trailing is null ? result : result.Concat(trailing).ToArray();
    }

    private static byte[] PluginToken()
    {
        static byte[] Token(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value + "\0");
            return [(byte)bytes.Length, .. bytes];
        }
        return [.. "Plugin UID\0"u8.ToArray(), .. new byte[22], .. Token("56535473796C3173796C656E74683100"),
            .. new byte[3], .. Token("Plugin Name"), .. new byte[5], .. Token("Sylenth1")];
    }

    [Fact]
    public async Task BAK_WithCubaseHeader_OpensAsReadOnlyCandidate()
    {
        var dir = Directory.CreateTempSubdirectory("cpr-backup-test-");
        try
        {
            var backup = Path.Combine(dir.FullName, "autosave_2019.bak");
            await File.WriteAllBytesAsync(backup, Cpr());
            var reader = new CprEvidenceReader();
            Assert.True(reader.CanRead(backup));
            var inspected = await new ProjectInspector([reader]).InspectAsync(backup);
            Assert.True(inspected.IsSuccess, inspected.Error?.Message);
            Assert.Equal(EvidenceLevel.Candidate, inspected.Value!.Evidence);
            Assert.Contains(inspected.Value.Warnings, w => w.Contains("backup", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(backup, inspected.Value.FilePath);
            var scan = await new FolderScanner(new ProjectInspector([reader])).ScanAsync(dir.FullName);
            Assert.True(scan.IsSuccess, scan.Error?.Message);
            Assert.Single(scan.Value!.Projects);
            Assert.Empty(scan.Value.Failures);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task UnrelatedBAK_IsNotRecognized_OrIncludedInFolderScan()
    {
        var dir = Directory.CreateTempSubdirectory("cpr-backup-test-");
        try
        {
            var backup = Path.Combine(dir.FullName, "database.bak");
            await File.WriteAllTextAsync(backup, "RIFF...WAVE data, not a Cubase project");
            var reader = new CprEvidenceReader();
            Assert.False(reader.CanRead(backup));
            var inspected = await new ProjectInspector([reader]).InspectAsync(backup);
            Assert.Equal("UNSUPPORTED", inspected.Error?.Code);
            var scan = await new FolderScanner(new ProjectInspector([reader])).ScanAsync(dir.FullName);
            Assert.Empty(scan.Value!.Projects);
            Assert.Empty(scan.Value.Failures);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task UnrelatedBackupIsExplicitlyCountedAsSkipped()
    {
        var directory = Directory.CreateTempSubdirectory("cpr-other-backups-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "app.bak"), "not cubase");
            var report = await new FolderScanner(
                new ProjectInspector([new CprEvidenceReader()])).ScanAsync(directory.FullName);
            Assert.True(report.IsSuccess);
            Assert.Empty(report.Value!.Projects);
            Assert.Equal(1, report.Value.SkippedUnrelatedBackups);
            Assert.Equal(0, report.Value.Discovered);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task IncompleteCrashBAK_ReadsPartialEvidenceAndReportsStructuralError()
    {
        var dir = Directory.CreateTempSubdirectory("cpr-backup-test-");
        try
        {
            var backup = Path.Combine(dir.FullName, "crash_2018.bak");
            await File.WriteAllBytesAsync(backup, Cpr(truncate: true, trailing: PluginToken()));
            Assert.True(new CprEvidenceReader().CanRead(backup));
            var structural = await new CprStructureInspector().InspectAsync(backup);
            Assert.True(new[] { "TRUNCATED_CPR", "TRUNCATED_CHUNK" }.Contains(structural.Error?.Code));
            var snapshot = await new ProjectInspector([new CprEvidenceReader()]).InspectAsync(backup);
            Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
            Assert.Equal(EvidenceLevel.Candidate, snapshot.Value!.Evidence);
            Assert.Contains(snapshot.Value.Warnings, warning =>
                warning.Contains("structural read error [" + structural.Error!.Code + "]", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(snapshot.Value.Warnings, warning =>
                warning.Contains("not a recovered project", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(snapshot.Value.Plugins, plugin => plugin.Name == "Sylenth1");
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task HeaderlessCprWithReadablePluginPropertiesReturnsPartialReadAndError()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(file, PluginToken());

            var parsed = await new ProjectInspector([new CprEvidenceReader()]).InspectAsync(file);

            Assert.True(parsed.IsSuccess, parsed.Error?.Message);
            Assert.Contains(parsed.Value!.Plugins, plugin => plugin.Name == "Sylenth1");
            Assert.Contains(parsed.Value.Warnings, warning => warning.Contains("NOT_CPR_CONTAINER", StringComparison.Ordinal));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task BAK_Copy_PreservesEveryByte()
    {
        var dir = Directory.CreateTempSubdirectory("cpr-backup-test-");
        try
        {
            var backup = Path.Combine(dir.FullName, "crash.bak");
            var output = Path.Combine(dir.FullName, "recovery-copy.cpr");
            await File.WriteAllBytesAsync(backup, Cpr());
            var copy = await new SafeCopyService().CopyUnmodifiedAsync(backup, output);
            Assert.True(copy.IsSuccess, copy.Error?.Message);
            Assert.Equal(await File.ReadAllBytesAsync(backup), await File.ReadAllBytesAsync(output));
        }
        finally { dir.Delete(true); }
    }
}
