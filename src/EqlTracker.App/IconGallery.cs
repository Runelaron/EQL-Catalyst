using System.Windows;
using System.Windows.Controls;

namespace EqlTracker.App;

public sealed class IconGallery : FrostWindow
{
    private readonly Dictionary<string, List<Image>> images = [];
    private Image? selectedPreview;
    internal void RefreshIcons()
    {
        foreach (var (id, targets) in images) foreach (var image in targets) image.Source = CatalystBranding.Image(id);
        if (selectedPreview != null) selectedPreview.Source = CatalystBranding.Image(Controller.Profile.AppIconId);
    }
    public IconGallery(TrackerController controller) : base(controller, "App icon", "Saved for this character · applies immediately to tray and companion windows", 620, 540)
    {
        var preview = new Image { Width = 72, Height = 72, Source = CatalystBranding.Image(controller.Profile.AppIconId) };
        selectedPreview = preview;
        var name = UI.Text(CatalystBranding.Resolve(controller.Profile.AppIconId).Name, 16, "Foreground", true);
        var tiles = new WrapPanel(); var buttons = new Dictionary<string, Button>();
        void Highlight()
        {
            foreach (var (id, button) in buttons)
            { button.SetResourceReference(Control.BackgroundProperty, id == CatalystBranding.Resolve(controller.Profile.AppIconId).Id ? "Selection" : "Surface"); button.SetResourceReference(Control.BorderBrushProperty, id == controller.Profile.AppIconId ? "Accent" : "Edge"); }
        }
        foreach (var choice in CatalystBranding.Choices)
        {
            var image = new Image { Width = 40, Height = 40, Source = CatalystBranding.Image(choice.Id) };
            var tiny = new Image { Width = 16, Height = 16, Source = image.Source, Margin = new(4, 0, 0, 0) };
            images[choice.Id] = [image, tiny];
            var label = UI.Text(choice.Name, 11); label.TextAlignment = TextAlignment.Center;
            var button = new Button { Width = 96, Height = 82, Margin = new(3), Content = UI.Stack(UI.Row(image, tiny), label), ToolTip = choice.Name + " · select for " + controller.Profile.Character };
            button.Click += (_, _) => { controller.Profile.AppIconId = choice.Id; ((App)Application.Current).ApplyIcon(); controller.Save(); preview.Source = CatalystBranding.Image(choice.Id); name.Text = choice.Name; Highlight(); };
            tiles.Children.Add(button); buttons[choice.Id] = button;
        }
        Highlight(); Body.Children.Add(UI.Scroll(UI.Stack(UI.Row(preview, UI.Stack(name, UI.Text("Launcher icon remains Blue Orb. Class detection does not change your choice.", 11, "Muted"))), tiles)));
    }
}
