using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CubaseInspector.Core;

/// <summary>One Reset for large reorders; small incremental scans stay append-only.</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IReadOnlyList<T> ordered)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in ordered) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

public static class ProjectListReconciler
{
    private const int IndividualChangesLimit = 256;

    public static bool Synchronize<T>(BulkObservableCollection<T> target, IReadOnlyList<T> ordered)
        where T : class
    {
        if (target.Count == ordered.Count &&
            target.Zip(ordered).All(pair => ReferenceEquals(pair.First, pair.Second)))
            return false;
        // The common path during a running scan is a small tail append.
        var common = Math.Min(target.Count, ordered.Count);
        var prefix = true;
        for (var i = 0; i < common; i++)
            if (!ReferenceEquals(target[i], ordered[i])) { prefix = false; break; }
        if (prefix)
        {
            // A filter reducing 10k rows to zero is also a large change; avoid
            // thousands of individual removal events even when it is a prefix.
            if (Math.Abs(target.Count - ordered.Count) > IndividualChangesLimit)
            {
                target.ReplaceAll(ordered);
                return true;
            }
            while (target.Count > ordered.Count) target.RemoveAt(target.Count - 1);
            for (var i = target.Count; i < ordered.Count; i++) target.Add(ordered[i]);
            return true;
        }
        if (Math.Max(target.Count, ordered.Count) > IndividualChangesLimit)
        {
            target.ReplaceAll(ordered);
            return true;
        }
        var expected = ordered.ToHashSet();
        for (var i = target.Count - 1; i >= 0; i--)
            if (!expected.Contains(target[i])) target.RemoveAt(i);
        for (var i = 0; i < ordered.Count; i++)
        {
            if (i >= target.Count) target.Add(ordered[i]);
            else if (!ReferenceEquals(target[i], ordered[i]))
            {
                var from = target.IndexOf(ordered[i]);
                if (from >= 0) target.Move(from, i);
                else target.Insert(i, ordered[i]);
            }
        }
        return true;
    }
}
