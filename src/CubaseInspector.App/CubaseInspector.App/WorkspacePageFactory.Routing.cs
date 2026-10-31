using CubaseInspector.Core;
using Microsoft.UI.Xaml;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement RoutingPage()
    {
        var project = shell.Workspace.Project;
        var root = Page("Routing & Signal Flow", shell.Workspace.RoutingSummary);
        if (project is null)
        {
            root.Children.Add(Empty("Select a project to inspect its declared routing."));
            return root;
        }
        if (project.Evidence != EvidenceLevel.Confirmed)
            root.Children.Add(Note("CPR routing interpretation · select a mixer record to explore its bus links and stored values. Bus IDs without a unique matching record stay visible as unresolved destinations."));
        root.Children.Add(FocusedRoutingView(CprSignalFlowPresentation.Create(project)));
        return root;
    }
}
