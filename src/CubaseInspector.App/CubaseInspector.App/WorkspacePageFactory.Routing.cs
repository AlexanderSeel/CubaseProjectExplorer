using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CubaseInspector.App.ViewModels;
using CubaseInspector.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

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
        {
            var observations = project.NativeMixerObservations ?? [];
            root.Children.Add(Note("Mixer values below are read from CPR named records. Channel ownership and signal connections are not verified, so no signal-flow lines are drawn."));
            if (observations.Count == 0)
            {
                root.Children.Add(Empty("No named mixer records were decoded from this file. The reader kept other project observations and warnings."));
                return root;
            }

            root.Children.Add(new TextBlock
            {
                Text = $"MIXER RECORDS · {observations.Count:N0} · UNVERIFIED",
                FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 8)
            });
            var mixerItems = new ItemsControl
            {
                ItemsSource = observations.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(item =>
                    {
                        var rawFields = string.Join("\n", (item.CandidateFields ?? []).Take(12)
                            .Select(field => $"{field.Path} = {field.Value} ({field.Type}; unit/meaning unknown)"));
                        return $"{item.Name}\n\nVolume anchor  {item.VolumeDb?.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + " dB · candidate" ?? "Not decoded"}\nPan  {item.Pan?.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) ?? "Not decoded"}\nMute  {Flag(item.Mute)}     Solo  {Flag(item.Solo)}" +
                            (rawFields.Length > 0 ? "\n\nOther persisted mixer-like values · raw candidates\n" + rawFields : "") +
                            $"\n\n{item.SourceStream} · offset 0x{item.ArchiveOffset:X}\nUnverified CPR record; channel ownership and field semantics are not confirmed.";
                    })
                    .ToArray(),
                ItemTemplate = new DataTemplate(() =>
                {
                    var cardText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
                    cardText.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new(".") });
                    return new Border
                    {
                        Background = Surface, BorderBrush = new SolidColorBrush(Color.FromArgb(255, 74, 83, 94)),
                        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8),
                        Child = cardText
                    };
                })
            };
            root.Children.Add(mixerItems);
            return root;

            static string Flag(bool? value) => value switch { true => "On", false => "Off", _ => "Unknown" };
        }
        root.Children.Add(FocusedRoutingView(project));
        return root;
    }
}
