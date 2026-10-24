using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed record PlanningDisplay(string Id, Rect Work, double DpiX, double DpiY);
internal sealed record PlanningBounds(double Left, double Top, double Width, double Height);

/// <summary>Normal planning-window bounds; overlay placement remains independently managed.</summary>
internal sealed class PlanningWindowPlacement
{
    private readonly FrostWindow window;
    private readonly TrackerController controller;
    private readonly string key;
    private readonly double defaultWidth, defaultHeight, minimumWidth, minimumHeight;
    private readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool ready, restoring;
    private double normalWidth, normalHeight;
    internal bool ReviewEnabled { get; set; }
    private bool Enabled => !controller.Demo || ReviewEnabled;
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    {
        public int Size; public NativeRect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);

    internal PlanningWindowPlacement(FrostWindow window, TrackerController controller, string key)
    {
        this.window = window; this.controller = controller; this.key = key;
        defaultWidth = normalWidth = window.Width; defaultHeight = normalHeight = window.Height;
        minimumWidth = window.MinWidth; minimumHeight = window.MinHeight;
        if (!controller.Demo) window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.SourceInitialized += (_, _) => Restore();
        window.Loaded += (_, _) => { if (!ready) Restore(); ready = true; };
        window.SizeChanged += (_, _) => Queue(); window.LocationChanged += (_, _) => Queue(); window.DpiChanged += (_, _) => Queue();
        window.IsVisibleChanged += (_, _) => { if (!window.IsVisible) Commit(); };
        window.Deactivated += (_, _) => Commit();
        window.Closing += (_, _) => { if (!App.Exiting) Commit(); };
        window.Closed += (_, _) => settle.Stop();
        settle.Tick += (_, _) => { settle.Stop(); if (window.IsMouseCaptureWithin) Queue(); else Commit(); };
    }
    private void Queue() { if (!Enabled || !ready || restoring||window.IsAttachedWorkspace) return; settle.Stop(); settle.Start(); }
    internal Size ExpandedSize
    {
        get
        {
            if(window.IsAttachedWorkspace&&controller.Profile.Panels.TryGetValue(key,out var state))
                return new(double.IsFinite(state.Width)&&state.Width>=minimumWidth?state.Width:normalWidth,double.IsFinite(state.Height)&&state.Height>=minimumHeight?state.Height:normalHeight);
            return new(normalWidth,normalHeight);
        }
    }
    internal static PlanningBounds Resolve(PanelState? state, double width, double height, double minWidth, double minHeight, IReadOnlyList<PlanningDisplay> displays)
    {
        if (displays.Count == 0) throw new ArgumentException("A display is required", nameof(displays));
        bool PointValid(double? n) => n.HasValue && double.IsFinite(n.Value);
        bool hasPoint = PointValid(state?.ScreenLeft) && PointValid(state?.ScreenTop);
        var display = displays.FirstOrDefault(d => d.Id == state?.MonitorDevice) ?? (hasPoint ? displays.OrderBy(d => Distance(d.Work, state!.ScreenLeft!.Value, state.ScreenTop!.Value)).First() : displays[0]);
        bool validWidth = state != null && double.IsFinite(state.Width) && state.Width >= minWidth && state.Width * display.DpiX <= int.MaxValue;
        bool validHeight = state != null && double.IsFinite(state.Height) && state.Height >= minHeight && state.Height * display.DpiY <= int.MaxValue;
        double w = validWidth ? state!.Width : width, h = validHeight ? state!.Height : height;
        bool changed = !string.IsNullOrEmpty(state?.MonitorDevice) && (display.Id != state.MonitorDevice || Math.Abs(display.DpiX - state.DpiX) > .01 || Math.Abs(display.DpiY - state.DpiY) > .01);
        if (!validWidth || changed) w = Math.Max(minWidth, Math.Min(w, display.Work.Width / display.DpiX));
        if (!validHeight || changed) h = Math.Max(minHeight, Math.Min(h, display.Work.Height / display.DpiY));
        double x = hasPoint ? state!.ScreenLeft!.Value : display.Work.Left + (display.Work.Width - w * display.DpiX) / 2;
        double y = hasPoint ? state!.ScreenTop!.Value : display.Work.Top + (display.Work.Height - h * display.DpiY) / 2;
        if (hasPoint && changed && state!.DpiX > 0 && state.DpiY > 0 && double.IsFinite(state.DpiX) && double.IsFinite(state.DpiY) && double.IsFinite(state.MonitorLeft) && double.IsFinite(state.MonitorTop))
        { x = display.Work.Left + (x - state.MonitorLeft) * display.DpiX / state.DpiX; y = display.Work.Top + (y - state.MonitorTop) * display.DpiY / state.DpiY; }
        x = Math.Clamp(x, display.Work.Left - w * display.DpiX + Math.Min(64 * display.DpiX, w * display.DpiX), display.Work.Right - Math.Min(64 * display.DpiX, w * display.DpiX));
        y = Math.Clamp(y, display.Work.Top, Math.Max(display.Work.Top, display.Work.Bottom - Math.Min(28 * display.DpiY, h * display.DpiY)));
        return new(x, y, w, h);
    }
    private static double Distance(Rect r, double x, double y) => Math.Pow(Math.Max(Math.Max(r.Left - x, 0), x - r.Right), 2) + Math.Pow(Math.Max(Math.Max(r.Top - y, 0), y - r.Bottom), 2);
    private IReadOnlyList<PlanningDisplay> Displays()=>AvailableDisplays(window);
    internal static IReadOnlyList<PlanningDisplay> AvailableDisplays(Window window)
    {
        var displays = new List<PlanningDisplay>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (GetMonitorInfo(monitor, ref info))
            {
                uint x = 96, y = 96; GetDpiForMonitor(monitor, 0, out x, out y);
                displays.Add(new(info.Device, new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top), x > 0 ? x / 96d : 1, y > 0 ? y / 96d : 1));
            }
            return true;
        }, IntPtr.Zero);
        if (displays.Count == 0) { var dpi = VisualTreeHelper.GetDpi(window); var work = SystemParameters.WorkArea; displays.Add(new("", new(work.Left * dpi.DpiScaleX, work.Top * dpi.DpiScaleY, work.Width * dpi.DpiScaleX, work.Height * dpi.DpiScaleY), dpi.DpiScaleX, dpi.DpiScaleY)); }
        return displays;
    }
    internal void Restore()
    {
        if (!Enabled || restoring) return;
        restoring = true;
        try
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            var bounds = Resolve(controller.Profile.Panels.GetValueOrDefault(key), defaultWidth, defaultHeight, minimumWidth, minimumHeight, Displays());
            var handle = new WindowInteropHelper(window).Handle;
            SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top), 0, 0, 0x0001 | 0x0004 | 0x0010);
            var limits = WindowResizeController.Limits(window);
            normalWidth = Math.Max(minimumWidth,Math.Min(bounds.Width, limits.Width)); normalHeight = Math.Max(minimumHeight,Math.Min(bounds.Height, limits.Height));
            window.Width = normalWidth;
            // Loaded/collapse can run while native placement is being restored.
            // Restore expanded bounds without expanding an already-collapsed face.
            window.Height = window.IsCollapsed ? 28 : normalHeight;
            if (normalWidth != bounds.Width || normalHeight != bounds.Height)
            {
                var adjusted = EquipmentPositions.Copy(controller.Profile.Panels.GetValueOrDefault(key) ?? new PanelState()); adjusted.Width = normalWidth; adjusted.Height = normalHeight;
                var reachable = Resolve(adjusted,normalWidth,normalHeight,minimumWidth,minimumHeight,Displays());
                SetWindowPos(handle,IntPtr.Zero,(int)Math.Round(reachable.Left),(int)Math.Round(reachable.Top),0,0,0x0001|0x0004|0x0010);
            }
        }
        finally { restoring = false; }
    }
    internal void Commit()
    {
        settle.Stop();
        if(window.IsAttachedWorkspace)return;
        if (!Enabled || !ready || restoring || window.WindowState != WindowState.Normal || App.Exiting) return;
        var handle = new WindowInteropHelper(window).Handle; if (handle == IntPtr.Zero || IsIconic(handle) || IsZoomed(handle) || !GetWindowRect(handle, out var rect)) return;
        window.UpdateLayout();
        if (!window.IsCollapsed)
        {
            if (double.IsFinite(window.ActualWidth) && window.ActualWidth >= window.MinWidth) normalWidth = window.ActualWidth;
            if (double.IsFinite(window.ActualHeight) && window.ActualHeight >= window.MinHeight) normalHeight = window.ActualHeight;
        }
        var display = Displays().OrderBy(d => Distance(d.Work, rect.Left, rect.Top)).First();
        lock (controller.Sync)
        {
            if (!controller.Profile.Panels.TryGetValue(key, out var state)) controller.Profile.Panels[key] = state = new();
            state.Collapsed = window.IsCollapsed; state.Width = normalWidth; state.Height = normalHeight;
            state.ScreenLeft = rect.Left; state.ScreenTop = rect.Top; state.MonitorDevice = display.Id;
            state.MonitorLeft = display.Work.Left; state.MonitorTop = display.Work.Top; state.DpiX = display.DpiX; state.DpiY = display.DpiY;
            if (!controller.Demo) controller.Save();
        }
    }
    internal void CommitResize()
    {
        if(!window.IsAttachedWorkspace){Commit();return;}
        if(!Enabled||window.WindowState!=WindowState.Normal)return;
        normalWidth=window.ActualWidth;normalHeight=window.ActualHeight;
        lock(controller.Sync)
        {
            if(!controller.Profile.Panels.TryGetValue(key,out var state))controller.Profile.Panels[key]=state=new();
            state.Width=normalWidth;state.Height=normalHeight;
            if(!controller.Demo)controller.Save();
        }
    }
}
