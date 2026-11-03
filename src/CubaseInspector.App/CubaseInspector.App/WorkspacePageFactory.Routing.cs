using CubaseInspector.Core;
using Microsoft.UI.Xaml;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement RoutingPage()
    {
        var project = shell.Workspace.Project;
        var root = Page("Routing", "Choose a channel to follow its saved inputs, inserts and destinations.");
        if (project is null)
        {
            root.Children.Add(Empty("Select a project to inspect its declared routing."));
            return root;
        }
        if (project.Evidence != EvidenceLevel.Confirmed)
            root.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock { Text = "Some saved connections need review. Include unresolved references to inspect them, or select a channel's info button for source details.",
                Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(FocusedRoutingView(CprSignalFlowPresentation.Create(project)));
        return root;
    }
}
