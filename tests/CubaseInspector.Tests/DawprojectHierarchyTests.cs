using System.IO.Compression;
using System.Text;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class DawprojectHierarchyTests
{
    [Fact]
    public async Task NestedStructuredTracksPreserveDeclaredParentsAndOrdering()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dawproject");
        try
        {
            await using (var output = File.Create(path))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry("project.xml");
                await using var entryStream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes("""
                    <Project version="1.0"><Transport><TimeSignature numerator="7" denominator="8"/></Transport><Structure>
                      <Track id="folder" name="Main folder">
                        <Track id="audio" name="Vocal" contentType="audio" color="#33FF66" comment="Keep natural" loaded="false">
                          <Channel id="ch-vocal"/>
                        </Track>
                        <Track id="nested" name="Subfolder">
                          <Track id="midi" name="Piano" contentType="notes"/>
                        </Track>
                      </Track>
                      <Track id="separate" name="Master"/>
                    </Structure><Arrangement><Lanes track="audio" timeUnit="beats"><Audio channels="2" sampleRate="48000" duration="3.5"><File path="audio/voice.wav" external="false"/></Audio><Clips><Clip name="Verse" time="4.0" duration="8.0" enable="true" reference="source-clip"/><Clip name="Invalid" time="NaN"/></Clips></Lanes><Markers><Marker name="Chorus" time="12.5"/></Markers></Arrangement></Project>
                    """);
                await entryStream.WriteAsync(bytes);
            }
            var result = await new DawprojectReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var tracks = result.Value!.Tracks;
            Assert.Equal(["folder", "audio", "nested", "midi", "separate"], tracks.Select(t => t.Id).ToArray());
            Assert.Null(tracks[0].ParentTrackId);
            Assert.Equal("folder", tracks[1].ParentTrackId);
            Assert.Equal("#33FF66", tracks[1].Color);
            Assert.Equal("Keep natural", tracks[1].Comment);
            Assert.False(tracks[1].Loaded);
            Assert.Equal("folder", tracks[2].ParentTrackId);
            Assert.Equal("nested", tracks[3].ParentTrackId);
            Assert.Null(tracks[4].ParentTrackId);
            Assert.Equal(7, result.Value.TimeSignatureNumerator);
            Assert.Equal(8, result.Value.TimeSignatureDenominator);
            var media = Assert.Single(result.Value.MediaReferences!);
            Assert.Equal("audio/voice.wav", media.Path);
            Assert.Equal(48000, media.SampleRate);
            Assert.Equal(2, media.Channels);
            Assert.Equal(3.5, media.Duration);
            Assert.False(media.External);
            Assert.Equal("audio", media.TrackId);
            Assert.Equal(2, result.Value.TimelineItems!.Count);
            Assert.Contains(result.Value.Warnings, w => w.Contains("Malformed or missing Clip/Marker time"));
            Assert.Contains(result.Value.TimelineItems, x => x.Kind == "Clip" &&
                x.Name == "Verse" && x.Time == 4 && x.Duration == 8 &&
                x.Reference == "source-clip" && x.Enabled == true);
            Assert.Contains(result.Value.TimelineItems, x => x.Kind == "Marker" &&
                x.Name == "Chorus" && x.Time == 12.5 && x.Duration is null);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
