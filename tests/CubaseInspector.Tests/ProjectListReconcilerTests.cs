using System.Collections.Specialized;
using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectListReconcilerTests
{
    [Fact]
    public void TenThousandRowsCanReverseWithOneResetAndStableItemReferences()
    {
        var projects = Enumerable.Range(0, 10000).Select(i => new object()).ToArray();
        var current = new BulkObservableCollection<object>();
        current.ReplaceAll(projects);
        var selected = projects[4280];
        var actions = new List<NotifyCollectionChangedAction>();
        current.CollectionChanged += (_, e) => actions.Add(e.Action);

        Assert.True(ProjectListReconciler.Synchronize(current, projects.Reverse().ToArray()));
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, actions);
        Assert.Same(selected, current[10000 - 1 - 4280]);
        actions.Clear();
        Assert.False(ProjectListReconciler.Synchronize(current, projects.Reverse().ToArray()));
        Assert.Empty(actions);
    }

    [Fact]
    public void ProgressiveScanAppendsWithoutClearingVirtualizedRows()
    {
        var rows = Enumerable.Range(0, 1200).Select(i => new object()).ToArray();
        var collection = new BulkObservableCollection<object>();
        collection.ReplaceAll(rows[..1000]);
        var events = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, e) => events.Add(e.Action);
        ProjectListReconciler.Synchronize(collection, rows);
        Assert.Equal(200, events.Count);
        Assert.All(events, x => Assert.Equal(NotifyCollectionChangedAction.Add, x));
        Assert.Same(rows[0], collection[0]);
    }

    [Fact]
    public void SmallListsMoveInsteadOfResetAndSupportFiltering()
    {
        var first = new object();
        var second = new object();
        var third = new object();
        var collection = new BulkObservableCollection<object>();
        collection.ReplaceAll([first, second, third]);
        var actions = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, e) => actions.Add(e.Action);
        ProjectListReconciler.Synchronize(collection, [third, first]);
        Assert.Equal(new[] { third, first }, collection);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void FilteringThousandsOfRowsToZeroEmitsOneResetInsteadOfThousandsOfRemovals()
    {
        var collection = new BulkObservableCollection<object>();
        collection.ReplaceAll(Enumerable.Range(0, 10000).Select(_ => new object()).ToArray());
        var actions = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, e) => actions.Add(e.Action);
        Assert.True(ProjectListReconciler.Synchronize(collection, Array.Empty<object>()));
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, actions);
        Assert.Empty(collection);
    }

    [Fact]
    public void BulkResetRetainsReferenceIdentityButDoesNotDuplicateItems()
    {
        var source = Enumerable.Range(0, 300).Select(i => new object()).ToArray();
        var collection = new BulkObservableCollection<object>();
        collection.ReplaceAll(source);
        ProjectListReconciler.Synchronize(collection, source.Reverse().ToArray());
        Assert.Equal(300, collection.Count);
        Assert.Equal(300, collection.Distinct().Count());
    }
}
