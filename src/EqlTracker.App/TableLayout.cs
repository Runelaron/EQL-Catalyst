using System.Collections;
using System.ComponentModel;
using System.Windows.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

// Defaults are measured once per table/population, never on combat refreshes or column drags.
public static class TableLayout
{
    private sealed class State
    {
        public bool Fitted, Sampled, Applying;
        public string Key = "";
        public FrostWindow? Window;
        public readonly HashSet<string> Manual = [];
        public readonly Dictionary<DataGridColumn, double> Preferred = [];
        public readonly Dictionary<DataGridColumn, (string Caption, double Width)> Headers = [];
        public string Metrics = "";
        public readonly HashSet<DataGridColumn> Attached = [];
        public readonly HashSet<ScrollBar> Scrollbars = [];
        public readonly Dictionary<DataGridColumn, double> AutoMaximum = [];
        public bool Queued, DeferredResize;
        public ScrollContentPresenter? ContentViewport;
        public double Viewport = -1;
        public DataGridColumn? DragColumn;
        public readonly Dictionary<string, double> TypeWidths = [];
    }
    private static readonly ConditionalWeakTable<DataGrid, State> states = new();
    private static readonly ConditionalWeakTable<DataGridColumn, object> noCellTooltip = new();
    public static void SuppressCellTooltip(DataGridColumn column) => noCellTooltip.GetOrCreateValue(column);
    public static void SetAutoMaximum(DataGrid table, DataGridColumn column, double width) => states.GetOrCreateValue(table).AutoMaximum[column] = width;
    public static void SetKey(DataGrid table, string key) => states.GetOrCreateValue(table).Key = key;
    internal static DataGridColumn? ResizingColumn(DataGrid table) => states.GetOrCreateValue(table).DragColumn;
    public static void Attach(DataGrid table)
    {
        var state = states.GetOrCreateValue(table);
        ScrollViewer.SetHorizontalScrollBarVisibility(table, ScrollBarVisibility.Auto);
        void AttachColumns()
        { foreach (var column in table.Columns.Where(c => state.Attached.Add(c)))
        {
            if (column is DataGridTextColumn textColumn)
            {
                var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
                if (noCellTooltip.TryGetValue(column, out _)) style.Setters.Add(new Setter(ToolTipService.IsEnabledProperty, false));
                else if (textColumn.Binding is System.Windows.Data.Binding binding) style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(binding.Path.Path)));
                textColumn.ElementStyle = style;
            }
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn)).AddValueChanged(column, (_, _) =>
            {
                if (state.Applying || state.Window == null || !table.IsMouseCaptureWithin || Mouse.LeftButton != MouseButtonState.Pressed) return;
                string key = Key(state, column); state.Manual.Add(key); state.Window.LayoutProfile.ColumnWidths[key] = column.Width.IsAbsolute ? column.Width.Value : column.ActualWidth;
                state.Preferred[column] = column.Width.IsAbsolute ? column.Width.Value : column.ActualWidth;
                if (state.DragColumn == column) ResizeColumn(table, column, state.Preferred[column]);
            });
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.VisibilityProperty, typeof(DataGridColumn)).AddValueChanged(column, (_, _) => { state.Viewport = -1; QueueFit(table, state); });
        } }
        AttachColumns(); table.Columns.CollectionChanged += (_, _) => { AttachColumns(); state.Viewport = -1; QueueFit(table, state); };
        table.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, e) =>
        {
            var node = e.OriginalSource as DependencyObject;
            while (node != null && node is not DataGridColumnHeader) node = VisualTreeHelper.GetParent(node);
            state.DragColumn = (node as DataGridColumnHeader)?.Column;
            if (e.OriginalSource is FrameworkElement { Name: "PART_LeftHeaderGripper" } && state.DragColumn is { DisplayIndex: > 0 } current)
                state.DragColumn = table.Columns.Where(c => c.Visibility == Visibility.Visible && c.DisplayIndex < current.DisplayIndex).OrderByDescending(c => c.DisplayIndex).FirstOrDefault();
        }), true);
        table.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { state.DragColumn = null; state.Window?.SaveLayout(); }), true);
        table.Loaded += (_, _) =>
        {
            var owner = Window.GetWindow(table) as FrostWindow; if (state.Window == null && owner != null) { state.Fitted = state.Sampled = false; } state.Window = owner;
            if (state.Key.Length == 0) state.Key = (state.Window?.Title ?? "table") + ":" + string.Join('|', table.Columns.Select(c => c.SortMemberPath));
            string legacy = (state.Window?.Title ?? "table") + ":" + string.Join('|', table.Columns.Select(c => c.SortMemberPath));
            if (state.Window != null && legacy != state.Key) foreach (var column in table.Columns) { string old = legacy + ":" + Path(column), key = Key(state, column); if (!state.Window.LayoutProfile.ColumnWidths.ContainsKey(key) && state.Window.LayoutProfile.ColumnWidths.TryGetValue(old, out double width)) state.Window.LayoutProfile.ColumnWidths[key] = width; }
            state.ContentViewport = Usability.Tree(table).OfType<ScrollContentPresenter>().FirstOrDefault(p => p.Name == "PART_ScrollContentPresenter");
            Fit(table, state, false);
            foreach (var bar in Usability.Tree(table).OfType<ScrollBar>().Where(state.Scrollbars.Add))
                bar.IsVisibleChanged += (_, _) => QueueFit(table, state);
            var menu = new ContextMenu(); var reset = new MenuItem { Header = "Reset columns", ToolTip = "Restore measured default column widths for this table." }; reset.Click += (_, _) => Reset(table); if (table.ContextMenu == null) { menu.Items.Add(reset); table.ContextMenu = menu; } else if (!table.ContextMenu.Items.OfType<MenuItem>().Any(i => i.Header?.ToString() == "Reset columns")) { table.ContextMenu.Items.Add(new Separator()); table.ContextMenu.Items.Add(reset); }
        };
        table.SizeChanged += (_, _) => QueueFit(table, state);
        table.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => { if (e.ViewportWidthChange != 0) QueueFit(table, state); }));
        table.PreviewMouseLeftButtonUp += (_, _) => { if (state.Manual.Count > 0) state.Window?.SaveLayout(); if (state.DeferredResize) { state.DeferredResize = false; QueueFit(table, state); } };
    }
    private static string Path(DataGridColumn column) => column.SortMemberPath.Length > 0 ? column.SortMemberPath : (column as DataGridBoundColumn)?.Binding is Binding b ? b.Path?.Path ?? column.Header?.ToString() ?? "" : column.Header?.ToString() ?? "";
    private static string Key(State state, DataGridColumn column) => state.Key + ":" + Path(column);
    private static void QueueFit(DataGrid table, State state)
    {
        if (state.Queued || !table.IsLoaded) return; state.Queued = true;
        table.Dispatcher.BeginInvoke(() => { state.Queued = false; if (!state.Applying && !table.IsMouseCaptureWithin) Fit(table, state, true); else if (table.IsMouseCaptureWithin && Math.Abs(table.ActualWidth - state.Viewport) > 24) state.DeferredResize = true; }, System.Windows.Threading.DispatcherPriority.Render);
    }
    internal static void Prefer(DataGrid table, DataGridColumn column, double width)
    {
        var state = states.GetOrCreateValue(table); state.Preferred[column] = width; state.Manual.Add(Key(state, column));
        if (state.Window != null) state.Window.LayoutProfile.ColumnWidths[Key(state, column)] = width;
        state.Applying = true; column.Width = width; state.Applying = false;
    }
    private static bool Flexible(DataGridColumn column) => Path(column) is "Item" or "Name" or "Ability" or "Target" or "Targets" or "Zone" or "Label" or "SourceName" or "Source" or "Effect" or "Description" or "Details" or "Instructions" or "Quest" or "Replaces" or "Weapons" or "Loadout";
    internal static void ResizeColumn(DataGrid table, DataGridColumn column, double requested)
    {
        var state = states.GetOrCreateValue(table); if (state.Applying) return;
        var visible = table.Columns.Where(c => c.Visibility == Visibility.Visible).ToArray();
        var widths = visible.ToDictionary(c => c, c => c == column ? Math.Max(c.MinWidth, requested) : c.ActualWidth);
        double available = state.Viewport > 0 ? state.Viewport : table.ActualWidth;
        double extra = available - widths.Values.Sum(); var others = visible.Where(c => c != column && Flexible(c)).ToList();
        if (others.Count == 0) others = visible.Where(c => c != column && Path(c) is "Type" or "Ownership").ToList();
        if (extra >= 0 && others.Count > 0) foreach (var other in others) widths[other] += extra / others.Count;
        else if (extra < 0)
        {
            double capacity = others.Sum(c => Math.Max(0, widths[c] - c.MinWidth));
            if (capacity > 0) foreach (var other in others) widths[other] -= Math.Min(-extra, capacity) * Math.Max(0, widths[other] - other.MinWidth) / capacity;
        }
        state.Applying = true;
        try { foreach (var (c, width) in widths) { c.Width = width; state.Preferred[c] = width; state.Manual.Add(Key(state, c)); if (state.Window != null) state.Window.LayoutProfile.ColumnWidths[Key(state, c)] = width; } }
        finally { state.Applying = false; }
    }
    public static void SampleChanged(DataGrid table)
    {
        if (!table.IsLoaded) return; var state = states.GetOrCreateValue(table);
        var typeColumn = table.Columns.FirstOrDefault(c => Path(c) == "Type");
        if (typeColumn != null && table.Items.Count > 0)
        {
            var values = table.Items.Cast<object>().Where(x => x != CollectionView.NewItemPlaceholder).Take(200).ToArray();
            var property = values.FirstOrDefault()?.GetType().GetProperty("Type");
            if (property != null) foreach (string text in values.Select(x => property.GetValue(x)?.ToString() ?? "").Distinct())
            {
                if (!state.TypeWidths.TryGetValue(text, out double width)) state.TypeWidths[text] = width = TypeWidth(table, text);
                if (typeColumn.MinWidth + .25 < width) { typeColumn.MinWidth = width; state.Viewport = -1; QueueFit(table, state); }
            }
        }
        if (!state.Sampled && table.Items.Count > 0) Fit(table, state, false);
    }
    public static void Reset(DataGrid table)
    {
        var state = states.GetOrCreateValue(table);
        foreach (var c in table.Columns) state.Window?.LayoutProfile.ColumnWidths.Remove(Key(state, c));
        state.Manual.Clear(); state.Preferred.Clear(); state.Viewport = -1; state.Fitted = state.Sampled = false; Fit(table, state, false); state.Window?.SaveLayout();
    }
    private static double TextWidth(DataGrid table, string text) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(table.FontFamily, table.FontStyle, table.FontWeight, table.FontStretch), table.FontSize, Brushes.White, VisualTreeHelper.GetDpi(table).PixelsPerDip).WidthIncludingTrailingWhitespace;
    // A compound armor label retains its full cell tooltip without forcing every
    // future filtered view to reserve space for an entire list of body slots.
    private static double TypeWidth(DataGrid table,string text) => text.Split(" / ",StringSplitOptions.RemoveEmptyEntries).Select(part=>TextWidth(table,part)).DefaultIfEmpty(0).Max()+18;
    private static void Fit(DataGrid table, State state, bool resized)
    {
        if (state.Applying || !table.IsLoaded || table.ActualWidth < 1 || table.IsMouseCaptureWithin) return;
        string metrics = $"{table.FontFamily.Source}|{table.FontSize}|{table.FontStyle}|{table.FontWeight}|{table.FontStretch}|{VisualTreeHelper.GetDpi(table).PixelsPerDip}";
        if (state.Metrics != metrics) { state.Metrics = metrics; state.Headers.Clear(); state.Viewport = -1; state.Fitted = state.Sampled = false; }
        if (state.Fitted && !resized && (state.Sampled || table.Items.Count == 0)) return;
        state.Applying = true;
        try
        {
            double gutter = Usability.Tree(table).OfType<ScrollBar>().Where(b => b.Orientation == Orientation.Vertical && b.IsVisible).Select(b => b.ActualWidth).DefaultIfEmpty(0).Max();
            double available = state.ContentViewport is { ActualWidth: > 0 } viewport ? viewport.ActualWidth : Math.Max(0, table.ActualWidth - gutter - table.BorderThickness.Left - table.BorderThickness.Right);
            if (table.HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row)) available -= table.RowHeaderActualWidth;
            if (resized && state.Fitted && Math.Abs(state.Viewport - available) < .25 && table.Columns.All(state.Preferred.ContainsKey)) return;
            var values = resized && state.Fitted && table.Columns.All(state.Preferred.ContainsKey) ? [] : table.Items.Cast<object>().Where(x => x is not CollectionViewGroup && x != CollectionView.NewItemPlaceholder).Take(200).ToArray();
            var widths = new Dictionary<DataGridColumn, double>();
            var saved = state.Window?.LayoutProfile.ColumnWidths;
            foreach (var column in table.Columns)
            {
                string key = Key(state, column), caption = column.Header?.ToString() ?? "";
                if (!state.Headers.TryGetValue(column, out var measuredHeader) || measuredHeader.Caption != caption)
                    state.Headers[column] = measuredHeader = (caption, Math.Ceiling(TextWidth(table, caption) + (column.CanUserSort ? 30 : 16)));
                double header = measuredHeader.Width;
                column.MaxWidth = double.PositiveInfinity; column.MinWidth = Math.Max(column.MinWidth, header);
                if (saved != null && saved.TryGetValue(key, out double user) && double.IsFinite(user) && user >= header && user < 10000) { widths[column] = user; state.Manual.Add(key); continue; }
                bool name = Path(column) is "Item" or "Name" or "Ability" or "Target" or "SourceName" or "Source" or "Effect";
                if (state.Fitted && resized && state.Preferred.TryGetValue(column, out double preferred)) { widths[column] = preferred; continue; }
                var property = values.FirstOrDefault()?.GetType().GetProperty(Path(column));
                var measured = property == null ? [] : values.Select(x => Path(column)=="Type" ? TypeWidth(table,Format(property.GetValue(x),column))-18 : TextWidth(table, Format(property.GetValue(x), column))).ToArray();
                if (Path(column) == "Type" && measured.Length > 0) column.MinWidth = Math.Max(column.MinWidth, measured.Max() + 18);
                double typical = measured.Length > 0 ? measured.Average() + 20 : name ? 160 : header;
                widths[column] = Math.Max(header, Math.Min(name ? 310 : 200, typical));
            }
            foreach (var column in table.Columns) widths[column] = Math.Max(column.MinWidth, widths[column]);
            foreach (var (column, width) in widths) state.Preferred[column] = width;
            // Pane resizes redistribute text space using cached preferences. Header dragging does not refit neighbors.
            if (!state.Fitted || resized || state.Manual.Count == 0)
            {
                double extra = available - widths.Where(p => p.Key.Visibility == Visibility.Visible).Sum(p => p.Value);
                var flexible = table.Columns.Where(c => c.Visibility == Visibility.Visible && Flexible(c)).ToList();
                if (extra > 0 && flexible.Count > 0) foreach (var column in flexible) widths[column] += extra / flexible.Count;
                else if (extra < 0) foreach (var column in flexible.OrderByDescending(c => widths[c])) { double reduction = Math.Min(-extra, Math.Max(0, widths[column] - column.MinWidth)); widths[column] -= reduction; extra += reduction; }
            }
            foreach (var (column, width) in widths) column.Width = new(Math.Max(column.MinWidth, state.AutoMaximum.TryGetValue(column, out double limit) && !state.Manual.Contains(Key(state, column)) ? Math.Min(width, limit) : width));
            state.Viewport = available; state.Fitted = true; state.Sampled |= values.Length > 0;
        }
        finally { state.Applying = false; }
    }
    private static string Format(object? value, DataGridColumn column)
    {
        string? format = (column as DataGridTextColumn)?.Binding?.StringFormat;
        try { return format == null ? value?.ToString() ?? "" : string.Format(CultureInfo.CurrentCulture, format, value); } catch (FormatException) { return value?.ToString() ?? ""; }
    }
}
