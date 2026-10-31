namespace CubaseInspector.Core;

/// <summary>Deterministic next-review navigation over the currently visible catalog groups.</summary>
public static class PluginReviewQueue
{
    public static int NextIndex(IReadOnlyList<string> states, int currentIndex)
    {
        if (states.Count == 0) return -1;
        for (int step = 1; step <= states.Count; step++)
        {
            var index = (((currentIndex < 0 ? -1 : currentIndex) + step) % states.Count);
            if (PluginCatalogCoverage.Matches("Needs review", states[index]))
                return index;
        }
        return -1;
    }
}
