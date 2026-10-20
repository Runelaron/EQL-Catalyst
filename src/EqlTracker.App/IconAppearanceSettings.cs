using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class SettingsWindow
{
    private readonly ComboBox iconStyle = UI.Select(["Clean", "Vibrant"], "Vibrant", 115);
    private readonly Dictionary<DamageEmblem, Button> damageEmblemButtons = [];
    private UIElement BuildIconStyleControls()
    {
        iconStyle.ToolTip = "Clean uses crisp line glyphs and the original app icons. Vibrant uses painterly fantasy artwork. Applies immediately across the icon family without changing your selected app icon.";
        var previews = new WrapPanel();
        foreach (var style in Enum.GetValues<IconArtworkStyle>())
        {
            var sample = UI.Row(new DamageEmblemPreview { StyleOverride = style, Width = 32, Height = 32 }, new DamageEmblemPreview { StyleOverride = style, Width = 14, Height = 14, Margin = new(5, 0, 5, 0) }, UI.Text(style.ToString(), 11));
            sample.Margin = new(0, 0, 12, 2); sample.ToolTip = style + " · the selected Damage emblem at 32 and 14 DIP"; previews.Children.Add(sample);
        }
        var badge = UI.Select(["Chest", "Frieren in a Mimic Chest"], Controller.Profile.InventoryBadge == InventoryBadgeStyle.FrierenMimic ? "Frieren in a Mimic Chest" : "Chest", 210);
        badge.ToolTip = "Ownership emblem for recorded player inventory. This appearance choice changes no inventory evidence.";
        badge.SelectionChanged += (_, _) => { Controller.Profile.InventoryBadge = badge.SelectedIndex == 1 ? InventoryBadgeStyle.FrierenMimic : InventoryBadgeStyle.Chest; ApplyAppearance(); appearanceSave.Stop(); Controller.Save(); };
        return UI.Stack(UI.Field("Icon style · per character", iconStyle), previews, UI.Field("Inventory badge", badge), UI.Row(new ItemRoleGlyph { Role = "inventory-chest" }, new ItemRoleGlyph { Role = "inventory-mimic-frieren" }));
    }
    private UIElement BuildDamageEmblemGallery()
    {
        var tiles = new WrapPanel();
        foreach (var choice in IconArtwork.DamageChoices)
        {
            var button = UI.Button(choice.Label, () => {
                Controller.Profile.DamageEmblem = choice.Id; ApplyAppearance(); appearanceSave.Stop(); Controller.Save(); RefreshIconPresentation();
            });
            var label = UI.Text(choice.Label, 11); label.TextAlignment = TextAlignment.Center;
            button.Width = 150; button.Height = 92; button.Margin = new(3);
            button.Tag = choice.Id;
            button.ContentTemplate = null;
            button.Content = UI.Stack(UI.Row(new DamageEmblemPreview { Emblem = choice.Id, Width = 48, Height = 48 }, new DamageEmblemPreview { Emblem = choice.Id, Width = 14, Height = 14, Margin = new(6, 0, 0, 0) }), label);
            button.ToolTip = "Use " + choice.Name + " for Damage mode. Crossing-diagonal version " + choice.Number + "; independent of Physical attacks, Self and Total DPS. Saves immediately.";
            damageEmblemButtons[choice.Id] = button; tiles.Children.Add(button);
        }
        RefreshIconPresentation();
        return new Expander { Header = "Damage emblem · 1 / 3 / 5 / 7 / 9 / 11", Content = tiles, ToolTip = "Choose one of the six crossing-diagonal sword-and-spell compositions. Fire is the default; both icon styles retain this selection." };
    }
    internal void RefreshIconPresentation()
    {
        iconPreview.Source = CatalystBranding.Image(Controller.Profile.AppIconId);
        iconName.Text = CatalystBranding.Resolve(Controller.Profile.AppIconId).Name;
        foreach (var (id, button) in damageEmblemButtons) UI.SetPressed(button, id == Controller.Profile.DamageEmblem);
    }
}
