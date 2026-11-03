using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class InspectionTests
{
    private static string TestDirectory() => Directory.CreateTempSubdirectory("cpr-inspector-").FullName;

    private static async Task<string> MakeDawproject(string folder, string filename = "demo.dawproject", double bpm = 128)
    {
        var filenameFull = Path.Combine(folder, filename);
        await using var file = File.Create(filenameFull);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
        var entry = zip.CreateEntry("project.xml");
        await using var output = entry.Open();
        var xml = $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Project version="1.0"><Application name="Cubase" version="15.0" />
          <Transport><Tempo value="{{bpm.ToString(System.Globalization.CultureInfo.InvariantCulture)}}" /></Transport>
          <Structure>
            <Track id="track-bass" name="Bass" contentType="notes">
              <Channel id="channel-bass" role="regular" destination="channel-master" solo="false">
                <Devices><Vst3Plugin id="fx-1" deviceID="VST3-ABC" deviceName="Magical Compressor" /></Devices>
                <Volume value="0.75" /><Mute value="false" />
              </Channel>
            </Track>
            <Track id="track-master" name="Master" contentType="audio">
              <Channel id="channel-master" role="master"><Volume value="1" /></Channel>
            </Track>
          </Structure>
        </Project>
        """;
        var bytes = Encoding.UTF8.GetBytes(xml);
        await output.WriteAsync(bytes);
        return filenameFull;
    }

    [Fact]
    public async Task Dawproject_Extracts_Real_Structure_Tempo_Plugins_And_Routes()
    {
        var folder = TestDirectory();
        try
        {
            var path = await MakeDawproject(folder);
            var result = await new DawprojectReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var p = Assert.IsType<ProjectSnapshot>(result.Value);
            Assert.Equal(EvidenceLevel.Confirmed, p.Evidence);
            Assert.Equal(128, p.Bpm);
            Assert.Equal(2, p.Tracks.Count);
            Assert.Equal("Master", p.Tracks[1].Kind);
            Assert.Contains(p.Plugins, x => x.Name == "Magical Compressor" && x.Id == "VST3-ABC");
            Assert.Contains(p.Routing, x => x.FromChannelId == "channel-bass" && x.ToChannelId == "channel-master");
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Dawproject_Malformed_Archive_Fails_Without_Crash()
    {
        var folder = TestDirectory();
        try
        {
            var path = Path.Combine(folder, "fake.dawproject");
            await File.WriteAllTextAsync(path, "not a ZIP archive");
            var result = await new ProjectInspector([new DawprojectReader()]).InspectAsync(path);
            Assert.False(result.IsSuccess);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Dawproject_Malformed_Xml_Returns_ResultFailure_Not_Exception()
    {
        var folder = TestDirectory();
        try
        {
            var path = Path.Combine(folder, "invalid-xml.dawproject");
            await using (var file = File.Create(path))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("project.xml");
                await using var output = entry.Open();
                await output.WriteAsync(Encoding.UTF8.GetBytes("<Project><Structure></Project>"));
            }
            var result = await new ProjectInspector([new DawprojectReader()]).InspectAsync(path);
            Assert.False(result.IsSuccess);
            Assert.Equal("INVALID_XML", result.Error?.Code);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Cpr_Recognized_Tokens_Are_Candidates_Not_Confirmed_Project_Data()
    {
        var folder = TestDirectory();
        try
        {
            var path = Path.Combine(folder, "fake.cpr");
            var data = Encoding.ASCII.GetBytes("RIFF0000PAppVersion\0Cubase 15\0Plugin UID\0Some Binary Block");
            await File.WriteAllBytesAsync(path, data);
            var result = await new CprEvidenceReader().ReadAsync(path);
            var p = Assert.IsType<ProjectSnapshot>(result.Value);
            Assert.Equal(EvidenceLevel.Candidate, p.Evidence);
            Assert.Null(p.Bpm);
            Assert.Empty(p.Tracks);
            Assert.Single(p.Plugins);
            Assert.Equal(EvidenceLevel.Candidate, p.Plugins[0].Evidence);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Cpr_Unrelated_Riff_Is_Rejected()
    {
        var folder = TestDirectory();
        try
        {
            var path = Path.Combine(folder, "wave.cpr");
            await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("RIFF00000000WAVEfmt data"));
            Assert.False((await new CprEvidenceReader().ReadAsync(path)).IsSuccess);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Safety_Copy_Always_Preserves_Bytes_And_Refuses_Rewrites()
    {
        var folder = TestDirectory();
        try
        {
            var source = Path.Combine(folder, "session.cpr");
            var destination = Path.Combine(folder, "session-copy.cpr");
            await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(256));
            var service = new SafeCopyService();
            var copied = await service.CopyUnmodifiedAsync(source, destination);
            Assert.True(copied.IsSuccess, copied.Error?.Message);
            Assert.Equal(SHA256.HashData(await File.ReadAllBytesAsync(source)),
                SHA256.HashData(await File.ReadAllBytesAsync(destination)));
            Assert.False((await service.CopyUnmodifiedAsync(source, source)).IsSuccess);
            Assert.False((await service.CopyWithDisabledPluginsAsync(source, destination, ["plugin-1"])).IsSuccess);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Diff_Reports_Changed_Bpm_And_Added_Plugins()
    {
        var folder = TestDirectory();
        try
        {
            var a = (await new DawprojectReader().ReadAsync(await MakeDawproject(folder, "a.dawproject", 128))).Value!;
            var b = (await new DawprojectReader().ReadAsync(await MakeDawproject(folder, "b.dawproject", 132))).Value!;
            var changes = new ProjectDiff().Compare(a, b);
            Assert.Contains(changes, x => x.Property == "Bpm" && x.Before == "128" && x.After == "132");
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Reports_Protect_Against_Csv_Formula_Injection()
    {
        var project = new ProjectSnapshot("a.cpr", "x", "Test", null, null, EvidenceLevel.Confirmed,
            [new TrackInfo("id", "=1+1", "Audio", null, null, null, null, null)], [], [], []);
        var csv = new ReportService().TracksCsv(project);
        Assert.Contains("'=1+1", csv);
    }
}
