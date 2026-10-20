using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EqlTracker.Core;
namespace EqlTracker.App;

internal sealed class ItemRoleGlyph : FrameworkElement
{
    internal string Role { get; set; } = "glyph-unknown";
    private static readonly Dictionary<string, DrawingImage> clean = [];
    internal ItemRoleGlyph() { Width = Height = 28; Margin = new(0, 0, 5, 2); IconArtwork.Observe(this); }
    protected override void OnRender(DrawingContext dc)
    {
        string role = Role == "inventory" ? IconArtwork.Badge == InventoryBadgeStyle.FrierenMimic ? "inventory-mimic-frieren" : "inventory-chest" : Role;
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (IconArtwork.DrawPainted(dc, role, bounds, VisualTreeHelper.GetDpi(this).PixelsPerDip)) return;
        if (!clean.TryGetValue(role, out var drawing))
        {
            var group = new DrawingGroup(); using (var d = group.Open())
            {
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(225, 193, 119)), 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; pen.Freeze();
                var frost = new SolidColorBrush(Color.FromRgb(106, 214, 234)); frost.Freeze();
                string path = role switch {
                    "item-greatsword" => "M2,15L5,12 M2,11L6,15 M4,12L11,3L15,1L14,5L6,14 M5,12L14,2",
                    "glyph-sword" => "M3,14L6,11 M3,10L7,14 M5,11L11,3L14,2L13,5L7,13",
                    "glyph-dagger" => "M2,10L6,10 M4,8L4,12 M6,9L15,8L10,11L6,11",
                    "glyph-spear" => "M2,15L12,4 M10,4L15,1L14,6Z",
                    "glyph-mace" => "M3,15L9,7 M6,4L9,1L14,5L11,9Z M8,3L12,7",
                    "item-mace-1h" => "M3,15L8,8 M6,3L9,1L14,6L11,9Z M7,2L12,8 M5,5L11,10 M2,12L5,15",
                    "item-mace-2h" => "M1,15L10,6 M7,2L10,1L15,6L12,9Z M8,2L13,8 M7,5L12,10 M2,11L5,14 M4,9L7,12",
                    "item-axe-1h" => "M3,15L10,5 M6,4L9,1L11,3Q9,8 15,7L13,11Q7,10 6,4 M2,12L5,14",
                    "item-axe-2h" => "M1,15L10,6 M5,2Q9,3 10,6Q14,6 15,10Q11,11 10,6Q6,7 5,2 M8,1L12,5 M2,11L5,14 M4,9L7,12",
                    "item-hammer-1h" => "M3,15L10,6 M5,4L8,1L13,6L10,9Z M12,5L14,6L13,8 M2,12L5,15",
                    "item-hammer-2h" => "M1,15L11,5 M7,3L10,0L15,5L12,8Z M13,3L15,4L14,6 M2,11L5,14 M4,9L7,12",
                    "item-staff" => "M1,15L15,1 M3,11L5,13 M5,9L7,11 M9,5L11,7 M11,3L13,5",
                    "glyph-bow" => "M5,1Q16,8 5,15 M5,1L3,8L5,15 M1,8L14,8 M12,6L15,8L12,10",
                    "glyph-shield" => "M2,2L8,1L14,2L13,10L8,15L3,10Z M8,3L8,12",
                    "item-armor" => "M4,1L6,3L10,3L12,1L15,4L12,7L12,14L4,14L4,7L1,4Z",
                    "item-head" => "M3,14L2,7Q2,1 8,1Q14,1 14,7L13,14L10,12L10,7L6,7L6,12Z M3,5L13,5",
                    "item-face" => "M2,4L5,2L8,4L11,2L14,4L13,11L8,15L3,11Z M4,6L6,7 M10,7L12,6 M8,7L8,11",
                    "item-shoulders" => "M1,7Q3,1 7,3L7,8L2,10Z M9,3Q13,1 15,7L14,10L9,8Z M2,6L6,5 M10,5L14,6",
                    "item-arms" => "M5,1L11,2L12,7L10,15L4,14L5,7Z M5,7L11,8 M5,4L11,5 M5,11L10,12",
                    "item-wrists" => "M4,3L11,2L13,13L5,15Z M5,6L11,5 M6,11L12,10 M7,4L9,4L10,13L8,13Z",
                    "item-gloves" => "M2,14L1,9L1,4L3,4L3,7L4,2L6,2L6,9L7,7L8,8L7,13Z M10,14L9,9L9,4L11,4L11,7L12,2L14,2L14,9L15,7L16,8L15,13Z",
                    "item-legs" => "M2,1L6,1L7,6L5,15L2,15L1,7Z M10,1L14,1L15,7L14,15L11,15L9,6Z M2,7L6,7 M10,7L14,7",
                    "item-boots" => "M2,1L6,1L6,10L8,12L7,15L1,15L1,11Z M10,1L14,1L14,10L16,12L15,15L9,15L9,11Z M2,4L6,4 M10,4L14,4",
                    "item-ring" or "item-earring" => "M3,9A5,5 0 1 0 13,9A5,5 0 1 0 3,9 M5,4L8,1L11,4L8,7Z",
                    "item-necklace" => "M2,1Q2,10 8,11Q14,10 14,1 M8,10L11,13L8,16L5,13Z",
                    "item-cloak" => "M5,2Q8,-1 11,2L15,14Q8,17 1,14Z M5,2Q8,5 11,2 M8,5L8,13",
                    "item-belt" => "M1,5L15,5L15,11L1,11Z M6,4L11,4L11,12L6,12Z M7,8L12,8",
                    "item-held" => "M8,1L13,5L11,13L8,15L4,12L3,5Z M8,1L8,15 M3,5L13,5",
                    "item-arrow" => "M2,15L13,2 M9,2L15,1L14,7 M2,10L2,15L7,15",
                    "item-ammunition" => "M1,13L9,1 M6,2L10,0L10,5 M1,8L1,13L5,13 M3,14L11,3 M1,5L3,7L5,5L4,8L6,10L3,9L1,11L2,8Z M9,10L13,9L16,12L14,15L10,15L8,12Z",
                    "glyph-fist" => "M3,14L1,8L2,5L4,5L4,2L6,2L6,5L7,1L9,1L9,5L10,2L12,2L12,7L14,6L15,8L12,14Z M4,8L11,8 M5,11L11,11",
                    "glyph-claw" => "M2,15L5,13L8,1L6,2Z M6,15L9,13L12,1L10,2Z M10,15L13,13L16,1L14,2Z",
                    "item-scroll" => "M4,2Q1,2 1,5L11,5Q15,5 15,2L4,2L4,14L13,14L13,5 M7,8L10,8 M7,11L10,11",
                    "item-bag" => "M5,1L11,1L9,5Q15,8 14,14Q8,17 2,14Q1,8 7,5Z M5,5L11,5",
                    "item-potion" => "M6,1L10,1L10,5Q16,9 13,14Q8,17 3,14Q0,9 6,5Z M4,10L12,10",
                    "inventory-chest" => "M2,6L1,1Q8,-1 15,1L14,6Z M1,8L15,8L15,15L1,15Z M4,9L4,14 M12,9L12,14 M7,10L9,10L9,12L7,12Z M4,8Q2,5 5,4Q8,5 6,8 M10,3L12,3L12,5Q15,7 12,8L10,8Q8,7 10,5Z",
                    "inventory-mimic-frieren" => "M1,7L15,7L15,14L1,14Z M1,7Q1,2 5,2L11,2Q15,2 15,7 M4,4L4,14 M12,4L12,14 M7,8L9,8L9,11L7,11Z",
                    _ => "M8,1L15,8L8,15L1,8Z M8,5L8,9 M8,12L8,12.2" };
                d.DrawRectangle(Brushes.Transparent, null, new(0, 0, 16, 16)); d.DrawGeometry(null, pen, Geometry.Parse(path));
                if (role == "inventory-mimic-frieren") { d.DrawEllipse(frost, null, new(6, 4), 2, 2); d.DrawGeometry(null, pen, Geometry.Parse("M3,6L4,8L5,6 M10,6L11,8L12,6 M3,3L1,1 M8,3L11,1")); }
            }
            if(role=="inventory-mimic-frieren")group.Transform=new ScaleTransform(-1,1,8,8);
            group.Freeze(); drawing = new DrawingImage(group); drawing.Freeze(); clean[role] = drawing;
        }
        dc.DrawImage(drawing, bounds);
    }
}
internal sealed class ItemHeaderIcons : Grid
{
    private readonly ItemRoleGlyph ownership = new() { Role = "inventory" };
    private readonly List<ItemRoleGlyph> types = [];
    private readonly WrapPanel typeGroup=new(){HorizontalAlignment=HorizontalAlignment.Left};
    internal TextBlock Title {get;}
    internal ItemHeaderIcons(TextBlock? title=null)
    {
        Title=title??UI.Text("",16,"Accent",true);Title.VerticalAlignment=VerticalAlignment.Center;
        ColumnDefinitions.Add(new());ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});
        Children.Add(typeGroup);Children.Add(ownership);Children.Add(Title);SetColumn(ownership,1);SetRow(Title,1);SetColumnSpan(Title,2);
        ownership.HorizontalAlignment=HorizontalAlignment.Right;ownership.Margin=new(4,0,0,2);
    }
    private void Adapt()
    {
        // The type group naturally wraps within its share of the icon row.
    }
    internal void Show(ItemDefinition item, ItemOwnershipEvidence owned)
    {
        Visibility = typeGroup.Visibility=Visibility.Visible; var icons=ItemEquipmentIcons.For(item);
        while(types.Count<icons.Count) types.Add(new());
        if(typeGroup.Children.Count!=icons.Count){typeGroup.Children.Clear();foreach(var glyph in types.Take(icons.Count))typeGroup.Children.Add(glyph);}
        for(int n=0;n<icons.Count;n++)
        {
            var icon=icons[n];var glyph=types[n];glyph.Role=icon.Role;glyph.ToolTip=icon.Label+"\n"+icon.Evidence;glyph.InvalidateVisual();
            System.Windows.Automation.AutomationProperties.SetName(glyph,icon.Label);
        }
        ownership.Visibility = owned.Known ? Visibility.Visible : Visibility.Collapsed; ownership.ToolTip = "Recorded player ownership\n" + owned.Description + (!owned.QuantityKnown ? "\nQuantity unknown; collection presence is not a copy count." : "");
        System.Windows.Automation.AutomationProperties.SetName(ownership, "Player inventory ownership");
        Adapt();
    }
    internal void Clear(){ownership.Visibility=typeGroup.Visibility=Visibility.Collapsed;}
}
public sealed partial class QuestWindow
{
    private static Expander PetDetailsDisclosure(UIElement content, string help)
    {
        var expander = FocusedLayout.Disclosure("Pet Details", content, help);
        expander.Loaded += (_, _) => { expander.ApplyTemplate(); if (Usability.Tree(expander).OfType<ToggleButton>().FirstOrDefault() is { } header) { header.HorizontalAlignment = HorizontalAlignment.Left; header.Padding = new(2, 0, 8, 0); } };
        return expander;
    }
}
