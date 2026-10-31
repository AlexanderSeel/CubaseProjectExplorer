using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CubaseInspector.App.Services;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;

namespace CubaseInspector.App.ViewModels;

public sealed record SampleUseRow(
    string ProjectId, string ProjectName, string ProjectPath, int ReferenceIndex,
    string DeclaredPath, string Folder, string FileType, string Source,
    string TrackName, int? Channels, int? SampleRate, double? Duration, int UseCount = 1);

public sealed record NativeSampleCandidateRow(
    string ProjectId, string ProjectName, string ProjectPath, int Observation,
    string DeclaredPath, string? CandidateTrackName, long? CandidateTrackOffset,
    long FileOffset, EvidenceLevel Evidence, string EvidenceSource, string Warning)
{
    public string TrackName => CandidateTrackName ?? "Track name unavailable";
    public string FileType => Path.GetExtension(DeclaredPath).TrimStart('.').ToUpperInvariant();
}

public sealed record MidiTrackLibraryRow(
    string ProjectId, string ProjectName, string ProjectPath, string TrackId,
    string Name, string Kind, int NoteCount, string PitchRange, int ChannelCount,
    string TimeUnit, bool CanExport, string ExportLimitation);

public sealed record SampleProjectScopeOption(string Path, string DisplayName);

public sealed partial class MediaLibraryViewModel(
    LibraryDocumentStore store, IProjectPathPicker picker,
    IProjectMediaPreviewService previewService, IProjectMediaPackageService packageService,
    IProjectMediaDuplicateService duplicateService) : ObservableObject
{
    private SampleUseRow[] _allSamples = [];
    private NativeSampleCandidateRow[] _allNativeSampleCandidates = [];
    private MidiTrackLibraryRow[] _allMidiTracks = [];
    private IndexedMidiNoteCandidate[] _allMidiCandidates = [];
    private PreparedMediaPreview? _preparedPreview;
    private int _midiPreviewRequest;
    private CancellationTokenSource? _duplicateScanCancellation;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Index media and MIDI data from project snapshots stored in the library.";
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _sampleType = "All";
    [ObservableProperty] private string _midiSearch = string.Empty;
    [ObservableProperty] private string _sampleProjectPath = string.Empty;
    [ObservableProperty] private Uri? _previewUri;
    [ObservableProperty] private string _waveformSparkline = "Select a WAV sample and prepare its preview to analyze a waveform.";
    [ObservableProperty] private string _waveformSummary = "Waveform analysis is available for PCM or IEEE-float WAV only.";
    [ObservableProperty] private MidiPianoRollPreview? _midiPianoRoll;
    [ObservableProperty] private bool _isMidiPreviewBusy;
    [ObservableProperty] private string _midiPianoRollStatus = "Select a MIDI track to preview its exported notes.";
    [ObservableProperty] private bool _isDuplicateScanBusy;
    [ObservableProperty] private string _duplicateScanStatus = "Byte-identity scan is opt-in. It reads selected external files; embedded samples are skipped.";

    public ResettableObservableCollection<SampleUseRow> Samples { get; } = [];
    public ResettableObservableCollection<NativeSampleCandidateRow> NativeSampleCandidates { get; } = [];
    public ResettableObservableCollection<MidiTrackLibraryRow> MidiTracks { get; } = [];
    public ResettableObservableCollection<IndexedMidiNoteCandidate> NativeMidiCandidates { get; } = [];
    public ObservableCollection<MediaDuplicateGroup> IdenticalSampleGroups { get; } = [];
    public IReadOnlyList<string> SampleTypes { get; private set; } = ["All"];
    public IReadOnlyList<SampleProjectScopeOption> SampleProjects { get; private set; } = [];
    public bool HasPreview => PreviewUri is not null;

    partial void OnSearchChanged(string value) => RefreshSamples();
    partial void OnSampleTypeChanged(string value) => RefreshSamples();
    partial void OnMidiSearchChanged(string value) => RefreshMidiTracks();
    partial void OnSampleProjectPathChanged(string value) => RefreshSamples();
    partial void OnPreviewUriChanged(Uri? value) => OnPropertyChanged(nameof(HasPreview));

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "Loading indexed media and MIDI tracks…";
        await Task.Yield();
        try
        {
            await LoadIndexContentsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        { Status = "Could not load the media/MIDI index: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task LoadIndexContentsAsync()
    {
        var indexes = await store.GetProjectAssetIndexesAsync();
        _allSamples = indexes.Where(document => document.Index is not null && document.Index.Evidence == EvidenceLevel.Confirmed)
            .SelectMany(document => document.Index!.MediaUses.Select(media => new SampleUseRow(
                document.Id, document.Index.ProjectName, document.ProjectPath, media.ReferenceIndex,
                media.DeclaredPath, ResolveFolder(document.ProjectPath, media), media.FileType,
                media.External switch { true => "External reference", false => "Embedded archive entry", _ => "Location unknown" },
                media.TrackName ?? "Track unknown", media.Channels, media.SampleRate, media.Duration)))
            .ToArray();
        var useCounts = _allSamples.GroupBy(row => row.ProjectPath + "\0" + row.DeclaredPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        _allSamples = _allSamples.Select(row => row with
            { UseCount = useCounts[row.ProjectPath + "\0" + row.DeclaredPath] }).ToArray();
        _allMidiTracks = indexes.Where(document => document.Index is not null && document.Index.Evidence == EvidenceLevel.Confirmed)
            .SelectMany(document => document.Index!.MidiTracks.Select(track => new MidiTrackLibraryRow(
                document.Id, document.Index.ProjectName, document.ProjectPath, track.TrackId,
                track.Name, track.Kind, track.NoteCount,
                track.LowestKey is int low && track.HighestKey is int high ? $"{low}–{high}" : "Unknown",
                track.ChannelCount, track.TimeUnit ?? "Unknown", track.CanExportStandardMidi, track.ExportLimitation)))
            .ToArray();
        _allMidiCandidates = indexes.SelectMany(document => document.Index?.NativeMidiNoteCandidates ?? [])
            .Where(candidate => candidate.Evidence == EvidenceLevel.Candidate).ToArray();
        _allNativeSampleCandidates = indexes.SelectMany(document => (document.Index?.NativeMediaReferenceCandidates ?? [])
            .Select(candidate => new NativeSampleCandidateRow(document.Id, candidate.ProjectName,
                candidate.ProjectPath, candidate.Observation, candidate.DeclaredPath,
                candidate.CandidateTrackName, candidate.CandidateTrackOffset, candidate.FileOffset,
                candidate.Evidence, candidate.EvidenceSource, candidate.Warning))).ToArray();
        SampleTypes = ["All", .. _allSamples.Select(row => row.FileType)
            .Concat(_allNativeSampleCandidates.Select(row => row.FileType)).Where(type => type.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(type => type, StringComparer.OrdinalIgnoreCase)];
        SampleProjects = _allSamples.Select(row => (row.ProjectPath, row.ProjectName))
            .Concat(_allNativeSampleCandidates.Select(row => (row.ProjectPath, row.ProjectName)))
            .GroupBy(row => row.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SampleProjectScopeOption(group.Key,
                group.Select(row => row.ProjectName).First() + " · " + Path.GetFileName(group.Key)))
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        OnPropertyChanged(nameof(SampleTypes));
        OnPropertyChanged(nameof(SampleProjects));
        RefreshSamples();
        RefreshMidiTracks();
        Status = $"Indexed {_allSamples.Length:N0} confirmed media uses and {_allNativeSampleCandidates.Length:N0} native CPR audio-path candidates across {indexes.Count:N0} projects · {_allMidiTracks.Length:N0} MIDI tracks · {_allMidiCandidates.Length:N0} unverified CPR note observations.";
        if (indexes.Count == 0) Status = "No asset indexes exist yet. Build the index from the Library database page.";
    }

    [RelayCommand]
    private async Task RebuildIndexAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "Rebuilding asset indexes from saved project snapshots…";
        try
        {
            var count = await store.RebuildProjectAssetIndexesAsync();
            Status = $"Rebuilt indexes for {count:N0} stored projects · refreshing browser…";
            await LoadIndexContentsAsync();
            Status = $"Rebuilt indexes for {count:N0} stored projects. " + Status;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        { Status = "Asset index rebuild failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task PreviewSampleAsync(SampleUseRow row)
    {
        StopPreview();
        if (IsBusy) return;
        IsBusy = true;
        WaveformSparkline = "Preparing bounded waveform…";
        WaveformSummary = "Analyzing selected sample bytes…";
        Status = $"Preparing preview: {row.DeclaredPath}";
        try
        {
            var document = await store.GetProjectAsync(row.ProjectId);
            if (document?.Snapshot is not { } project || row.ReferenceIndex < 0 ||
                project.MediaReferences is not { } media || row.ReferenceIndex >= media.Count ||
                !string.Equals(media[row.ReferenceIndex].Path, row.DeclaredPath, StringComparison.Ordinal))
            {
                Status = "This index row is stale. Rebuild the asset index and try again.";
                return;
            }
            var result = await previewService.PrepareAsync(project, media[row.ReferenceIndex]);
            if (!result.IsSuccess || result.Value is null)
            {
                Status = result.Error?.Code + ": " + result.Error?.Message;
                return;
            }
            _preparedPreview = result.Value;
            PreviewUri = result.Value.SourceUri;
            if (Path.GetExtension(row.DeclaredPath).Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(row.DeclaredPath).Equals(".wave", StringComparison.OrdinalIgnoreCase))
            {
                var waveform = await Task.Run(() =>
                {
                    using var stream = File.OpenRead(result.Value.SourceUri.LocalPath);
                    return WaveformAnalyzer.Analyze(stream);
                });
                if (waveform.IsSuccess && waveform.Value is not null)
                {
                    WaveformSparkline = waveform.Value.Sparkline;
                    WaveformSummary = $"{waveform.Value.DurationSeconds:N2} s · {waveform.Value.SampleRate:N0} Hz · {waveform.Value.Channels} ch · {waveform.Value.FrameCount:N0} frames · peak envelope from WAV data";
                }
                else
                {
                    WaveformSparkline = "Waveform unavailable";
                    WaveformSummary = waveform.Error?.Message ?? "WAV data could not be analyzed.";
                }
            }
            else
            {
                WaveformSparkline = "Waveform unavailable for this format";
                WaveformSummary = "Waveform preview currently analyzes uncompressed PCM/IEEE-float WAV files only.";
            }
            Status = $"Preview ready · {row.ProjectName} · {row.TrackName} · {row.FileType}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Status = "Preview could not be prepared: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task PrepareNativeSamplePreviewAsync(NativeSampleCandidateRow row)
    {
        StopPreview();
        if (IsBusy) return;
        IsBusy = true;
        WaveformSparkline = "Preparing bounded waveform from a candidate media path…";
        WaveformSummary = "The path and track link are unverified; the source project remains unchanged.";
        Status = $"Checking candidate audio path from {row.ProjectName}…";
        try
        {
            var document = await store.GetProjectAsync(row.ProjectId);
            if (document?.Snapshot is not { Evidence: EvidenceLevel.Candidate } project ||
                !string.Equals(project.FilePath, row.ProjectPath, StringComparison.OrdinalIgnoreCase) ||
                !(project.NativeAudioMediaCandidates ?? []).Any(candidate =>
                    candidate.FileOffset == row.FileOffset && string.Equals(candidate.DeclaredPath,
                        row.DeclaredPath, StringComparison.OrdinalIgnoreCase)))
            {
                Status = "This candidate index row is stale. Rescan the project and rebuild the asset index.";
                return;
            }

            var result = await previewService.PrepareAsync(project,
                new ProjectMediaReference(row.DeclaredPath, true, null, null, null));
            if (!result.IsSuccess || result.Value is null)
            {
                Status = result.Error?.Code + ": " + result.Error?.Message;
                return;
            }
            _preparedPreview = result.Value;
            PreviewUri = result.Value.SourceUri;
            if (Path.GetExtension(row.DeclaredPath).Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(row.DeclaredPath).Equals(".wave", StringComparison.OrdinalIgnoreCase))
            {
                var waveform = await Task.Run(() =>
                {
                    using var stream = File.OpenRead(result.Value.SourceUri.LocalPath);
                    return WaveformAnalyzer.Analyze(stream);
                });
                if (waveform.IsSuccess && waveform.Value is not null)
                {
                    WaveformSparkline = waveform.Value.Sparkline;
                    WaveformSummary = $"Candidate preview · {waveform.Value.DurationSeconds:N2} s · {waveform.Value.SampleRate:N0} Hz · {waveform.Value.Channels} ch · {waveform.Value.FrameCount:N0} frames";
                }
                else { WaveformSparkline = "Waveform unavailable"; WaveformSummary = waveform.Error?.Message ?? "WAV data could not be analyzed."; }
            }
            else
            {
                WaveformSparkline = "Waveform unavailable for this format";
                WaveformSummary = "Candidate path resolved. Waveform overview supports PCM/IEEE-float WAV only.";
            }
            Status = $"Candidate preview ready · {row.ProjectName} · {row.TrackName} · track/media ownership unverified.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { Status = "Candidate preview could not be prepared: " + ex.Message; }
        finally { IsBusy = false; }
    }

    public void StopPreview()
    {
        PreviewUri = null;
        WaveformSparkline = "Select a WAV sample and prepare its preview to analyze a waveform.";
        WaveformSummary = "Waveform analysis is available for PCM or IEEE-float WAV only.";
        if (_preparedPreview is not null) previewService.Release(_preparedPreview);
        _preparedPreview = null;
    }

    public void ReportPreviewFailure(string message) => Status = "The media player could not decode this file: " + message;

    public async Task ExportSampleAsync(SampleUseRow row)
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = $"Preparing a copy of {row.DeclaredPath}…";
        PreparedMediaPreview? prepared = null;
        try
        {
            var document = await store.GetProjectAsync(row.ProjectId);
            if (document?.Snapshot is not { } project || row.ReferenceIndex < 0 ||
                project.MediaReferences is not { } media || row.ReferenceIndex >= media.Count ||
                !string.Equals(media[row.ReferenceIndex].Path, row.DeclaredPath, StringComparison.Ordinal))
            {
                Status = "This index row is stale. Rebuild the asset index and try again.";
                return;
            }
            var result = await previewService.PrepareAsync(project, media[row.ReferenceIndex]);
            if (!result.IsSuccess || result.Value is null)
            {
                Status = result.Error?.Code + ": " + result.Error?.Message;
                return;
            }
            prepared = result.Value;
            var extension = Path.GetExtension(row.DeclaredPath);
            var safeName = Path.GetFileNameWithoutExtension(row.DeclaredPath);
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "sample";
            var path = await picker.PickSaveFileAsync(safeName + extension, extension, "Audio sample copy");
            if (path is null) return;
            File.Copy(prepared.SourceUri.LocalPath, path, overwrite: false);
            Status = $"Exported a copy of {row.DeclaredPath}. The project and referenced source are unchanged.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { Status = "Sample export failed: " + ex.Message; }
        finally
        {
            if (prepared is not null) previewService.Release(prepared);
            IsBusy = false;
        }
    }

    public async Task CollectProjectMediaAsync(SampleUseRow row)
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = $"Preparing used-media handoff for {row.ProjectName}…";
        try
        {
            var document = await store.GetProjectAsync(row.ProjectId);
            if (document?.Snapshot is not { } project)
            {
                Status = "The saved project snapshot is unavailable; no handoff package was created.";
                return;
            }
            var path = await picker.PickSaveFileAsync(row.ProjectName + "-used-media", ".zip", "ZIP used-media handoff package");
            if (path is null) return;
            var result = await packageService.CreateAsync(project, path);
            if (!result.IsSuccess || result.Value is null)
            {
                Status = result.Error?.Code + ": " + result.Error?.Message;
                return;
            }
            var uncollected = result.Value.References.Count(reference => reference.Status is not "Included" and not "Included duplicate reference");
            Status = $"Created a separate handoff ZIP with {result.Value.IncludedFileCount:N0} audio files ({result.Value.IncludedBytes / (1024d * 1024d):N1} MiB). {uncollected:N0} references are listed as embedded, missing, unsupported or not checked in manifest.json. The source project was not changed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { Status = "Media handoff package failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task LoadMidiPianoRollAsync(MidiTrackLibraryRow row)
    {
        var request = Interlocked.Increment(ref _midiPreviewRequest);
        IsMidiPreviewBusy = true;
        MidiPianoRoll = null;
        MidiPianoRollStatus = "Loading exported MIDI note events…";
        try
        {
            var document = await store.GetProjectAsync(row.ProjectId);
            if (request != Volatile.Read(ref _midiPreviewRequest)) return;
            if (document?.Snapshot is not { } project)
            {
                MidiPianoRollStatus = "The saved project snapshot is unavailable.";
                return;
            }
            var result = await Task.Run(() => MidiPianoRollPreviewer.Build(project, row.TrackId));
            if (request != Volatile.Read(ref _midiPreviewRequest)) return;
            if (!result.IsSuccess || result.Value is null)
            {
                MidiPianoRollStatus = result.Error?.Message ?? "The selected track cannot be previewed.";
                return;
            }
            MidiPianoRoll = result.Value;
            MidiPianoRollStatus = $"Read-only note overview in {result.Value.TimeUnit} · {result.Value.TotalNoteCount:N0} notes" +
                (result.Value.IsDecimated ? $" · showing a bounded {result.Value.VisibleNotes.Count:N0}-note overview" : "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        { if (request == Volatile.Read(ref _midiPreviewRequest)) MidiPianoRollStatus = "Could not load MIDI preview: " + ex.Message; }
        finally { if (request == Volatile.Read(ref _midiPreviewRequest)) IsMidiPreviewBusy = false; }
    }

    public void ClearMidiPianoRoll()
    {
        Interlocked.Increment(ref _midiPreviewRequest);
        IsMidiPreviewBusy = false;
        MidiPianoRoll = null;
        MidiPianoRollStatus = "Select a MIDI track to preview its exported notes.";
    }

    [RelayCommand]
    public async Task FindByteIdenticalSamplesAsync()
    {
        if (IsDuplicateScanBusy || IsBusy) return;
        var rows = Samples.Where(row => row.Source == "External reference").ToArray();
        if (rows.Length == 0)
        {
            DuplicateScanStatus = "No explicitly external samples are visible in the current filter.";
            return;
        }
        IsDuplicateScanBusy = IsBusy = true;
        IdenticalSampleGroups.Clear();
        DuplicateScanStatus = "Preparing the filtered external sample list…";
        _duplicateScanCancellation = new CancellationTokenSource();
        try
        {
            var selections = new List<ProjectMediaReferenceSelection>(rows.Length);
            var snapshots = new Dictionary<string, ProjectSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                _duplicateScanCancellation.Token.ThrowIfCancellationRequested();
                if (!snapshots.TryGetValue(row.ProjectId, out var project))
                {
                    var document = await store.GetProjectAsync(row.ProjectId, _duplicateScanCancellation.Token);
                    if (document?.Snapshot is not { } loaded) continue;
                    snapshots[row.ProjectId] = project = loaded;
                }
                if (project.MediaReferences is not { } media || row.ReferenceIndex < 0 || row.ReferenceIndex >= media.Count ||
                    !string.Equals(media[row.ReferenceIndex].Path, row.DeclaredPath, StringComparison.Ordinal)) continue;
                selections.Add(new(project, row.ReferenceIndex));
            }
            if (selections.Count == 0)
            {
                DuplicateScanStatus = "The visible index rows are stale; refresh the media index and try again.";
                return;
            }
            var progress = new Progress<MediaDuplicateScanProgress>(value =>
                DuplicateScanStatus = $"Hashing external audio · {value.CompletedFiles:N0}/{value.TotalFiles:N0} files · {value.BytesRead / (1024d * 1024d):N1} MiB read");
            var report = await duplicateService.ScanAsync(selections, progress, _duplicateScanCancellation.Token);
            foreach (var group in report.Groups) IdenticalSampleGroups.Add(group);
            DuplicateScanStatus = report.Groups.Count == 0
                ? $"No byte-identical external files found among {report.AnalyzedFileCount:N0} analyzed files. {report.SkippedFileCount:N0} were unavailable/unsupported." 
                : $"Found {report.Groups.Count:N0} byte-identical groups among {report.AnalyzedFileCount:N0} files · {report.BytesRead / (1024d * 1024d):N1} MiB read · {report.SkippedFileCount:N0} skipped.";
            if (report.LimitReached) DuplicateScanStatus += " Scan stopped at the 500-file or 1 GiB limit.";
        }
        catch (OperationCanceledException)
        { DuplicateScanStatus = "Byte-identity scan canceled; no source files were changed."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { DuplicateScanStatus = "Byte-identity scan failed: " + ex.Message; }
        finally
        {
            _duplicateScanCancellation?.Dispose();
            _duplicateScanCancellation = null;
            IsDuplicateScanBusy = IsBusy = false;
        }
    }

    public void CancelDuplicateScan() => _duplicateScanCancellation?.Cancel();

    [RelayCommand]
    private async Task ExportMidiAsync(MidiTrackLibraryRow row)
    {
        if (IsBusy) return;
        if (!row.CanExport) { Status = row.ExportLimitation; return; }
        IsBusy = true;
        Status = $"Preparing MIDI export: {row.Name}";
        try
        {
            var document = await store.GetProjectAsync(row.ProjectId);
            if (document?.Snapshot is not { } project)
            {
                Status = "The saved project snapshot is unavailable; MIDI was not exported.";
                return;
            }
            var bytes = MidiFileExporter.Export(project, row.TrackId);
            var path = await picker.PickSaveFileAsync(row.Name + ".mid", ".mid", "Standard MIDI file");
            if (path is null) return;
            await File.WriteAllBytesAsync(path, bytes);
            Status = $"Exported {row.NoteCount:N0} notes from {row.Name} as Standard MIDI. The project source is unchanged.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { Status = "MIDI export failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private void RefreshSamples()
    {
        var query = Search.Trim();
        var filtered = _allSamples.Where(row => (string.IsNullOrWhiteSpace(SampleProjectPath) ||
                string.Equals(row.ProjectPath, SampleProjectPath, StringComparison.OrdinalIgnoreCase)) &&
            (SampleType == "All" || row.FileType.Equals(SampleType, StringComparison.OrdinalIgnoreCase)) &&
            (query.Length == 0 || row.DeclaredPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             row.Folder.Contains(query, StringComparison.OrdinalIgnoreCase) || row.ProjectName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             row.TrackName.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Source.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(row => row.FileType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.DeclaredPath, StringComparer.OrdinalIgnoreCase).ToArray();
        Samples.ReplaceAll(filtered);
        var candidateQuery = Search.Trim();
        var candidates = _allNativeSampleCandidates.Where(row =>
                (string.IsNullOrWhiteSpace(SampleProjectPath) ||
                    string.Equals(row.ProjectPath, SampleProjectPath, StringComparison.OrdinalIgnoreCase)) &&
                (SampleType == "All" || row.FileType.Equals(SampleType, StringComparison.OrdinalIgnoreCase)) &&
                (candidateQuery.Length == 0 || row.DeclaredPath.Contains(candidateQuery, StringComparison.OrdinalIgnoreCase) ||
                 row.ProjectName.Contains(candidateQuery, StringComparison.OrdinalIgnoreCase) ||
                 row.TrackName.Contains(candidateQuery, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.TrackName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.DeclaredPath, StringComparer.OrdinalIgnoreCase).ToArray();
        NativeSampleCandidates.ReplaceAll(candidates);
        OnPropertyChanged(nameof(SampleSummary));
    }

    private void RefreshMidiTracks()
    {
        var query = MidiSearch.Trim();
        var filtered = _allMidiTracks.Where(row => query.Length == 0 || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.ProjectName.Contains(query, StringComparison.OrdinalIgnoreCase) || row.ProjectPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Kind.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        MidiTracks.ReplaceAll(filtered);
        var candidates = _allMidiCandidates.Where(row => query.Length == 0 ||
                row.ProjectName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.ProjectPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Pitch.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                $"Unassigned note {row.Observation}".Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Observation).ToArray();
        NativeMidiCandidates.ReplaceAll(candidates);
        OnPropertyChanged(nameof(MidiSummary));
    }

    public string SampleSummary => $"{Samples.Count:N0} confirmed uses · {NativeSampleCandidates.Count:N0} candidate CPR paths shown of {_allNativeSampleCandidates.Length:N0}";
    public string MidiSummary => $"{MidiTracks.Count:N0} shown of {_allMidiTracks.Length:N0} indexed MIDI tracks · {_allMidiCandidates.Length:N0} unverified CPR note observations";

    public void SetSampleProjectScope(string? projectPath)
    {
        SampleProjectPath = projectPath ?? string.Empty;
        Search = string.Empty;
        SampleType = "All";
    }

    private static string ResolveFolder(string projectPath, IndexedProjectMediaUse media)
    {
        if (media.External == false)
            return "Inside project archive · " + (Path.GetDirectoryName(media.DeclaredPath) ?? "archive root");
        try
        {
            var full = Path.IsPathRooted(media.DeclaredPath) ? Path.GetFullPath(media.DeclaredPath)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, media.DeclaredPath));
            return Path.GetDirectoryName(full) ?? "Folder unknown";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        { return "Folder unknown"; }
    }
}
