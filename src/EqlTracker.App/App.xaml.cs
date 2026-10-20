using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

public partial class App : Application
{
    public static bool Exiting { get; private set; }
    public TrackerController Controller { get; private set; } = null!;
    public OverlayWindow ObjectivesOverlay { get; private set; } = null!;
    public OverlayWindow DpsOverlay { get; private set; } = null!;
    private QuestWindow? quests;
    private DpsWindow? dps;
    private SettingsWindow? settings;
    private NativeControlsWindow? nativeControls;
    private IconGallery? iconGallery;
    private System.Windows.Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private Native.Hotkeys? hotkeys;
    private Mutex? singleton;
    private Mutex? applicationPresence;
    private EventWaitHandle? stopSignal;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer iconRefresh = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string currentTheme = "";
    private int tick;
    public GameSyncCoordinator GameSync { get; private set; } = null!;
    internal string DataRoot { get; private set; } = "";
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ContextMenu), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => OverlayMenus.AutoDismiss((System.Windows.Controls.ContextMenu)sender)));
        string? Value(string flag) { int i = Array.IndexOf(e.Args, flag); return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null; }
        var benchmark = Value("--benchmark"); var preview = Value("--preview"); var smoke = Value("--smoke");
        bool diagnostic = benchmark != null || preview != null || smoke != null;
        bool firstSetup = false;
        DataRoot = Path.GetFullPath(Value("--data-root") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EQLTracker"));
        if(int.TryParse(Value("--wait-for-parent"),out int parentId)&&parentId>0&&parentId!=Environment.ProcessId)
        {
            try{using var parent=System.Diagnostics.Process.GetProcessById(parentId);await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));}
            catch(ArgumentException){ /* Previous process has already finished. */ }
            catch(TimeoutException){MessageBox.Show("The previous character is still saving. Reopen EQL Catalyst after it closes.");Shutdown(1);return;}
        }
        if (Value("--first-run-cancel-audit") is string cancelAudit)
        {
            if (Directory.Exists(DataRoot)) { Shutdown(1); return; }
            Appearance.Apply("coldain");
            var form = new FirstRunDialog([]) { ShowInTaskbar = false, Left = -15000, Top = -15000, WindowStartupLocation = WindowStartupLocation.Manual };
            form.Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() => form.Close()), DispatcherPriority.ApplicationIdle);
            bool cancelled = form.ShowDialog() != true && !Directory.Exists(DataRoot);
            Json.Write(cancelAudit, new { Cancelled = cancelled, NoProfileDirectoryCreated = !Directory.Exists(DataRoot) });
            Shutdown(cancelled ? 0 : 1); return;
        }
        var launcher = LauncherPreferences.Read(DataRoot);
        string? character = Value("--character"), server = Value("--server"), selectedGame = Value("--game-folder");
        if (diagnostic) { character ??= "ExampleHero"; server ??= "example"; }
        bool maintenance = e.Args.Any(a => a.StartsWith("--native-", StringComparison.Ordinal) || a.StartsWith("--display-", StringComparison.Ordinal));
        if (!diagnostic && (character == null || server == null))
        {
            if (character != null || server != null) { MessageBox.Show("Pass both --character and --server."); Shutdown(1); return; }
            var existing = LauncherPreferences.Existing(DataRoot);
            var chosen = existing.FirstOrDefault(p => p.Character.Equals(launcher.LastProfile?.Character, StringComparison.OrdinalIgnoreCase) && p.Server.Equals(launcher.LastProfile?.Server, StringComparison.OrdinalIgnoreCase)) ?? (existing.Count == 1 ? existing[0] : null);
            if (chosen == null && e.Args.Contains("--shutdown")) { Shutdown(); return; }
            if (chosen == null && maintenance) { MessageBox.Show("Specify --character, --server and --game-folder for setup commands."); Shutdown(1); return; }
            if (chosen == null)
            {
                Appearance.ApplySecondaryText("Candlelight Cream"); Appearance.Apply("coldain");
                var setup = new FirstRunDialog(existing);
                if (setup.ShowDialog() != true || setup.Selection == null) { Shutdown(); return; }
                chosen = setup.Selection; firstSetup = true; MainWindow = null;
            }
            character = chosen.Character; server = chosen.Server; selectedGame ??= chosen.GameFolder;
        }
        if (!LauncherPreferences.ValidName(character) || !LauncherPreferences.ValidName(server)) { MessageBox.Show("Invalid character/server name."); Shutdown(1); return; }
        character ??= ""; server ??= "";
        string stopName = "Local\\EQLTracker-stop-" + character.ToLowerInvariant() + "-" + server.ToLowerInvariant();
        if (e.Args.Contains("--shutdown")) { if (EventWaitHandle.TryOpenExisting(stopName, out var signal)) { using (signal) signal.Set(); } Shutdown(); return; }
        string? displayFolder = Value("--display-prepare") ?? Value("--display-apply") ?? Value("--display-restore");
        if (displayFolder != null)
        {
            try { object result; if (Value("--display-prepare") != null) { var resolution = Native.GameResolution(); result = BorderlessSetup.Prepare(selectedGame ?? throw new InvalidDataException("Specify --game-folder."), displayFolder, resolution.Width, resolution.Height); } else result = BorderlessSetup.Apply(displayFolder, Value("--display-restore") != null); Json.Write(Path.Combine(displayFolder, "last-operation.json"), new { Success = true, Result = result }); Shutdown(); }
            catch (Exception error) { Directory.CreateDirectory(displayFolder); Json.Write(Path.Combine(displayFolder, "last-operation.json"), new { Success = false, Error = error.Message }); Shutdown(1); }
            return;
        }
        string? nativeFolder = Value("--native-prepare") ?? Value("--native-apply") ?? Value("--native-restore") ?? Value("--native-label-repair");
        if (nativeFolder != null)
        {
            try
            {
                object outcome;
                if (Value("--native-label-repair") != null) { string original = Value("--from-package") ?? throw new InvalidDataException("Pass --from-package with the original setup package."); outcome = NativeSetup.GenerateLabelRepair(original, nativeFolder); }
                else if (Value("--native-prepare") != null) outcome = NativeSetup.Generate(selectedGame ?? throw new InvalidDataException("Specify --game-folder."), character!, server!, nativeFolder);
                else if (Value("--native-restore") != null) outcome = NativeSetup.Restore(nativeFolder);
                else outcome = NativeSetup.Apply(nativeFolder, !e.Args.Contains("--native-standard"));
                Json.Write(Path.Combine(nativeFolder, "last-operation.json"), new { Success = true, Result = outcome }); Shutdown();
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(nativeFolder); Json.Write(Path.Combine(nativeFolder, "last-operation.json"), new { Success = false, Error = error.Message }); Shutdown(1);
            }
            return;
        }
        if (preview == null && smoke == null && benchmark == null)
        {
            if (Mutex.TryOpenExisting("Local\\EQLCatalyst-SetupRunning", out var setupMutex)) { setupMutex.Dispose(); MessageBox.Show("EQL Catalyst Setup is running. Finish or cancel Setup before opening the app."); Shutdown(); return; }
            singleton = new Mutex(true, "Local\\EQLTracker-" + character.ToLowerInvariant() + "-" + server.ToLowerInvariant(), out bool created);
            if (!created) { MessageBox.Show("This character's tracker is already running. Open it from the system tray."); Shutdown(); return; }
            stopSignal = new(false, EventResetMode.AutoReset, stopName);
            applicationPresence = new Mutex(false, "Local\\EQLCatalyst-AppRunning");
        }
        try
        {
            Controller = new TrackerController(benchmark ?? smoke ?? (preview == null ? Path.Combine(DataRoot, character + "_" + server) : Path.Combine(preview, ".preview-state")), preview != null, character!, server!, selectedGame);
            if (!diagnostic) launcher.Remember(DataRoot, new(Controller.Profile.Character, Controller.Profile.Server, Controller.Profile.GameFolder));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException)
        { MessageBox.Show("Unable to open this profile. Existing data was not replaced.\n\n" + error.Message, "EQL Catalyst"); Shutdown(1); return; }
        IconArtwork.Apply(Controller.Profile);
        RefreshShortcutHints();
        CatalystBranding.SelectedId = CatalystBranding.Resolve(Controller.Profile.AppIconId).Id;
        CatalystBranding.SelectedStyle = IconArtwork.Style;
        DispatcherUnhandledException += (_, args) => { File.AppendAllText(Path.Combine(Controller.DataFolder, "errors.log"), args.Exception + Environment.NewLine); args.Handled = true; if (preview != null) { Json.Write(Path.Combine(preview, "validation-failure.json"), new { Error = args.Exception.ToString() }); Shutdown(1); } else MessageBox.Show("Tracker error: " + args.Exception.Message); };
        if (preview == null && (Controller.Profile.Character != character || Controller.Profile.Server != server)) { Controller.Profile.Character = character; Controller.Profile.Server = server; Controller.Profile.CurrentClasses = []; Controller.Profile.OptionalClasses = ["WAR"]; Controller.Profile.ActiveLoadout.Classes = []; }
        if (selectedGame is { } gameFolder) Controller.Profile.GameFolder = gameFolder;
        if (benchmark != null)
        { ApplyTheme(); try { await PerformanceChecks.Run(Controller, benchmark); } catch (Exception error) { Json.Write(Path.Combine(benchmark, "benchmark-failure.json"), new { Error = error.ToString() }); } ExitTracker(); return; }
        if (smoke != null)
        {
            var started = DateTime.UtcNow; Controller.Start();
            while ((DateTime.UtcNow - started).TotalSeconds < 180)
            {
                await Task.Delay(1000);
                lock (Controller.Sync)
                {
                    if (Controller.Status.StartsWith("Reader paused", StringComparison.Ordinal)) break;
                    using var verify = new TrackerStore(Path.Combine(smoke, "tracker.db"));
                    var offset = verify.Get<FileCheckpoint>("checkpoint");
                    var log = Controller.GameLogPath;
                    if (offset != null && File.Exists(log) && offset.Offset >= new FileInfo(log).Length) break;
                }
            }
            lock (Controller.Sync)
            {
                Controller.Combat.Finish();
                Json.Write(Path.Combine(smoke, "smoke-report.json"), new { Controller.Status, ElapsedSeconds = (DateTime.UtcNow - started).TotalSeconds, Profile = Controller.Profile, InventoryRows = Controller.Inventory.Entries.Count, Controller.Inventory.Complete, Encounters = Controller.Combat.Encounters.Count, PlayerDamage = Controller.Combat.Encounters.Sum(x => x.PlayerDamage), PetDamage = Controller.Combat.Encounters.Sum(x => x.PetDamage), Objectives = Controller.Objectives.Count });
            }
            ExitTracker(); return;
        }
        ApplyTheme();
        if(Value("--character-switch-audit") is { } auditServer)
        {
            if(Value("--data-root")==null||!Path.GetFileName(DataRoot).StartsWith("eql-switch-review-",StringComparison.Ordinal)||Controller.Profile.GameFolder.Length!=0||!LauncherPreferences.ValidName(auditServer))throw new InvalidDataException("Dedicated offline switch-audit folder required.");
            characterSwitchAudit=true;var target=new LaunchProfile(Controller.Profile.Character,auditServer,"");characterChoices=[ActiveCharacter,target];
            Json.Write(Path.Combine(DataRoot,"handoff.json"),new{From=ActiveCharacter,Target=target,WholeApplication=true});
            if(!SwitchCharacter(target,TrackerTab.Gear))throw new InvalidOperationException("Profile handoff failed.");return;
        }
        if (e.Args.Contains("--launch-audit"))
        {
            Controller.Save();
            Json.Write(Path.Combine(Controller.DataFolder, "launch-audit.json"), new { Controller.Profile.Character, Controller.Profile.Server, Controller.Profile.GameFolder, Controller.Profile.Theme, Controller.Profile.CurrentClasses, Controller.Profile.NativeSync, Controller.Profile.ActiveLoadout.Level, Controller.Profile.ActiveLoadout.Race, DataRoot, Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, Version = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion });
            Exiting = true; Controller.Dispose(); stopSignal?.Dispose(); singleton?.Dispose(); applicationPresence?.Dispose(); Shutdown(); return;
        }
        if (preview != null && e.Args.Contains("--mode-buttons-only"))
        {
            try { CombatModeButtonReview.Run(Controller, preview); }
            catch (Exception error) { Json.Write(Path.Combine(preview, "mode-button-failure.json"), new { Error = error.ToString() }); Exiting = true; Controller.Dispose(); Shutdown(1); return; }
            ExitTracker(); return;
        }
        GameSync = new(Controller);
        ObjectivesOverlay = new(Controller, false); DpsOverlay = new(Controller, true);
        if(preview!=null&&e.Args.Contains("--tracker-polish-performance-only"))
        {try{await TrackerPolishPerformance.Run(Controller,DpsOverlay,preview);}catch(Exception error){Json.Write(Path.Combine(preview,"performance-failure.json"),new{Error=error.ToString()});}ExitTracker();return;}
        if (preview != null)
        {
            File.Delete(Path.Combine(preview, "validation-failure.json"));
            var scale = double.TryParse(Value("--scale"), System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 1;
            quests = new(Controller); dps = new(Controller); quests.SetZoneFilter("Current zone");
            try
            {
                if (e.Args.Contains("--cascade-pointer")) { CascadingResizeReview.OpenPointer(preview,Controller); return; }
                if(e.Args.Contains("--compact-settings-only")){CompactSettingsReview.Run(preview,Controller,quests);ExitTracker();return;}
                if (e.Args.Contains("--cascade-only")) { CascadingResizeReview.Run(preview,Controller,quests,dps,e.Args.Contains("--gear-capture-only"),e.Args.Contains("--lab-panels-only")); ExitTracker(); return; }
                if (e.Args.Contains("--public-screens")) { PublicScreens.Run(preview, Controller, quests, dps, DpsOverlay); ExitTracker(); return; }
                if (e.Args.Contains("--beta-only")) { BetaReview.Run(preview, Controller, quests, dps, DpsOverlay); ExitTracker(); return; }
                if(e.Args.Contains("--menu-display-only"))
                { await dps.ReviewMenuDisplay(preview,quests,DpsOverlay,ObjectivesOverlay);ExitTracker();return; }
                if(e.Args.Contains("--stats-tabs-only"))
                { dps.ReviewStatsTabs(preview,quests);ExitTracker();return; }
                if(e.Args.Contains("--group-raid-only"))
                { await dps.ReviewGroupRaid(preview,DpsOverlay,ObjectivesOverlay);ExitTracker();return; }
                if(e.Args.Contains("--tracker-polish-only"))
                { quests.Left=quests.Top=-15000;quests.ShowActivated=false;quests.Show();await quests.ReviewTrackerPolish(preview,dps,DpsOverlay,e.Args.Contains("--retain-polish-renders"));ExitTracker();return; }
                if (e.Args.Contains("--gear-controls-final-only"))
                {
                    quests.Left = quests.Top = -15000; quests.ShowActivated = false; quests.Show();
                    quests.ReviewGearControlsFinal(preview,!e.Args.Contains("--retain-gear-renders")); ExitTracker(); return;
                }
                if (e.Args.Contains("--gear-controls-only"))
                {
                    quests.Left = quests.Top = -15000; quests.ShowActivated = false; quests.Show();
                    await quests.ReviewGearControls(preview); ExitTracker(); return;
                }
                if (e.Args.Contains("--equipment-icons-only"))
                {
                    quests.Left = quests.Top = -15000; quests.ShowActivated = false; quests.Show();
                    quests.ReviewEquipmentIcons(preview); ExitTracker(); return;
                }
                if (e.Args.Contains("--equipment-badges-only"))
                {
                    quests.Left = quests.Top = -15000; quests.ShowActivated = false; quests.Show();
                    quests.ReviewEquipmentBadges(preview); ExitTracker(); return;
                }
                if (e.Args.Contains("--clear-items-only"))
                {
                    quests.Left = quests.Top = -15000; quests.ShowActivated = false; quests.Show();
                    await quests.ReviewClearItems(preview); ExitTracker(); return;
                }
                if (e.Args.Contains("--pet-controls-only"))
                {
                    quests.Left = quests.Top = -15000; quests.ShowActivated = false; quests.Show();
                    await quests.ReviewPetControls(preview); ExitTracker(); return;
                }
                if (e.Args.Contains("--smooth-items-only"))
                {
                    quests.Left = quests.Top = -15000; quests.ShowActivated = false; quests.Show();
                    await quests.ReviewSmoothItems(preview, e.Args.Contains("--inspectors-only")); ExitTracker(); return;
                }
                if (e.Args.Contains("--pet-details-only"))
                {
                    quests.Left = quests.Top = -15000; quests.ShowActivated = false; quests.Show();
                    quests.ReviewPetDetails(preview); ExitTracker(); return;
                }
                if (e.Args.Contains("--icon-style-only"))
                {
                    quests.Left = quests.Top = dps.Left = dps.Top = -15000; quests.ShowActivated = dps.ShowActivated = false;
                    quests.Width = 1220; quests.Height = 800; quests.Show(); dps.Show();
                    var checks = new List<string>();
                    void Check(bool passed, string label) { if (!passed) throw new InvalidOperationException(label); checks.Add(label); }
                    IconStyleUiChecks.Run(Controller, quests, Check, preview);
                    BrandUiChecks.Run(Controller, Check);
                    CombatModeButtonReview.Run(Controller, preview);
                    Json.Write(Path.Combine(preview, "icon-style-checks.json"), new { Passed = checks.Count, Checks = checks, Scope = "Icon styles, Damage emblems, Pet expansion filter and existing mode-button/native-icon checks" });
                    ExitTracker(); return;
                }
                if (e.Args.Contains("--checks-only"))
                {
                    foreach (var window in new FrostWindow[] { quests, dps, ObjectivesOverlay, DpsOverlay })
                    { window.ShowActivated = false; window.Left = window.Top = -15000; window.Show(); }
                    quests.Width = 1220; quests.Height = 800; quests.ShowView(TrackerTab.Items);
                    dps.Width = 1170; dps.Height = 740; DpsOverlay.Width = 440; DpsOverlay.Height = 410; ObjectivesOverlay.Width = 380; ObjectivesOverlay.Height = 360;
                    foreach (var window in new FrostWindow[] { quests, dps, ObjectivesOverlay, DpsOverlay }) window.UpdateLayout();
                }
                else if (e.Args.Contains("--session-preview-only")) { foreach (var window in new FrostWindow[] { quests, dps, ObjectivesOverlay, DpsOverlay }) { window.Left = window.Top = -15000; window.Show(); } SessionPolishPreviews.Render(preview, Controller, dps); quests.Width = 1220; quests.Height = 800; dps.Width = 1170; dps.Height = 740; DpsOverlay.Width = 440; DpsOverlay.Height = 410; ObjectivesOverlay.Width = 380; ObjectivesOverlay.Height = 360; }
                else if (e.Args.Contains("--polish-preview-only")) { foreach (var window in new FrostWindow[] { quests, dps, ObjectivesOverlay, DpsOverlay }) { window.Left = window.Top = -15000; window.Show(); } CombatPolishPreviews.Render(preview, Controller, dps, quests); quests.Width = 1220; quests.Height = 800; dps.Width = 1170; dps.Height = 740; DpsOverlay.Width = 440; DpsOverlay.Height = 410; ObjectivesOverlay.Width = 380; ObjectivesOverlay.Height = 360; DpsOverlay.Refresh(); ObjectivesOverlay.Refresh(); }
                else PreviewRenderer.Render(preview, quests, dps, ObjectivesOverlay, DpsOverlay, scale);
                UiChecks.Run(Controller, dps, DpsOverlay, preview, quests);
            }
            catch (Exception error)
            {
                Json.Write(Path.Combine(preview, "validation-failure.json"), new { Error = error.ToString() });
                Exiting = true; Controller.Dispose(); Shutdown(1); return;
            }
            ExitTracker(); return;
        }
        CatalystBranding.SelectedId = CatalystBranding.Resolve(Controller.Profile.AppIconId).Id;
        foreach (Window window in Windows) window.Icon = CatalystBranding.WindowIcon;
        trayIcon = CatalystBranding.CreateTrayIcon();
        tray = new System.Windows.Forms.NotifyIcon { Icon = trayIcon, Text = CatalystBranding.Name, Visible = true };
        iconRefresh.Tick += (_, _) => { iconRefresh.Stop(); ApplyIcon(true); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        var menu = new System.Windows.Forms.ContextMenuStrip();menu.Items.Add(CatalystBranding.Name).Enabled=false;menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());menu.Items.Add("Quest && Gear Tracker",null,(_,_)=>OpenQuests());menu.Items.Add("Combat lab",null,(_,_)=>OpenDps());
        foreach(var action in Enum.GetValues<OverlayShortcutAction>()){var item=menu.Items.Add(ShortcutActionName(action),null,(_,_)=>Toggle((int)action));item.Tag=action;}
        menu.Items.Add("Native game controls",null,(_,_)=>OpenNativeControls());menu.Items.Add("Settings",null,(_,_)=>OpenSettings());menu.Items.Add("Exit tracker",null,(_,_)=>ExitTracker());tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>OpenQuests();RefreshShortcutHints();
        hotkeys = new(Toggle,Controller.Profile); if (hotkeys.Failed.Count > 0) tray.ShowBalloonTip(7000, "Some hotkeys are already in use", string.Join(", ", hotkeys.Failed) + ". Use the tray menu.", System.Windows.Forms.ToolTipIcon.Info);
        menu.Items.Insert(3, new System.Windows.Forms.ToolStripMenuItem("Sync now", null, async (_, _) => await GameSync.Request()));
        timer.Tick += (_, _) =>
        {
            if (stopSignal?.WaitOne(0) == true) { ExitTracker(); return; }
            wheelRouter?.UpdateSurfaces();
            GameSync.Tick();
            bool foreground = !Controller.Profile.HideWhenUnfocused || Native.GameOrCompanionForeground();
            foreach (var panel in Panels())
            {
                if (panel.RequestedVisible && foreground) { if (!panel.IsVisible) panel.Show(); }
                else if (panel.IsVisible) { panel.CancelInteraction(); panel.Hide(); }
            }
            var watch = System.Diagnostics.Stopwatch.StartNew(); RefreshWindows();
            uiSamples.Add(watch.Elapsed.TotalMilliseconds); if (uiSamples.Count > 2048) uiSamples.RemoveAt(0);
            if (++tick % 8 == 0) Native.MaintainBorderless(Controller.Profile.GameFolder, Controller.Profile.BorderlessEnabled);
            if (tick % 60 == 0)
            {
                var input = wheelRouter?.Diagnostics(); var display = Native.DisplayDiagnostics;
                var ordered = uiSamples.Order().ToArray();
                var diagnostics = UpdateDiagnostics.Drain();
                var perf = new { Diagnostics = diagnostics, UiP99Ms = ordered.Length == 0 ? 0 : ordered[(int)((ordered.Length - 1) * .99)], UiMaxMs = ordered.LastOrDefault(), SnapshotMs = Controller.LastSnapshotMilliseconds, Samples = ordered.Length, CapturedAt = DateTime.Now };
                _ = Task.Run(() => { if (input != null) Json.Write(Path.Combine(Controller.DataFolder, "input-status.json"), input); Json.Write(Path.Combine(Controller.DataFolder, "performance-status.json"), perf); if (display != null) Json.Write(Path.Combine(Controller.DataFolder, "display-status.json"), display); });
            }
        };
        wheelRouter = new();
        wheelRouter.ForegroundChanged += allowed => { if (!allowed) foreach (var panel in Panels()) { panel.CancelInteraction(); if (Controller.Profile.HideWhenUnfocused) panel.Hide(); } };
        timer.Start(); Controller.Start(); StartCharacterDiscovery();
        if (firstSetup || e.Args.Contains("--open-tracker")) { OpenQuests(); if(Enum.TryParse<TrackerTab>(Value("--tracker-tab"),out var requested))quests?.ShowView(requested); }
    }
    private readonly List<double> uiSamples = [];
    private IEnumerable<FrostWindow> Panels() => new FrostWindow?[] { ObjectivesOverlay, DpsOverlay, quests, dps, settings, nativeControls, iconGallery }.OfType<FrostWindow>();

    public void OpenQuests(string? zoneFilter = null) { quests ??= new(Controller); if (zoneFilter != null) quests.SetZoneFilter(zoneFilter); quests.Refresh(); Native.ShowPanel(quests); }
    public void OpenDps() { dps ??= new(Controller); dps.Refresh(); Native.ShowPanel(dps); }
    public void OpenSettings() { settings ??= new(Controller); settings.UpdateStatus(); Native.ShowPanel(settings); }
    public void OpenOverlay(bool damage) { var panel=damage?DpsOverlay:ObjectivesOverlay;panel.RequestedVisible=true;panel.Show();Native.SetOverlay(panel,panel.ClickThrough);if(!NativeSetup.GameRunning())panel.Activate(); }
    public void OpenIcons() { iconGallery ??= new(Controller); Native.ShowPanel(iconGallery); }
    public void OpenNativeControls() { nativeControls ??= new(Controller); nativeControls.UpdateStatus(); Native.ShowPanel(nativeControls); }
    private WheelRouter? wheelRouter;
    private void RefreshWindows()
    {
        if (Exiting) return;
        using (UpdateDiagnostics.Measure("UI refresh"))
        {
            ApplyTheme();
            if (ObjectivesOverlay.IsVisible && !ObjectivesOverlay.IsCollapsed) ObjectivesOverlay.Refresh();
            if (DpsOverlay.IsVisible && !DpsOverlay.IsCollapsed) DpsOverlay.Refresh();
            if (quests?.IsVisible == true && !quests.IsCollapsed) quests.Refresh();
            if (dps?.IsVisible == true && !dps.IsCollapsed) dps.Refresh();
            if (tick % 4 == 0) { if (settings?.IsVisible == true) settings.UpdateStatus(); if (nativeControls?.IsVisible == true) nativeControls.UpdateStatus(); }
        }
    }
    private void Toggle(int id)
    {
        if (id == 1) ObjectivesOverlay.RequestedVisible = !ObjectivesOverlay.RequestedVisible;
        if (id == 2) DpsOverlay.RequestedVisible = !DpsOverlay.RequestedVisible;
        if (id == 3) { bool enabled = !Controller.Profile.Objectives.ClickThrough; Controller.Profile.Objectives.ClickThrough = Controller.Profile.Dps.ClickThrough = enabled; ApplySettings(); Controller.Save(); }
    }
    public void ApplySettings() { IconArtwork.Apply(Controller.Profile);RefreshShortcutHints(); ObjectivesOverlay.ApplySettings(); DpsOverlay.ApplySettings(); ApplyTheme(true); ApplyIcon(); iconGallery?.RefreshIcons(); settings?.RefreshIconPresentation(); }
    internal string ShortcutLabel(OverlayShortcutAction action)=>OverlayShortcuts.Label(Controller.Profile,action);
    private static string ShortcutActionName(OverlayShortcutAction action)=>action switch{OverlayShortcutAction.Objectives=>"Toggle objectives",OverlayShortcutAction.Dps=>"Toggle DPS",_=>"Toggle click-through"};
    private void RefreshShortcutHints()
    {
        Resources["ClickThroughShortcutHint"]="("+ShortcutLabel(OverlayShortcutAction.ClickThrough)+" to restore)";
        if(tray?.ContextMenuStrip is {} menu)foreach(System.Windows.Forms.ToolStripItem item in menu.Items)if(item.Tag is OverlayShortcutAction action)item.Text=ShortcutActionName(action)+"  ("+ShortcutLabel(action)+")";
    }
    internal bool TryApplyShortcuts(IReadOnlyDictionary<OverlayShortcutAction,GlobalShortcut> requested,out string error)
    {if(!OverlayShortcuts.Validate(requested,out error))return false;return Controller.Demo||hotkeys==null||hotkeys.TryApply(requested,out error);}
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshIconForDisplay);
    public void RefreshIconForDisplay() { iconRefresh.Stop(); iconRefresh.Start(); }
    public void ApplyIcon(bool force = false)
    {
        string id = CatalystBranding.Resolve(Controller.Profile.AppIconId).Id;
        if (id == CatalystBranding.SelectedId && IconArtwork.Style == CatalystBranding.SelectedStyle && !force) return;
        CatalystBranding.SelectedId = id;
        CatalystBranding.SelectedStyle = IconArtwork.Style;
        foreach (Window window in Windows) window.Icon = CatalystBranding.WindowIcon;
        var replacement = CatalystBranding.CreateTrayIcon(id); var previous = trayIcon;
        trayIcon = replacement; if (tray != null) tray.Icon = replacement; previous?.Dispose();
    }
    public void ResetPositions() { ObjectivesOverlay.Left = 24; ObjectivesOverlay.Top = 120; DpsOverlay.Left = 430; DpsOverlay.Top = 120; }
    private void ApplyTheme(bool force = false)
    {
        var theme = Controller.Profile.Theme == "Auto" ? Controller.ThemeName : Controller.Profile.Theme; if (!force && theme == currentTheme) return; currentTheme = theme;
        Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme)); Appearance.Apply(theme);
        if (!Equals(Resources["TrackerTabStyle"],Controller.Profile.TrackerTabs)) Resources["TrackerTabStyle"]=Controller.Profile.TrackerTabs;
    }
    internal bool CanChangeEquipmentContext() => quests == null || quests.ConfirmGearWorkspaceExit() && quests.ConfirmPetWorkspaceExit();
    internal void ResetTrackerLayout(){OpenQuests();quests?.ResetTrackerLayout();}
    public void ExitTracker()
    {
        if (!Exiting && !Controller.Demo && (quests?.ConfirmGearWorkspaceExit() == false || quests?.ConfirmPetWorkspaceExit() == false)) return;
        if (Exiting) return; Exiting = true; timer.Stop(); iconRefresh.Stop(); StopCharacterDiscovery(); Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged; Native.RestoreGameFrame(); hotkeys?.Dispose(); wheelRouter?.Dispose(); tray?.Dispose(); trayIcon?.Dispose(); Controller.Dispose(); stopSignal?.Dispose(); singleton?.Dispose(); applicationPresence?.Dispose(); foreach (var window in Windows.Cast<Window>().ToArray()) window.Close(); Shutdown();
    }
}
