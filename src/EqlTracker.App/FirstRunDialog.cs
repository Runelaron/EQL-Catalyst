using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

/// <summary>Profile selection only. Never changes game files or enables native captures.</summary>
internal sealed class FirstRunDialog : FrostDialog
{
    private readonly TextBox folder = UI.Input(""), character = UI.Input("", 190), server = UI.Input("", 190);
    private readonly ComboBox profiles = new() { MinWidth = 230, Height = 28, Margin = new(0, 3, 0, 6) };
    private readonly TextBlock status = UI.Text("", 12, "Mint");
    private readonly List<LaunchProfile> saved;
    internal LaunchProfile? Selection { get; private set; }
    internal FirstRunDialog(IReadOnlyList<LaunchProfile> existing) : base("Welcome · Beta 1.0", 610, 475)
    {
        saved = existing.ToList(); ShowInTaskbar = true; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        folder.MinWidth = 220; folder.Width = double.NaN; folder.ToolTip = "Choose the folder containing eqgame.exe and Logs, or leave blank to use saved/offline data.";
        var browse = UI.Button("Browse…", () => { var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Choose your EverQuest Legends folder" }; if (picker.ShowDialog(this) == true) { folder.Text = picker.FolderName; Discover(); } });
        browse.ToolTip = "Select the game folder. No game files are changed.";
        var folderRow = new DockPanel(); DockPanel.SetDock(browse, Dock.Right); folderRow.Children.Add(browse); folderRow.Children.Add(folder);
        profiles.ToolTip = "Select an existing character or a character discovered from game filenames. You can also enter a new identity below.";
        profiles.SelectionChanged += (_, _) => { if (profiles.SelectedItem is LaunchProfile p) { character.Text = p.Character; server.Text = p.Server; folder.Text = p.GameFolder; } };
        Editor.Children.Add(UI.Stack(UI.Text("Choose your character once. EQL Catalyst remembers the last profile you open.", 13), UI.Label("EverQuest Legends folder · optional offline"), folderRow, UI.Button("Find characters", Discover), UI.Label("Saved or discovered characters"), profiles, UI.Row(UI.Field("Character", character), UI.Field("Server", server)), UI.Text("Offline: leave the game folder blank. Existing profiles keep their saved game path. Captures and game controls remain opt-in.", 12, "SecondaryText"), status));
        character.ToolTip = "Your character name; no level or classes are assumed."; server.ToolTip = "Server identifier used in log and inventory filenames.";
        var open = UI.Button("Open EQL Catalyst", Accept, true); open.IsDefault = true; open.ToolTip = "Open this character and remember it for the next launch.";
        Actions.Children.Add(open); Actions.Children.Add(UI.Button("Cancel", Close));
        Discover();
        if (saved.Count == 1) profiles.SelectedItem = saved[0];
    }
    private void Discover()
    {
        try
        {
            var choices = saved.Concat(LauncherPreferences.FromGame(folder.Text.Trim())).DistinctBy(p => (p.Character.ToLowerInvariant(), p.Server.ToLowerInvariant())).ToArray();
            var old = profiles.SelectedItem as LaunchProfile; profiles.ItemsSource = choices;
            profiles.SelectedItem = choices.FirstOrDefault(p => p.Character == old?.Character && p.Server == old.Server);
            status.Text = choices.Length == 0 ? "No characters found. Enter a character and server to start offline." : $"{choices.Length} character profile(s) available.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { status.Text = "Cannot inspect that folder: " + e.Message; }
    }
    private void Accept()
    {
        var p = new LaunchProfile(character.Text.Trim(), server.Text.Trim(), folder.Text.Trim());
        if (!p.Valid) { status.Text = "Enter a character and server using letters, numbers, underscores or hyphens; start each with a letter."; return; }
        var existing = saved.FirstOrDefault(s => s.Character.Equals(p.Character, StringComparison.OrdinalIgnoreCase) && s.Server.Equals(p.Server, StringComparison.OrdinalIgnoreCase));
        if (p.GameFolder.Length > 0 && p.GameFolder != existing?.GameFolder && (!Directory.Exists(p.GameFolder) || !File.Exists(Path.Combine(p.GameFolder, "eqgame.exe")))) { status.Text = "Choose the game folder containing eqgame.exe, or clear it to continue offline."; return; }
        if (p.GameFolder.Length == 0 && existing != null) p = p with { GameFolder = existing.GameFolder };
        Selection = p with { GameFolder = p.GameFolder.Length > 0 ? Path.GetFullPath(p.GameFolder) : "" }; DialogResult = true;
    }
}
