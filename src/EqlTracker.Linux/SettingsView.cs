using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EqlTracker.App;
using EqlTracker.Core;
using EqlTracker.Companion;

namespace EqlTracker.Linux;
internal sealed partial class CatalystWindow
{
    private void ShowSettings()
    {
        if (controller == null) return; var content = Column(); content.Children.Add(Heading("Appearance"));
        var themes = new ComboBox { ItemsSource = ThemeCatalog.Choices.Skip(1).Select(t => t.Name).ToArray(), SelectedItem = ThemeCatalog.Resolve(controller.Profile.Theme).Name, Height = 28, Width = 180 };
        themes.SelectionChanged += (_, _) => { controller.Profile.Theme = ThemeCatalog.Resolve(themes.SelectedItem?.ToString()).Id; ApplyTheme(); controller.Save(); }; content.Children.Add(Field("Theme", themes));
        var game = Entry("EQ folder"); game.Text = controller.Profile.GameFolder; content.Children.Add(Field("Game folder", game));
        var motion = new CheckBox { Content = "Animate workspace transitions", IsChecked = controller.Profile.SmoothMeterMotion }; content.Children.Add(motion);
        content.Children.Add(Heading("Phone sharing")); var enabled = new CheckBox { Content = "Enable authenticated read-only sharing", IsChecked = controller.PhoneSettings.Enabled }; var address = Entry("Private LAN IPv4 address"); address.Text = controller.PhoneSettings.Address; content.Children.Add(enabled); content.Children.Add(address);
        content.Children.Add(Text("Linux sharing requires an unlocked Secret Service and a trusted NetworkManager LAN. No credentials are stored as plaintext. Existing pairing and permissions remain required."));
        content.Children.Add(Action("Save", async () => { controller.Profile.GameFolder = game.Text ?? ""; controller.Profile.SmoothMeterMotion = motion.IsChecked == true; controller.Save(); await controller.ConfigurePhoneAsync(new CompanionHostSettings { Enabled = enabled.IsChecked == true, Address = address.Text ?? "", Port = controller.PhoneSettings.Port }); status.Text = controller.PhoneStatus; }));
        content.Children.Add(Text("Linux preview: compositor-specific positioning, global shortcuts and overlay behavior require environment acceptance. Gaming Mode is experimental."));
        var window = new Window { Title = "EQL Catalyst / Settings", Width = 640, Height = 540, MinWidth = 460, MinHeight = 360, Background = Background, Foreground = Foreground, Content = Scroll(content) }; window.Show(this);
    }
    private void ApplyTheme()
    {
        var theme = ThemeCatalog.Resolve(controller?.Profile.Theme); Background = Brush.Parse(theme.Id switch { "sunstrider" => "#EB332A1B", "shadowman" => "#EB211D2B", _ => "#EB102531" });
        TrackerController.PhoneThemeTokens = new Dictionary<string, string> { ["background"] = theme.Id == "sunstrider" ? "#332A1B" : theme.Id == "shadowman" ? "#211D2B" : "#102531", ["surface"] = "#192E3C", ["text"] = "#E0EDF2", ["secondary"] = "#A2B7C5", ["accent"] = "#69D7EC", ["line"] = "#43616F", ["mint"] = "#7DE0A8" };
    }
    private sealed class LinuxLayout
    {
        public double Width { get; set; } = 915; public double Height { get; set; } = 650; public double ToolbarWidth { get; set; } = 440;
        public string Workspace { get; set; } = "Tracker"; public bool Expanded { get; set; } = true;
        public Dictionary<string, double[]> Sizes { get; set; } = [];
    }
}
