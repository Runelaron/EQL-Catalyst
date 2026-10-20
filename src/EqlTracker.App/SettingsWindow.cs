using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class SettingsWindow : FrostWindow
{
    private readonly TextBox folder, currentClasses, reached50, priority, timeout, resume;
    private readonly ComboBox secondaryText, theme, petType, loadout, meterStyle;
    private readonly CheckBox hide, sounds, click, locked, smoothMotion, borderless;
    private readonly TextBlock displayStatus = UI.Text("", 11, "Mint");
    private readonly Slider opacity = new() { Minimum = .3, Maximum = 1, TickFrequency = .05, Width = 250, IsSnapToTickEnabled = true, Margin = new(0, 4, 0, 6) };
    private readonly TextBlock status = UI.Text("", 12, "Muted");
    private bool changingSecondary;
    private readonly MeterPreview preview = new();
    private readonly System.Windows.Threading.DispatcherTimer appearanceSave = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ComboBox objectiveMode;
    public SettingsWindow(TrackerController controller) : base(controller, "Tracker Settings", "", 940, 610)
    {
        var profile = controller.Profile;
        folder = UI.Input(profile.GameFolder); currentClasses = UI.Input(string.Join('/', profile.CurrentClasses)); reached50 = UI.Input(string.Join(',', profile.Level50Classes)); priority = UI.Input(string.Join(',', profile.GoalPriority)); timeout = UI.Input(profile.EncounterTimeoutSeconds.ToString(), 130); resume = UI.Input(profile.EncounterResumeSeconds.ToString(), 130); resume.ToolTip = "Resume an unresolved fight within this many seconds of its last combat activity. Set 0 to disable resumption. The idle timeout still applies.";
        Appearance.RaisedButtons(Body);
        theme = UI.Select(Appearance.Themes, ThemeCatalog.SelectedName(profile.Theme), 250);
        secondaryText = UI.Select(Appearance.SecondaryTextChoices, TrackerPresentation.Secondary(profile,profile.Theme=="Auto"?controller.ThemeName:profile.Theme), 168);
        objectiveMode = UI.Select(["Current zone only", "My queue", "Suggestions"], profile.ObjectiveMode, 160);
        objectiveMode.ToolTip = "Always show local goals first. When none remain, My queue follows your pinned order; Suggestions ranks available goals by class priority and source evidence.";
        petType = UI.Select(["Auto", "Earth", "Fire", "Air", "Water", "Undead", "Animation", "Warder", "Spirit", "Warrior"], profile.PetElementOverride.Length == 0 ? "Auto" : profile.PetElementOverride, 170);
        petType.ToolTip = "Auto uses correlated summon evidence, then explicitly inferred owned-pet signatures. A manual choice takes precedence.";
        loadout = UI.Select(profile.Loadouts.Select(x => x.Name), profile.SelectedLoadout, 220);
        hide = new() { Content = "Hide overlays outside EverQuest and this companion", IsChecked = profile.HideWhenUnfocused };
        sounds = new() { Content = "Play a sound for needed-item alerts", IsChecked = profile.Sounds };
        click = new() { Content = "Click-through overlays (Ctrl+Alt+T toggles)", IsChecked = profile.Objectives.ClickThrough && profile.Dps.ClickThrough };
        locked = new() { Content = "Lock overlay positions and sizes", IsChecked = profile.Objectives.Locked && profile.Dps.Locked };
        smoothMotion = new() { Content = "Smooth motion", IsChecked = profile.SmoothMeterMotion, ToolTip = "Animate damage totals, bars and row movement. DPS/HPS readings remain exact on their two-second cadence." };
        borderless = new() { Content = "Fit windowed EverQuest borderlessly to its monitor", IsChecked = profile.BorderlessEnabled };
        meterStyle = UI.Select(["Minimal", "Frost", "Luminous"], profile.MeterStyle, 120);
        opacity.Value = profile.Objectives.Opacity;
        iconStyle.SelectedItem = profile.IconStyle.ToString();
        BuildPages(); UpdateStatus(); preview.Apply(profile);
        appearanceSave.Tick += (_, _) => { appearanceSave.Stop(); Controller.Save(); };
        secondaryText.SelectionChanged += (_, _) => { if(changingSecondary)return; string current=profile.Theme; profile.SecondaryTextByTheme[TrackerPresentation.ThemeFamily(current=="Auto"?Controller.ThemeName:current)]=secondaryText.SelectedItem?.ToString()??"Candlelight Cream"; ApplyAppearance(); }; theme.SelectionChanged += (_, _) => { profile.Theme=ThemeCatalog.Choices.First(c=>c.Name==theme.SelectedItem?.ToString()).Id; changingSecondary=true;string current=profile.Theme;secondaryText.SelectedItem=TrackerPresentation.Secondary(profile,current=="Auto"?Controller.ThemeName:current);changingSecondary=false;ApplyAppearance(); }; meterStyle.SelectionChanged += (_, _) => ApplyAppearance();
        iconStyle.SelectionChanged += (_, _) => { ApplyAppearance(); appearanceSave.Stop(); Controller.Save(); };
        objectiveMode.SelectionChanged += (_, _) => { profile.ObjectiveMode = objectiveMode.SelectedItem?.ToString() ?? "My queue"; Controller.Save(); };
        opacity.ValueChanged += (_, _) => ApplyAppearance(); smoothMotion.Click += (_, _) => ApplyAppearance();
        IsVisibleChanged += (_, _) => { if (!IsVisible && appearanceSave.IsEnabled) { appearanceSave.Stop(); Controller.Save(); } };
    }
    private void ApplyAppearance()
    {
        var p = Controller.Profile; p.SecondaryText = secondaryText.SelectedItem?.ToString() ?? "Bright Frost";
        p.IconStyle = Enum.TryParse<IconArtworkStyle>(iconStyle.SelectedItem?.ToString(), out var selectedStyle) ? selectedStyle : IconArtworkStyle.Vibrant;
        p.MeterStyle = meterStyle.SelectedItem?.ToString() ?? "Frost"; p.SmoothMeterMotion = smoothMotion.IsChecked == true;
        p.Objectives.Opacity = p.Dps.Opacity = opacity.Value;
        ((App)Application.Current).ApplySettings(); preview.Apply(p); RefreshIconPresentation();
        appearanceSave.Stop(); appearanceSave.Start();
    }
    public void UpdateStatus()
    {
        captures?.Refresh();
        lock (Controller.Sync)
        {
            string Age(DateTime? value) => value.HasValue && value != default(DateTime) ? value.Value.ToString("MMM dd HH:mm:ss") + $" ({Math.Max(0, (DateTime.Now - value.Value).TotalMinutes):N0} min ago)" : "not observed";
            var identity = Controller.Profile.PetIdentities.LastOrDefault(x => x.Until == null);
            status.Text = Controller.Status + "\nLog activity: " + Age(Controller.LastLogActivity) + " · Classes: " + Age(Controller.Profile.ClassesObservedAt) + "\nInventory snapshot: " + Age(Controller.Inventory.CapturedAt == default ? null : Controller.Inventory.CapturedAt) + "\nPet: " + Controller.Profile.EffectivePetElement + " · " + (Controller.Profile.PetElementOverride.Length > 0 ? "Manual override" : identity?.ElementConfidence ?? "Unknown") + "\n" + identity?.ElementEvidence + "\nTheme: " + ThemeCatalog.Resolve(Controller.ThemeName).Name + " · catalog: " + Controller.Catalog.Items.Count + " items / " + Controller.Catalog.Quests.Count + " class entries\n" + Controller.EquipmentSync.Status + $" · {Controller.EquipmentSync.Equipment:N0} equipment records cached";
            if (displayStatus.Text.Length == 0) displayStatus.Text = Controller.Profile.DisplaySetupFolder.Length == 0 ? "No display package prepared." : Controller.Profile.DisplaySetupFolder;
            iconPreview.Source = CatalystBranding.Image(Controller.Profile.AppIconId); iconName.Text = CatalystBranding.Choices.FirstOrDefault(x => x.Id == Controller.Profile.AppIconId)?.Name ?? "Blue Orb";
        }
    }
    private void Save()
    {
        if (!Directory.Exists(folder.Text)) { Invalid(SettingsSection.Sync, "Choose an existing EverQuest Legends folder."); return; }
        var valid = Controller.Catalog.Quests.Select(x => x.Class).ToHashSet();
        List<string> Parse(string text) => text.ToUpperInvariant().Split([',', '/', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        var current = Parse(currentClasses.Text); var fifty = Parse(reached50.Text); var order = Parse(priority.Text);
        if (current.Count > 3 || current.Concat(fifty).Any(c => !valid.Contains(c))) { Invalid(SettingsSection.Character, "Use up to three valid current class codes; leave blank while unknown."); return; }
        if (order.Any(c => !valid.Contains(c))) { Invalid(SettingsSection.Objectives, "Goal priority requires valid class codes."); return; }
        if (!int.TryParse(timeout.Text, out int seconds) || seconds is < 2 or > 120) { Invalid(SettingsSection.Combat, "Encounter timeout must be 2–120 seconds."); return; }
        if (!int.TryParse(resume.Text, out int resumeSeconds) || resumeSeconds is < 0 or > 300 || resumeSeconds != 0 && resumeSeconds < seconds) { Invalid(SettingsSection.Combat, "Resume window must be 0 or at least the idle timeout, up to 300 seconds."); return; }
        if (loadout.SelectedItem?.ToString() != Controller.Profile.SelectedLoadout && !((App)Application.Current).CanChangeEquipmentContext()) return;
        lock (Controller.Sync)
        {
            var p = Controller.Profile; p.EncounterResumeSeconds = resumeSeconds; p.GameFolder = Path.GetFullPath(folder.Text); p.CurrentClasses = current; p.Level50Classes = fifty.ToHashSet(); p.GoalPriority = order; p.EncounterTimeoutSeconds = seconds;
            p.HideWhenUnfocused = hide.IsChecked == true; p.Sounds = sounds.IsChecked == true;
            p.SmoothMeterMotion = smoothMotion.IsChecked == true; p.MeterStyle = meterStyle.SelectedItem?.ToString() ?? "Frost";
            p.SelectedLoadout = loadout.SelectedItem?.ToString() ?? "Current"; p.ActiveLoadout.Classes = current;
            p.PetElementOverride = petType.SelectedItem?.ToString() is null or "Auto" ? "" : petType.SelectedItem.ToString()!; p.ActiveLoadout.PetType = p.EffectivePetElement; p.BorderlessEnabled = borderless.IsChecked == true;
            foreach (var overlay in new[] { p.Objectives, p.Dps }) { overlay.Opacity = opacity.Value; overlay.Locked = locked.IsChecked == true; overlay.ClickThrough = click.IsChecked == true; }
            Controller.Save(); ((App)Application.Current).ApplySettings();
        }
        UpdateStatus();
        validation.Text = "Settings saved.";
    }
    private void PrepareDisplay()
    {
        try { var resolution = Native.GameResolution(); string package = Path.Combine(Controller.DataFolder, "display-setup", DateTime.Now.ToString("yyyyMMdd-HHmmss")); BorderlessSetup.Prepare(Controller.Profile.GameFolder, package, resolution.Width, resolution.Height); Controller.Profile.DisplaySetupFolder = package; Controller.Save(); displayStatus.Text = "Ready for your next game restart: " + package; }
        catch (Exception error) { displayStatus.Text = error.Message; }
    }
    private void ApplyDisplay(bool restore)
    {
        try { if (Controller.Profile.DisplaySetupFolder.Length == 0) PrepareDisplay(); displayStatus.Text = BorderlessSetup.Apply(Controller.Profile.DisplaySetupFolder, restore); }
        catch (Exception error) { displayStatus.Text = error.Message; }
    }
    private void ResetPositions()
    {
        Controller.Profile.Objectives.Left = 24; Controller.Profile.Objectives.Top = 120; Controller.Profile.Dps.Left = 430; Controller.Profile.Dps.Top = 120; ((App)Application.Current).ResetPositions(); Controller.Save();
    }
    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON settings|*.json", FileName = Controller.Profile.Character + "-tracker-settings.json" };
        if (dialog.ShowDialog(this) == true) Json.Write(dialog.FileName, Controller.Profile);
    }
    private void SwitchCharacter()
    {
        var form = new EntryDialog("Open another character profile", [("Character", Controller.Profile.Character), ("Server", Controller.Profile.Server)]) { Owner = this };
        if (form.ShowDialog() != true) return;
        if (form.Values.Any(x => !System.Text.RegularExpressions.Regex.IsMatch(x, @"^[A-Za-z][A-Za-z0-9_-]{0,60}$"))) { MessageBox.Show("Use character/server names containing letters, numbers, underscores, or hyphens."); return; }
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "--character", form.Values[0], "--server", form.Values[1], "--data-root", ((App)Application.Current).DataRoot }) start.ArgumentList.Add(argument);
        Process.Start(start);
    }
}
