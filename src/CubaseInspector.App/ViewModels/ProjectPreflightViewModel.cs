using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CubaseInspector.App.Services;
using CubaseInspector.Core;

namespace CubaseInspector.App.ViewModels;

public sealed partial class ProjectPreflightViewModel(
    ProjectSessionState session, PluginInventoryViewModel inventory, IPluginScannerService scanner,
    IProjectPathPicker picker) : ObservableObject
{
    private readonly ProjectPreflightService _service = new();
    [ObservableProperty] private string _summary = "Select a project to review its dependencies.";
    [ObservableProperty] private string _evidence = "No project selected";
    public ObservableCollection<string> Rows { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task ExportMarkdownAsync() => await ExportAsync(".md", "Markdown report", report => _service.Markdown(report));

    [RelayCommand]
    private async Task ExportCsvAsync() => await ExportAsync(".csv", "CSV report", report => _service.Csv(report));

    private async Task ExportAsync(string extension, string description, Func<PreflightReport, string> formatter)
    {
        if (session.SelectedProject is not { } project)
        {
            Status = "Select a project before exporting a preflight report.";
            return;
        }
        IsBusy = true;
        Status = "Choose where to save the dependency report…";
        try
        {
            var path = await picker.PickSaveFileAsync(project.Name + "-dependencies", extension, description);
            if (path is null) return;
            Status = "Building and writing the dependency report…";
            var checks = inventory.HasScanned ? scanner.Check(project, inventory.Plugins.ToArray()) : null;
            var report = await Task.Run(() => _service.Analyze(project, checks));
            var contents = await Task.Run(() => formatter(report));
            await File.WriteAllTextAsync(path, contents);
            Status = $"Preflight report saved: {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Status = "Preflight export failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    public void Refresh()
    {
        Rows.Clear();
        Warnings.Clear();
        if (session.SelectedProject is not { } project)
        {
            Summary = "Select a project to review its dependencies.";
            Evidence = "No project selected";
            return;
        }
        var checks = inventory.HasScanned ? scanner.Check(project, inventory.Plugins.ToArray()) : null;
        var report = _service.Analyze(project, checks);
        Evidence = $"{project.Format} · {project.Evidence} evidence";
        Summary = project.Evidence == EvidenceLevel.Confirmed
            ? $"{report.Plugins.Count:N0} plug-in dependencies found · {report.UnresolvedCandidateMarkers:N0} project references need review · local inventory {(checks is null ? "not checked" : "checked")}."
            : $"Plug-in dependencies need review · {report.UnresolvedCandidateMarkers:N0} project references could not be matched to a saved plug-in instance.";
        foreach (var dependency in report.Plugins)
            Rows.Add($"{dependency.Name}  ·  {dependency.Manufacturer ?? "Vendor unknown"}  ·  {dependency.Format}  ·  {dependency.Instances:N0} instance(s)  ·  {dependency.LocalEvidence}{(dependency.FilenameMatchPath is null ? "" : "  ·  " + dependency.FilenameMatchPath)}");
        foreach (var warning in report.Warnings) Warnings.Add(warning);
    }
}
