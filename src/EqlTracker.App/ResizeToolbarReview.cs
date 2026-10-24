using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewResizeToolbar(string folder)
    {
        if (!Controller.Demo) throw new InvalidOperationException("Synthetic review only");
        Directory.CreateDirectory(Path.Combine(folder, "qa"));
        var checks = new List<object>(); var timings = new List<object>(); var dragTimings = new List<object>(); var geometry = new List<object>();
        int failures = 0;
        void Check(bool passed, string name) { checks.Add(new { Name = name, Passed = passed }); if (!passed) failures++; }
        void Flush(Window? target = null) { (target ?? this).UpdateLayout(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); (target ?? this).UpdateLayout(); }
        void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); }
        bool Available(FrostWindow w) => w.OuterResize!.Grip is { IsVisible: true, IsEnabled: true, IsHitTestVisible: true };
        bool Hit(FrostWindow w)
        {
            var grip = w.OuterResize!.Grip; if (!grip.IsVisible) return false;
            var point = grip.TranslatePoint(new(grip.ActualWidth / 2, grip.ActualHeight / 2), w);
            var hit = w.InputHitTest(point) as DependencyObject;
            return hit == grip || (hit != null && grip.IsAncestorOf(hit));
        }
        Point dragOrigin = new();
        void Begin(FrostWindow w) { w.OuterResize!.Grip.CaptureMouse(); dragOrigin = w.OuterResize.Grip.PointToScreen(new(5, 5)); w.OuterResize.Grip.RaiseEvent(new DragStartedEventArgs(5, 5) { RoutedEvent = Thumb.DragStartedEvent }); }
        void Move(FrostWindow w, double x, double y, bool flush = true)
        {
            var current = w.OuterResize!.Grip.PointToScreen(new(5, 5)); var dpi = VisualTreeHelper.GetDpi(w);
            w.OuterResize.Grip.RaiseEvent(new DragDeltaEventArgs((dragOrigin.X + x * dpi.DpiScaleX - current.X) / dpi.DpiScaleX, (dragOrigin.Y + y * dpi.DpiScaleY - current.Y) / dpi.DpiScaleY) { RoutedEvent = Thumb.DragDeltaEvent }); if (flush) Flush(w);
        }
        void End(FrostWindow w, bool cancel = false) { w.OuterResize!.Grip.RaiseEvent(new DragCompletedEventArgs(0, 0, cancel) { RoutedEvent = Thumb.DragCompletedEvent }); w.OuterResize.Grip.ReleaseMouseCapture(); Flush(w); }
        void ResizeAfterRestore(FrostWindow w, string name)
        {
            Flush(w); double width = w.ActualWidth;
            Check(Available(w) && Hit(w), name + ": visible, enabled and hit-testable corner");
            if (!Available(w)) return;
            var watch = Stopwatch.StartNew(); long allocated = GC.GetAllocatedBytesForCurrentThread();
            Begin(w); Check(w.OuterResize!.Grip.IsMouseCaptured, name + ": actual WPF mouse capture"); Move(w, 17, 9); End(w);
            Check(Math.Abs(w.ActualWidth - width - 17) < 1, name + ": routed corner drag changes native window size");
            dragTimings.Add(new { Name = name, Milliseconds = watch.Elapsed.TotalMilliseconds, Bytes = GC.GetAllocatedBytesForCurrentThread() - allocated });
            geometry.Add(WindowResizeController.Geometry(w, name));
        }
        void Capture(string name)
        {
            foreach (double scale in new[] { 1d, 1.5, 2d, 3d })
            {
                var watch = Stopwatch.StartNew(); long allocated = GC.GetAllocatedBytesForCurrentThread();
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture((FrameworkElement)Content, Width, Height, scale)));
                using var output = File.Create(Path.Combine(folder, "qa", name + "-" + scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "x.png")); png.Save(output);
                timings.Add(new { Name = name, Scale = scale, Milliseconds = watch.Elapsed.TotalMilliseconds, Bytes = GC.GetAllocatedBytesForCurrentThread() - allocated });
            }
        }

        GearBuildFixtures.Apply(Controller); ShowView(TrackerTab.Pet); Width = 1000; Height = 600; Flush();
        var collapse = HeaderActions.Children.OfType<Button>().Single(b => b.Content?.ToString() == "−");
        for (int i = 0; i < 3; i++)
        {
            double expanded = Height; Click(collapse); Check(IsCollapsed && !Available(this), "Collapse hides corner " + i);
            Click(collapse); Check(!IsCollapsed && Math.Abs(Height - expanded) < 1, "Expand preserves height " + i);
            ResizeAfterRestore(this, "Collapse / expand " + i);
            WindowState = WindowState.Minimized; Flush(); Check(!Available(this), "Minimizing disables corner " + i);
            WindowState = WindowState.Normal; Flush(); ResizeAfterRestore(this, "Minimize / restore " + i);
            Close(); Check(!IsVisible, "Close hides reusable Tracker " + i); Show(); Flush(); ResizeAfterRestore(this, "Close / reopen " + i);
        }
        ResizeMode = ResizeMode.NoResize; Flush(); Check(!Available(this), "NoResize disables corner without changing dimensions");
        ResizeMode = ResizeMode.CanResize; Flush(); ResizeAfterRestore(this, "Resize-mode restoration without SizeChanged");
        IsLocked = true; Flush(); Check(!Available(this), "Lock disables corner immediately"); IsLocked = false; Flush(); ResizeAfterRestore(this, "Unlock");
        IsLocked = true; Click(collapse); Click(collapse); Check(!Available(this), "Restoring a locked window keeps resizing disabled"); IsLocked = false; Flush(); ResizeAfterRestore(this, "Unlock after collapsed restoration");

        double before = Width; Begin(this); Move(this, 70, 20, false); Click(collapse); Flush();
        Check(IsCollapsed && Height == 28 && Math.Abs(Width - before) < 1, "Collapse rejects an unrendered drag callback without overwriting expanded bounds");
        Click(collapse); ResizeAfterRestore(this, "Interrupted collapse recovery");
        before = Width; Begin(this); Move(this, 70, 20, false); Hide(); Flush(); Show(); Flush();
        Check(Math.Abs(Width - before) < 1, "Hide rejects an unrendered drag callback"); ResizeAfterRestore(this, "Interrupted hide recovery");
        before = Width; Begin(this); Move(this, 70, 20, false); WindowState = WindowState.Minimized; Flush(); WindowState = WindowState.Normal; Flush();
        Check(Math.Abs(Width - before) < 1, "Minimize rejects an unrendered drag callback"); ResizeAfterRestore(this, "Interrupted minimize recovery");
        before = Width; Begin(this); Move(this, 22, 12); End(this, true); Check(Math.Abs(Width - before) < 1, "Cancelled drag restores snapshot");
        before = Width; Begin(this); Move(this, 20, 10); RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }); Flush(); Check(Math.Abs(Width - before) < 1, "Escape restores snapshot");
        before = Width; Begin(this); Move(this, 20, 10); OuterResize!.Grip.ReleaseMouseCapture(); Flush(); Check(Math.Abs(Width - before) < 1, "Capture loss restores snapshot");
        Begin(this); Move(this, 12, 8); double drawn = Width; OuterResize.InterruptForDpi(); Move(this, 200, 50); End(this); Check(Math.Abs(Width - drawn) < 1, "DPI interruption rejects old coordinates");
        Width = 1000; Height = 600; Flush(); before = Width; Begin(this); Move(this, 40, 25); Move(this, -25, -15); End(this); Check(Math.Abs(Width - before + 25) < 1, "Reversal uses total pointer displacement from one snapshot");
        before = Width; Begin(this); Move(this, 20, 10, false); Move(this, 35, 15, false); Flush(); End(this); Check(Math.Abs(Width - before - 35) < 1, "Queued drag updates coalesce without drift");
        Begin(this); Move(this, -10000, -10000); End(this); Check(ActualWidth >= MinWidth && ActualHeight >= MinHeight, "Extreme drag respects existing window minimums");
        Width = 1000; Height = 600; Flush();

        foreach (var window in new FrostWindow[] { new DpsWindow(Controller), new FrostWindow(Controller, "Resize recovery review", "", 700, 500) })
        {
            window.Left = window.Top = -15000; window.ShowActivated = false; window.Show(); Flush(window);
            window.ToggleCollapse(); Flush(window); window.ToggleCollapse(); Flush(window); ResizeAfterRestore(window, window is DpsWindow ? "Combat Lab recovery" : "Companion recovery"); window.Hide();
        }

        ShowView(TrackerTab.Pet); Flush();
        var details = Usability.Tree(this).OfType<Button>().Single(e=>e.Content?.ToString()=="Pet Details…");
        var weights = Usability.Tree(this).OfType<Button>().Single(b => b.IsVisible && b.Content?.ToString() == "Pet weights…");
        var toolbar = (WrapPanel)((FrameworkElement)details.Parent).Parent;
        var panelSplits = JsonSerializer.Serialize(Controller.Profile.PanelSplits);
        foreach (string theme in new[] { "coldain", "sunstrider", "shadowman" })
        {
            Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile, theme)); Appearance.Apply(theme);
            foreach (bool narrow in new[] { false, true })
            {
                Width = narrow ? 640 : 1400; Height = narrow ? 650 : 820; ClosePetDetails(); Flush();
                var toggle = details;
                Check(Math.Abs(toggle.ActualHeight - 28) < .5 && Math.Abs(toggle.ActualHeight - weights.ActualHeight) < .5, theme + ": exact 28-DIP header, narrow=" + narrow);
                if (!narrow) Check(Math.Abs(toggle.TranslatePoint(new(), this).Y - weights.TranslatePoint(new(), this).Y) < .5, theme + ": aligned header bounds");
                var header = (FrameworkElement)toolbar.Parent;
                var content = ((DockPanel)header.Parent).Children.OfType<ScrollViewer>().Last();
                double gap = content.TranslatePoint(new(), this).Y - (toolbar.TranslatePoint(new(), this).Y + toolbar.ActualHeight - 4);
                Check(Math.Abs(gap - 8) < .5 && (!narrow || toggle.TranslatePoint(new(), this).Y > weights.TranslatePoint(new(), this).Y + 28), theme + ": one 8-DIP gap below final wrapped toolbar row, narrow=" + narrow);
                Check(petItems.ActualHeight >= 100 && petComparison.IsVisible, theme + ": inventory and details retain usable space, narrow=" + narrow);
                Capture(theme + (narrow ? "-toolbar-narrow" : "-toolbar"));
                toggle.Focus(); Flush(); Check(toggle.IsKeyboardFocused && toggle.ActualHeight == 28, theme + ": keyboard focus preserves header size");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                Check(toggle.ActualHeight == 28 && petSpell.IsVisible, theme + ": expanded header stays compact and context remains visible");
                if (!narrow) Capture(theme + "-toolbar-expanded"); ClosePetDetails(); Flush();
            }
        }
        Check(JsonSerializer.Serialize(Controller.Profile.PanelSplits) == panelSplits, "Toolbar sizing and wrapping preserve saved pane proportions");
        string Sha(string file) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        Json.Write(Path.Combine(folder, "resize-toolbar-checks.json"), new { Passed = checks.Count - failures, Failed = failures, AppAssemblySha256 = Sha(typeof(App).Assembly.Location), CoreAssemblySha256 = Sha(typeof(GearScorer).Assembly.Location), Checks = checks, Timings = timings, DragTimings = dragTimings, Geometry = geometry, Scope = "Synthetic offscreen native windows; routed drag events, real WPF capture/hit-testing, native bounds; rendered scales do not substitute for physical monitor acceptance." });
        if (failures > 0) throw new InvalidOperationException(failures + " resize/toolbar checks failed");
        await Task.CompletedTask;
    }
}
