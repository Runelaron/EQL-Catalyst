using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

public enum SettingsSection { Appearance, Character, Combat, Objectives, Sync, Advanced }
public sealed partial class SettingsWindow
{
    private readonly Dictionary<SettingsSection, ScrollViewer> pages = [];
    private readonly Dictionary<SettingsSection, Button> navigation = [];
    private readonly Grid pageHost = new();
    private readonly TextBlock validation = UI.Text("", 11, "Mint");
    private CaptureSettingsControls? captures;
    private EncounterTimingControls timingInputs=null!;
    private readonly Image iconPreview = new() { Width = 28, Height = 28, Margin = new(0, 0, 6, 4) };
    private readonly TextBlock iconName = UI.Text("Blue Orb", 12);
    internal SettingsSection SelectedSection { get; private set; }
    internal void SelectSection(SettingsSection section)
    {
        SelectedSection = section;
        foreach (var (id, page) in pages) page.Visibility = id == section ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (id, button) in navigation) UI.SetPressed(button, id == section);
    }
    private void Invalid(SettingsSection section, string text) { SelectSection(section); validation.Text = text; }
    private static UIElement Field(string title, Control control, string help)
    { control.ToolTip = help; return UI.Field(title, control); }
    private static Border Group(string title, params UIElement[] contents) => UI.Card(UI.Stack(new[] { UI.Text(title, 13, "Accent", true) }.Concat(contents).ToArray()));
    private static WrapPanel Flow(params UIElement[] children) { var panel = new WrapPanel(); foreach (var child in children) panel.Children.Add(child); return panel; }
    private void BuildPages()
    {
        HeadingTitle.Text = ""; HeadingTitle.Inlines.Clear();
        HeadingTitle.FontSize = 22; var wordmark = new Run("EQL Catalyst") { FontFamily = new FontFamily("Georgia"), Foreground = new LinearGradientBrush(Color.FromRgb(255, 235, 168), Color.FromRgb(197, 142, 54), 90) }; wordmark.SetBinding(TextElement.FontSizeProperty, new System.Windows.Data.Binding("FontSize") { Source = HeadingTitle }); HeadingTitle.Inlines.Add(wordmark);
        HeadingTitle.Inlines.Add(new Run(" / Settings") { FontSize = 16 }); HeadingTitle.Visibility = Visibility.Visible;
        HeadingTitle.ToolTip = "EQL Catalyst settings · saved per character";
        foreach (var control in new Control[] { theme, petType, loadout, objectiveMode }) control.Width = 180;
        theme.Width = 168; meterStyle.Width = 130; timeout.Width = resume.Width = 76; currentClasses.Width = reached50.Width = priority.Width = 200;
        hide.Content = "Hide outside game"; sounds.Content = "Sound alerts"; click.Content = "Click-through"; locked.Content = "Lock position";
        hide.ToolTip = "Hide panels when neither EverQuest nor EQL Catalyst is foreground."; sounds.ToolTip = "Play a sound when a needed-item alert arrives.";
        click.ToolTip = "Send overlay mouse input through to EverQuest. "+OverlayShortcuts.Label(Controller.Profile,OverlayShortcutAction.ClickThrough)+" restores interaction."; locked.ToolTip = "Prevent moving or resizing the compact overlays.";
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var main = new Grid(); main.ColumnDefinitions.Add(new() { Width = new(132) }); main.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var rail = new StackPanel { Margin = new(0, 0, 8, 0) };
        Grid.SetColumn(pageHost, 1); main.Children.Add(rail); main.Children.Add(pageHost); root.Children.Add(main);
        void Page(SettingsSection id, string title, UIElement content)
        {
            var button = UI.Button(title, () => SelectSection(id)); button.HorizontalContentAlignment = HorizontalAlignment.Left; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Height = 34; button.ToolTip = "Open " + title.ToLowerInvariant() + " settings";
            navigation[id] = button; rail.Children.Add(button);
            var scroll = UI.Scroll(content); scroll.Padding = new(0, 0, 4, 0); pages[id] = scroll; pageHost.Children.Add(scroll);
        }
        var icon = UI.Row(iconPreview, iconName, UI.Button("Choose icon…", () => ((App)Application.Current).OpenIcons())); iconName.VerticalAlignment = VerticalAlignment.Center; iconName.Margin = new(0, 0, 8, 0);
        var look=UI.Stack(Flow(Field("Theme",theme,"Auto follows the saved game skin; manual themes update every open panel immediately."),Field("Secondary text",secondaryText,"Helper text for this theme family. Coldain defaults to Candlelight Cream; Sunstrider to Warm Gold; Shadowman to Ash Silver. Status and combat colors stay unchanged.")),BuildIconStyleControls(),BuildVisualAppearanceControls(),UI.Label("App icon · per character"),icon);
        opacity.Width = 170; opacity.ToolTip = "Whole-overlay opacity; updates both overlays and the preview while dragging. Saved automatically.";
        var percent = UI.Text("", 12); void OpacityText() => percent.Text = $"{opacity.Value:P0}"; OpacityText(); opacity.ValueChanged += (_, _) => OpacityText();
        var behavior = new UniformGridEx(2); foreach (var box in new[] { hide, click, locked, sounds }) behavior.Children.Add(box);
        var menuSize=UI.Select(Enum.GetNames<OverlayMenuSize>(),Controller.Profile.OverlayMenus.ToString(),150);
        menuSize.SelectionChanged+=(_,_)=>{if(Enum.TryParse<OverlayMenuSize>(menuSize.SelectedItem?.ToString(),out var selected)){Controller.Profile.OverlayMenus=selected;OverlayMenuDensity.Update(Controller.Profile);Controller.Save();}};
        Page(SettingsSection.Appearance, "Appearance", UI.Stack(Group("Theme, style & icon", look), Flow(Group("Motion & opacity", smoothMotion, UI.Row(opacity, percent)), Group("Window behavior", behavior, UI.Field("Overlay menu size",menuSize))), new Expander { Header = "Advanced appearance", Content = UI.Button("Reset overlay positions", ResetPositions) }));
        Page(SettingsSection.Character, "Character", UI.Stack(Group("Character & classes", Flow(Field("Current classes", currentClasses, "Up to three class codes, separated by /. Leave blank while unknown. Fresh /who output updates these."), Field("Reached level 50", reached50, "Historical class confirmations; manual corrections are retained.")), Field("Active loadout", loadout, "Equipment and combat sample profile to use.")), Group("Profiles", UI.Text(Controller.Profile.Character + " · " + Controller.Profile.Server, 13), UI.Button("Switch character…", SwitchCharacter))));
        ((StackPanel)pages[SettingsSection.Character].Content).Children.Add(Group("Stat profiles", UI.Text("Game evidence fills known levels, classes and AA ranks. Review missing totals and your usual buffs only when needed for a comparison.", 12, "Muted"), UI.Button("Review Self profiles…", () => { var loadout = Controller.Profile.ActiveLoadout; Native.ShowPanel(new StatProfileDialog(Controller, loadout, false, EqlTracker.Core.EquipmentPositions.For(loadout, false), UpdateStatus) { Owner = this }); })));
        timingInputs=new(timeout,resume,Controller.Profile.EncounterTimeoutSeconds);
        Page(SettingsSection.Combat,"Combat",UI.Stack(Group("Encounter timing",timingInputs),Group("Pet identity",Field("Element override",petType,petType.ToolTip!.ToString()!),UI.Text("Auto uses summon and owned-pet evidence. A manual override takes precedence.",12,"Muted"))));
        Page(SettingsSection.Objectives, "Objectives", UI.Stack(Group("Planning & fallback", Field("When this zone has no goals", objectiveMode, objectiveMode.ToolTip!.ToString()!), Field("Allocation priority", priority, "Class codes, highest priority first. Shared physical stock is allocated once; queue order is independent.")), UI.Text("Manage tracked quests and your ordered queue in Quest & Gear Tracker.", 12, "Muted"), UI.Button("Open tracker", () => ((App)Application.Current).OpenQuests())));
        folder.Width = double.NaN;
        captures=new(Controller);
        var wikiRefresh=UI.Select(["Daily","On startup","Manual"],Controller.Profile.WikiRefresh switch{WikiRefreshMode.Startup=>"On startup",WikiRefreshMode.Manual=>"Manual",_=>"Daily"},150);
        wikiRefresh.SelectionChanged+=(_,_)=>{Controller.Profile.WikiRefresh=wikiRefresh.SelectedIndex switch{1=>WikiRefreshMode.Startup,2=>WikiRefreshMode.Manual,_=>WikiRefreshMode.Daily};Controller.Save();};
        var wikiControls=Group("Wiki & workspace",Field("Refresh wiki",wikiRefresh,"Daily checks once per 24 hours while running. On startup checks once per launch. Manual checks only when requested. Missing item lookups remain available; cached facts stay usable offline."),UI.Text(Controller.Profile.LastWikiRefreshAt is {} last?"Last source check: "+last.ToString("MMM dd HH:mm"):"No completed source check recorded.",11,"Muted"),Flow(UI.Button("Refresh wiki now",()=>_ = Controller.RefreshWikiAsync()),UI.Button("Reset Tracker layout",()=>((App)Application.Current).ResetTrackerLayout())));
        Page(SettingsSection.Sync, "Sync & files", UI.Stack(Group("Inventory capture",captures),Group("Game & synchronization", Field("EverQuest folder", folder, "Game folder containing Logs, character configuration and inventory exports."), UI.Text("Fresh exports import automatically. Link and test the Sync shortcut in Native controls.", 12), Flow(UI.Button("Native controls…", () => ((App)Application.Current).OpenNativeControls()), UI.Button("Sync now", () => _ = ((App)Application.Current).GameSync.Request()), UI.Button("Read existing export", () => { lock (Controller.Sync) Controller.ReadInventory(); Controller.Save(); UpdateStatus(); }))), Group("Local data", UI.Text(Controller.DataFolder, 11, "Muted"), Flow(UI.Button("Open folder", () => Process.Start(new ProcessStartInfo(Controller.DataFolder) { UseShellExecute = true })), UI.Button("Export settings", Export), UI.Button("Fetch metadata", () => _ = Controller.RefreshWikiAsync(true))))));
        ((StackPanel)pages[SettingsSection.Sync].Content).Children.Insert(1,wikiControls);
        var displayActions = Flow(UI.Button("Prepare setup", PrepareDisplay), UI.Button("Apply · game closed", () => ApplyDisplay(false)), UI.Button("Restore", () => ApplyDisplay(true)));
        shortcutSettings=new(Controller.Profile);
        Page(SettingsSection.Advanced,"Advanced",UI.Stack(Group("Display setup · reversible",borderless,UI.Text("Prepare a reviewable backup. Apply and restore only while EverQuest is closed; the game is never closed automatically.",12),displayActions,displayStatus),Group("Hotkeys",shortcutSettings),new Expander{Header="Diagnostics & coverage",Content=Group("Observed status",status)},Flow(UI.Button("Quest tracker",()=>((App)Application.Current).OpenQuests()),UI.Button("Combat Lab",()=>((App)Application.Current).OpenDps()))));
        var footer = new DockPanel { Margin = new(0, 6, 0, 0) }; var save = UI.Button("Save changes", Save, true); save.ToolTip = "Validate and save non-appearance changes. Appearance saves automatically."; DockPanel.SetDock(save, Dock.Right); footer.Children.Add(save);
        var footerText = UI.Text(Controller.Profile.Character + " · " + Controller.Profile.Server + "  |  " + ReleaseInfo.Label + "  |  Appearance saves instantly", 11, "Muted"); footerText.TextTrimming = TextTrimming.CharacterEllipsis; footerText.TextWrapping = TextWrapping.NoWrap;
        footer.Children.Add(UI.Stack(footerText, validation)); Grid.SetRow(footer, 1); root.Children.Add(footer); Body.Children.Add(root); SelectSection(SettingsSection.Appearance);
        Usability.Tooltips(Body);
    }
}
