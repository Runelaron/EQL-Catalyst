using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class NativeControlsWindow : FrostWindow
{
    private readonly TextBlock state = UI.Text("", 12, "Mint"), package = UI.Text("", 12, "Muted"), result = UI.Text("", 12);
    private readonly Button compact, standard, restore;
    private readonly TextBlock syncState = UI.Text("", 12, "ReadableNote");
    private readonly TextBox shortcut = UI.Input("", 170);
    private readonly CaptureSettingsControls captures;
    private string packageFolder;
    public NativeControlsWindow(TrackerController controller) : base(controller, "Native Game Controls", "Real EverQuest hotbuttons. Local, reversible setup.", 800, 580)
    {
        packageFolder = Controller.Profile.NativeSetupFolder;
        Appearance.RaisedButtons(Body);
        var sync = controller.Profile.NativeSync; shortcut.Text = sync.Shortcut; shortcut.IsReadOnly = true; shortcut.ToolTip = "Bind the native Sync hotbutton to a shortcut in EverQuest, then press that same shortcut here. Linking alone does not verify it.";
        shortcut.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift) return;
            var modifiers = Keyboard.Modifiers;
            if (key == Key.Escape || (modifiers & ModifierKeys.Windows) != 0 || key == Key.F4 && (modifiers & ModifierKeys.Alt) != 0 || key is Key.Q or Key.D or Key.T && modifiers == (ModifierKeys.Control | ModifierKeys.Alt)) { e.Handled = true; return; }
            if (!(key is >= Key.F1 and <= Key.F12) && modifiers == ModifierKeys.None) { syncState.Text = "Use a function key or a modified key to avoid ordinary text keys."; e.Handled = true; return; }
            sync.Key = KeyInterop.VirtualKeyFromKey(key); sync.Ctrl = modifiers.HasFlag(ModifierKeys.Control); sync.Alt = modifiers.HasFlag(ModifierKeys.Alt); sync.Shift = modifiers.HasFlag(ModifierKeys.Shift); sync.Verified = false;
            shortcut.Text = sync.Shortcut; controller.Save(); e.Handled = true;
        };
        captures=new(controller);
        var syncNow = UI.Button("Test / Sync now", async () => await ((App)Application.Current).GameSync.Request(), true); syncNow.ToolTip = "Foreground EverQuest and invoke the linked native Sync shortcut once. Success requires fresh logging, your /who result, and a complete new inventory export.";
        var forget = UI.Button("Unlink", () => { sync.Key = 0; sync.Verified = false; sync.Login = sync.Periodic = false; captures.Refresh(true); shortcut.Text = sync.Shortcut; controller.Save(); }); forget.ToolTip = "Forget the companion shortcut and disable automatic sync; native game macros remain unchanged.";
        var syncControls = UI.Stack(UI.Label("COMPANION SYNC · LINK YOUR NATIVE SYNC SHORTCUT"), UI.Row(shortcut, syncNow, forget), captures, syncState);
        var commands = UI.Table(("Button", "Button", 110, null), ("Native command", "Command", 0, null)); commands.Height = 144;
        commands.ItemsSource = new[] { new { Button = "Log", Command = "/log on" }, new { Button = "Classes", Command = "/who" }, new { Button = "Inventory", Command = "/outputfile inventory" }, new { Button = "Sync", Command = "Log → Classes → Inventory (brief pauses between commands)" } };
        compact = UI.Button("Install compact controls", () => Apply(true), true); standard = UI.Button("Install standard bar", () => Apply(false)); restore = UI.Button("Restore previous setup", Restore);
        var actions = new WrapPanel(); foreach (var button in new[] { compact, standard, restore }) actions.Children.Add(button);
        Body.Children.Add(UI.Scroll(UI.Stack(syncControls, state, commands,
            UI.Card(UI.Stack(UI.Label("REVIEWABLE SETUP PACKAGE"), package, UI.Row(UI.Button("Create setup package", Prepare, true), UI.Button("Prepare label fix", PrepareLabels), UI.Button("Choose existing package", Choose), UI.Button("Review changes", Review)))),
            actions, result,
            UI.Text("Setup prefers unused hotbar 11 and allocates four unused social macros. Close EverQuest before installing or restoring. In game, bind Sync to a shortcut, link it above, then Test. Keep the native bar visible until you verify the shortcut also works while hidden.", 12, "ReadableNote"),
            UI.Text("Automatic sync is optional and never steals focus. Standard mode is available if the compact bar is incompatible. Failed verification stops automatic requests until a successful manual test.", 12, "ReadableNote"))));
        foreach (var button in Descendants(Body).OfType<Button>()) { ToolTipService.SetShowOnDisabled(button, true); if (button.ToolTip == null) button.ToolTip = button.Content?.ToString() switch { "Create setup package" => "Generate a new reversible package without changing game files.", "Prepare label fix" => "Prepare shorter native labels while preserving existing bindings.", "Choose existing package" => "Choose a previously generated setup manifest.", "Review changes" => "Open the exact proposed file changes and backup/restoration instructions.", _ => "Requires a valid setup package and EverQuest closed. Existing macros and unrelated settings are preserved." }; }
        UpdateStatus();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    public void UpdateStatus()
    {
        captures.Refresh();
        bool running = NativeSetup.GameRunning(); bool ready = File.Exists(Path.Combine(packageFolder, "manifest.json"));
        state.Text = running ? "EverQuest is running · package review available · close the game to install" : "EverQuest is closed · native installation and restoration available";
        package.Text = ready ? packageFolder : "Create a package to review the exact macro, hotbar, and layout changes.";
        compact.IsEnabled = standard.IsEnabled = restore.IsEnabled = ready && !running;
        syncState.Text = Controller.Profile.NativeSync.Shortcut + " · " + (Controller.Profile.NativeSync.Verified ? "Verified" : "Unverified") + "\n" + Controller.SyncRequest.State + " · " + Controller.SyncRequest.Message;
        foreach (var button in new[] { compact, standard, restore }) button.ToolTip = running ? "Close EverQuest before changing character files; the game can overwrite them while running." : !ready ? "Create or choose a reviewable setup package first." : button == restore ? "Restore the backed-up files from this package while preserving unrelated settings." : "Apply the reviewed native setup with backups; existing macros and occupied bars are preserved.";
        if (ready)
        {
            try { var plan = Json.Read<NativeSetupPlan>(Path.Combine(packageFolder, "manifest.json")); compact.IsEnabled &= plan.CompactAvailable; compact.Content = plan.LabelRepairOnly ? "Install label fix" : "Install compact controls"; standard.Visibility = plan.LabelRepairOnly ? Visibility.Collapsed : Visibility.Visible; }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { compact.IsEnabled = standard.IsEnabled = restore.IsEnabled = false; result.Text = "Package unavailable: " + e.Message; }
        }
    }
    private void Prepare()
    {
        try
        {
            string folder = Path.Combine(Controller.DataFolder, "native-controls", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
            var plan = NativeSetup.Generate(Controller.Profile.GameFolder, Controller.Profile.Character, Controller.Profile.Server, folder);
            packageFolder = folder; Controller.Profile.NativeSetupFolder = folder; Controller.Save();
            result.Text = $"Package ready · unused bar {plan.Bar}\n" + plan.CompactStatus; UpdateStatus();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.Xml.XmlException) { result.Text = e.Message; }
    }
    private void Choose()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Native setup manifest|manifest.json", Title = "Choose a reviewed native setup package" };
        if (dialog.ShowDialog(this) == true) { packageFolder = Path.GetDirectoryName(dialog.FileName)!; Controller.Profile.NativeSetupFolder = packageFolder; Controller.Save(); UpdateStatus(); }
    }
    private void PrepareLabels()
    {
        try
        {
            string folder = Path.Combine(Controller.DataFolder, "native-controls", "labels-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            NativeSetup.GenerateLabelRepair(packageFolder, folder); packageFolder = folder; Controller.Profile.NativeSetupFolder = folder; Controller.Save();
            result.Text = "Label fix ready for review. Only macro names change to Log / Who / Inv / Sync. Close EverQuest before installing."; UpdateStatus();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException) { result.Text = e.Message; }
    }
    private void Review()
    {
        string path = Path.Combine(packageFolder, "REVIEW.md");
        if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    private void Apply(bool small)
    {
        try { result.Text = NativeSetup.Apply(packageFolder, small).Message; }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException) { result.Text = e.Message; }
        UpdateStatus();
    }
    private void Restore()
    {
        try { result.Text = NativeSetup.Restore(packageFolder).Message; }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException) { result.Text = e.Message; }
        UpdateStatus();
    }
}
