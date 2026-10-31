using System.IO.Compression;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ExtendedInspectionTests
{
    private static async Task<string> Archive(string xml, bool duplicate = false)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dawproject");
        await using var fs = File.Create(path);
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var count = duplicate ? 2 : 1;
            for (var i = 0; i < count; i++)
            {
                await using var output = archive.CreateEntry("project.xml", CompressionLevel.Optimal).Open();
                await output.WriteAsync(Encoding.UTF8.GetBytes(xml));
            }
        }
        return path;
    }

    private static async Task<string> ArchiveWithEntries(int entryCount)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dawproject");
        await using var fs = File.Create(path);
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < entryCount; i++)
            {
                var name = i == 0 ? "project.xml" : $"metadata/{i}.bin";
                await using var output = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
                if (i == 0) await output.WriteAsync("<Project/>"u8.ToArray());
            }
        }
        return path;
    }

    [Fact]
    public async Task Parses_DeviceVendor_Version_Role_Slots_And_Sends()
    {
        const string xml = """
        <Project version="1.0"><Application name="Cubase" version="15"/>
          <Structure>
            <Track id="t" name="Bass" contentType="audio"><Channel id="a" destination="bus">
              <Devices>
                <Vst3Plugin id="fxA" deviceID="class-A" deviceName="Compressor" deviceVendor="ACME"
                  pluginVersion="3.7" deviceRole="audioFX"><Enabled value="false"/></Vst3Plugin>
                <Vst3Plugin id="fxB" deviceID="class-B" deviceName="Delay" deviceRole="audioFX"/>
              </Devices>
              <Sends><Send destination="bus" type="pre"><Enable value="false"/><Volume value="0.42"/></Send></Sends>
            </Channel></Track>
            <Track id="g" name="Group" contentType="audio"><Channel id="bus" role="submix"/></Track>
          </Structure>
        </Project>
        """;
        var path = await Archive(xml);
        try
        {
            var result = await new DawprojectReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var project = result.Value!;
            Assert.Contains(project.Tracks, t => t.Name == "Group" && t.Kind == "Group");
            Assert.Equal(2, project.Plugins.Count);
            Assert.Equal("ACME", project.Plugins[0].Manufacturer);
            Assert.Equal("3.7", project.Plugins[0].Version);
            Assert.Equal("audioFX", project.Plugins[0].Role);
            Assert.Equal(0, project.Plugins[0].Slot);
            Assert.Equal(false, project.Plugins[0].Enabled);
            Assert.Equal(1, project.Plugins[1].Slot);
            Assert.Contains(project.Routing, e => e.Kind == "Send" && e.Level == .42 &&
                e.Enabled == false && e.Tap == "pre");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Rejects_Archive_With_Duplicate_ProjectXml()
    {
        var path = await Archive("<Project/>", true);
        try
        {
            var result = await new DawprojectReader().ReadAsync(path);
            Assert.Equal("DUPLICATE_PROJECT_XML", result.Error?.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Rejects_Archive_Exceeding_Entry_Count_Limit()
    {
        var path = await ArchiveWithEntries(5001);
        try
        {
            var parsed = await new DawprojectReader().ReadAsync(path);
            Assert.False(parsed.IsSuccess);
            Assert.Equal("ARCHIVE_LIMIT", parsed.Error?.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Supports_Default_Namespace_Without_Losing_Channel_Data()
    {
        const string xml = """
        <Project xmlns="urn:dawproject" version="1">
          <Transport><Tempo value="123.5"/></Transport>
          <Structure><Track id="t" name="Synth" contentType="notes">
            <Channel id="c"><Devices><Vst3Plugin deviceID="unique-id" deviceName="Synth"/></Devices></Channel>
          </Track></Structure>
        </Project>
        """;
        var path = await Archive(xml);
        try
        {
            var parsed = await new DawprojectReader().ReadAsync(path);
            Assert.True(parsed.IsSuccess, parsed.Error?.Message);
            Assert.Equal(123.5, parsed.Value!.Bpm);
            Assert.Single(parsed.Value.Plugins);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Rejects_Dtd_Without_Executing_Entities()
    {
        const string xml = "<!DOCTYPE Project [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><Project>&x;</Project>";
        var path = await Archive(xml);
        try
        {
            var parsed = await new ProjectInspector([new DawprojectReader()]).InspectAsync(path);
            Assert.False(parsed.IsSuccess);
            Assert.Equal("INVALID_XML", parsed.Error?.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Rejects_Deeply_Nested_Tracks_Without_Stack_Overflow()
    {
        var xml = "<Project><Structure>" + string.Concat(Enumerable.Repeat("<Track>", 150))
            + string.Concat(Enumerable.Repeat("</Track>", 150)) + "</Structure></Project>";
        var path = await Archive(xml);
        try
        {
            var parsed = await new ProjectInspector([new DawprojectReader()]).InspectAsync(path);
            Assert.False(parsed.IsSuccess);
            Assert.Equal("XML_DEPTH_LIMIT", parsed.Error?.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Rejects_High_Ratio_Xml_Archive()
    {
        var path = await Archive("<Project>" + new string(' ', 120000) + "</Project>");
        try
        {
            var result = await new DawprojectReader().ReadAsync(path);
            Assert.Equal("ARCHIVE_RATIO_LIMIT", result.Error?.Code);
        }
        finally { File.Delete(path); }
    }

    private static ProjectSnapshot Snapshot(IReadOnlyList<TrackInfo> tracks,
        IReadOnlyList<PluginReference>? plugins = null, IReadOnlyList<RoutingEdge>? routes = null,
        EvidenceLevel level = EvidenceLevel.Confirmed) =>
        new("test.dawproject", "Test", "DAWproject", null, 120, level,
            tracks, plugins ?? [], routes ?? [], []);

    [Fact]
    public void Compare_DoesNotSilentlyMatch_AmbiguousDuplicateTracks()
    {
        TrackInfo Old(string id) => new(id, "Bass", "Audio", id, null, null, null, null);
        var before = Snapshot([Old("oldA"), Old("oldB")]);
        var after = Snapshot([Old("newA"), Old("newB")]);
        var result = new ProjectDiff().Compare(before, after);
        Assert.Contains(result, c => c.Category == "Ambiguous");
        Assert.Equal(2, result.Count(c => c.Property == "Added"));
        Assert.Equal(2, result.Count(c => c.Property == "Removed"));
    }

    [Fact]
    public void Compare_Detects_SendLevel_PluginVersion_And_InsertOrder()
    {
        var tracks = new[] { new TrackInfo("t", "Bass", "Audio", "channel", null, 1, false, false) };
        var plugin = new PluginReference("class", "Comp", "Acme", "Vst3Plugin",
            "t", EvidenceLevel.Confirmed, "1", "audioFX", 0);
        var a = Snapshot(tracks, [plugin], [new RoutingEdge("channel", "bus", "Send", .3, true, "pre")]);
        var b = Snapshot(tracks, [plugin with { Version = "2", Slot = 1 }],
            [new RoutingEdge("channel", "bus", "Send", .6, true, "post")]);
        var diff = new ProjectDiff().Compare(a, b);
        Assert.Equal(2, diff.Count(x => x.Category == "Plugin"));
        Assert.Equal(2, diff.Count(x => x.Category == "Routing"));
    }

    [Fact]
    public void Compare_UserMatchPreservesRenamedTrackWithBeforeAfterValues()
    {
        var before = Snapshot([new TrackInfo("track-id", "Old name", "Audio", "bus", "master", .3, false, false)]);
        var after = Snapshot([new TrackInfo("track-id", "New name", "Audio", "bus", "master", .8, true, false)]);
        var changes = new ProjectDiff().Compare(before, after, [new TrackMatchDecision(0, 0)]);
        Assert.Contains(changes, c => c.Property == "Name" && c.Before == "Old name" &&
            c.After == "New name" && c.MatchQuality == "User-specified track match");
        Assert.Contains(changes, c => c.Property == "Mute" && c.Before == "False" && c.After == "True");
        Assert.DoesNotContain(changes, c => c.Property == "Added" || c.Property == "Removed");
    }

    [Fact]
    public void Compare_ReportsPanAndParentTrackChangesForUserMatchedTrack()
    {
        var before = Snapshot([new TrackInfo("track-id", "Bass", "Audio", "bus", null,
            1, false, false, ParentTrackId: "Folder A", Pan: -0.25)]);
        var after = Snapshot([new TrackInfo("track-id", "Bass", "Audio", "bus", null,
            1, false, false, ParentTrackId: "Folder B", Pan: 0.25)]);

        var changes = new ProjectDiff().Compare(before, after, [new TrackMatchDecision(0, 0)]);

        Assert.Contains(changes, change => change.Property == "Pan" && change.Before == "-0.25" && change.After == "0.25");
        Assert.Contains(changes, change => change.Property == "Parent track" &&
            change.Before == "Folder A" && change.After == "Folder B");
        Assert.All(changes, change => Assert.Equal("User-specified track match", change.MatchQuality));
    }

    [Fact]
    public void Compare_LabelsUniqueNameTypeFallbackAsHeuristic()
    {
        var before = Snapshot([new TrackInfo("a", "Bass", "Audio", null, null, null, null, null)]);
        var after = Snapshot([new TrackInfo("b", "Bass", "Audio", null, null, .7, null, null)]);
        var changes = new ProjectDiff().Compare(before, after);
        Assert.Contains(changes, c => c.Property == "Volume" &&
            c.MatchQuality.Contains("heuristic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuditCsvEscapesFormulaAndDoesNotCallFileMtimePluginLastUse()
    {
        var project = Snapshot([], [new PluginReference("id", "=Unsafe", null,
            "VST3", null, EvidenceLevel.Confirmed)]);
        var audit = new PluginUsageAuditService().Analyze(new FolderReport([project], []));
        var csv = new PluginUsageAuditService().ToCsv(audit);
        Assert.Contains("'=Unsafe", csv);
        Assert.Contains("LatestProjectFileModifiedUtc", csv);
        Assert.Contains("InstanceCount", csv);
    }

    [Fact]
    public void Compare_Rejected_Unconfirmed_Preserves_Safety()
    {
        var project = Snapshot([], level: EvidenceLevel.Candidate);
        Assert.Equal("Unavailable", Assert.Single(new ProjectDiff().Compare(project, Snapshot([]))).Category);
    }

    [Fact]
    public void ShortSummary_Labels_UnverifiedCandidateReferences()
    {
        var source = Snapshot([], [new PluginReference("offset", "Unknown plugin", null,
            "Unknown", null, EvidenceLevel.Candidate)], level: EvidenceLevel.Candidate);
        var text = new ReportService().ShortSummary(source);
        Assert.Contains("evidence: Candidate", text);
        Assert.Contains("candidate plugin tokens: 1", text);
    }

    [Fact]
    public void Preflight_GroupsInstancesAndOwnerTracks_WithoutClaimingInstallation()
    {
        var tracks = new[] { new TrackInfo("t1", "Bass", "Audio", "c1", null, null, null, null),
            new TrackInfo("t2", "Drums", "Audio", "c2", null, null, null, null) };
        var p = new PluginReference("class-id", "Comp", "Acme", "Vst3Plugin", "t1", EvidenceLevel.Confirmed);
        var report = new ProjectPreflightService().Analyze(Snapshot(tracks,
            [p, p with { TrackId = "t2" }, p]),
            [new PluginCheck(p, PluginStatus.InstalledByName, @"C:\VST3\Comp.vst3")]);
        var plugin = Assert.Single(report.Plugins);
        Assert.Equal(3, plugin.Instances);
        Assert.Equal(2, plugin.Tracks.Count);
        Assert.Equal(LocalPluginEvidence.FilenameMatchOnly, plugin.LocalEvidence);
        Assert.Contains(report.Warnings, w => w.Contains("not proof", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Preflight_CandidatesNotDependencies_AndNoMatchDoesNotMeanMissing()
    {
        var p = new PluginReference("id", "No match", null, "Vst3Plugin", "t", EvidenceLevel.Confirmed);
        var tool = new ProjectPreflightService();
        Assert.Equal(LocalPluginEvidence.Unverified, Assert.Single(tool.Analyze(Snapshot([], [p]), []).Plugins).LocalEvidence);
        Assert.Equal(LocalPluginEvidence.NotChecked, Assert.Single(tool.Analyze(Snapshot([], [p])).Plugins).LocalEvidence);
        var candidate = tool.Analyze(Snapshot([], [p with { Evidence = EvidenceLevel.Candidate }], level: EvidenceLevel.Candidate));
        Assert.Empty(candidate.Plugins);
        Assert.Equal(1, candidate.UnresolvedCandidateMarkers);
    }

    [Fact]
    public void Preflight_CsvNeutralizesFormulaValues()
    {
        var plugin = new PluginReference("id", "=1+2", null, "Vst3Plugin", null, EvidenceLevel.Confirmed);
        var csv = new ProjectPreflightService().Csv(new ProjectPreflightService().Analyze(Snapshot([], [plugin])));
        Assert.Contains("'=1+2", csv);
    }

    [Fact]
    public void DiffReport_Formats_Markdown_And_Escapes_CsvFormula()
    {
        var changes = new[] { new ProjectChange("Track", "=2+2", "Name", "Bass", "Bass|Lead", "Exact ID") };
        var exporter = new ProjectChangeReportService();
        Assert.Contains("Bass\\|Lead", exporter.Markdown(changes, "a", "b"));
        Assert.Contains("'=2+2", exporter.Csv(changes));
    }
}
