namespace CubaseInspector.Core;

/// <summary>
/// Eight synchronized project-grid column proportions. Star weights retain header/row
/// alignment when the workspace is resized and do not require a non-virtualized table.
/// </summary>
public static class LibraryColumnLayout
{
    public static readonly IReadOnlyList<string> Presets =
        ["Balanced", "Project focus", "Counts and dates", "Custom"];

    public const int ColumnCount = 8;
    private static readonly double[] Balanced = [3, 1, 1, 1, 1, 1, 1, 2];
    private static readonly double[] Project = [5, .8, 1.2, .8, .8, .8, .8, 1.8];
    private static readonly double[] Metrics = [2, 1, 1, 1, 1.4, 1.4, 1.3, 2.5];

    public static double[] ForPreset(string? name) => (name switch
    {
        "Project focus" => Project,
        "Counts and dates" => Metrics,
        _ => Balanced
    }).ToArray();

    public static double[] Normalize(IReadOnlyList<double>? weights)
    {
        if (weights is null || weights.Count != ColumnCount ||
            weights.Any(w => !double.IsFinite(w) || w < .4 || w > 12))
            return ForPreset("Balanced");
        return weights.ToArray();
    }

    public static double[] Resize(IReadOnlyList<double> weights, int boundary,
        double deltaPixels, double availablePixels)
    {
        var next = Normalize(weights);
        if (boundary < 0 || boundary >= ColumnCount - 1 ||
            !double.IsFinite(deltaPixels) || !double.IsFinite(availablePixels) ||
            availablePixels < 100) return next;
        var total = next.Sum();
        var diff = deltaPixels / availablePixels * total;
        var left = next[boundary];
        var right = next[boundary + 1];
        // Preserve the pair sum and enforce a usable minimum at every drag step.
        var adjusted = Math.Clamp(left + diff, .4, left + right - .4);
        next[boundary] = adjusted;
        next[boundary + 1] = left + right - adjusted;
        return next;
    }
}
