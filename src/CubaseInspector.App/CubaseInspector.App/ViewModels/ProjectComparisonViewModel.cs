using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CubaseInspector.App.Services;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;

namespace CubaseInspector.App.ViewModels;

public sealed partial class ProjectComparisonViewModel(
    IProjectInspector inspector, IProjectDiff diff, IProjectPathPicker picker, ProjectSessionState session,
    LibraryDocumentStore store) : ObservableObject
{
    private CancellationTokenSource? _rebuildCancellation;
    private ProjectSnapshot? _matchingBefore;
    private ProjectSnapshot? _matchingAfter;
    private IReadOnlyList<TrackMatchDecision>? _appliedMatches;
    [ObservableProperty] private ProjectSnapshot? _before;
    [ObservableProperty] private ProjectSnapshot? _after;
    [ObservableProperty] private string _status = "Choose two projects to compare their tracks, plug-ins, media, and saved settings.";
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _category = "All";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _needsMatchReview;
    [ObservableProperty] private string _matchingStatus = "";
    public ResettableObservableCollection<ProjectChangeDisplay> Changes { get; } = [];
    public ResettableObservableCollection<TrackMatchRowViewModel> TrackMatches { get; } = [];
    public bool CanReviewMatches => Before?.Evidence == EvidenceLevel.Confirmed && After?.Evidence == EvidenceLevel.Confirmed;
    public string BeforeName => Before?.Name ?? "Choose project A";
    public string AfterName => After?.Name ?? "Choose project B";
    public string ChangeCount => Changes.Count.ToString("N0");

    partial void OnBeforeChanged(ProjectSnapshot? value) { OnPropertyChanged(nameof(BeforeName)); Rebuild(); }
    partial void OnAfterChanged(ProjectSnapshot? value) { OnPropertyChanged(nameof(AfterName)); Rebuild(); }
    partial void OnSearchChanged(string value) => Rebuild();
    partial void OnCategoryChanged(string value) => Rebuild();

    [RelayCommand]
    private async Task ChooseBeforeAsync() => await LoadAsync(true);
    [RelayCommand]
    private async Task ChooseAfterAsync() => await LoadAsync(false);
    [RelayCommand]
    private void UseSelectedAsBefore() { Before = session.SelectedProject; }
    [RelayCommand]
    private void UseSelectedAsAfter() { After = session.SelectedProject; }
    [RelayCommand]
    private void Swap() { (Before, After) = (After, Before); }
    [RelayCommand]
    private void Clear() { Before = null; After = null; Changes.Clear(); }

    [RelayCommand]
    private void ReviewMatches()
    {
        if (!CanReviewMatches || IsBusy) return;
        NeedsMatchReview = true;
        Changes.ReplaceAll([]);
        Status = "Review project A → B track matches, then save or compare once.";
    }

    [RelayCommand]
    private Task SaveMatchesAsync() => ApplyMatchesAsync(save: true);

    [RelayCommand]
    private Task CompareOnceAsync() => ApplyMatchesAsync(save: false);

    private async Task ApplyMatchesAsync(bool save)
    {
        if (Before is null || After is null || IsBusy || !NeedsMatchReview) return;
        var before = Before;
        var after = After;
        IsBusy = true;
        try
        {
            var decisions = TrackMatches.Select(row => new TrackMatchDecision(row.BeforeIndex, row.SelectedTarget.Index)).ToArray();
            ProjectTrackMatching.Validate(before, after, decisions);
            if (save) await Task.Run(() => store.SaveTrackMatchesAsync(before, after, decisions));
            if (!ReferenceEquals(Before, before) || !ReferenceEquals(After, after)) return;
            _appliedMatches = decisions;
            MatchingStatus = save ? "User-specified matches saved for this project pair." : "User-specified matches for this session; not saved.";
            NeedsMatchReview = false;
        }
        catch (Exception ex)
        {
            Status = "Track matches were not applied: " + ex.Message;
            return;
        }
        finally { IsBusy = false; }
        Rebuild();
    }

    private async Task LoadAsync(bool isBefore)
    {
        IsBusy = true;
        Status = "Choose a project for comparison…";
        try
        {
            var path = await picker.PickProjectAsync();
            if (path is null) return;
            Status = "Reading the selected project…";
            var result = await inspector.InspectAsync(path);
            if (!result.IsSuccess || result.Value is null)
            {
                Status = result.Error?.Code + ": " + result.Error?.Message;
                return;
            }
            if (isBefore) Before = result.Value; else After = result.Value;
        }
        finally
        {
            if (_rebuildCancellation is null) IsBusy = false;
        }
    }

    private void Rebuild()
    {
        _rebuildCancellation?.Cancel();
        OnPropertyChanged(nameof(CanReviewMatches));
        if (Before is null || After is null)
        {
            _rebuildCancellation = null;
            IsBusy = false;
            NeedsMatchReview = false;
            _matchingBefore = _matchingAfter = null;
            _appliedMatches = null;
            TrackMatches.ReplaceAll([]);
            Changes.ReplaceAll([]);
            Status = "Choose both project files. Source files are only read.";
            OnPropertyChanged(nameof(ChangeCount));
            return;
        }
        var before = Before;
        var after = After;
        var category = Category;
        var search = Search;
        var cancellation = new CancellationTokenSource();
        _rebuildCancellation = cancellation;
        IsBusy = true;
        Status = "Comparing project fields…";
        _ = RebuildAsync(before, after, category, search, cancellation);
    }

    private async Task RebuildAsync(ProjectSnapshot before, ProjectSnapshot after, string category,
        string search, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(100, cancellation.Token);
            if (before.Evidence == EvidenceLevel.Confirmed && after.Evidence == EvidenceLevel.Confirmed &&
                (!ReferenceEquals(_matchingBefore, before) || !ReferenceEquals(_matchingAfter, after)))
            {
                var saved = await Task.Run(() => store.GetTrackMatchesAsync(before, after, cancellation.Token), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                _matchingBefore = before;
                _matchingAfter = after;
                _appliedMatches = saved?.Decisions;
                var suggestions = saved?.Decisions ?? ProjectTrackMatching.Suggest(before, after);
                var targets = new[] { new TrackMatchTarget(null, "Leave unmatched") }
                    .Concat(after.Tracks.Select((track, index) => new TrackMatchTarget(index,
                        $"{index + 1}. {track.Name} · {track.Kind}"))).ToArray();
                TrackMatches.ReplaceAll(suggestions.OrderBy(decision => decision.BeforeIndex).Select(decision =>
                    new TrackMatchRowViewModel(decision.BeforeIndex,
                        $"{decision.BeforeIndex + 1}. {before.Tracks[decision.BeforeIndex].Name} · {before.Tracks[decision.BeforeIndex].Kind}",
                        targets, targets[decision.AfterIndex is int target ? target + 1 : 0])));
                MatchingStatus = saved is null
                    ? "Review suggested name/type matches. Export IDs are local to each file. Duplicate names stay unmatched."
                    : $"Loaded user-specified matches saved {saved.SavedUtc.ToLocalTime():g}.";
                NeedsMatchReview = saved is null;
            }
            if (NeedsMatchReview && CanReviewMatches)
            {
                Changes.ReplaceAll([]);
                Status = "Choose how project A tracks correspond to project B, then save the matches or compare once.";
                return;
            }
            var matches = CanReviewMatches ? _appliedMatches : null;
            var changes = await Task.Run(() => ProjectChangePresentation.Filter(diff.Compare(before, after, matches), category, search)
                .ToArray(), cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            Changes.ReplaceAll(changes);
            Status = $"{Changes.Count:N0} differences · {before.Evidence} A · {after.Evidence} B" +
                (CanReviewMatches ? " · " + MatchingStatus : "");
            OnPropertyChanged(nameof(ChangeCount));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Changes.ReplaceAll([]);
            Status = "Comparison failed: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_rebuildCancellation, cancellation))
            {
                _rebuildCancellation = null;
                IsBusy = false;
            }
            cancellation.Dispose();
        }
    }
}

public sealed record TrackMatchTarget(int? Index, string Label);

public sealed partial class TrackMatchRowViewModel(int beforeIndex, string label,
    IReadOnlyList<TrackMatchTarget> targets, TrackMatchTarget selectedTarget) : ObservableObject
{
    public int BeforeIndex { get; } = beforeIndex;
    public string Label { get; } = label;
    public IReadOnlyList<TrackMatchTarget> Targets { get; } = targets;
    [ObservableProperty] private TrackMatchTarget _selectedTarget = selectedTarget;
}
