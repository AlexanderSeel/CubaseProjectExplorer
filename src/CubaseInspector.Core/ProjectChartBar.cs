namespace CubaseInspector.Core;

/// <summary>A counted library distribution with explicit units and evidence.</summary>
public sealed record ProjectChartBar(
    string Label, int Count, double Percentage, EvidenceLevel Evidence, string CountLabel);
