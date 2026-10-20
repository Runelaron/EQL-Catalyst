using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

/// <summary>Nested horizontal splits form one ordered group; existing controls and saved keys stay alive.</summary>
internal static class PaneLayout
{
    private sealed class Group(Grid grid)
    {
        internal readonly Grid Grid = grid;
        internal UIElement[] Children = [];
        internal ColumnDefinition[] Columns = [];
        internal GridSplitter[] Grips = [];
        internal CharacterProfile? Profile;
        internal string Key = "";
        internal Action? Saved;
        internal readonly HashSet<UIElement> Observed = [];
        internal CascadingDrag Input = null!;
    }
    private sealed class Node(Group? group, ColumnDefinition? column, double width)
    {
        internal readonly Group? Group = group;
        internal readonly ColumnDefinition? Column = column;
        internal readonly double Width = width;
        internal readonly List<Node> Children = [];
        internal double Gutter;
        internal int Leaf = -1;
        internal double Applied;
    }
    private static readonly ConditionalWeakTable<Grid, Group> groups = new();
    internal static void Register(Grid grid, UIElement[] children, ColumnDefinition[] columns, GridSplitter[] grips, CharacterProfile? profile = null, string key = "", Action? saved = null)
    {
        if (!groups.TryGetValue(grid, out var group))
        {
            group = new(grid); groups.Add(grid, group);
            group.Input = new CascadingDrag(grid, thumb => thumb is GridSplitter splitter ? Create(group, splitter) : null);
            grid.IsVisibleChanged += (_, _) => { if (!grid.IsVisible) Cancel(grid); };
        }
        else Cancel(grid);
        group.Children = children; group.Columns = columns; group.Grips = grips; group.Profile = profile; group.Key = key; group.Saved = saved;
        foreach (var child in children.Where(group.Observed.Add)) child.IsVisibleChanged += (_, _) => Cancel(grid);
    }
    internal static void Cancel(Grid grid)
    {
        DependencyObject? current = grid;
        while (current != null)
        {
            if (current is Grid parent && groups.TryGetValue(parent, out var group)) group.Input.Cancel();
            current = current is Visual ? VisualTreeHelper.GetParent(current) : null;
        }
    }
    internal static CascadingDrag.Session? Prepare(Grid grid, GridSplitter grip) => groups.TryGetValue(grid, out var group) ? Create(group, grip) : null;
    private static CascadingDrag.Session? Create(Group group, GridSplitter grip)
    {
        var leaves = new List<Node>(); var boundaries = new Dictionary<GridSplitter, int>();
        Node Build(Group current, ColumnDefinition? parentColumn)
        {
            var node = new Node(current, parentColumn, current.Grid.ActualWidth);
            for (int i = 0; i < current.Children.Length; i++)
            {
                var column = current.Columns[i]; var child = current.Children[i];
                if (child.Visibility != Visibility.Visible || column.ActualWidth <= .01 || column.Width.Value == 0) continue;
                if (i > 0 && node.Children.Count > 0 && current.Grips.ElementAtOrDefault(i - 1) is { Visibility: Visibility.Visible } divider)
                { boundaries[divider] = leaves.Count - 1; node.Gutter += divider.ActualWidth; }
                Node next;
                if (child is Grid nested && groups.TryGetValue(nested, out var nestedGroup)) next = Build(nestedGroup, column);
                else { next = new(null, column, column.ActualWidth) { Leaf = leaves.Count }; leaves.Add(next); }
                node.Children.Add(next);
            }
            return node;
        }
        var tree = Build(group, null);
        if (!boundaries.TryGetValue(grip, out int boundary) || leaves.Count < 2) return null;
        var tracks = leaves.Select(n => new WidthTrack(n.Width, n.Column!.MinWidth, n.Column!.MaxWidth)).ToArray();
        // A parent minimum can exceed the sum of its children's minimums (including inner gutters).
        double Minimum(Node node)
        {
            if (node.Leaf >= 0) return tracks[node.Leaf].Minimum;
            double total = node.Gutter + node.Children.Sum(Minimum);
            double extra = Math.Max(0, (node.Column?.MinWidth ?? 0) - total);
            if (extra > 0)
            {
                var last = node; while (last.Leaf < 0 && last.Children.Count > 0) last = last.Children[^1];
                if (last.Leaf >= 0) { var old = tracks[last.Leaf]; tracks[last.Leaf] = old with { Minimum = Math.Min(old.Width, old.Minimum + extra) }; }
            }
            return total + extra;
        }
        Minimum(tree);
        void Apply(Node node, double[] widths)
        {
            if (node.Leaf >= 0) { node.Applied = widths[node.Leaf]; return; }
            foreach (var child in node.Children) Apply(child, widths);
            node.Applied = node.Gutter + node.Children.Sum(n => n.Applied);
            foreach (var child in node.Children) child.Column!.Width = new(Math.Max(.001, child.Applied), GridUnitType.Star);
        }
        void Save(Node node)
        {
            foreach (var child in node.Children) Save(child);
            if (node.Group is not { } current) return;
            if (current.Profile != null && current.Key.Length > 0 && node.Children.Count == 2)
                current.Profile.PanelSplits[current.Key] = node.Children[1].Applied / Math.Max(1, node.Children.Sum(n => n.Applied));
            current.Saved?.Invoke();
        }
        return new(tracks, boundary, widths => Apply(tree, widths), canceled =>
        {
            if (canceled) return;
            Save(tree); (Window.GetWindow(group.Grid) as FrostWindow)?.SaveLayout();
        });
    }
}
