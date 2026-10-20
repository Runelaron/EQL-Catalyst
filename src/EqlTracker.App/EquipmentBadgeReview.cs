using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;
public sealed partial class QuestWindow
{
    internal void ReviewEquipmentBadges(string folder)
    {
        if(!Controller.Demo)throw new InvalidOperationException("An isolated preview profile is required.");
        Directory.CreateDirectory(Path.Combine(folder,"qa"));var checks=new List<string>();var previous=Content;
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks.Add(message);}
        string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        try
        {
            var item=new ItemDefinition{Name="Owned adventurer equipment",Slots=["CHEST"],Display=new(){Type="Armor"}};
            var icons=new ItemHeaderIcons();icons.Show(item,new(true,"Bank1 · complete inventory snapshot · quantity 1",true,1));
            var panel=UI.Stack(UI.Text("Player inventory",15,"Accent",true),icons,UI.Text("Chest armor · recorded ownership",12));panel.Background=UI.Brush("Surface");Content=panel;Width=320;Height=180;
            var hashes=new Dictionary<string,string>();
            foreach(var badge in new[]{InventoryBadgeStyle.Chest,InventoryBadgeStyle.FrierenMimic})foreach(var style in new[]{IconArtworkStyle.Clean,IconArtworkStyle.Vibrant})
            {
                Controller.Profile.IconStyle=style;Controller.Profile.InventoryBadge=badge;IconArtwork.Apply(Controller.Profile);Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
                foreach(var glyph in Usability.Tree(panel).OfType<ItemRoleGlyph>())glyph.InvalidateVisual();
                foreach(double scale in new[]{1d,1.5,2,3})
                {
                    var image=PreviewRenderer.Capture(panel,320,150,scale);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var data=new MemoryStream();encoder.Save(data);
                    string key=$"badge-{badge.ToString().ToLowerInvariant()}-{style.ToString().ToLowerInvariant()}-{scale:0.0}x";File.WriteAllBytes(Path.Combine(folder,"qa","equipment-"+key+".png"),data.ToArray());if(scale==1)hashes[badge+"/"+style]=Hash(data.ToArray());
                }
                Check(icons.Children.OfType<ItemRoleGlyph>().Single(g=>g.Role=="inventory").Visibility==Visibility.Visible,"Ownership badge remains visible in "+badge+" / "+style);
                Check(icons.Children.OfType<ItemRoleGlyph>().Single(g=>g.Role=="inventory").ToolTip!.ToString()!.Contains("Bank1"),"Ownership location evidence retained in "+badge+" / "+style);
            }
            Check(hashes["Chest/Clean"]!=hashes["Chest/Vibrant"],"Live style switching redraws the chest icon");
            Check(hashes["Chest/Vibrant"]!=hashes["FrierenMimic/Vibrant"],"Chest and approved Frieren remain distinct choices");
            Check(IconArtwork.Painted("inventory-chest")!=null&&IconArtwork.Painted("inventory-mimic-frieren")!=null,"Both inventory identity resources load successfully");
            Json.Write(Path.Combine(folder,"equipment-badge-checks.json"),new{Passed=checks.Count,Checks=checks,AppSha256=Hash(File.ReadAllBytes(typeof(App).Assembly.Location)),CoreSha256=Hash(File.ReadAllBytes(typeof(Catalog).Assembly.Location)),Scope="Targeted final badge proof only; existing affected-window review retained. No unrelated windows or repeated benchmarks."});
        }
        finally{Content=previous;}
    }
}
