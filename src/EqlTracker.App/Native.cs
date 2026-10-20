using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using EqlTracker.Core;

namespace EqlTracker.App;

public static partial class Native
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointNative point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RectNative rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out RectNative rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref PointNative point);
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectNative Monitor, Work; public uint Flags; }
    private static (IntPtr Handle, int Style, RectNative Rect)? savedGame;
    public static object? DisplayDiagnostics { get; private set; }
    public static IntPtr ForegroundHandle => GetForegroundWindow();
    public static Point CursorPosition() { GetCursorPos(out var p); return new(p.X, p.Y); }
    public static IntPtr BeginInteraction(Window window)
    {
        var prior = GetForegroundWindow(); SetOverlay(window, false, false); SetForegroundWindow(new WindowInteropHelper(window).Handle); return prior;
    }
    public static void EndInteraction(Window window, IntPtr prior, bool passive, bool clickThrough)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (passive && GetForegroundWindow() == handle && prior != IntPtr.Zero) SetForegroundWindow(prior);
        SetOverlay(window, clickThrough, passive);
    }
    public static void SetOverlay(Window window, bool clickThrough, bool passive = true)
    {
        var handle = new WindowInteropHelper(window).Handle; if (handle == IntPtr.Zero) return;
        int style = GetWindowLong(handle, -20); style |= 0x00000080;
        if (passive) style |= 0x08000000; else style &= ~0x08000000;
        if (clickThrough) style |= 0x00000020; else style &= ~0x00000020;
        SetWindowLong(handle, -20, style);
        SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0020);
    }
    public static void ShowPanel(FrostWindow window) { window.RequestedVisible = true; window.Show(); SetOverlay(window, false, false); if (!NativeSetup.GameRunning()) window.Activate(); }
    public static (int Width, int Height) GameResolution()
    {
        using var game = Process.GetProcessesByName("eqgame").FirstOrDefault();
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(MonitorFromWindow(game?.MainWindowHandle ?? IntPtr.Zero, 2), ref info)) return (info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
        return (3840, 2160);
    }
    public static void MaintainBorderless(string gameFolder, bool enabled)
    {
        if (!enabled) { DisplayDiagnostics = new { Mode = "Borderless framing disabled" }; RestoreGameFrame(); return; }
        // A saved windowed configuration is required before removing the native frame.
        if (!GameSettingsReader.TryReadText(Path.Combine(gameFolder, "eqclient.ini"), out var displaySettings)) { DisplayDiagnostics = new { Mode = "Display settings unavailable; retaining layout and retrying" }; return; }
        if (!System.Text.RegularExpressions.Regex.IsMatch(displaySettings, @"(?im)^Fullscreen\s*=\s*0\s*$")) { DisplayDiagnostics = new { Mode = "Saved windowed setup required" }; return; }
        using var game = Process.GetProcessesByName("eqgame").FirstOrDefault(); var handle = game?.MainWindowHandle ?? IntPtr.Zero; if (handle == IntPtr.Zero) { DisplayDiagnostics = new { Mode = "Waiting for game window" }; return; }
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() }; if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info) || !GetWindowRect(handle, out var rect)) { DisplayDiagnostics = new { Mode = "Monitor bounds unavailable", Window = handle.ToInt64(), MonitorInfoSize = info.Size }; return; }
        int style = GetWindowLong(handle, -16);
        if (savedGame?.Handle != handle) savedGame = (handle, style, rect);
        else if (ClientBounds(handle) is { } client && client.Left == info.Monitor.Left && client.Top == info.Monitor.Top && client.Right == info.Monitor.Right && client.Bottom == info.Monitor.Bottom) { DisplayDiagnostics = new { Mode = "Borderless viewport", Width = client.Right - client.Left, Height = client.Bottom - client.Top, FrameOutsideDisplay = (style & 0x00C40000) != 0 }; return; }
        // Clear maximized placement as well as the frame; otherwise Windows can
        // constrain the client to the work area and leave the taskbar exposed.
        int priorStyle = SetWindowLong(handle, -16, (style & ~(0x00C00000 | 0x00040000 | 0x01000000 | 0x20000000)) | unchecked((int)0x80000000));
        int styleError = priorStyle == 0 ? Marshal.GetLastWin32Error() : 0;
        bool placed = SetWindowPos(handle, IntPtr.Zero, info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top, 0x0010 | 0x0004 | 0x0020);
        int positionError = placed ? 0 : Marshal.GetLastWin32Error();
        GetWindowRect(handle, out var after);
        // Some EQ clients immediately restore WS_CAPTION in their own window
        // procedure. Fit the client area exactly and keep that frame off-screen.
        // This uses normal window positioning and does not hook the game.
        if ((GetWindowLong(handle, -16) & 0x00C40000) != 0 && ClientBounds(handle) is { } viewport)
        {
            int left = viewport.Left - after.Left, top = viewport.Top - after.Top, right = after.Right - viewport.Right, bottom = after.Bottom - viewport.Bottom;
            placed = SetWindowPos(handle, IntPtr.Zero, info.Monitor.Left - left, info.Monitor.Top - top, info.Monitor.Right - info.Monitor.Left + left + right, info.Monitor.Bottom - info.Monitor.Top + top + bottom, 0x0010 | 0x0004 | 0x0020);
            if (!placed) positionError = Marshal.GetLastWin32Error();
        }
        var final = ClientBounds(handle); bool fits = final.HasValue && final.Value.Left == info.Monitor.Left && final.Value.Top == info.Monitor.Top && final.Value.Right == info.Monitor.Right && final.Value.Bottom == info.Monitor.Bottom;
        DisplayDiagnostics = new { Mode = fits ? "Borderless viewport" : "Windowed; frame adjustment not accepted", Width = final.HasValue ? final.Value.Right - final.Value.Left : 0, Height = final.HasValue ? final.Value.Bottom - final.Value.Top : 0, ClientLeft = final?.Left, ClientTop = final?.Top, ExpectedWidth = info.Monitor.Right - info.Monitor.Left, ExpectedHeight = info.Monitor.Bottom - info.Monitor.Top, FrameOutsideDisplay = (GetWindowLong(handle, -16) & 0x00C40000) != 0, StyleError = styleError, PositionError = positionError };
    }
    private static RectNative? ClientBounds(IntPtr handle)
    {
        if (!GetClientRect(handle, out var client)) return null; var origin = new PointNative(); if (!ClientToScreen(handle, ref origin)) return null;
        return new RectNative { Left = origin.X, Top = origin.Y, Right = origin.X + client.Right, Bottom = origin.Y + client.Bottom };
    }
    public static void RestoreGameFrame()
    {
        if (savedGame is not { } saved) return; savedGame = null; SetWindowLong(saved.Handle, -16, saved.Style);
        SetWindowPos(saved.Handle, IntPtr.Zero, saved.Rect.Left, saved.Rect.Top, saved.Rect.Right - saved.Rect.Left, saved.Rect.Bottom - saved.Rect.Top, 0x0010 | 0x0004 | 0x0020);
    }
    public static bool GameOrCompanionForeground()
    {
        var window = GetForegroundWindow(); GetWindowThreadProcessId(window, out var pid);
        if (pid == Environment.ProcessId) return true;
        try { return Process.GetProcessById((int)pid).ProcessName.Equals("eqgame", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
    public sealed class Hotkeys : IDisposable
    {
        private readonly HwndSource? source;
        private readonly Action<int> callback;
        private readonly Func<int,GlobalShortcut,bool> register;
        private readonly Action<int> unregister;
        private Dictionary<GlobalShortcut,(int Id,OverlayShortcutAction Action)> active=[];
        private int nextId=100;
        public List<string> Failed { get; } = [];
        public Hotkeys(Action<int> handler,CharacterProfile profile)
        {
            callback = handler; source = new HwndSource(new HwndSourceParameters("EQLTracker hotkeys") { Width = 0, Height = 0, WindowStyle = 0 }); source.AddHook(Hook);
            register=(id,key)=>RegisterHotKey(source.Handle,id,(uint)key.Modifiers|0x4000,(uint)key.VirtualKey);unregister=id=>UnregisterHotKey(source.Handle,id);
            foreach(var (action,key) in OverlayShortcuts.Effective(profile))
            {int id=nextId++;if(register(id,key))active[key]=(id,action);else Failed.Add(key.Label);}
        }
        internal Hotkeys(Action<int> handler,Func<int,GlobalShortcut,bool> register,Action<int> unregister){callback=handler;this.register=register;this.unregister=unregister;}
        internal bool TryApply(IReadOnlyDictionary<OverlayShortcutAction,GlobalShortcut> requested,out string error)
        {
            if(!OverlayShortcuts.Validate(requested,out error))return false;
            var next=new Dictionary<GlobalShortcut,(int Id,OverlayShortcutAction Action)>();var created=new List<int>();
            foreach(var (action,key) in requested)
            {
                if(active.TryGetValue(key,out var existing)){next[key]=(existing.Id,action);continue;}
                int id=nextId++;
                if(!register(id,key)){foreach(int made in created)unregister(made);error=key.Label+" is unavailable or reserved by Windows. Your previous shortcuts remain active.";return false;}
                created.Add(id);next[key]=(id,action);
            }
            foreach(var (key,value) in active)if(!next.ContainsKey(key))unregister(value.Id);
            active=next;Failed.Clear();error="";return true;
        }
        internal void Dispatch(int id){var registration=active.Values.FirstOrDefault(v=>v.Id==id);if(registration.Id!=0&&!ShortcutSettings.SuppressGlobal)callback((int)registration.Action);}
        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) { if(message==0x0312){Dispatch(wParam.ToInt32());handled=true;}return IntPtr.Zero; }
        public void Dispose(){foreach(var value in active.Values)unregister(value.Id);active.Clear();if(source!=null){source.RemoveHook(Hook);source.Dispose();}}
    }
}
