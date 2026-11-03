namespace CubaseInspector.Core;

/// <summary>Small page history for predictable in-app back, forward, and breadcrumb navigation.</summary>
public sealed class NavigationHistory(string initialPage)
{
    private readonly List<string> _pages = [initialPage];
    private int _index;

    public string Current => _pages[_index];
    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index < _pages.Count - 1;
    public IReadOnlyList<string> Trail => _pages.Take(_index + 1).ToArray();

    public bool Navigate(string page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(page);
        if (string.Equals(Current, page, StringComparison.Ordinal)) return false;
        _pages.RemoveRange(_index + 1, _pages.Count - _index - 1);
        _pages.Add(page);
        _index = _pages.Count - 1;
        return true;
    }

    public bool Back()
    {
        if (!CanGoBack) return false;
        _index--;
        return true;
    }

    public bool Forward()
    {
        if (!CanGoForward) return false;
        _index++;
        return true;
    }

    public bool JumpTo(int trailIndex)
    {
        if (trailIndex < 0 || trailIndex > _index) return false;
        if (trailIndex == _index) return false;
        _pages.RemoveRange(trailIndex + 1, _pages.Count - trailIndex - 1);
        _index = trailIndex;
        return true;
    }
}
