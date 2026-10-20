using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using EqlTracker.Core;

namespace EqlTracker.App;

/// <summary>Owns pointer input before WPF's header/splitter handlers can resize the same boundary.</summary>
internal sealed class CascadingDrag
{
    internal sealed record Observation(string Surface, string Grip, int Boundary, bool Canceled, double[] Before, double[] After, double[] Minimums, double[] ApplyMilliseconds, long[] ApplyAllocations);
    internal static Action<Observation>? Observe;
    internal sealed class Session(WidthTrack[] tracks, int boundary, Action<double[]> apply, Action<bool> finish)
    {
        internal readonly WidthTrack[] Tracks = tracks;
        internal readonly double[] Widths = new double[tracks.Length];
        internal readonly int Boundary = boundary;
        internal readonly Action<double[]> Apply = apply;
        internal readonly Action<bool> Finish = finish;
    }
    private readonly FrameworkElement root;
    private readonly Func<Thumb, Session?> create;
    private Session? active;
    private Thumb? capture;
    private Point start;
    private double pending;
    private bool queued;
    private Window? window;
    private List<double>? timings;
    private List<long>? allocations;
    internal bool IsDragging => active != null;
    internal CascadingDrag(FrameworkElement root, Func<Thumb, Session?> create)
    {
        this.root = root; this.create = create;
        root.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Down), true);
        root.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(Move), true);
        root.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(Up), true);
        root.AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler((_, _) => { if (capture != null && !capture.IsMouseCaptured) Cancel(); }), true);
        root.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(Key), true);
        root.Unloaded += (_, _) => { Cancel(); if (window != null) window.Deactivated -= Deactivated; window = null; };
        root.Loaded += (_, _) => { var owner = Window.GetWindow(root); if (window == owner) return; if (window != null) window.Deactivated -= Deactivated; window = owner; if (window != null) window.Deactivated += Deactivated; };
        root.SizeChanged += (_, e) => { if (e.WidthChanged && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > .25) Cancel(); };
    }
    private static Thumb? Grip(DependencyObject? source)
    {
        while (source != null) { if (source is Thumb thumb) return thumb; source = source is Visual ? VisualTreeHelper.GetParent(source) : null; }
        return null;
    }
    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || e.ChangedButton != MouseButton.Left || Grip(e.OriginalSource as DependencyObject) is not { } thumb) return;
        var session = create(thumb); if (session == null) return;
        e.Handled = true; active = session; capture = thumb; start = e.GetPosition(root); pending = 0;
        if (thumb is GridSplitter) Keyboard.Focus(thumb);
        timings = Observe == null ? null : []; allocations = Observe == null ? null : [];
        if (!thumb.CaptureMouse()) { End(true); return; }
        // Retain the host window's passive-interaction lifecycle, without invoking native sizing.
        thumb.RaiseEvent(new DragStartedEventArgs(start.X, start.Y) { RoutedEvent = Thumb.DragStartedEvent, Handled = true });
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (active == null) return;
        pending = e.GetPosition(root).X - start.X; e.Handled = true;
        if (queued) return; queued = true;
        root.Dispatcher.BeginInvoke(() => { queued = false; Apply(); }, DispatcherPriority.Render);
    }
    private void Apply()
    {
        if (active is not { } session) return;
        long tick = timings == null ? 0 : Stopwatch.GetTimestamp(), bytes = timings == null ? 0 : GC.GetAllocatedBytesForCurrentThread();
        CascadeWidths.Resize(session.Tracks, session.Boundary, pending, session.Widths); session.Apply(session.Widths);
        if (timings != null) { timings.Add(Stopwatch.GetElapsedTime(tick).TotalMilliseconds); allocations!.Add(GC.GetAllocatedBytesForCurrentThread()-bytes); }
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (active == null || e.ChangedButton != MouseButton.Left) return;
        pending = e.GetPosition(root).X - start.X; Apply(); e.Handled = true; End(false);
    }
    private void Key(object sender, KeyEventArgs e)
    {
        if (active != null && e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Cancel(); return; }
        if (active != null || e.Handled || e.Key is not (System.Windows.Input.Key.Left or System.Windows.Input.Key.Right) || Grip(e.OriginalSource as DependencyObject) is not GridSplitter thumb) return;
        var session = create(thumb); if (session == null) return;
        e.Handled = true; double delta = thumb.KeyboardIncrement * (e.Key == System.Windows.Input.Key.Left ? -1 : 1);
        CascadeWidths.Resize(session.Tracks, session.Boundary, delta, session.Widths); session.Apply(session.Widths); session.Finish(false);
    }
    private void Deactivated(object? sender, EventArgs e) => Cancel();
    internal void Cancel() => End(true);
    private void End(bool canceled)
    {
        if (active is not { } session) return;
        var thumb = capture; active = null; capture = null;
        if (canceled) { for (int i = 0; i < session.Tracks.Length; i++) session.Widths[i] = session.Tracks[i].Width; session.Apply(session.Widths); }
        session.Finish(canceled);
        Observe?.Invoke(new(root.Name.Length > 0 ? root.Name : root.GetType().Name, thumb?.Name ?? "", session.Boundary, canceled, session.Tracks.Select(t=>t.Width).ToArray(), session.Widths.ToArray(), session.Tracks.Select(t=>t.Minimum).ToArray(), timings?.ToArray() ?? [], allocations?.ToArray() ?? []));
        timings = null; allocations = null;
        if (thumb?.IsMouseCaptured == true) thumb.ReleaseMouseCapture();
        thumb?.RaiseEvent(new DragCompletedEventArgs(pending, 0, canceled) { RoutedEvent = Thumb.DragCompletedEvent, Handled = true });
    }
}
