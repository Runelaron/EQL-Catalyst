using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;
public sealed partial class QuestWindow
{
    internal void ReviewEquipmentIcons(string folder)
    {
        if(!Controller.Demo)throw new InvalidOperationException("An isolated preview profile is required.");
        Directory.CreateDirectory(Path.Combine(folder,"qa"));var checks=new List<string>();
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks.Add(message);}
        void Flush(){Refresh();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        void Capture(string name,FrameworkElement component,double width=1220,double height=700)
        {
            if(ReferenceEquals(component,Content)){Width=width;Height=height;Flush();}
            foreach(var glyph in Usability.Tree(component).OfType<ItemRoleGlyph>())glyph.InvalidateVisual();
            foreach(double scale in new[]{1d,1.5,2,3}){var image=PreviewRenderer.Capture(component,width,height,scale);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(folder,"qa",$"equipment-{name}-{scale:0.0}x.png"));encoder.Save(file);}
        }
        void Style(IconArtworkStyle style){Controller.Profile.IconStyle=style;IconArtwork.Apply(Controller.Profile);Flush();}
        GearBuildFixtures.Apply(Controller);Controller.Profile.PanelSplits.Clear();Controller.Profile.ColumnWidths.Clear();Controller.Profile.SmoothMeterMotion=false;Controller.Profile.InventoryBadge=InventoryBadgeStyle.Chest;
        Style(IconArtworkStyle.Vibrant);Width=1220;Height=700;ShowView(TrackerTab.Gear);SelectGearPosition("CHEST:1");gearSearch.Text="Red Dragonscale Armor";Flush();gearTable.SelectedItem=gearTable.Items.OfType<GearRow>().FirstOrDefault();Flush();
        Check(gearTable.Items.OfType<GearRow>().Any(r=>r.Type=="Chest armor"),"Gear candidates refine cached Armor into Chest armor");
        Check(Usability.Tree(gearInspector).OfType<ItemRoleGlyph>().Any(g=>g.ToolTip?.ToString()?.StartsWith("Chest armor") == true),"Gear inspector exposes the exact armor tooltip");
        Check(gearEquipment.Columns.Any(c=>c.Header?.ToString()=="Type"),"Equipment Type column remains present");Capture("gear",(FrameworkElement)Content);Capture("gear-narrow",(FrameworkElement)Content,640,420);
        Check(gearTable.Columns.Single(c=>c.SortMemberPath=="Type").MinWidth<180,"Compound armor labels do not lock the Type column to an entire slot list");
        Width=1220;Height=700;ShowView(TrackerTab.Pet);petSearch.Text="Indicolite";Flush();petResults.SelectedItem=petResults.Items.OfType<PetSearchRow>().FirstOrDefault(r=>r.Item.Slots.Contains("ARMS"))??petResults.Items.OfType<PetSearchRow>().FirstOrDefault();Flush();
        Check(petResults.Items.OfType<PetSearchRow>().Any(r=>r.Type!="Armor"),"Pet candidate Type uses refined armor classifications");Capture("pet",(FrameworkElement)Content);
        ShowView(TrackerTab.Exaltations);exaltSearch.Text="Disciple Symbol of Innoruuk";Flush();exaltTable.SelectedItem=exaltTable.Items.OfType<ExaltationOption>().FirstOrDefault();Flush();Capture("effect",(FrameworkElement)Content);
        var multi=new ItemDefinition{Key="review multi armor",Name="Multi-slot armor · presentation fixture",Slots=["HEAD","ARMS","CHEST","HANDS","WRIST","LEGS","FEET"],Display=new(){Type="Armor",TypeEvidence="Illustrative sourced-slot fixture"},Classes=["ALL"],Stats=new(){["AC"]=20}};
        var hammer=new ItemDefinition{Key="review hammer",Name="Two-handed hammer · presentation fixture",Slots=["PRIMARY","RANGE"],Display=new(){Type="2H Blunt",Skill="2H Blunt",WeaponShape=WeaponShape.Hammer,WeaponShapeEvidence="Illustrative explicit-shape fixture"},Stats=new(){["Damage"]=24,["Delay"]=30,["Ratio"]=.8}};
        var inventory=new InventorySnapshot{CapturedAt=DateTime.Now,Entries=[new(){Name=multi.Name,ConfirmedItemKey=multi.Key,Count=1,Location="Bank1"}]};
        var header=new ItemHeaderIcons();header.Show(multi,ItemOwnership.For(multi,inventory));
        Check(header.Children.OfType<ItemRoleGlyph>().Count()==8,"Seven distinct armor-slot glyphs plus one independent ownership badge");
        Check(header.Children.OfType<ItemRoleGlyph>().Where(g=>g.Role!="inventory").All(g=>System.Windows.Automation.AutomationProperties.GetName(g).Length>0&&g.ToolTip?.ToString()?.Contains("armor")==true||g.ToolTip?.ToString()?.StartsWith("Gloves")==true||g.ToolTip?.ToString()?.StartsWith("Boots")==true),"All armor glyphs have precise accessible names and tooltips");
        var panel=UI.Stack(header,UI.Text(multi.Name,16,"Accent",true),new ClassicItemCard(multi,0));panel.Background=UI.Brush("Surface");
        Capture("multi-slot",panel,255,420);Check(header.ActualHeight>30,"Multi-slot armor icons wrap inside a narrow inspector");
        header.Show(hammer,new(false,"",false,null));Check(header.Children.OfType<ItemRoleGlyph>().Count(g=>g.Role!="inventory")==1&&header.Children.OfType<ItemRoleGlyph>().First().Role=="item-hammer-2h","Multi-slot weapon keeps one sourced weapon-shape glyph");
        header.Show(multi,ItemOwnership.For(multi,inventory));Check(header.Children.OfType<ItemRoleGlyph>().Count()==8,"Reusing a header restores all armor icons without duplicates");
        foreach(var style in new[]{IconArtworkStyle.Clean,IconArtworkStyle.Vibrant})
        {
            Style(style);var grid=new UniformGrid{Columns=5,Background=UI.Brush("Surface")};
            var types=new List<ItemDefinition>();
            foreach(var shape in new[]{WeaponShape.Sword,WeaponShape.Axe,WeaponShape.Hammer,WeaponShape.Mace})foreach(string handed in new[]{"1H","2H"})types.Add(new(){Name=shape.ToString(),Display=new(){Type=handed+" "+(shape is WeaponShape.Sword or WeaponShape.Axe?"Slashing":"Blunt"),WeaponShape=shape,WeaponShapeEvidence="Explicit shape review fixture"}});
            foreach(string slot in new[]{"HEAD","FACE","SHOULDERS","ARMS","WRIST","CHEST","HANDS","LEGS","FEET","BACK","WAIST","EAR","FINGER","NECK"})types.Add(new(){Slots=[slot],Display=new(){Type="Armor"}});
            types.AddRange(new[]{new ItemDefinition{Slots=["AMMO"]},new ItemDefinition{Display=new(){Type="2H Blunt",WeaponShape=WeaponShape.Staff,WeaponShapeEvidence="Explicit shape review fixture"}},new ItemDefinition{Display=new(){Type="1H Piercing"}},new ItemDefinition{Display=new(){Type="2H Piercing"}},new ItemDefinition{Display=new(){Type="Bow"}},new ItemDefinition{Display=new(){Type="Hand to Hand"}},new ItemDefinition{Display=new(){Type="Shield"}},new ItemDefinition()});
            foreach(var item in types){var icon=ItemEquipmentIcons.For(item).Single();var glyph=new ItemRoleGlyph{Role=icon.Role,Width=42,Height=42};var card=UI.Stack(glyph,UI.Text(icon.Label,11));card.Margin=new(8);grid.Children.Add(card);if(style==IconArtworkStyle.Vibrant)Check(IconArtwork.Painted(icon.Role)!=null||icon.Role=="glyph-unknown","Vibrant resource or explicit neutral fallback exists: "+icon.Role);}
            Capture("icons-"+style.ToString().ToLowerInvariant(),grid,850,620);Capture("multi-slot-"+style.ToString().ToLowerInvariant(),panel,180,420);
            var pair=new PetPairComparisonView(ItemPairComparison.Create(multi,null,hammer,0,null,Controller.Profile.ActiveLoadout,Controller.Catalog));Capture("comparison-"+style.ToString().ToLowerInvariant(),pair,700,660);
        }
        Controller.Profile.InventoryBadge=InventoryBadgeStyle.FrierenMimic;IconArtwork.Apply(Controller.Profile);Check(IconArtwork.Badge==InventoryBadgeStyle.FrierenMimic,"Approved mimic preference still applies immediately");Controller.Profile.InventoryBadge=InventoryBadgeStyle.Chest;IconArtwork.Apply(Controller.Profile);
        Json.Write(Path.Combine(folder,"equipment-icons-checks.json"),new{Passed=checks.Count,Checks=checks,Types=Controller.Catalog.Items.GroupBy(PetItemRules.Type).ToDictionary(g=>g.Key,g=>g.Count()),AppSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))).ToLowerInvariant(),CoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Catalog).Assembly.Location))).ToLowerInvariant(),Scope="One affected Gear/Pet/Effect/shared header and comparison review, with a targeted correction for compound Type sizing and detached proof invalidation. No unrelated-window sweeps, combat benchmarks or live profile writes."});
    }
}
