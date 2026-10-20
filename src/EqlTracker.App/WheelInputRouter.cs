using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace EqlTracker.App;

// Never wait for WPF, controller locks or disk from the low-level callback.
public sealed class WheelRouter : IDisposable
{
    private delegate IntPtr MouseProc(int code, IntPtr message, IntPtr data);
    private delegate void ForegroundProc(IntPtr hook, uint evt, IntPtr window, int obj, int child, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public Point Position; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public Point Position; public uint Private; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, MouseProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, ForegroundProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    private readonly MouseProc callback;
    private readonly ForegroundProc foregroundCallback;
    private readonly Dispatcher dispatcher;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly ConcurrentQueue<WheelDelivery> pending = new();
    private readonly long[] latency = new long[2048];
    private record WheelDelivery(IntPtr Window, int X, int Y, int Delta, bool Horizontal, int Epoch);
    private IntPtr hook, foregroundHook;
    private uint threadId, gameProcess;
    private int epoch, scheduled, disposed, routed, restarts, allowed;
    private long samples;
    private IntPtr previousForeground;
    private HashSet<IntPtr> surfaces = [];
    public bool Installed => hook != IntPtr.Zero;
    public int HookError { get; private set; }
    public int RoutedEvents => routed;
    public event Action<bool>? ForegroundChanged;
    public WheelRouter()
    {
        dispatcher = Dispatcher.CurrentDispatcher; callback = Handle; foregroundCallback = OnForeground;
        thread = new Thread(Run) { IsBackground = true, Name = "EQL wheel input" };
        thread.Start(); ready.Wait(TimeSpan.FromSeconds(2)); UpdateSurfaces();
    }
    private void Run()
    {
        threadId = GetCurrentThreadId(); Reinstall();
        foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, foregroundCallback, 0, 0, 0);
        ready.Set();
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            if (message.Id == 0x8001) { CheckForeground(); Reinstall(); } else DispatchMessage(ref message);
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); hook = IntPtr.Zero;
        if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
    }
    private void Reinstall()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        HookError = hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0; Interlocked.Increment(ref restarts);
    }
    public void UpdateSurfaces()
    {
        if (disposed != 0) return;
        Volatile.Write(ref surfaces, PresentationSource.CurrentSources.OfType<HwndSource>()
            .Where(s => s.RootVisual is UIElement && s.Handle != IntPtr.Zero && IsWindowVisible(s.Handle)).Select(s => s.Handle).ToHashSet());
        GetWindowThreadProcessId(GetForegroundWindow(), out uint process);
        if (process != Environment.ProcessId && process != gameProcess)
        {
            try { using var app = Process.GetProcessById((int)process); if (app.ProcessName.Equals("eqgame", StringComparison.OrdinalIgnoreCase)) Volatile.Write(ref gameProcess, process); }
            catch (ArgumentException) { } catch (System.ComponentModel.Win32Exception) { }
        }
        CheckForeground();
    }
    private void OnForeground(IntPtr h, uint evt, IntPtr window, int obj, int child, uint tid, uint time) => CheckForeground();
    private void CheckForeground()
    {
        var foreground = GetForegroundWindow(); GetWindowThreadProcessId(foreground, out uint process);
        if (process != 0 && process != Environment.ProcessId && process != Volatile.Read(ref gameProcess))
        {
            try { using var foregroundApp = Process.GetProcessById((int)process); if (foregroundApp.ProcessName.Equals("eqgame", StringComparison.OrdinalIgnoreCase)) Volatile.Write(ref gameProcess, process); }
            catch (ArgumentException) { } catch (System.ComponentModel.Win32Exception) { }
        }
        int next = process == Environment.ProcessId || process != 0 && process == Volatile.Read(ref gameProcess) ? 1 : 0;
        if (foreground == previousForeground && next == allowed) return;
        previousForeground = foreground; int prior = Interlocked.Exchange(ref allowed, next);
        Interlocked.Increment(ref epoch); while (pending.TryDequeue(out _)) { }
        if (prior != next)
        {
            if (next == 1 && threadId != 0) PostThreadMessage(threadId, 0x8001, UIntPtr.Zero, IntPtr.Zero);
            dispatcher.BeginInvoke(() => { if (disposed == 0) { Mouse.Capture(null); ForegroundChanged?.Invoke(next == 1); UpdateSurfaces(); } }, DispatcherPriority.Input);
        }
    }
    private IntPtr Handle(int code, IntPtr message, IntPtr data)
    {
        long began = Stopwatch.GetTimestamp();
        try
        {
            if (code < 0 || message.ToInt32() is not (0x020A or 0x020E) || Volatile.Read(ref allowed) == 0)
                return CallNextHookEx(hook, code, message, data);
            var mouse = Marshal.PtrToStructure<MouseData>(data); var window = WindowFromPoint(mouse.Position);
            GetWindowThreadProcessId(window, out uint process);
            // Native fallback includes a popup opened since the previous surface snapshot.
            if ((Volatile.Read(ref surfaces).Contains(window) || process == Environment.ProcessId && IsWindowVisible(window)) && (GetWindowLong(window, -20) & 0x20) == 0)
            {
                pending.Enqueue(new(window, mouse.Position.X, mouse.Position.Y, unchecked((short)(mouse.Data >> 16)), message.ToInt32() == 0x020E, Volatile.Read(ref epoch)));
                Interlocked.Increment(ref routed);
                if (Interlocked.Exchange(ref scheduled, 1) == 0) dispatcher.BeginInvoke(Deliver, DispatcherPriority.Input);
                return new(1);
            }
            return CallNextHookEx(hook, code, message, data);
        }
        finally { long i = Interlocked.Increment(ref samples); Volatile.Write(ref latency[(int)((i - 1) % latency.Length)], Stopwatch.GetTimestamp() - began); }
    }
    private void Deliver()
    {
        Interlocked.Exchange(ref scheduled, 0);
        var deltas = new Dictionary<(ScrollViewer Viewer, bool Horizontal), int>();
        while (pending.TryDequeue(out var delivery))
        {
            if (delivery.Epoch != epoch || allowed == 0 || disposed != 0) continue;
            if (HwndSource.FromHwnd(delivery.Window)?.RootVisual is not UIElement root || !root.IsVisible || PresentationSource.FromVisual(root) == null) continue;
            try
            {
                var point = root.PointFromScreen(new(delivery.X, delivery.Y));
                if (!new Rect(new System.Windows.Point(), root.RenderSize).Contains(point)) continue;
                var hit = root.InputHitTest(point) as DependencyObject;
                var scroll = Ancestor(hit) ?? UI.FindScroll(root);
                if (scroll != null) { var key = (scroll, delivery.Horizontal); deltas[key] = deltas.GetValueOrDefault(key) + delivery.Delta; }
            }
            catch (InvalidOperationException) { /* The popup/window disconnected before delivery. */ }
        }
        foreach (var (key, delta) in deltas) Scroll(key.Viewer, delta, key.Horizontal);
    }
    private static ScrollViewer? Ancestor(DependencyObject? node)
    {
        while (node != null) { if (node is ScrollViewer scroll) return scroll; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node); }
        return null;
    }
    public object Diagnostics()
    {
        var values = latency.Take((int)Math.Min(samples, latency.Length)).Select(x => x * 1000d / Stopwatch.Frequency).Order().ToArray();
        return new { Installed, HookError, RoutedEvents, Restarts = restarts, CallbackSamples = samples, CallbackP99Ms = values.Length == 0 ? (double?)null : values[(int)((values.Length - 1) * .99)], CallbackMaxMs = values.Length == 0 ? (double?)null : values.Last(), CallbackState = samples == 0 ? "No callbacks observed" : "Callbacks observed", RegisteredSurfaces = surfaces.Count, ForegroundEligible = allowed == 1 };
    }
    public static void Scroll(ScrollViewer scroll, int delta, bool horizontal = false)
    {
        Interlocked.Increment(ref UI.ScrollInteractionRevision);
        double distance = delta / 120d * (SystemParameters.WheelScrollLines < 0 ? scroll.ViewportHeight : Math.Max(1, SystemParameters.WheelScrollLines) * 12);
        if (horizontal) scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + distance);
        else scroll.ScrollToVerticalOffset(scroll.VerticalOffset - distance);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        PostThreadMessage(threadId, 0x0012, UIntPtr.Zero, IntPtr.Zero); thread.Join(TimeSpan.FromSeconds(2)); ready.Dispose();
    }
}
