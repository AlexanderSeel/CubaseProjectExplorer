using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CubaseInspector.App.Services;
using CubaseInspector.Core;

namespace CubaseInspector.App.ViewModels;

public sealed partial class ProjectWorkspaceViewModel : ObservableObject
{
    private readonly ProjectSessionState _session;

    [ObservableProperty] private ProjectSnapshot? _project;
    public ObservableCollection<TrackInfo> Tracks { get; } = [];
    public ObservableCollection<PluginReference> Plugins { get; } = [];
    public ObservableCollection<RoutingEdge> Routes { get; } = [];
    public ObservableCollection<ProjectMediaReference> Media { get; } = [];

    public ProjectWorkspaceViewModel(ProjectSessionState session)
    {
        _session = session;
        session.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(session.SelectedProject)) Project = session.SelectedProject;
        };
        Project = session.SelectedProject;
    }

    public bool HasProject => Project is not null;
    public string ProjectName => Project?.Name ?? "No project selected";
    public string ProjectPath => Project?.FilePath ?? "Select Analyze to choose a project or pick one from the Library.";
    public string Format => Project?.Format ?? "—";
    public string SavedBy => Project?.CreatorVersion ?? "Unknown";
    public string Evidence => Project is null ? "No project open" : EvidencePresentation.Label(Project.Evidence);
    public string Bpm => Project?.Bpm is double bpm
        ? bpm.ToString("0.##") + " BPM"
        : CandidateValue("Tempo event BPM", "Tempo track BPM", "Musical tempo BPM") is { } tempo ? tempo + " BPM · needs review" : "Not listed";
    public string TimeSignature => Project is { TimeSignatureNumerator: int n, TimeSignatureDenominator: int d }
        ? $"{n}/{d}" : CandidateValue("Time signature event") is { } signature ? signature + " · needs review" : "Not listed";
    public string TrackCount => Project is null ? "—" : Project.Tracks.Count.ToString("N0");
    public string TrackEvidence => Project?.Evidence == EvidenceLevel.Candidate
        ? "Track information found · some links need review" : "Tracks read from the project export";
    public string PluginCount => Project is null ? "—" : Project.Plugins.Count.ToString("N0");
    public string PluginEvidence => Project?.Evidence == EvidenceLevel.Candidate
        ? "Plug-in names found · product details may need review"
        : Project?.Plugins.Any(p => p.Evidence == EvidenceLevel.Candidate) == true
            ? "some plug-in details need review"
            : Project?.Plugins.Any(p => p.Evidence == EvidenceLevel.Unknown) == true
            ? "some references are not available in the source" : "plug-ins listed by the project export";
    public string RouteCount => Project is null ? "—" : CprSignalFlowPresentation.Create(Project).Routing.Count.ToString("N0");
    public string RouteEvidence => Project?.Routing.Count > 0 && Project.Evidence == EvidenceLevel.Confirmed
        ? "exported connections" : Project?.Evidence == EvidenceLevel.Candidate
            ? "CPR bus-field interpretation" : "no exported connections";
    public string WarningCount => Project is null ? "0" : Project.Warnings.Count.ToString("N0");
    public string OverviewSummary => Project is null
        ? "Choose a project from the Library to inspect its available evidence."
        : Project.Evidence == EvidenceLevel.Candidate
        ? "Track and plug-in information was found in this project. Some track/channel links and product details may need review; Help & reader FAQ explains what is known and what is still being researched."
            : "Showing fields declared in the project export. Missing values remain unknown.";
    public string RoutingSummary => Project?.Evidence == EvidenceLevel.Candidate
        ? $"{CprSignalFlowPresentation.Create(Project).Tracks.Count:N0} channel details · {CprSignalFlowPresentation.Create(Project).Routing.Count:N0} saved connections found. Some channel links may need review."
        : Project?.Routing.Count > 0
            ? "Connections shown below come from exported project data. Hardware device mappings are not established."
            : "No routing connections were supplied by this project format.";
    public string IoSummary => "Follow how the project connects tracks, plug-ins, buses and outputs. Find audio paths and MIDI parts in the media view.";

    private string? CandidateValue(params string[] fields)
    {
        var values = Project?.NativeMetadataCandidates?.Where(candidate => fields.Contains(candidate.Field, StringComparer.Ordinal))
            .Select(candidate => candidate.Value).Distinct(StringComparer.Ordinal).ToArray();
        return values?.Length switch
        {
            1 => values[0],
            > 1 => "Multiple values",
            _ => null
        };
    }

    partial void OnProjectChanged(ProjectSnapshot? value)
    {
        Tracks.Clear(); Plugins.Clear(); Routes.Clear(); Media.Clear();
        if (value is not null)
        {
            foreach (var item in value.Tracks) Tracks.Add(item);
            foreach (var item in value.Plugins) Plugins.Add(item);
            foreach (var item in value.Routing) Routes.Add(item);
            foreach (var item in value.MediaReferences ?? []) Media.Add(item);
        }
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ProjectName)); OnPropertyChanged(nameof(ProjectPath));
        OnPropertyChanged(nameof(Format)); OnPropertyChanged(nameof(SavedBy));
        OnPropertyChanged(nameof(Evidence)); OnPropertyChanged(nameof(Bpm));
        OnPropertyChanged(nameof(TimeSignature)); OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(TrackEvidence)); OnPropertyChanged(nameof(PluginCount));
        OnPropertyChanged(nameof(PluginEvidence)); OnPropertyChanged(nameof(RouteCount));
        OnPropertyChanged(nameof(RouteEvidence)); OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(OverviewSummary)); OnPropertyChanged(nameof(RoutingSummary));
        OnPropertyChanged(nameof(IoSummary));
    }
}
