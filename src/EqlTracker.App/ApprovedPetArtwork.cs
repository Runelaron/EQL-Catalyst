using System.Windows;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

/// <summary>Approved family art only. Review candidates are never loaded into this table.</summary>
internal static class ApprovedPetArtwork
{
    internal const string PolicyVersion="approved-pet-art-v4";
    private sealed record Art(string Color,string Highlight,Geometry Shape,Geometry Detail,Geometry Mark);
    private static Geometry Path(string value){var shape=Geometry.Parse(value);shape.Freeze();return shape;}
    private static readonly Dictionary<string,Art> artwork=new()
    {
        ["pet-monster"]=new("#AE5943","#E9CC8B",Path("M2 3L10 7L17 3L18 8L23 10L21 18L13 24L5 21L1 13L4 9Z M19 1L24 5L22 10L18 8L12 20L10 18L20 5Z"),Path("M5 12L11 14L18 11L19 17L12 21L6 18Z"),Path("M5 11L10 13L9 15L5 13Z M15 13L19 11L18 14L15 15Z M8 18L11 19L10 22Z")),
        ["pet-water"]=new("#22AFC6","#C5FCFF",Path("M2 23Q9 17 4 12Q0 10 3 4L5 2Q3 9 9 10Q14 7 10 1Q18 0 18 7Q17 13 21 9L20 4Q26 6 24 13Q20 18 23 24Z"),Path("M7 22Q13 15 9 10Q16 13 15 20L18 23Z M3 8L6 11L9 16L6 16Z"),Path("M3 4L5 2L5 7L8 9L7 11L3 8Z M11 1L14 1Q19 7 14 10L13 8Q15 4 11 1Z M20 9L21 6L24 8L23 12L20 14Z")),
        ["pet-undead-skeleton"]=new("#C9C1A7","#F4ECDD",Path("M6 1L17 1L21 6L20 13L17 15L17 18L23 19L24 24L0 24L1 19L7 18L7 15L3 12L3 5Z"),Path("M6 6L10 5L11 10L7 11L5 9Z M14 5L18 6L19 9L15 11L13 10Z M5 20L11 21L11 24L4 23Z M14 21L20 20L21 23L14 24Z"),Path("M10 13L12 10L14 13Z M8 15L16 15L16 17L8 17Z M11 18L13 18L13 24L11 24Z")),
        ["pet-undead-wraith-a"]=new("#6C5E8F","#E1C5FF",Path("M13 0L19 6L19 10L24 16L20 16L24 23L17 22L15 24L12 19L8 24L6 21L0 23L4 16L0 16L6 10L7 6Z"),Path("M12 4L16 7L17 11L13 16L8 11L9 7Z M7 14L11 19L8 21Z M16 14L19 20L16 18Z"),Path("M9 8L12 10L11 12L9 10Z M14 10L16 8L16 10L14 12Z")),
        ["pet-undead-wraith-b"]=new("#4E8497","#C6EFF8",Path("M17 0L22 7L20 11L24 16L21 17L24 21L18 20Q19 23 14 24L8 22L1 24L4 20L0 18L5 11L8 8L10 3Z"),Path("M13 4L18 4L19 9L15 13L10 11L10 7Z M7 13L11 16L15 19Q16 21 9 22L7 20L12 20L9 18L5 17Z"),Path("M11 7L14 8L13 10L11 9Z M16 8L18 6L18 9L16 10Z M10 15L15 18L18 22L15 23L14 20L9 17Z")),
        ["pet-fire"]=new("#EE8632","#FFEAB0",Path("M10 0L15 3L15 7L18 9L20 4L22 3L24 6L22 9L22 23L19 23L19 13L16 14L14 18L17 22L13 24L3 24L0 21L5 18L7 14L4 11L5 6Z"),Path("M9 5L14 5L13 10L10 13L7 9Z M10 14L14 15L11 20L12 22L5 23L7 19Z"),Path("M20 7L22 5L23 6L21 10L21 22L20 22Z M10 7L12 7L11 10Z")),
        ["pet-fire-crystal"]=new("#F18A35","#FFF09A",Path("M15 0L20 4L17 8L20 10L23 9L24 14L21 18L17 17L15 19L20 22L16 24L7 23L5 20L9 17L6 17L2 18L0 14L1 9L5 8L8 10L9 6L8 3Z"),Path("M12 5L17 5L16 9L13 12L10 10Z M9 18L15 18L12 21L17 23L9 22L7 20Z"),Path("M13 12L17 15L13 18L9 15Z M3 10L6 12L7 16L4 15Z M21 10L23 12L21 16L18 16L19 13Z")),
    };
    internal static bool Detailed(double dip,IconArtworkStyle style)=>style==IconArtworkStyle.Vibrant&&dip>24;
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
        dc.PushTransform(new TranslateTransform(bounds.X,bounds.Y));dc.PushTransform(new ScaleTransform(bounds.Width/26,bounds.Height/26));dc.PushTransform(new TranslateTransform(1,1));
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
