using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class PetPaintingReview
{
    internal static void Run(string folder,Action<bool,string> check)
    {
        string[] roles=["pet-earth","pet-air","pet-air-classic","pet-water","pet-water-classic","pet-water-torrent","pet-fire","pet-fire-classic","pet-fire-crystal","pet-undead-skeleton","pet-undead-wraith-a","pet-undead-wraith-b","pet-warder","pet-animation","pet-monster","pet-monster-aviak-b","pet-monster-aviak-c","pet-monster-dragon-b","pet-monster-dragon-c"];
        var samples=new List<object>();
        string Pixels(string role,int dip,double dpi,bool direct,IconArtworkStyle style)
        {
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(dpi,dpi));var bounds=new Rect(0,0,dip,dip);
                if(direct&&style==IconArtworkStyle.Vibrant)dc.DrawImage(IconArtwork.Painted(role,dip*dpi)!,bounds);
                else if(direct)ApprovedPetArtwork.Vector(dc,role,bounds,false);
                else ApprovedPetArtwork.Draw(dc,role,bounds,dpi,style);
            }
            int size=(int)Math.Ceiling(dip*dpi);var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var bytes=new byte[size*size*4];bitmap.CopyPixels(bytes,size*4,0);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }
        foreach(string role in roles)foreach(double dpi in new[]{1d,1.5,2d,3d})foreach(int dip in new[]{13,14,16,20,24,32,64})
        {
            var painting=IconArtwork.Painted(role,dip*dpi);
            check(painting!=null&&painting.PixelWidth>=(int)Math.Ceiling(dip*dpi),role+" decodes enough painting pixels at "+dip+" DIP / "+dpi+"x");
            foreach(var style in Enum.GetValues<IconArtworkStyle>())check(Pixels(role,dip,dpi,false,style)==Pixels(role,dip,dpi,true,style),role+" "+dip+" DIP / "+dpi+"x renders "+(style==IconArtworkStyle.Vibrant?"the original painting":"the Clean vector"));
            samples.Add(new{Role=role,DIP=dip,DPI=dpi,DecodedPixels=painting!.PixelWidth});
        }
        Json.Write(Path.Combine(folder,"painted-size-checks.json"),new{Passed=true,Roles=roles.Length,AllVibrantSizesPainted=true,CleanVectorsPreserved=true,Samples=samples});
    }
}
