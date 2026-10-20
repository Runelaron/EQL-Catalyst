using System.Diagnostics;
using System.Runtime.InteropServices;
using EqlTracker.Core;

namespace EqlTracker.App;

public static partial class Native
{
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Tick; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyInput { public ushort Key, Scan; public uint Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KeyInput Key; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput input);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    public static double IdleSeconds { get { var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() }; return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Tick) / 1000d : 0; } }
    public static bool KeysHeld => Enumerable.Range(1, 255).Any(k => (GetAsyncKeyState(k) & 0x8000) != 0);
    public static bool GameForeground(IntPtr handle) => handle != IntPtr.Zero && GetForegroundWindow() == handle;
    public static void SendSync(IntPtr game, NativeSyncSettings shortcut)
    {
        if (!GameForeground(game) || KeysHeld) throw new InvalidOperationException("Sync deferred: release all keys and keep EverQuest foreground.");
        var keys = new List<int>(); if (shortcut.Ctrl) keys.Add(0x11); if (shortcut.Alt) keys.Add(0x12); if (shortcut.Shift) keys.Add(0x10); keys.Add(shortcut.Key);
        Input InputFor(int key, bool up) => new() { Type = 1, Data = new() { Key = new() { Key = (ushort)key, Flags = up ? 2u : 0u } } };
        var inputs = keys.Select(k => InputFor(k, false)).Concat(keys.AsEnumerable().Reverse().Select(k => InputFor(k, true))).ToArray();
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
        { var releases = keys.AsEnumerable().Reverse().Select(k => InputFor(k, true)).ToArray(); SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>()); throw new InvalidOperationException("Windows did not accept Sync input. Check foreground and game/companion privilege levels, then test manually."); }
    }
    public static bool FocusGame(IntPtr handle) => SetForegroundWindow(handle);
}

public sealed class GameSyncCoordinator(TrackerController controller)
{
    private bool sending, automaticStopped;
    private DateTime nextPoll;
    private string worldSession = "", syncedSession = "";
    public async Task Request(bool automatic = false)
    {
        if (sending || controller.SyncRequest.Active || controller.Demo) return; sending = true;
        try
        {
            var shortcut = controller.Profile.NativeSync;
            if (shortcut.Key == 0 || automatic && !shortcut.Verified) throw new InvalidOperationException("Link and test the native Sync shortcut first.");
            using var game = FindGame(); if (game == null || game.MainWindowHandle == IntPtr.Zero) throw new InvalidOperationException("EverQuest is not running from the configured game folder.");
            if (!automatic) { Native.FocusGame(game.MainWindowHandle); await Task.Delay(250); }
            if (!Native.GameForeground(game.MainWindowHandle) || Native.KeysHeld) throw new InvalidOperationException("Sync deferred: EverQuest must be foreground with keys released.");
            controller.BeginSync(); Native.SendSync(game.MainWindowHandle, shortcut); syncedSession = worldSession;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { controller.SyncFailed(error.Message); if (automatic) automaticStopped = true; }
        finally { sending = false; }
    }
    private Process? FindGame()
    {
        foreach (var process in Process.GetProcessesByName("eqgame"))
        {
            try { if (Path.GetDirectoryName(process.MainModule?.FileName)?.Equals(Path.GetFullPath(controller.Profile.GameFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) == true) return process; }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            process.Dispose();
        }
        return null;
    }
    public void Tick()
    {
        using var measure=UpdateDiagnostics.Measure("Capture scheduler tick");
        try { TickCore(); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        { controller.SyncRequest.State = "Deferred"; controller.SyncRequest.Message = "Game state changed; waiting for the next eligibility check."; }
    }
    private void TickCore()
    {
        if (controller.Demo || DateTime.UtcNow < nextPoll) return; nextPoll = DateTime.UtcNow.AddSeconds(1);
        controller.CheckSync();
        if (controller.SyncRequest.State == "Complete") automaticStopped = false;
        if (controller.SyncRequest.State is "Partial" or "Failed") automaticStopped = true;
        var settings = controller.Profile.NativeSync;
        if (!settings.Verified || automaticStopped || controller.SyncRequest.Active || sending || !settings.Login && !settings.Periodic) return;
        using var game = FindGame(); if (game == null) { worldSession = ""; return; }
        if (controller.WorldEvidenceAt is not { } observed || observed < game.StartTime.AddSeconds(-1)) return;
        worldSession = game.Id + ":" + game.StartTime.Ticks + ":" + controller.WorldSession;
        bool due = NativeSyncPolicy.Due(settings, DateTime.Now, syncedSession != worldSession);
        if (!due) return;
        bool combat; lock (controller.Sync) combat = controller.Combat.Current != null;
        if (!NativeSyncPolicy.Eligible(Native.GameForeground(game.MainWindowHandle), Native.IdleSeconds, Native.KeysHeld, combat, System.Windows.Input.Mouse.Captured != null || System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().Any(w => w.IsMouseCaptureWithin)))
        { controller.SyncRequest.State = "Deferred"; controller.SyncRequest.Message = "Waiting for game foreground, five seconds idle and no active/resumable fight."; return; }
        _ = Request(true);
    }
}
