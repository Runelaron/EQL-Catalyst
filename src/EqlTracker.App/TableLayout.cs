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
        public ScrollViewer? Scroll;
        public readonly Dictionary<DataGridColumn,double> DefaultScales = [];
        public readonly Dictionary<DataGridColumn,string> DefaultSamples = [];
        public double Viewport = -1;
        public DataGridColumn? DragColumn;
        public CascadingDrag? Input;
        public readonly Dictionary<DataGridTextColumn, Style> CellStyles = [];
        public readonly Dictionary<string, double> TypeWidths = [];
    }
    private static readonly ConditionalWeakTable<DataGrid, State> states = new();
    private static readonly ConditionalWeakTable<DataGridColumn, object> noCellTooltip = new();
    public static void SuppressCellTooltip(DataGridColumn column) => noCellTooltip.GetOrCreateValue(column);
    public static void SetAutoMaximum(DataGrid table, DataGridColumn column, double width) => states.GetOrCreateValue(table).AutoMaximum[column] = width;
    public static void SetDefaultScale(DataGrid table, DataGridColumn column, double scale) => states.GetOrCreateValue(table).DefaultScales[column] = scale;
    public static void SetDefaultSample(DataGrid table, DataGridColumn column, string sample)
    {
        var state=states.GetOrCreateValue(table);
        if(state.DefaultSamples.GetValueOrDefault(column)==sample) return;
        state.DefaultSamples[column]=sample;state.Fitted=false;state.Viewport=-1;QueueFit(table,state);
    }
    public static void SetKey(DataGrid table, string key) => states.GetOrCreateValue(table).Key = key;
    internal static DataGridColumn? ResizingColumn(DataGrid table) => states.GetOrCreateValue(table).DragColumn;
    public static void Attach(DataGrid table)
    {
        var state = states.GetOrCreateValue(table);
        ScrollViewer.SetHorizontalScrollBarVisibility(table, ScrollBarVisibility.Auto);
        void AttachColumns()
        { foreach (var column in table.Columns.Where(c => state.Attached.Add(c)))
        {
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.VisibilityProperty, typeof(DataGridColumn)).AddValueChanged(column, (_, _) => { state.Input?.Cancel(); state.Viewport = -1; QueueFit(table, state); });
            foreach (var property in new[] { DataGridColumn.MinWidthProperty, DataGridColumn.MaxWidthProperty })
                DependencyPropertyDescriptor.FromProperty(property, typeof(DataGridColumn)).AddValueChanged(column, (_, _) => { if (state.Applying) return; state.Input?.Cancel(); state.Viewport = -1; QueueFit(table, state); });
        } }
        AttachColumns(); table.Columns.CollectionChanged += (_, _) => { state.Input?.Cancel(); AttachColumns(); state.Viewport = -1; QueueFit(table, state); };
        table.ColumnDisplayIndexChanged += (_, _) => { state.Input?.Cancel(); state.Viewport = -1; QueueFit(table, state); };
        state.Input = new CascadingDrag(table, thumb =>
        {
            if (thumb.Name is not ("PART_LeftHeaderGripper" or "PART_RightHeaderGripper")) return null;
            DependencyObject? node = thumb;
            while (node != null && node is not DataGridColumnHeader) node = VisualTreeHelper.GetParent(node);
            if (node is not DataGridColumnHeader { Column: { } current }) return null;
            var columns = Visible(table); int index = Array.IndexOf(columns, current);
            int boundary = index - (thumb.Name == "PART_LeftHeaderGripper" ? 1 : 0);
            state.DragColumn = boundary >= 0 && boundary < columns.Length - 1 ? columns[boundary] : null;
            return CreateResize(table, state, columns, table.CanUserResizeColumns ? boundary : -1);
        });
        table.Loaded += (_, _) =>
        {
            var owner = Window.GetWindow(table) as FrostWindow; if (state.Window == null && owner != null) { state.Fitted = state.Sampled = false; } state.Window = owner;
            if (state.Key.Length == 0) state.Key = (state.Window?.Title ?? "table") + ":" + string.Join('|', table.Columns.Select(c => c.SortMemberPath));
            string legacy = (state.Window?.Title ?? "table") + ":" + string.Join('|', table.Columns.Select(c => c.SortMemberPath));
            if (state.Window != null && legacy != state.Key && !state.Key.StartsWith("tracker.pet.",StringComparison.Ordinal)) foreach (var column in table.Columns) { string old = legacy + ":" + Path(column), key = Key(state, column); if (!state.Window.LayoutProfile.ColumnWidths.ContainsKey(key) && state.Window.LayoutProfile.ColumnWidths.TryGetValue(old, out double width)) state.Window.LayoutProfile.ColumnWidths[key] = width; }
            state.Scroll = Usability.Tree(table).OfType<ScrollViewer>().FirstOrDefault(p => ReferenceEquals(p.TemplatedParent,table));
            foreach (var column in table.Columns.OfType<DataGridTextColumn>()) ApplyCellStyle(state, column);
            Fit(table, state, false);
            foreach (var bar in Usability.Tree(table).OfType<ScrollBar>().Where(state.Scrollbars.Add))
                bar.IsVisibleChanged += (_, _) => QueueFit(table, state);
            var menu = new ContextMenu(); var reset = new MenuItem { Header = "Reset columns", ToolTip = "Restore measured default column widths for this table." }; reset.Click += (_, _) => Reset(table); if (table.ContextMenu == null) { menu.Items.Add(reset); table.ContextMenu = menu; } else if (!table.ContextMenu.Items.OfType<MenuItem>().Any(i => i.Header?.ToString() == "Reset columns")) { table.ContextMenu.Items.Add(new Separator()); table.ContextMenu.Items.Add(reset); }
        };
        table.SizeChanged += (_, _) => QueueFit(table, state);
        table.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => { if (e.ViewportWidthChange != 0) QueueFit(table, state); }));
        table.Unloaded += (_, _) => state.Input?.Cancel();
    }
    private static string Path(DataGridColumn column) => column.SortMemberPath.Length > 0 ? column.SortMemberPath : (column as DataGridBoundColumn)?.Binding is Binding b ? b.Path?.Path ?? column.Header?.ToString() ?? "" : column.Header?.ToString() ?? "";
    private static string Key(State state, DataGridColumn column) => state.Key + ":" + Path(column);
    private static void QueueFit(DataGrid table, State state)
    {
        if (state.Queued || !table.IsLoaded) return; state.Queued = true;
        table.Dispatcher.BeginInvoke(() => { state.Queued = false; if (!state.Applying && state.Input?.IsDragging != true && !table.IsMouseCaptureWithin) Fit(table, state, true); else state.DeferredResize = true; }, System.Windows.Threading.DispatcherPriority.Render);
    }
    internal static void Prefer(DataGrid table, DataGridColumn column, double width)
    {
        var state = states.GetOrCreateValue(table); state.Preferred[column] = width; state.Manual.Add(Key(state, column));
        if (state.Window != null) state.Window.LayoutProfile.ColumnWidths[Key(state, column)] = width;
        state.Applying = true; column.Width = width; state.Applying = false;
    }
    private static bool Flexible(DataGridColumn column) => Path(column) is "Item" or "Name" or "Ability" or "Target" or "Targets" or "Zone" or "Label" or "SourceName" or "Source" or "Effect" or "Description" or "Details" or "Instructions" or "Quest" or "Replaces" or "Weapons" or "Loadout";
    private static DataGridColumn[] Visible(DataGrid table) => table.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).ToArray();
    private static bool HasTooltip(Style? style) => style != null && (style.Setters.OfType<Setter>().Any(s => s.Property == FrameworkElement.ToolTipProperty) || HasTooltip(style.BasedOn));
    private static void ApplyCellStyle(State state, DataGridTextColumn column)
    {
        if (state.CellStyles.TryGetValue(column, out var installed) && ReferenceEquals(column.ElementStyle, installed)) return;
        var style = new Style(typeof(TextBlock), column.ElementStyle);
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
        if (noCellTooltip.TryGetValue(column, out _)) style.Setters.Add(new Setter(ToolTipService.IsEnabledProperty, false));
        else if (!HasTooltip(column.ElementStyle) && column.Binding is Binding binding) style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(binding.Path.Path)));
        column.ElementStyle = style; state.CellStyles[column] = style;
    }
    private static CascadingDrag.Session CreateResize(DataGrid table, State state, DataGridColumn[] columns, int boundary)
    {
        var tracks = columns.Select(c => new WidthTrack(c.ActualWidth, c.CanUserResize ? c.MinWidth : c.ActualWidth, c.CanUserResize ? c.MaxWidth : c.ActualWidth)).ToArray();
        return new(tracks, boundary, widths =>
        {
            state.Applying = true;
            try { for (int i = 0; i < columns.Length; i++) columns[i].Width = widths[i]; }
            finally { state.Applying = false; }
        }, canceled =>
        {
            state.DragColumn = null;
            if (!canceled) foreach (var column in columns)
            {
                double width = column.Width.Value; string key = Key(state, column);
                state.Preferred[column] = width; state.Manual.Add(key);
                if (state.Window != null) state.Window.LayoutProfile.ColumnWidths[key] = width;
            }
            if (!canceled) state.Window?.SaveLayout();
            if (state.DeferredResize) { state.DeferredResize = false; QueueFit(table, state); }
        });
    }
    internal static void ResizeColumn(DataGrid table, DataGridColumn column, double requested)
    {
        var state = states.GetOrCreateValue(table); if (state.Applying) return;
        var columns = Visible(table); int index = Array.IndexOf(columns, column); if (index < 0 || !column.CanUserResize) return;
        bool last = index == columns.Length - 1;
        var session = CreateResize(table, state, columns, last ? index - 1 : index);
        double delta = Math.Clamp(requested, column.MinWidth, column.MaxWidth) - column.ActualWidth;
        CascadeWidths.Resize(session.Tracks, session.Boundary, last ? -delta : delta, session.Widths);
        session.Apply(session.Widths); session.Finish(false);
    }
    public static void SampleChanged(DataGrid table)
    {
        if (!table.IsLoaded) return; var state = states.GetOrCreateValue(table);
        var typeColumn = table.Columns.FirstOrDefault(c => Path(c) == "Type");
        bool changed=false;
        if (typeColumn != null && table.Items.Count > 0)
        {
            var values=table.Items.Cast<object>().Where(x=>x!=CollectionView.NewItemPlaceholder).Take(200).ToArray();
            var property=values.FirstOrDefault()?.GetType().GetProperty("Type");
            if(property!=null) foreach(string text in values.Select(x=>property.GetValue(x)?.ToString()??"").Distinct())
                if(!state.TypeWidths.ContainsKey(text)) { state.TypeWidths[text]=TypeWidth(table,text); changed=true; }
            // Content affects defaults, never a permanent minimum or a manually chosen width.
            if(changed && !state.Manual.Contains(Key(state,typeColumn))) { state.Preferred.Remove(typeColumn);state.Fitted=false;state.Sampled=false;state.Viewport=-1;QueueFit(table,state); }
        }
        if (!state.Sampled && table.Items.Count > 0) QueueFit(table,state);
    }
    public static void Reset(DataGrid table)
    {
        var state = states.GetOrCreateValue(table);
        state.Input?.Cancel();
        foreach (var c in table.Columns) state.Window?.LayoutProfile.ColumnWidths.Remove(Key(state, c));
        state.Manual.Clear(); state.Preferred.Clear(); state.Viewport = -1; state.Fitted = state.Sampled = false; Fit(table, state, false); state.Window?.SaveLayout();
    }
    private static double TextWidth(DataGrid table, string text) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(table.FontFamily, table.FontStyle, table.FontWeight, table.FontStretch), table.FontSize, Brushes.White, VisualTreeHelper.GetDpi(table).PixelsPerDip).WidthIncludingTrailingWhitespace;
    // A compound armor label retains its full cell tooltip without forcing every
    // future filtered view to reserve space for an entire list of body slots.
    private static double TypeWidth(DataGrid table,string text) => text.Split(" / ",StringSplitOptions.RemoveEmptyEntries).Select(part=>TextWidth(table,part)).DefaultIfEmpty(0).Max()+18;
    internal static void FitSnapshot(DataGrid table) => Fit(table,states.GetOrCreateValue(table),true,true);
    private static void Fit(DataGrid table, State state, bool resized, bool snapshot=false)
    {
        if (state.Applying || !snapshot && !table.IsLoaded || table.ActualWidth < 1 || table.IsMouseCaptureWithin) return;
        string metrics = $"{table.FontFamily.Source}|{table.FontSize}|{table.FontStyle}|{table.FontWeight}|{table.FontStretch}|{VisualTreeHelper.GetDpi(table).PixelsPerDip}";
        if (state.Metrics != metrics) { state.Metrics = metrics; state.Headers.Clear(); state.TypeWidths.Clear(); state.Viewport = -1; state.Fitted = state.Sampled = false; }
        if (state.Fitted && !resized && (state.Sampled || table.Items.Count == 0)) return;
        state.Applying = true;
        try
        {
            double gutter = Usability.Tree(table).OfType<ScrollBar>().Where(b => b.Orientation == Orientation.Vertical && b.IsVisible).Select(b => b.ActualWidth).DefaultIfEmpty(0).Max();
            double available = state.Scroll is { ViewportWidth: > 0 } viewport ? viewport.ViewportWidth : Math.Max(0, table.ActualWidth - gutter - table.BorderThickness.Left - table.BorderThickness.Right);
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
                column.MinWidth = Math.Min(column.MaxWidth, Math.Max(column.MinWidth, header));
                if (saved != null && saved.TryGetValue(key, out double user) && double.IsFinite(user) && user > 0 && user < 10000) { widths[column] = Math.Clamp(user, column.MinWidth, column.MaxWidth); state.Manual.Add(key); continue; }
                bool name = Path(column) is "Item" or "Name" or "Ability" or "Target" or "SourceName" or "Source" or "Effect";
                if (state.Fitted && resized && state.Preferred.TryGetValue(column, out double preferred)) { widths[column] = preferred; continue; }
                var property = values.FirstOrDefault()?.GetType().GetProperty(Path(column));
                var measured = property == null ? [] : values.Select(x => Path(column)=="Type" ? TypeWidth(table,Format(property.GetValue(x),column))-18 : TextWidth(table, Format(property.GetValue(x), column))).ToArray();

                double typical = measured.Length > 0 ? (Path(column)=="Type" ? measured.Max() : measured.Average()) + 20 : name ? 160 : header;
                if(state.DefaultSamples.TryGetValue(column,out string? sample)) typical=TextWidth(table,sample)+20;
                widths[column] = Math.Max(header, Math.Min(name ? 310 : 200, typical) * state.DefaultScales.GetValueOrDefault(column,1));
            }
            foreach (var column in table.Columns) widths[column] = Math.Clamp(widths[column], column.MinWidth, column.MaxWidth);
            foreach (var (column, width) in widths) state.Preferred[column] = width;
            var visible = Visible(table); bool anyFlexible = visible.Any(Flexible);
            var tracks = visible.Select(c =>
            {
                double maximum = state.AutoMaximum.TryGetValue(c, out double limit) && !state.Manual.Contains(Key(state, c)) ? Math.Max(c.MinWidth, Math.Min(c.MaxWidth, limit)) : c.MaxWidth;
                double preferred = Math.Clamp(widths[c], c.MinWidth, maximum);
                return Flexible(c) || !anyFlexible ? new WidthTrack(preferred, c.MinWidth, maximum) : new WidthTrack(preferred, preferred, preferred);
            }).ToArray();
            if (tracks.Sum(t => t.Minimum) > available) for (int i = 0; i < tracks.Length; i++)
                tracks[i] = tracks[i] with { Minimum = visible[i].MinWidth };
            var fitted = new double[tracks.Length]; CascadeWidths.Fit(tracks, available, fitted);
            for (int i = 0; i < visible.Length; i++) visible[i].Width = fitted[i];
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
