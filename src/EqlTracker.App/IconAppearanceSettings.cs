using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class SettingsWindow
{
    private readonly ComboBox iconStyle = UI.Select(["Clean", "Vibrant"], "Vibrant", 115);
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
        var styleGroup=UI.Stack(UI.Field("Icon style · per character",iconStyle),previews);
        var badgeGroup=UI.Stack(UI.Field("Inventory badge",badge),UI.Row(new ItemRoleGlyph{Role="inventory-chest"},new ItemRoleGlyph{Role="inventory-mimic-frieren"}));
        styleGroup.Margin=new(0,0,12,6);badgeGroup.Margin=new(0,0,12,6);return Flow(styleGroup,badgeGroup,BuildElementalAppearance("Earth",EarthChoice,EarthChoices),BuildElementalAppearance("Air",AirChoice,AirChoices),BuildElementalAppearance("Water",WaterChoice,WaterChoices),BuildFireAppearance(),BuildElementalAppearance("Monster",MonsterChoice,MonsterChoices),BuildUndeadAppearance());
    }
    internal void RefreshIconPresentation()
    {
        iconPreview.Source = CatalystBranding.Image(Controller.Profile.AppIconId);
        iconName.Text = CatalystBranding.Resolve(Controller.Profile.AppIconId).Name;
        if(damageChoice.Items.Count>0&& !Equals(damageChoice.SelectedValue,Controller.Profile.DamageEmblem))damageChoice.SelectedValue=Controller.Profile.DamageEmblem;
        if(tabChoice.Items.Count>0&& !Equals(tabChoice.SelectedValue,Controller.Profile.TrackerTabs))tabChoice.SelectedValue=Controller.Profile.TrackerTabs;
        if(selectedTabPreview.Child==null||selectedTabPreview.StyleId!=Controller.Profile.TrackerTabs)selectedTabPreview.Set(Controller.Profile.TrackerTabs);
    }
}
