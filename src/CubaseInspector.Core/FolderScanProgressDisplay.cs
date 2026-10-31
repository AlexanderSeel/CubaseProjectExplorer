namespace CubaseInspector.Core;

/// <summary>Display projection only; folder discovery has no known final total.</summary>
public sealed record FolderScanProgressDisplay(string Stage, string Summary, string CurrentPath,
    bool IsIndeterminate, double Percent)
{
    public static FolderScanProgressDisplay Create(ScanProgress update,
        int readableProjects, int scanErrors, TimeSpan elapsed)
    {
        var seconds = elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        if (update.Stage == ScanStage.Discovering)
            return new("Discovering folders",
                $"{Math.Max(0, update.Completed)} folders explored · {Math.Max(0, update.Total)} candidate project files found · {scanErrors} issues · {seconds}s",
                update.CurrentPath, true, 0);
        var total = Math.Max(0, update.Total);
        var completed = Math.Clamp(update.Completed, 0, total);
        var percent = total == 0 ? 100 : 100d * completed / total;
        var stage = update.Stage == ScanStage.Hashing ? "Hashing project files" : "Inspecting project files";
        var action = update.Stage == ScanStage.Hashing ? "hashed" : "inspected";
        return new(stage,
            $"{completed} / {total} {action} ({percent:0.#}%) · {Math.Max(0, readableProjects)} readable · {Math.Max(0, scanErrors)} issues · {seconds}s",
            update.CurrentPath, false, percent);
    }
}
