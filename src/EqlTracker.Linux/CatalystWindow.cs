using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using EqlTracker.App;
using EqlTracker.Core;

namespace EqlTracker.Linux;

// Linux uses one top-level surface: toolbar and attached content cannot drift
// apart when the compositor moves the window.
internal sealed partial class CatalystWindow : Window
{
    private readonly DockPanel root = new();
    private readonly Grid panel = new() { RowDefinitions = new("Auto,*") };
    private readonly TextBlock status = Text("");
    private readonly ContentControl body = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Dictionary<string, Button> workspaceButtons = [];
    private readonly List<Action> live = [];
    private TrackerController? controller;
    private string workspace = "Tracker";
    private bool expanded = true, closingApproved, disposed, prompting;
    private PetWorkspaceDraft? petDraft;
    private Window? damageOverlay, healingOverlay;
    private readonly Dictionary<string, Size> workspaceSizes = [];
    private double toolbarWidth = 440;
    private static readonly IBrush Gold = Brush.Parse("#E8BC66");
    private static readonly IBrush Blue = Brush.Parse("#69D7EC");
    private static readonly IBrush Red = Brush.Parse("#FF797C");
    private static readonly IBrush Rose = Brush.Parse("#ED9FB7");
    private static readonly IBrush Green = Brush.Parse("#7DE0A8");

    internal CatalystWindow(string[] args)
    {
        Title = "EQL Catalyst — Linux preview"; Width = 915; Height = 650; MinWidth = 640; MinHeight = 420;
        Background = Brush.Parse("#EB102531"); Foreground = Brush.Parse("#E0EDF2"); FontSize = 14;
        WindowDecorations = WindowDecorations.None; ExtendClientAreaToDecorationsHint = true;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None];
        Content = root; BuildToolbar(); DockPanel.SetDock(status, Dock.Bottom); status.Margin = new(8, 4); status.FontSize = 11; status.TextWrapping = TextWrapping.Wrap; root.Children.Add(status);
        panel.Children.Add(body); Grid.SetRow(body, 1); root.Children.Add(panel);
        Closing += async (_, e) => { if (closingApproved || controller == null) return; e.Cancel = true; if (!await ResolveDraft()) return; closingApproved = true; SaveBounds(); DisposeController(); Close(); };
        Closed += (_, _) => DisposeController();
        timer.Tick += (_, _) => { if (controller == null || disposed) return; foreach (var update in live.ToArray()) update(); status.Text = controller.Status + " · " + controller.EquipmentSync.Status; };
        string Value(string name) { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : ""; }
        if(Value("--smoke").Length>0){ShowInTaskbar=false;Position=new PixelPoint(-12000,-12000);OpenProfile("ExampleHero","example","",true);Opened+=(_,_)=>_ = Smoke(Value("--smoke"));}
        else if (args.Contains("--demo")) OpenProfile("ExampleHero", "example", "", true);
        else if (Value("--character").Length > 0 && Value("--server").Length > 0) OpenProfile(Value("--character"), Value("--server"), Value("--game"), false);
        else BuildLaunch();
    }
    private void BuildToolbar()
    {
        var bar = new DockPanel { Height = 28, LastChildFill = true, Background = Brushes.Transparent };
        var actions = Flow(); DockPanel.SetDock(actions, Dock.Right);
        actions.Children.Add(Action("⚙", ShowSettings, 24)); actions.Children.Add(Action("×", Close, 24));
        bar.Children.Add(actions); var buttons = Flow();
        buttons.Children.Add(new TextBlock { Text = "EQL", Foreground = Gold, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 8, 0) });
        foreach (string name in new[] { "Tracker", "Combat Lab", "Overview" }) { var button = Action(name, () => _ = Switch(name), double.NaN); button.MinHeight = button.Height = 24; button.Background = Brushes.Transparent; workspaceButtons[name] = button; buttons.Children.Add(button); }
        bar.Children.Add(buttons); bar.PointerPressed += (_, e) => { if (e.Source is not (DockPanel or TextBlock)) return; if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { if (e.ClickCount == 2) _ = Switch(workspace); else BeginMoveDrag(e); } };
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
    }
    private void BuildLaunch()
    {
        live.Clear(); var content = Column(); content.Children.Add(Heading("EQL Catalyst — Choose a character"));
        var character = Entry("Character");var server = Entry("Server");var game = Entry("EQ folder (optional)");
        content.Children.Add(Field("Character", character)); content.Children.Add(Field("Server", server)); content.Children.Add(Field("Game folder", game));
        var buttons = Flow(); buttons.Children.Add(Action("Browse", async () => { var choices = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Choose the EQ folder", AllowMultiple = false }); if (choices.FirstOrDefault()?.TryGetLocalPath() is { } path) game.Text = path; }));
        buttons.Children.Add(Action("Open", () => { if (!new LaunchProfile(character.Text ?? "", server.Text ?? "", game.Text ?? "").Valid) { status.Text = "Enter valid character and server names."; return; } OpenProfile(character.Text!, server.Text!, game.Text ?? "", false); }));
        buttons.Children.Add(Action("Explore offline", () => OpenProfile("ExampleHero", "example", "", true))); content.Children.Add(buttons);
        content.Children.Add(Text("Game files are read-only. Select your EQ installation inside its Wine or Proton prefix. Profiles stay in the Linux XDG data directory.")); body.Content = Scroll(content);
    }
    private void OpenProfile(string character, string server, string game, bool demo)
    {
        try
        {
            DisposeController(); disposed = false;
            controller = new TrackerController(dataFolder:demo?Path.Combine(Path.GetTempPath(),"catalyst-linux-demo-"+Guid.NewGuid().ToString("N")):null,demo: demo, character: character, server: server, gameFolder: game);
            if (demo) controller.SeedDemo(); else controller.Start();
            Title = "EQL Catalyst — " + character; ApplyTheme();
            var layout=controller.ReadLinuxPresentation<LinuxLayout>();if(layout!=null){foreach(var pair in layout.Sizes.Where(p=>p.Value.Length==2))workspaceSizes[pair.Key]=new(pair.Value[0],pair.Value[1]);toolbarWidth=Math.Max(440,layout.ToolbarWidth);workspace=layout.Workspace is "Tracker" or "Combat Lab" or "Overview"?layout.Workspace:"Tracker";Width=Math.Max(640,layout.Width);Height=Math.Max(420,layout.Height);}
            timer.Start(); RenderWorkspace();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { status.Text = "Cannot open profile: " + e.Message; }
    }
    private async Task Switch(string next)
    {
        if (controller == null) return;
        if (!await ResolveDraft()) return;
        if (expanded) workspaceSizes[workspace] = new(Width, Height);
        expanded = next != workspace || !expanded; workspace = next;
        if (!expanded) { toolbarWidth = Math.Max(440, Math.Min(Width, toolbarWidth)); MinWidth = 440; MinHeight = 28; Height = 28; Width = toolbarWidth; panel.IsVisible = status.IsVisible = false; }
        else { MinWidth = 640; MinHeight = 420; var size = workspaceSizes.GetValueOrDefault(next, new(915, 650)); Width = size.Width; Height = size.Height; panel.IsVisible = status.IsVisible = true; RenderWorkspace(); }
        foreach (var pair in workspaceButtons) { pair.Value.BorderBrush = pair.Key == workspace && expanded ? Blue : Brushes.Transparent; pair.Value.BorderThickness = new(1); }
        SaveBounds();
    }
    private void RenderWorkspace()
    {
        live.Clear(); if (controller == null) return;
        panel.Children.OfType<TextBlock>().ToList().ForEach(t => panel.Children.Remove(t));
        var heading = Heading("EQL Catalyst — " + workspace); heading.Margin = new(8, 6, 8, 8); panel.Children.Add(heading);
        body.Content = workspace switch { "Combat Lab" => BuildLab(), "Overview" => BuildOverview(), _ => BuildTracker() };
    }
    private void SaveBounds()
    {
        if(controller==null)return;if(expanded)workspaceSizes[workspace]=new(Width,Height);else toolbarWidth=Width;
        controller.SaveLinuxPresentation(new LinuxLayout{Width=expanded?Width:915,Height=expanded?Height:650,ToolbarWidth=toolbarWidth,Workspace=workspace,Expanded=expanded,Sizes=workspaceSizes.ToDictionary(p=>p.Key,p=>new[]{p.Value.Width,p.Value.Height})});
    }
    private async Task<bool> ResolveDraft()
    {
        if (petDraft?.Dirty != true) return true; if (prompting) return false; prompting = true;
        try
        {
            string result = await Choice("Unsaved Pet changes", "Save the draft before leaving?", ["Save", "Discard", "Cancel"]);
            if (result == "Cancel") return false;
            if (result == "Save") return SavePet(); petDraft.Undo(); return true;
        }
        finally { prompting = false; }
    }
    private async Task<string> Choice(string title, string message, string[] choices)
    {
        var dialog = new Window { Title = title, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false, Background = Background, Foreground = Foreground, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var content = Column(); content.Margin = new(12); content.Children.Add(Heading(title)); content.Children.Add(Text(message)); var buttons = Flow();
        foreach (string choice in choices) buttons.Children.Add(Action(choice, () => dialog.Close(choice)));
        content.Children.Add(buttons); dialog.Content = content; return await dialog.ShowDialog<string>(this) ?? "Cancel";
    }
    private void DisposeController() { if (disposed) return; disposed = true; timer.Stop(); damageOverlay?.Close(); healingOverlay?.Close(); controller?.Dispose(); controller = null; }
    private static StackPanel Column() => new() { Spacing = 8, Margin = new(8), HorizontalAlignment = HorizontalAlignment.Stretch };
    private static WrapPanel Flow() => new() { Orientation = Orientation.Horizontal };
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 17, Foreground = Gold, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private static TextBox Entry(string placeholder) => new() { PlaceholderText = placeholder, MinHeight = 28, MinWidth = 170 };
    private static Button Action(string name, Action execute, double width = double.NaN) { var result = new Button { Content = name, MinHeight = 28, Height = 28, Width = width, Margin = new(0, 0, 4, 0), Padding = new(8, 2), Cursor = new(StandardCursorType.Hand) }; result.Click += (_, _) => execute(); return result; }
    private static StackPanel Field(string label, Control control) { var panel = new StackPanel { Spacing = 2 }; panel.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 11, Height = 19, Foreground = Gold }); panel.Children.Add(control); return panel; }
    private static ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
}
