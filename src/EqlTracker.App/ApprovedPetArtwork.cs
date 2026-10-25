using System.Windows;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

/// <summary>Approved family art only. Review candidates are never loaded into this table.</summary>
internal static class ApprovedPetArtwork
{
    internal const string PolicyVersion="approved-pet-art-v6";
    private sealed record Art(string Color,string Highlight,Geometry Shape,Geometry Detail,Geometry Mark,double Canvas=26);
    private static Geometry Path(string value){var shape=Geometry.Parse(value);shape.Freeze();return shape;}
    private static readonly Dictionary<string,Art> artwork=new()
    {
        ["pet-monster"]=new("#A5B6CA","#E6BD68",Path("M3 2L9 6L14 6L19 1L19 8L22 11L19 19L12 24L5 20L1 12L4 8Z M17 4L21 2L24 6L21 10L18 8L13 18L11 16Z"),Path("M5 10L10 13L14 12L18 9L18 17L12 20L6 17Z"),Path("M4 10L9 12L8 14L4 12Z M14 12L18 10L18 13L14 14Z M8 17L11 18L10 20Z M14 18L17 16L16 20Z")),
        ["pet-water"]=new("#269AC3","#B8F8FF",Path("M13 0L20 3L18 8L22 9L23 13L20 15L17 12L14 15L18 18L24 24L12 21L9 19L5 24L0 23L2 18L7 14L5 10L7 6L10 5Z"),Path("M11 6L17 5L16 10L13 13L9 10Z M8 15L12 15L11 19L6 22L2 22Z"),Path("M10 8L13 9L12 11Z M15 8L17 7L16 10Z M11 17L15 19L23 23L12 20L9 18Z"),27),
        ["pet-undead-skeleton"]=new("#C9C1A7","#F4ECDD",Path("M6 1L17 1L21 6L20 13L17 15L17 18L23 19L24 24L0 24L1 19L7 18L7 15L3 12L3 5Z"),Path("M6 6L10 5L11 10L7 11L5 9Z M14 5L18 6L19 9L15 11L13 10Z M5 20L11 21L11 24L4 23Z M14 21L20 20L21 23L14 24Z"),Path("M10 13L12 10L14 13Z M8 15L16 15L16 17L8 17Z M11 18L13 18L13 24L11 24Z")),
        ["pet-undead-wraith-a"]=new("#6C5E8F","#E1C5FF",Path("M13 0L19 6L19 10L24 16L20 16L24 23L17 22L15 24L12 19L8 24L6 21L0 23L4 16L0 16L6 10L7 6Z"),Path("M12 4L16 7L17 11L13 16L8 11L9 7Z M7 14L11 19L8 21Z M16 14L19 20L16 18Z"),Path("M9 8L12 10L11 12L9 10Z M14 10L16 8L16 10L14 12Z")),
        ["pet-undead-wraith-b"]=new("#4E8497","#C6EFF8",Path("M17 0L22 7L20 11L24 16L21 17L24 21L18 20Q19 23 14 24L8 22L1 24L4 20L0 18L5 11L8 8L10 3Z"),Path("M13 4L18 4L19 9L15 13L10 11L10 7Z M7 13L11 16L15 19Q16 21 9 22L7 20L12 20L9 18L5 17Z"),Path("M11 7L14 8L13 10L11 9Z M16 8L18 6L18 9L16 10Z M10 15L15 18L18 22L15 23L14 20L9 17Z")),
        ["pet-fire"]=new("#EE8632","#FFEAB0",Path("M10 0L15 3L15 7L18 9L20 4L22 3L24 6L22 9L22 23L19 23L19 13L16 14L14 18L17 22L13 24L3 24L0 21L5 18L7 14L4 11L5 6Z"),Path("M9 5L14 5L13 10L10 13L7 9Z M10 14L14 15L11 20L12 22L5 23L7 19Z"),Path("M20 7L22 5L23 6L21 10L21 22L20 22Z M10 7L12 7L11 10Z")),
        ["pet-fire-crystal"]=new("#F18A35","#FFF09A",Path("M15 0L20 4L17 8L20 10L23 9L24 14L21 18L17 17L15 19L20 22L16 24L7 23L5 20L9 17L6 17L2 18L0 14L1 9L5 8L8 10L9 6L8 3Z"),Path("M12 5L17 5L16 9L13 12L10 10Z M9 18L15 18L12 21L17 23L9 22L7 20Z"),Path("M13 12L17 15L13 18L9 15Z M3 10L6 12L7 16L4 15Z M21 10L23 12L21 16L18 16L19 13Z")),
        ["pet-earth"]=new("#CDA56A","#FFE1A5",Path("M2 22L1 13L5 10L5 4L10 5L13 1L17 6L21 5L22 13L24 16L21 23Z"),Path("M5 11L11 14L18 10L21 17L14 21L9 19Z"),Path("M6 12L10 13L9 15L6 14Z M15 13L19 11L19 14L15 15Z")),
        ["pet-air-classic"]=new("#BCEBF0","#FFFFFF",Path("M21 3C13 -1 3 4 2 12C0 20 8 25 17 22C9 22 8 16 11 13C14 9 23 14 23 7L16 9C19 7 21 6 21 3Z"),Path("M6 10C9 5 13 4 17 5L12 9L17 10C11 10 7 14 8 18C5 16 4 13 6 10Z"),Path("M10 10L15 8L14 11L10 12Z")),
        ["pet-fire-classic"]=new("#F07835","#FFE179",Path("M11 0C11 7 3 5 5 12L2 10C0 19 5 24 13 23C22 24 25 17 21 11L18 14C19 7 14 9 11 0Z"),Path("M12 8C14 13 19 12 18 17L14 21L8 19L6 15L10 16Z"),Path("M7 15L11 16L10 18L7 17Z M14 16L18 14L17 17L14 18Z")),
        ["pet-water-classic"]=new("#3DBCD1","#A8F7FF",Path("M14 0C13 7 5 8 3 13C-1 20 5 25 14 23C23 22 25 13 20 9C22 15 17 17 15 14C12 11 17 8 14 0Z"),Path("M7 14C2 20 13 23 18 18C12 21 8 18 10 15L14 16L12 12Z"),Path("M7 14L11 15L10 17L7 16Z")),
        ["pet-water-torrent"]=new("#22AFC6","#C5FCFF",Path("M2 23Q9 17 4 12Q0 10 3 4L5 2Q3 9 9 10Q14 7 10 1Q18 0 18 7Q17 13 21 9L20 4Q26 6 24 13Q20 18 23 24Z"),Path("M7 22Q13 15 9 10Q16 13 15 20L18 23Z M3 8L6 11L9 16L6 16Z"),Path("M3 4L5 2L5 7L8 9L7 11L3 8Z M11 1L14 1Q19 7 14 10L13 8Q15 4 11 1Z M20 9L21 6L24 8L23 12L20 14Z")),
        ["pet-air"]=new("#C6DDEB","#FFFFFF",Path("M10 0L17 2L15 7L17 10L20 7L24 8L25 12L22 16L17 16L14 13L15 19L20 22L15 25L5 24L5 21L9 18L8 13L5 15L1 13L0 10L3 7L7 8Z"),Path("M10 5L14 5L12 9L10 8Z M10 14L14 15L12 19L8 22L16 23L6 23Z"),Path("M18 9L23 9L24 12L21 15L17 14L16 12Z M2 10L5 10L5 12L2 12Z"),27),
        ["pet-warder"]=new("#BF813C","#F7DCAA",Path("M10 2L15 2L18 4L19 7L24 9L23 14L19 16L18 20L24 21L24 24L16 24L14 19L9 16L8 22L5 24L1 23L0 20L3 18L3 12L6 8L7 4Z"),Path("M10 5L15 5L17 8L18 11L22 10L22 14L17 16L13 13L8 12L6 16L4 16L6 10Z"),Path("M17 8L20 8L19 10L17 10Z M19 13L22 12L21 15L18 15Z M18 21L22 21L22 23L18 23Z M3 20L6 20L5 23L2 22Z"),27),
        ["pet-animation"]=new("#D8DDE3","#FFF0B2",Path("M7 0L10 3L10 13L13 15L10 17L10 23L6 23L6 17L3 15L6 13L6 3Z M13 8L23 10L22 18L18 24L13 20L11 13Z"),Path("M15 11L20 12L20 17L18 20L15 17Z"),Path("M7 3L8 4L8 13L7 13Z M17 12L18 12L18 18L17 17Z")),
        ["pet-monster-aviak-c"]=new("#477D89","#EAD29E",Path("M17 9L20 11L24 9L23 14L25 16L23 19L21 18L23 24L16 25L11 23L6 25L1 21L0 15L4 9L8 10L9 15L12 15L14 12Z M13 3L15 4L4 25L2 24Z M12 2Q7 0 5 0L3 6Q7 4 10 7L13 9Q16 9 19 13L22 6Q17 5 15 3Z"),Path("M17 14L22 13L23 16L19 19L16 17Z M12 19L18 18L20 23L15 24Z"),Path("M12 2Q7 0 5 0L3 6Q7 4 10 7L13 9Q16 9 19 13L22 6Q17 5 15 3Z"),27),
        ["pet-monster-aviak-b"]=new("#477D89","#EAD29E",Path("M15 1L19 4L23 1L22 6L25 9L23 13L20 13L21 16L25 18L25 24L18 23L15 25L10 23L5 24L2 19L5 13L11 12L11 7Z M5 3L7 2L24 23L22 25Z M4 1Q0 1 0 6L3 12Q3 8 6 7L8 6Q10 5 13 7L11 1Q8 0 7 3Z"),Path("M14 7L20 6L22 10L18 14L14 12Z M11 16L18 15L22 21L16 24L11 21Z"),Path("M4 1Q0 1 0 6L3 12Q3 8 6 7L8 6Q10 5 13 7L11 1Q8 0 7 3Z"),27),
        ["pet-monster-dragon-c"]=new("#B6573A","#EAD29E",Path("M17 8L21 10L24 8L24 13L25 16L23 19L22 24L15 25L11 23L5 25L1 21L1 14L4 9L8 9L9 14L12 15L14 12Z M13 3L15 4L4 25L2 24Z M12 2Q7 0 5 0L3 6Q7 4 10 7L13 9Q16 9 19 13L22 6Q17 5 15 3Z"),Path("M17 13L23 12L24 16L20 18L16 17Z M11 18L17 18L20 24L14 24Z"),Path("M12 2Q7 0 5 0L3 6Q7 4 10 7L13 9Q16 9 19 13L22 6Q17 5 15 3Z"),27),
        ["pet-monster-dragon-b"]=new("#B6573A","#EAD29E",Path("M14 1L18 4L22 1L23 6L25 9L23 13L20 14L24 18L25 24L19 24L15 22L11 25L5 23L2 18L5 12L10 12L11 6Z M5 3L7 2L24 23L22 25Z M4 1Q0 1 0 6L3 12Q3 8 6 7L8 6Q10 5 13 7L11 1Q8 0 7 3Z"),Path("M14 7L21 6L23 10L19 13L14 11Z M10 15L17 14L21 22L15 24L9 21Z"),Path("M4 1Q0 1 0 6L3 12Q3 8 6 7L8 6Q10 5 13 7L11 1Q8 0 7 3Z"),27),
    };
    internal static bool Detailed(double dip,IconArtworkStyle style)=>style==IconArtworkStyle.Vibrant&&dip>0;
    internal static bool Draw(DrawingContext dc,string role,Rect bounds,double dpi,IconArtworkStyle style)
    {
        if(!artwork.ContainsKey(role))return false;
        if(Detailed(Math.Max(bounds.Width,bounds.Height),style)&&IconArtwork.Painted(role,Math.Max(bounds.Width,bounds.Height)*dpi) is {} image)dc.DrawImage(image,bounds);
        else Vector(dc,role,bounds,style==IconArtworkStyle.Vibrant);
        return true;
    }
    internal static void Vector(DrawingContext dc,string role,Rect bounds,bool vibrant)
    {
        if(!artwork.TryGetValue(role,out var art))return;
        Brush Fill(string color)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        dc.PushTransform(new TranslateTransform(bounds.X,bounds.Y));dc.PushTransform(new ScaleTransform(bounds.Width/art.Canvas,bounds.Height/art.Canvas));dc.PushTransform(new TranslateTransform(1,1));
        dc.DrawGeometry(Fill(vibrant?art.Color:"#D8E4EB"),new Pen(Fill("#07131E"),1.15){LineJoin=PenLineJoin.Round},art.Shape);
        dc.PushOpacity(.8);dc.DrawGeometry(Fill("#122A3B"),null,art.Detail);dc.Pop();dc.DrawGeometry(Fill(vibrant?art.Highlight:"#FFFFFF"),null,art.Mark);
        dc.Pop();dc.Pop();dc.Pop();
    }
}

internal sealed class ApprovedPetArtworkPreview(string role):FrameworkElement
{
    internal ApprovedPetArtworkPreview(string role,double dip):this(role){Width=Height=dip;IconArtwork.Observe(this);}
    protected override void OnRender(DrawingContext dc)
    {double size=Math.Min(ActualWidth,ActualHeight);if(size>0)ApprovedPetArtwork.Draw(dc,role,new(0,0,size,size),VisualTreeHelper.GetDpi(this).DpiScaleX,IconArtwork.Style);}
}
