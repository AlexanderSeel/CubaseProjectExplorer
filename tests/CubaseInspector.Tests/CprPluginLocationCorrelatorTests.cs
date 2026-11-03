using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprPluginLocationCorrelatorTests
{
    [Fact]
    public void CorrelatesPluginNameToTrackOnlyInsideSameNamedRecord()
    {
        var plugin = new PluginReference("uid", "Compressor", null, "CPR UID", null,
            EvidenceLevel.Candidate, SourceByteOffset: 120);
        var record = new CprNamedRecordCandidate("Arrangement1", 100,
        [
            new("Name/String", "string", "Verse Vox", 105),
            new("InsertFolder/Slot[1]/Plugin Name", "string", "Compressor", 130)
        ], "candidate");

        var correlated = Assert.Single(CprPluginLocationCorrelator.AttachCandidateLocations([plugin], [record]));

        Assert.Equal("Verse Vox", correlated.CandidateOwnerName);
        Assert.Equal("InsertFolder/Slot[1]", correlated.CandidateLocationPath);
        Assert.Null(correlated.TrackId);
    }

    [Fact]
    public void DoesNotAttachNameFromUnrelatedRecord()
    {
        var plugin = new PluginReference("uid", "Compressor", null, "CPR UID", null,
            EvidenceLevel.Candidate, SourceByteOffset: 250);
        var record = new CprNamedRecordCandidate("Arrangement1", 100,
        [
            new("Name/String", "string", "Verse Vox", 105),
            new("InsertFolder/Slot[1]/Plugin Name", "string", "Compressor", 130)
        ], "candidate");

        var correlated = Assert.Single(CprPluginLocationCorrelator.AttachCandidateLocations([plugin], [record]));

        Assert.Null(correlated.CandidateOwnerName);
    }

    [Fact]
    public void DoesNotChooseFirstTrackWhenCandidateRangesOverlap()
    {
        var plugin = new PluginReference("uid", "Compressor", null, "CPR UID", null,
            EvidenceLevel.Candidate, SourceByteOffset: 120);
        var first = new CprNamedRecordCandidate("Arrangement1", 100,
        [
            new("Name/String", "string", "Verse Vox", 105),
            new("InsertFolder/Slot[1]/Plugin Name", "string", "Compressor", 130)
        ], "candidate");
        var second = first with
        {
            Values =
            [
                new("Name/String", "string", "Bass", 110),
                new("InsertFolder/Slot[2]/Plugin Name", "string", "Compressor", 140)
            ]
        };

        var correlated = Assert.Single(CprPluginLocationCorrelator.AttachCandidateLocations([plugin], [first, second]));

        Assert.Null(correlated.CandidateOwnerName);
        Assert.Null(correlated.CandidateLocationPath);
    }

    [Fact]
    public void DoesNotChooseFirstPluginSlotWhenRecordHasDuplicateNames()
    {
        var plugin = new PluginReference("uid", "Compressor", null, "CPR UID", null,
            EvidenceLevel.Candidate, SourceByteOffset: 120);
        var record = new CprNamedRecordCandidate("Arrangement1", 100,
        [
            new("Name/String", "string", "Verse Vox", 105),
            new("InsertFolder/Slot[1]/Plugin Name", "string", "Compressor", 130),
            new("InsertFolder/Slot[2]/Plugin Name", "string", "Compressor", 140)
        ], "candidate");

        var correlated = Assert.Single(CprPluginLocationCorrelator.AttachCandidateLocations([plugin], [record]));

        Assert.Null(correlated.CandidateOwnerName);
        Assert.Null(correlated.CandidateLocationPath);
    }
}
