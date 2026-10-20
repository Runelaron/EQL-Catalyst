using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

public static class UI
{
    public static void SetPressed(Button button, bool pressed) => Usability.SetIsPressed(button, pressed);
    public static SolidColorBrush Brush(string resource) => (SolidColorBrush)Application.Current.Resources[resource];
    public static TextBlock Text(string text, double size = 14, string color = "Foreground", bool bold = false)
    { var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 4) }; t.SetResourceReference(TextBlock.ForegroundProperty, color); return t; }
    public static TextBlock Label(string text) => Text(text.ToUpperInvariant(), 11, "SecondaryText", true);
    public static UIElement Field(string title, FrameworkElement control)
    {
        var label = Label(title); label.ToolTip = control.ToolTip; label.TextWrapping = TextWrapping.NoWrap;
        var grid = new Grid { Margin = new(0, 0, 5, 4), VerticalAlignment = VerticalAlignment.Bottom };
        grid.RowDefinitions.Add(new() { Height = new(19) }); grid.RowDefinitions.Add(new() { Height = new(28) });
        control.VerticalAlignment = VerticalAlignment.Center; control.HorizontalAlignment = double.IsNaN(control.Width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left; control.Margin = new(0); Grid.SetRow(control, 1);
        grid.Children.Add(label); grid.Children.Add(control);Usability.DescribeField(title,control,label); return grid;
    }
    public static UIElement ActionField(FrameworkElement control) => Field("", control);
    public static Button Button(string label, Action action, bool accent = false)
    { var b = new Button { Content = label }; if (accent) { b.SetResourceReference(Control.ForegroundProperty, "Accent");  } b.Click += (_, _) => action(); b.Loaded += (_, _) => { if (accent && b.Template?.FindName("Face", b) != null) b.ClearValue(Control.ForegroundProperty); Usability.Tooltips(b); }; return b; }
    public static Button CommitButton(string label, Action action) { var button = Button(label, action); FocusedLayout.SetIsPrimary(button, true); return button; }
    public static StackPanel Stack(params UIElement[] children) { var p = new StackPanel(); foreach (var c in children) p.Children.Add(c); return p; }
    public static StackPanel Row(params UIElement[] children) { var p = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var c in children) p.Children.Add(c); return p; }
    public static Border Card(UIElement child, Thickness? margin = null)
    { var b = new Border { Child = child, CornerRadius = new(3), Padding = new(6), Margin = margin ?? new(0, 0, 0, 6), BorderThickness = new(1) }; b.SetResourceReference(Border.BackgroundProperty, "InnerSurface"); b.SetResourceReference(Border.BorderBrushProperty, "Edge"); return b; }
    public static TextBox Input(string text = "", double width = double.NaN) => new() { Text = text, Width = width };
    public static ComboBox Select(IEnumerable<string> items, string selected, double width = 160)
    { var b = new ComboBox { ItemsSource = items.ToArray(), Width = width, SelectedItem = selected }; if (b.SelectedIndex < 0) b.SelectedIndex = 0; return b; }
    public static DataGrid Table(params (string Label, string Path, double Width, string? Format)[] columns)
    {
        var grid = new DataGrid();
        VirtualizingPanel.SetScrollUnit(grid, ScrollUnit.Pixel); VirtualizingPanel.SetIsVirtualizingWhenGrouping(grid, true); VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        foreach (var (label, path, width, format) in columns)
        { var binding = new Binding(path); if (format != null) binding.StringFormat = "{0:" + format + "}"; grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = binding, Width = width == 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width), SortMemberPath = path }); }
        TableLayout.Attach(grid); return grid;
    }
    public static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    public static long ScrollInteractionRevision;
    private static string RowKey(object? item) => item switch
    {
        EquipmentPosition row => row.Id,
        BuildContribution row => row.Stat,
        GroupMemberRow row => row.Id,
        EncounterHistoryRow row => row.Id,
        LoadoutRow row => row.Loadout + "\0" + row.Pet + "\0" + row.Weapons + "\0" + row.Buffs,
        ObjectiveRow row => row.QuestId + "\0" + row.Key,
        GearRow row => row.Name + "\0" + row.PositionId + "\0" + row.Instance?.Location + "\0" + row.Instance?.GameId,
        TargetRow row => row.Target.ToLowerInvariant(),
        HealingPartyRow row => row.Role + "\0" + row.Name.ToLowerInvariant(),
        DamageRow row => row.Kind + "\0" + row.Ability + "\0" + row.Channel,
        _ => item?.ToString() ?? ""
    };
    private static bool SameRow<T>(T before, T after) => (before, after) switch
    {
        (ObjectiveRow a, ObjectiveRow b) => a.Item == b.Item && a.Class == b.Class && a.Quantity == b.Quantity && a.TotalOwned == b.TotalOwned && a.Zone == b.Zone && a.Mob == b.Mob && a.Status == b.Status && a.Availability == b.Availability && a.Location == b.Location && a.Instructions == b.Instructions && a.Sources.SequenceEqual(b.Sources),
        (GearRow a, GearRow b) => a.Type == b.Type && a.Name == b.Name && a.Slot == b.Slot && a.Replaces == b.Replaces && a.Delta == b.Delta && a.BuffedDelta == b.BuffedDelta && a.Confidence == b.Confidence && a.Details == b.Details && a.Ownership == b.Ownership && a.Verdict == b.Verdict && a.ItemScore == b.ItemScore && a.Item.Revision == b.Item.Revision && a.Item.MetadataVersion == b.Item.MetadataVersion && a.Comparison?.Evaluation?.Buffed.Quality == b.Comparison?.Evaluation?.Buffed.Quality,
        _ => EqualityComparer<T>.Default.Equals(before, after)
    };
    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindVisual<T>(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
    public static void SetRows<T>(DataGrid table, List<T> rows)
    {
        using var measurement = UpdateDiagnostics.Measure("Rows apply");
        if (table.ItemsSource is IEnumerable<T> unchanged && unchanged.SequenceEqual(rows)) return;
        string selected = RowKey(table.SelectedItem);
        if (table.ItemsSource is System.Collections.ObjectModel.ObservableCollection<T> stable)
        {
            // Keep the view, grouping, sort descriptors, scroll host and unchanged row instances.
            var view = CollectionViewSource.GetDefaultView(stable);
            using (UpdateDiagnostics.Measure("Stable row changes"))
            {
                var desired = rows.Select(x => RowKey(x)).ToHashSet();
                for (int i = stable.Count - 1; i >= 0; i--) if (!desired.Contains(RowKey(stable[i]))) stable.RemoveAt(i);
                for (int i = 0; i < rows.Count; i++)
                {
                    string key = RowKey(rows[i]); int found = i < stable.Count && RowKey(stable[i]) == key ? i : -1;
                    if (found < 0) for (int j = i + 1; j < stable.Count; j++) if (RowKey(stable[j]) == key) { found = j; break; }
                    if (found < 0) stable.Insert(i, rows[i]);
                    else { if (found != i) stable.Move(found, i); if (!SameRow(stable[i], rows[i])) stable[i] = rows[i]; }
                }
                while (stable.Count > rows.Count) stable.RemoveAt(stable.Count - 1);
            }
            if (selected.Length > 0) table.SelectedItem = stable.FirstOrDefault(row => RowKey(row) == selected);
        }
        else table.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<T>(rows);
        TableLayout.SampleChanged(table);
    }
    public static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll) return scroll;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (FindScroll(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
    public static void OpenUrl(string url)
    { if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host.Equals("eqlwiki.com", StringComparison.OrdinalIgnoreCase)) Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
    public static Grid Split(UIElement left, UIElement right, double rightWidth = 320, CharacterProfile? profile = null, string key = "", double leftMin = 0, double rightMin = 0)
    {
        var grid = new Grid(); double fraction = profile?.PanelSplits.GetValueOrDefault(key, .3) ?? .3;
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, .01, .99) : .3;
        grid.ColumnDefinitions.Add(new() { Width = new(1 - fraction, GridUnitType.Star), MinWidth = leftMin });
        grid.ColumnDefinitions.Add(new() { Width = new(5) });
        grid.ColumnDefinitions.Add(new() { Width = profile != null ? new(fraction, GridUnitType.Star) : new(rightWidth), MinWidth = rightMin });
        var grip = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Background = Brushes.Transparent, Cursor = Cursors.SizeWE, ToolTip = "Drag to resize panels. The nearest neighbor shrinks first; dragging stops at minimum widths." };
        Grid.SetColumn(grip, 1); Grid.SetColumn(right, 2); grid.Children.Add(left); grid.Children.Add(grip); grid.Children.Add(right);
        PaneLayout.Register(grid, [left, right], [grid.ColumnDefinitions[0], grid.ColumnDefinitions[2]], [grip], profile, key); return grid;
    }
}
public enum WindowSurface { Theme, Solid }

public class FrostWindow : Window
{
    public WindowSurface SurfaceStyle { get; }
    internal CharacterProfile LayoutProfile => Controller.Profile;
    internal void SaveLayout() { if (!Controller.Demo) Controller.Save(); }
    public bool RequestedVisible { get; set; }
    public bool Passive { get; }
    private Point? dragStart;
    private Point windowStart;
    private IntPtr priorForeground;
    private readonly TextBlock dragGrip;
    public StackPanel Heading { get; }
    public StackPanel HeaderActions { get; } = new() { Orientation = Orientation.Horizontal };
    public TextBlock HeadingTitle { get; }
    public Grid Body { get; } = new();
    public TextBlock Subtitle { get; }
    protected ContentControl HeaderIcon { get; } = new() { Visibility = Visibility.Collapsed, Width = 20, Height = 20, Margin = new(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Top };
    protected Border Frame { get; }
    protected DockPanel Header { get; }
    public Action? HideRequested { get; set; }
    public Func<bool>? CanHide { get; set; }
    protected readonly TrackerController Controller;
    public bool IsCollapsed { get; private set; }
    private readonly Button collapseButton;
    private readonly string panelKey;
    private double expandedHeight, expandedMinimum;
    private Thickness expandedPadding;
    private double expandedFont;
    public void ToggleCollapse()
    {
        if (!IsCollapsed)
        {
            expandedHeight = Height; expandedMinimum = MinHeight; expandedPadding = Frame.Padding; expandedFont = HeadingTitle.FontSize;
            IsCollapsed = true; Body.Visibility = Subtitle.Visibility = Visibility.Collapsed; Frame.Padding = new(2); Header.Margin = new(0);
            HeadingTitle.FontSize = 11; HeadingTitle.Margin = new(0); HeaderIcon.Width = HeaderIcon.Height = 16;
            MinHeight = 28; Height = 28; ResizeMode = ResizeMode.NoResize; collapseButton.Content = "▾"; collapseButton.ToolTip = "Restore panel";
            foreach (var button in HeaderActions.Children.OfType<Button>()) button.Width = button.Height = 20;
        }
        else
        {
            IsCollapsed = false; Body.Visibility = Visibility.Visible; Frame.Padding = expandedPadding; HeadingTitle.FontSize = expandedFont;
            MinHeight = expandedMinimum; Height = Math.Max(expandedMinimum, expandedHeight); Subtitle.Visibility = Subtitle.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ResizeMode = IsLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip; collapseButton.Content = "−"; collapseButton.ToolTip = "Collapse to title bar";
            foreach (var button in HeaderActions.Children.OfType<Button>()) button.Width = button.Height = Passive ? 17 : 24;
        }
        Controller.Profile.Panels[panelKey] = new() { Collapsed = IsCollapsed, Width = Width, Height = expandedHeight };
        if (!Controller.Demo) Controller.Save();
    }
    public FrostWindow(TrackerController controller, string title, string subtitle, double width, double height, bool overlay = false, WindowSurface surface = WindowSurface.Solid)
    {
        Icon = CatalystBranding.Image(controller.Profile.AppIconId);
        Controller = controller; Passive = overlay; panelKey = title; Title = CatalystBranding.Name + " — " + title; Width = width; Height = height; MinWidth = overlay ? 128 : 640; MinHeight = overlay ? 72 : 420;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; AllowsTransparency = true; Background = Brushes.Transparent;
        if (!overlay && !controller.Demo) { Width = Math.Min(width, SystemParameters.WorkArea.Width * .96); Height = Math.Min(height, SystemParameters.WorkArea.Height * .96); WindowStartupLocation = WindowStartupLocation.CenterScreen; }
        ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        SurfaceStyle = overlay ? WindowSurface.Theme : surface;
        if (SurfaceStyle == WindowSurface.Solid) Appearance.SolidResources(this);
        if (!overlay) Opacity = 1;
        Frame = new Border { CornerRadius = new(4), BorderThickness = new(1), Padding = new(8) }; Frame.SetResourceReference(Border.BackgroundProperty, "Surface"); Frame.SetResourceReference(Border.BorderBrushProperty, "Accent");
        var content = new Grid(); content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var header = Header = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new(0, 0, 0, 4), LastChildFill = true };
        var close = UI.Button("×", () => { if (CanHide?.Invoke() == false) return; RequestedVisible = false; HideRequested?.Invoke(); Hide(); }); close.Padding = new(0); close.Margin = new(0); close.Height = 24; close.Width = 24; close.ToolTip = "Hide window";
        HeaderActions.Children.Add(close); HeaderActions.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(HeaderActions, Dock.Right); header.Children.Add(HeaderActions);
        collapseButton = UI.Button("−", ToggleCollapse); collapseButton.Width = collapseButton.Height = 24; collapseButton.Padding = new(0); collapseButton.Margin = new(0, 0, 2, 0); collapseButton.ToolTip = "Collapse to title bar"; HeaderActions.Children.Insert(0, collapseButton);
        HeadingTitle = UI.Text(title, overlay ? 14 : 18, "Foreground", true); HeadingTitle.TextWrapping = TextWrapping.NoWrap; HeadingTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        Heading = UI.Stack(HeadingTitle);
        Subtitle = UI.Text(subtitle, 12, "SecondaryText"); Subtitle.TextWrapping = TextWrapping.NoWrap; Subtitle.TextTrimming = TextTrimming.CharacterEllipsis; Subtitle.Visibility = subtitle.Length == 0 ? Visibility.Collapsed : Visibility.Visible; Heading.Children.Add(Subtitle);
        dragGrip = UI.Text("⠿", 10, "Muted"); dragGrip.Margin = new(0, 1, 2, 0); dragGrip.ToolTip = "Drag window"; DockPanel.SetDock(dragGrip, Dock.Left); header.Children.Add(dragGrip);
        var headingRow = new DockPanel { LastChildFill = true }; DockPanel.SetDock(HeaderIcon, Dock.Left); headingRow.Children.Add(HeaderIcon); headingRow.Children.Add(Heading); header.Children.Add(headingRow);
        header.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var source = e.OriginalSource as DependencyObject; while (source != null && source != header) { if (source is System.Windows.Controls.Primitives.ButtonBase) return; source = VisualTreeHelper.GetParent(source); }
            if (IsLocked) { e.Handled = true; return; }
            priorForeground = Controller.Demo ? IntPtr.Zero : Native.BeginInteraction(this);
            dragStart = Native.CursorPosition(); windowStart = new(Left, Top); header.CaptureMouse(); e.Handled = true;
        };
        header.PreviewMouseMove += (_, e) => { if (dragStart is not { } start || !header.IsMouseCaptured) return; var cursor = Native.CursorPosition(); var dpi = VisualTreeHelper.GetDpi(this); Left = windowStart.X + (cursor.X - start.X) / dpi.DpiScaleX; Top = windowStart.Y + (cursor.Y - start.Y) / dpi.DpiScaleY; e.Handled = true; };
        void EndDrag() { if (dragStart == null) return; dragStart = null; header.ReleaseMouseCapture(); if (!Controller.Demo) Native.EndInteraction(this, priorForeground, Passive, Passive && (this is OverlayWindow ow && ow.ClickThrough)); }
        header.PreviewMouseLeftButtonUp += (_, e) => { if (dragStart == null) return; EndDrag(); e.Handled = true; }; header.LostMouseCapture += (_, _) => EndDrag();
        SourceInitialized += (_, _) => { if (!Controller.Demo) Native.SetOverlay(this, false, Passive); };
        DpiChanged += (_, _) => { if (!Controller.Demo) ((App)Application.Current).RefreshIconForDisplay(); };
        // Resize thumbs receive the input instead of passing it through to the game.
        AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent, new System.Windows.Controls.Primitives.DragStartedEventHandler((_, _) => { if (Passive && !Controller.Demo) priorForeground = Native.BeginInteraction(this); }), true);
        AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent, new System.Windows.Controls.Primitives.DragCompletedEventHandler((_, _) => { if (Passive && !Controller.Demo) Native.EndInteraction(this, priorForeground, true, this is OverlayWindow ow && ow.ClickThrough); }), true);
        Grid.SetRow(Body, 1); content.Children.Add(header); content.Children.Add(Body); Frame.Child = content; Content = Frame;
        Closing += (_, e) => { if (!App.Exiting) { e.Cancel = true; if (CanHide?.Invoke() == false) return; RequestedVisible = false; HideRequested?.Invoke(); Hide(); } };
        Loaded += (_, _) => { Usability.Tooltips(Body); if (!IsCollapsed && controller.Profile.Panels.TryGetValue(panelKey, out var state) && state.Collapsed) { if (state.Width >= MinWidth) Width = state.Width; if (state.Height > MinHeight) Height = state.Height; ToggleCollapse(); } };
    }
    private bool locked;
    public bool IsLocked { get => locked; set { locked = value; Header.Cursor = value ? Cursors.Arrow : Cursors.SizeAll; dragGrip.Text = value ? "▣" : "⠿"; dragGrip.ToolTip = value ? "Position locked · unlock from menu" : "Drag window"; } }
    public void ShowInteractionState(bool clickThrough) { if (clickThrough) { dragGrip.Text = "⇥"; dragGrip.ToolTip = "Click-through enabled · "+((App)Application.Current).ShortcutLabel(OverlayShortcutAction.ClickThrough)+" restores input"; } else IsLocked = locked; }
    public void CancelInteraction()
    {
        dragStart = null; if (Header.IsMouseCaptured) Header.ReleaseMouseCapture();
        void ClosePopups(DependencyObject node)
        {
            if (node is Timeline timeline) timeline.ClearCursor();
            if (node is FrameworkElement element) { if (element.ContextMenu != null) element.ContextMenu.IsOpen = false; if (element.ToolTip is ToolTip tip) tip.IsOpen = false; }
            if (node is ComboBox combo) combo.IsDropDownOpen = false;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) ClosePopups(VisualTreeHelper.GetChild(node, i));
        }
        ClosePopups(this);
    }
}
