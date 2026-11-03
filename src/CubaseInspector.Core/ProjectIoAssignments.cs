namespace CubaseInspector.Core;

public sealed record SavedIoAssignment(string Direction, string From, string To, string Connection,
    bool EndpointsResolved, EvidenceLevel Evidence, string Source, long? SourceOffset);

/// <summary>Saved endpoint mappings, with unresolved references kept distinct from usable named assignments.</summary>
public static class ProjectIoAssignments
{
    public static IReadOnlyList<SavedIoAssignment> From(StudioIoDiagram diagram)
    {
        var nodes = diagram.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        bool Resolved(string id) => nodes.TryGetValue(id, out var node) &&
            node.Kind is not ("Bus reference" or "Unresolved track input") && !id.StartsWith("Bus UID ", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(node.Name) && node.Name != "(unnamed CPR record)";
        string Name(string id) => Resolved(id) ? nodes[id].Name : "Assignment not resolved";
        return diagram.Links.Where(link => link.Label is "Input" or "Output" or "Direct output" or "Record" or
                "Child bus" or "Parent bus" or "Saved track input" or "Saved input assignment" or "Saved output assignment")
            .Select(link => new SavedIoAssignment(link.Label switch
            {
                "Input" or "Saved track input" or "Saved input assignment" => "Input",
                "Record" => "Record",
                "Child bus" or "Parent bus" => "Bus layout",
                "Direct output" => "Direct output",
                _ => "Output"
            }, Name(link.From), Name(link.To), link.Label, Resolved(link.From) && Resolved(link.To),
                link.Evidence, link.Source, link.SourceOffset)).ToArray();
    }
}
