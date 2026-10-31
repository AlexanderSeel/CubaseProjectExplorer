using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class DawprojectExtendedFieldsTests
{
    [Fact]
    public async Task ReadsEqBandsWarpCoordinatesAndInheritedNoteTrackWithProvenance()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dawproject");
        try
        {
            await using (var output = File.Create(path))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                await AddEntry(archive, "project.xml", """
                    <Project><Transport><Tempo value="120"/></Transport><Structure>
                      <Track id="track" contentType="notes"><Channel id="channel"><Devices>
                        <Equalizer id="eq" deviceName="EQ"><Band type="lowShelf" order="2">
                          <Freq value="100" unit="hertz"/><Gain value="0" unit="decibel"/>
                          <Q value="1" unit="linear"/><Enabled value="false"/>
                        </Band><Band type="bell"><Freq value="NaN" unit="hertz"/><Gain value="bad"/></Band></Equalizer>
                      </Devices></Channel></Track></Structure>
                      <Arrangement><Lanes track="track" timeUnit="seconds"><Clips><Clip time="0">
                        <Warps id="warp" contentTimeUnit="beats"><Warp time="1.5" contentTime="2"/>
                          <Warp time="bad" contentTime="3"/></Warps>
                        <Notes><Note time="0" duration="1" channel="0" key="60" vel="0"/></Notes>
                      </Clip></Clips></Lanes></Arrangement></Project>
                    """);
            var result = await new DawprojectReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var project = result.Value!;
            Assert.Equal(2, project.EqBands!.Count);
            var band = project.EqBands[0];
            Assert.Equal(("track", "eq", 0, "lowShelf", 2), (band.TrackId, band.DeviceInstanceId, band.BandIndex, band.Type, band.Order));
            Assert.Equal(100d, band.Frequency);
            Assert.Equal(0d, band.Gain);
            Assert.False(band.Enabled);
            Assert.Equal("hertz", band.FrequencyUnit);
            Assert.Contains("project.xml/Structure/Channel[channel]", band.SourcePath);
            Assert.Null(project.EqBands[1].Frequency);
            var warp = Assert.Single(project.WarpPoints!);
            Assert.Equal(("track", "warp", 1.5, 2d, "seconds", "beats"),
                (warp.TrackId, warp.WarpId, warp.Time, warp.ContentTime, warp.TimeUnit, warp.ContentTimeUnit));
            Assert.Equal("track", Assert.Single(project.MidiNotes!).TrackId);
            Assert.Contains(project.Warnings, warning => warning.Contains("Malformed warp", StringComparison.Ordinal));
            Assert.Contains(project.Warnings, warning => warning.Contains("malformed frequency", StringComparison.Ordinal));
            Assert.Contains(project.Warnings, warning => warning.Contains("malformed Gain", StringComparison.Ordinal));
            var report = new ReportService().ToMarkdown(project);
            Assert.Contains("Exported channel EQ bands", report);
            Assert.Contains("content time=2 beats", report);
            Assert.Equal(ProjectFieldState.Present, ProjectFieldCoverage.Evaluate(project)["Channel EQ bands"]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(300_000_000u)]
    [InlineData(2_000_000_000u)]
    public async Task ReadsBoundedXmlWithoutOpeningLargeMediaPayload(uint declaredMediaLength)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dawproject");
        try
        {
            await using (var output = File.Create(path))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                await AddEntry(archive, "project.xml", """
                    <Project><Transport><Tempo value="174"/></Transport><Structure>
                      <Track id="audio" name="Example" contentType="audio"/>
                    </Structure></Project>
                    """);
                await AddEntry(archive, "audio/large.wav", "not audio");
            }

            // Advertise a large, deliberately inconsistent media size in the ZIP
            // directory. Reading/decompressing that payload would fail; metadata
            // inspection must leave it unopened, without allocating gigabytes.
            var bytes = await File.ReadAllBytesAsync(path);
            var patched = false;
            for (var offset = 0; offset + 46 <= bytes.Length; offset++)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)) != 0x02014b50)
                    continue;
                var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28, 2));
                if (offset + 46 + nameLength > bytes.Length ||
                    Encoding.UTF8.GetString(bytes, offset + 46, nameLength) != "audio/large.wav")
                    continue;
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24, 4), declaredMediaLength);
                patched = true;
                break;
            }
            Assert.True(patched);
            await File.WriteAllBytesAsync(path, bytes);

            var result = await new DawprojectReader().ReadAsync(path);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(174d, result.Value!.Bpm);
            Assert.Equal("Example", Assert.Single(result.Value.Tracks).Name);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task RejectsProjectXmlWithExcessiveNesting()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dawproject");
        try
        {
            await using (var output = File.Create(path))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var nested = string.Concat(Enumerable.Repeat("<x>", 130)) +
                    string.Concat(Enumerable.Repeat("</x>", 130));
                await AddEntry(archive, "project.xml", "<Project>" + nested + "</Project>");
            }

            var result = await new DawprojectReader().ReadAsync(path);

            Assert.False(result.IsSuccess);
            Assert.Equal("XML_DEPTH_LIMIT", result.Error?.Code);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task OmitsOnlyMetadataWhenMetadataXmlHasExcessiveNesting()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dawproject");
        try
        {
            await using (var output = File.Create(path))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                await AddEntry(archive, "project.xml", "<Project><Transport/><Structure/></Project>");
                var nested = string.Concat(Enumerable.Repeat("<x>", 130)) +
                    string.Concat(Enumerable.Repeat("</x>", 130));
                await AddEntry(archive, "metadata.xml", "<MetaData>" + nested + "</MetaData>");
            }

            var result = await new DawprojectReader().ReadAsync(path);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Null(result.Value!.Metadata);
            Assert.Contains(result.Value.Warnings, warning => warning.Contains("nesting limit", StringComparison.Ordinal));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ReadsDeclaredMetadataMixerPanMidiNotesAndPluginStateReference()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dawproject");
        try
        {
            await using (var output = File.Create(path))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                await AddEntry(archive, "project.xml", """
                    <Project version="1.0"><Application name="Test DAW" version="1"/><Transport/>
                      <Structure><Track id="track-1" name="Lead" contentType="notes">
                        <Channel id="channel-1" role="regular" solo="false">
                          <Pan value="0.25"/><Volume value="0.75"/><Mute value="false"/>
                          <Devices><Vst3Plugin id="instance-1" deviceID="vendor.product" deviceName="Test Synth"
                            deviceRole="instrument" deviceVendor="Maker" pluginVersion="2.1">
                            <Enabled value="true"/><State path="plugins/state.bin"/>
                          </Vst3Plugin></Devices>
                        </Channel>
                      </Track></Structure>
                      <Arrangement><Lanes timeUnit="beats"><Lanes track="track-1"><Notes track="track-1">
                        <Note time="2" duration="0.5" channel="1" key="64" vel="0.8" rel="0.6"/>
                      </Notes></Lanes></Lanes><Markers><Marker time="4" name="Chorus"/></Markers></Arrangement>
                    </Project>
                    """);
                await AddEntry(archive, "metadata.xml", """
                    <MetaData><Title>Example Song</Title><Artist>Studio Artist</Artist><Album>Sessions</Album>
                    <Composer>Composer</Composer><Year>2025</Year><Genre>Electronic</Genre><Comment>Mix v3</Comment></MetaData>
                    """);
            }

            var result = await new DawprojectReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var project = result.Value!;
            var track = Assert.Single(project.Tracks);
            Assert.Equal(0.25, track.Pan);
            Assert.Equal("regular", track.ChannelRole);
            var plugin = Assert.Single(project.Plugins);
            Assert.Equal("instance-1", plugin.InstanceId);
            Assert.Equal("vendor.product", plugin.Id);
            Assert.Equal("plugins/state.bin", plugin.StateReference);
            var note = Assert.Single(project.MidiNotes!);
            Assert.Equal("track-1", note.TrackId);
            Assert.Equal((2d, 0.5d, 1, 64), (note.Time, note.Duration, note.Channel, note.Key));
            Assert.Equal(0.8, note.Velocity);
            Assert.Equal(0.6, note.ReleaseVelocity);
            Assert.Equal("beats", note.TimeUnit);
            Assert.False(note.IsNestedInClip);
            Assert.Equal("Example Song", project.Metadata!.Title);
            Assert.Equal("Studio Artist", project.Metadata.Artist);
            Assert.Equal("Mix v3", project.Metadata.Comment);
            Assert.Contains("Mixer pan", ProjectFieldCoverage.Evaluate(project).Keys);
            Assert.Contains("MIDI note events", ProjectFieldCoverage.Evaluate(project).Keys);
            Assert.Contains("Example Song", new ReportService().ToMarkdown(project));
            Assert.Contains("StateReference", new ReportService().PluginsCsv(project));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static async Task AddEntry(ZipArchive archive, string name, string xml)
    {
        await using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(xml));
    }
}
