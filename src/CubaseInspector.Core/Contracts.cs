namespace CubaseInspector.Core;

public enum EvidenceLevel { Confirmed, Candidate, Unknown }
public enum PluginStatus { InstalledByName, Unverified, InstalledByClassId }

public sealed record AppError(string Code, string Message);

public sealed record Result<T>(T? Value, AppError? Error)
{
    public bool IsSuccess => Error is null;
    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Fail(string code, string message) => new(default, new(code, message));
}

public sealed record PluginReference(
    string Id, string Name, string? Manufacturer, string Format, string? TrackId,
    EvidenceLevel Evidence, string? Version = null,
    string? Role = null, int? Slot = null, bool? Enabled = null,
    long? SourceByteOffset = null,
    string? InstanceId = null, string? StateReference = null,
    string? CandidateOwnerName = null, string? CandidateLocationPath = null);

public sealed record TrackInfo(
    string Id, string Name, string Kind, string? ChannelId, string? DestinationChannelId,
    double? Volume, bool? Mute, bool? Solo,
    EvidenceLevel Evidence = EvidenceLevel.Confirmed,
    string? SourceRecordClass = null, long? SourceOffset = null,
    string? ParentTrackId = null,
    string? Color = null, string? Comment = null, bool? Loaded = null,
    double? Pan = null, string? ChannelRole = null,
    long? SourceEndOffset = null);

public sealed record RoutingEdge(string FromChannelId, string ToChannelId, string Kind,
    double? Level = null, bool? Enabled = null, string? Tap = null,
    EvidenceLevel Evidence = EvidenceLevel.Confirmed, long? SourceOffset = null, string? SourcePath = null,
    string? TargetLabel = null);

/// <summary>Declared DAWproject media reference; no local path resolution or content loading.</summary>
public sealed record ProjectMediaReference(string Path, bool? External,
    int? Channels, int? SampleRate, double? Duration, string? TrackId = null);

public sealed record PreparedMediaPreview(Uri SourceUri, string? TemporaryFilePath);

public interface IProjectMediaPreviewService
{
    Task<Result<PreparedMediaPreview>> PrepareAsync(ProjectSnapshot project, ProjectMediaReference reference,
        CancellationToken cancellationToken = default);
    void Release(PreparedMediaPreview? preview);
}

public sealed record MediaPackageReferenceResult(string DeclaredPath, string? TrackId,
    string Status, string? PackageEntry, string Detail);

public sealed record MediaPackageReport(string PackagePath, int IncludedFileCount,
    long IncludedBytes, IReadOnlyList<MediaPackageReferenceResult> References);

public interface IProjectMediaPackageService
{
    Task<Result<MediaPackageReport>> CreateAsync(ProjectSnapshot project, string destinationPath,
        CancellationToken cancellationToken = default);
}

public sealed record ProjectMediaReferenceSelection(ProjectSnapshot Project, int ReferenceIndex);

public sealed record MediaDuplicateUse(string ProjectName, string ProjectPath, string DeclaredPath,
    string TrackName, string Folder);

public sealed record MediaDuplicateSource(string ResolvedPath, long Length, IReadOnlyList<MediaDuplicateUse> Uses);

public sealed record MediaDuplicateGroup(string Sha256, long Length, IReadOnlyList<MediaDuplicateSource> Sources)
{
    public int SourceCount => Sources.Count;
    public string SizeSummary => $"{Sources.Count:N0} distinct files · {Length / (1024d * 1024d):N2} MiB each";
}

public sealed record MediaDuplicateScanReport(int AnalyzedFileCount, int SkippedFileCount,
    long BytesRead, bool LimitReached, IReadOnlyList<MediaDuplicateGroup> Groups);

public sealed record MediaDuplicateScanProgress(int CompletedFiles, int TotalFiles, long BytesRead);

public interface IProjectMediaDuplicateService
{
    Task<MediaDuplicateScanReport> ScanAsync(IReadOnlyList<ProjectMediaReferenceSelection> selections,
        IProgress<MediaDuplicateScanProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Declared static DAWproject arrangement item, not inferred playback behavior.</summary>
public sealed record ProjectTimelineItem(string Kind, string? Name,
    double Time, double? Duration, string? Reference, bool? Enabled);

/// <summary>Declared DAWproject MIDI note event; units follow its containing timeline.</summary>
public sealed record ProjectMidiNote(string TrackId, double Time, double Duration,
    int Channel, int Key, double? Velocity, double? ReleaseVelocity,
    string? TimeUnit = null, bool IsNestedInClip = false);

/// <summary>Declared EQ band parameters with their source units; no opaque state decoding.</summary>
public sealed record ProjectEqBand(string TrackId, string? DeviceInstanceId, int BandIndex,
    string? Type, int? Order, double? Frequency, string? FrequencyUnit,
    double? Gain, string? GainUnit, double? Q, string? QUnit, bool? Enabled, string SourcePath);

/// <summary>Declared timeline-to-content mapping; neither coordinate is a computed playback position.</summary>
public sealed record ProjectWarpPoint(string? TrackId, string? WarpId, int PointIndex,
    double Time, double ContentTime, string? TimeUnit, string? ContentTimeUnit, string SourcePath);

/// <summary>Optional user-authored DAWproject metadata.xml fields.</summary>
public sealed record ProjectMetadata(string? Title = null, string? Artist = null, string? Album = null,
    string? OriginalArtist = null, string? Composer = null, string? Songwriter = null,
    string? Producer = null, string? Arranger = null, string? Year = null, string? Genre = null,
    string? Copyright = null, string? Website = null, string? Comment = null);


public sealed record ProjectSnapshot(
    string FilePath, string Name, string Format, string? CreatorVersion,
    double? Bpm, EvidenceLevel Evidence,
    IReadOnlyList<TrackInfo> Tracks,
    IReadOnlyList<PluginReference> Plugins,
    IReadOnlyList<RoutingEdge> Routing,
    IReadOnlyList<string> Warnings,
    int? TimeSignatureNumerator = null,
    int? TimeSignatureDenominator = null,
    IReadOnlyList<ProjectMediaReference>? MediaReferences = null,
    IReadOnlyList<ProjectTimelineItem>? TimelineItems = null,
    ProjectMetadata? Metadata = null,
    IReadOnlyList<ProjectMidiNote>? MidiNotes = null,
    IReadOnlyList<CprMetadataCandidate>? NativeMetadataCandidates = null,
    IReadOnlyList<CprMixerObservation>? NativeMixerObservations = null,
    IReadOnlyList<ProjectEqBand>? EqBands = null,
    IReadOnlyList<ProjectWarpPoint>? WarpPoints = null,
    bool HasTempoAutomation = false,
    IReadOnlyList<CprRif2MidiNoteCandidate>? NativeMidiNoteCandidates = null,
    string? SourceSha256 = null,
    IReadOnlyList<CprAudioMediaReferenceCandidate>? NativeAudioMediaCandidates = null);

/// <summary>Declared VST3 audio processing class; supplied by an installed bundle's moduleinfo.json.</summary>
public sealed record InstalledPluginClass(string Cid, string Name, string? Manufacturer,
    string? Version, string Category, string? SnapshotPath = null);

public sealed record InstalledPlugin(string Name, string Path, string Format,
    IReadOnlyList<InstalledPluginClass>? Classes = null);
public sealed record PluginCheck(PluginReference Reference, PluginStatus Status, string? MatchedPath,
    InstalledPluginClass? MatchedClass = null);
public enum ScanStage { Discovering, Inspecting, Hashing }
public sealed record ProjectScanTypes(bool Cpr = true, bool Bak = true, bool Dawproject = true)
{
    public static ProjectScanTypes All { get; } = new();
    public bool AnyEnabled => Cpr || Bak || Dawproject;
}

public sealed record ScanProgress(int Completed, int Total, string CurrentPath,
    ProjectSnapshot? ParsedProject = null, AppError? Failure = null,
    ScanStage Stage = ScanStage.Inspecting, long? BytesProcessed = null, long? TotalBytes = null);
public sealed record FolderReport(IReadOnlyList<ProjectSnapshot> Projects,
    IReadOnlyList<(string Path, string Error)> Failures,
    bool Partial = false, int Discovered = 0, int Completed = 0,
    int SkippedUnrelatedBackups = 0,
    IReadOnlyList<ProjectLibraryEntry>? ProjectSummaries = null,
    IReadOnlyList<ProjectAssetIndex>? ProjectAssetIndexes = null,
    ProjectScanTypes? ProjectTypes = null);
public sealed record ProjectChange(string Category, string Entity, string Property, string? Before, string? After, string MatchQuality);

public interface IProjectReader
{
    bool CanRead(string path);
    Task<Result<ProjectSnapshot>> ReadAsync(string path, CancellationToken cancellationToken = default);
}

public interface IProjectInspector
{
    Task<Result<ProjectSnapshot>> InspectAsync(string path, CancellationToken cancellationToken = default);
}

public interface IPluginScannerService
{
    Task<Result<IReadOnlyList<InstalledPlugin>>> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
    IReadOnlyList<PluginCheck> Check(ProjectSnapshot project, IReadOnlyList<InstalledPlugin> installed);
}

public interface IFolderScanner
{
    Task<Result<FolderReport>> ScanAsync(string path, IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default, ProjectScanTypes? projectTypes = null,
        bool retainFullSnapshots = true);
}

public interface IProjectDiff
{
    IReadOnlyList<ProjectChange> Compare(ProjectSnapshot before, ProjectSnapshot after,
        IReadOnlyList<TrackMatchDecision>? trackMatches = null);
}

public interface ISafeCopyService
{
    Task<Result<string>> CopyUnmodifiedAsync(string source, string destination, CancellationToken cancellationToken = default);
    Task<Result<string>> CopyWithDisabledPluginsAsync(string source, string destination, IReadOnlyList<string> pluginIds, CancellationToken cancellationToken = default);
}

public interface IReportService
{
    string ToMarkdown(ProjectSnapshot project);
    string ShortSummary(ProjectSnapshot project);
    string TracksCsv(ProjectSnapshot project);
    string PluginsCsv(ProjectSnapshot project);
}


// CPR details are structural observations, not confirmed Cubase track/plugin data.
public sealed record CprTrackHint(string RecordClass, string? Name, long ArchiveOffset,
    string? NameSource = null, long? ArchiveEndOffset = null);
public sealed record CprChunkSummary(string Id, long Offset, long PayloadBytes, string? StreamName, string? ArchiveClass);

/// <summary>
/// Native MIDI field observation (historical type name retained for persisted/API compatibility). Values are not automatically
/// promoted to confirmed, track-owned MIDI in a normalized project snapshot.
/// </summary>
public sealed record CprRif2MidiNoteCandidate(long FileOffset, double PositionTicks,
    double DurationTicks, int MidiPitch, int Velocity,
    string Evidence = "Cubase 15.0.40 MMidiNote/112-byte layout observed in controlled fixtures",
    string SourceLayout = "RIF2 MMidiNote/112-byte",
    string? CandidateTrackName = null, string? CandidatePartName = null,
    long? CandidateTrackOffset = null, long? CandidatePartOffset = null,
    int? Channel = null, ushort? RawFlags = null);
public sealed record CprArchiveSummary(string StreamName, long DeclaredBytes, int BytesScanned, int RecordsFound,
    bool Partial, IReadOnlyDictionary<string, int> ClassCounts, IReadOnlyList<CprTrackHint> TrackHints,
    IReadOnlyList<CprRif2MidiNoteCandidate>? MidiNoteCandidates = null,
    IReadOnlyList<CprAudioMediaReferenceCandidate>? AudioMediaCandidates = null,
    IReadOnlyList<string>? LimitReasons = null);
public sealed record CprNamedRecordValue(string Path, string Type, string Value, long ArchiveOffset);
public sealed record CprNamedRecordCandidate(string StreamName, long ArchiveOffset,
    IReadOnlyList<CprNamedRecordValue> Values, string Status);
public sealed record CprMetadataCandidate(string Field, string Value, long ArchiveOffset,
    string SourcePattern, string Status);
/// <summary>Unverified mixer-like values grouped by one bounded CPR named-record observation.</summary>
public sealed record CprMixerObservation(string Name, double? VolumeDb, double? Pan,
    bool? Mute, bool? Solo, long ArchiveOffset, string SourceStream,
    string Evidence = "Unverified CPR named-record observation",
    IReadOnlyList<CprNamedRecordValue>? CandidateFields = null);
/// <summary>Audio path token nested in an audio-event record; track ownership is only structural evidence.</summary>
public sealed record CprAudioMediaReferenceCandidate(string DeclaredPath, long FileOffset,
    string? CandidateTrackName, long? CandidateTrackOffset, string SourceRecord,
    string Evidence = "Audio path token nested in MAudioEvent beneath MAudioTrackEvent",
    string Warning = "CPR event containment is a candidate link. Media path, clip use, track ownership, and current file identity are unverified.");
public sealed record CprEnvelopeSizeDiagnostics(bool HasRiffNundSignature,
    uint SizeFieldBigEndian, uint SizeFieldLittleEndian, long ActualLength,
    ulong BigEndianTotalLength, bool BigEndianTotalLengthMatches,
    ulong LittleEndianTotalLength, bool LittleEndianTotalLengthMatches);
public sealed record CprStructureReport(string Path, long DeclaredContainerBytes, IReadOnlyList<CprChunkSummary> Chunks,
    CprArchiveSummary? Arrangement, string? VersionTextCandidate, IReadOnlyList<string> Warnings,
    IReadOnlyList<CprNamedRecordCandidate>? NamedRecordCandidates = null,
    CprEnvelopeSizeDiagnostics? SizeDiagnostics = null,
    IReadOnlyList<CprMetadataCandidate>? MetadataCandidates = null,
    string? ContainerMagic = null,
    IReadOnlyList<CprArchiveRecordInventory>? ArchiveInventories = null);

public interface ICprStructureInspector
{
    Task<Result<CprStructureReport>> InspectAsync(string path, CancellationToken cancellationToken = default);
}
