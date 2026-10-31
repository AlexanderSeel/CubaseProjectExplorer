using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CubaseInspector.App.Services;
using CubaseInspector.Core;

namespace CubaseInspector.App.ViewModels;

public sealed partial class ProjectToolsViewModel(
    ProjectSessionState session, IReportService reports, ISafeCopyService safeCopy,
    IProjectPathPicker picker) : ObservableObject
{
    private ProjectSnapshot? _mediaAuditSource;
    private ProjectMediaReadinessReport? _mediaAuditReport;

    [ObservableProperty] private string _status = "Reports use only the fields available from the selected project.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasMediaAudit;
    [ObservableProperty] private string _mediaAuditStatus = "Run the check to assess explicitly external DAWproject media paths.";
    [ObservableProperty] private string _mediaAuditSummary = "No media handoff check has been run.";
    public ObservableCollection<string> MediaAuditRows { get; } = [];
    public bool HasCurrentMediaAudit => HasMediaAudit && ReferenceEquals(_mediaAuditSource, session.SelectedProject);

    public void ResetMediaAuditForDifferentProject()
    {
        if (_mediaAuditSource is null || ReferenceEquals(_mediaAuditSource, session.SelectedProject)) return;
        _mediaAuditSource = null;
        _mediaAuditReport = null;
        HasMediaAudit = false;
        MediaAuditRows.Clear();
        MediaAuditSummary = "No media handoff check has been run for this project.";
        MediaAuditStatus = "Run the check to assess explicitly external DAWproject media paths.";
    }

    [RelayCommand]
    private async Task CheckMediaAsync()
    {
        if (session.SelectedProject is not { } project) { MediaAuditStatus = "Select a project first."; return; }
        HasMediaAudit = false;
        MediaAuditRows.Clear();
        _mediaAuditReport = null;
        IsBusy = true;
        Status = "Checking declared external media paths…";
        MediaAuditStatus = "Checking declared external media paths…";
        try
        {
            var report = await Task.Run(() => ProjectMediaReadiness.Analyze(project));
            if (!ReferenceEquals(session.SelectedProject, project))
            {
                MediaAuditStatus = "The selected project changed while the check was running. Run the check again.";
                Status = MediaAuditStatus;
                return;
            }
            _mediaAuditSource = project;
            _mediaAuditReport = report;
            HasMediaAudit = true;
            MediaAuditSummary = report.Summary;
            MediaAuditStatus = report.Limitation;
            Status = report.Summary;
            MediaAuditRows.Clear();
            foreach (var item in report.Items)
                MediaAuditRows.Add($"{item.State} · {item.DeclaredPath} · {item.Detail}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { MediaAuditStatus = "Media check failed: " + ex.Message; Status = MediaAuditStatus; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ExportMediaAuditAsync()
    {
        var report = _mediaAuditReport;
        if (session.SelectedProject is not { } project || !HasCurrentMediaAudit || report is null)
        {
            MediaAuditStatus = "Run the media check for the selected project before exporting.";
            return;
        }
        IsBusy = true;
        Status = "Choose where to save the handoff checklist…";
        MediaAuditStatus = "Choose where to save the handoff checklist…";
        try
        {
            var path = await picker.PickSaveFileAsync(project.Name + "-media-handoff", ".md", "Markdown media handoff checklist");
            if (path is null) return;
            var markdown = await Task.Run(() => ProjectMediaReadiness.Markdown(report));
            await File.WriteAllTextAsync(path, markdown);
            MediaAuditStatus = "Media handoff checklist exported.";
            Status = MediaAuditStatus;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { MediaAuditStatus = "Media checklist export failed: " + ex.Message; Status = MediaAuditStatus; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ExportMarkdownAsync()
    {
        if (session.SelectedProject is not { } project) { Status = "Select a project first."; return; }
        IsBusy = true;
        Status = "Choose where to save the Markdown report…";
        try
        {
            var path = await picker.PickSaveFileAsync(project.Name + "-report", ".md", "Markdown report");
            if (path is null) return;
            Status = "Preparing and writing the Markdown report…";
            var markdown = await Task.Run(() => reports.ToMarkdown(project));
            await File.WriteAllTextAsync(path, markdown);
            Status = "Markdown report saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { Status = "Report export failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CreateUnmodifiedCopyAsync()
    {
        if (session.SelectedProject is not { } project) { Status = "Select a project first."; return; }
        IsBusy = true;
        Status = "Choose where to create the project copy…";
        try
        {
            var path = await picker.PickSaveFileAsync(project.Name + "-copy", ".cpr", "Cubase project copy");
            if (path is null) return;
            Status = "Copying the project without modifying its contents…";
            var result = await safeCopy.CopyUnmodifiedAsync(project.FilePath, path);
            Status = result.IsSuccess ? "Byte-identical safety copy created; plug-ins were not changed."
                : result.Error?.Code + ": " + result.Error?.Message;
        }
        finally { IsBusy = false; }
    }
}
