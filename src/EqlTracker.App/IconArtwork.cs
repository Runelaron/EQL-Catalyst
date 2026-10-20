using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed record DamageEmblemChoice(DamageEmblem Id,int Number,string Name,string Role)
{ public string Label=>Number+" · "+Name; }
internal static class IconArtwork
{
    internal static IReadOnlyList<DamageEmblemChoice> DamageChoices {get;}=new[]{
        new DamageEmblemChoice(DamageEmblem.Fire,1,"Red-orange fire","damage-sword-spell"),
        new DamageEmblemChoice(DamageEmblem.ArcaneOrb,3,"Arcane Orb","damage-arcane-orb"),
        new DamageEmblemChoice(DamageEmblem.Lightning,5,"Lightning","damage-lightning"),
        new DamageEmblemChoice(DamageEmblem.ArcaneSigil,7,"Violet Sigil","damage-arcane-sigil"),
        new DamageEmblemChoice(DamageEmblem.MagicMissiles,9,"Magic Missiles","damage-magic-missiles"),
        new DamageEmblemChoice(DamageEmblem.ElementalVortex,11,"Elemental Vortex","damage-elemental-vortex")};
    internal static IconArtworkStyle Style {get;private set;}=IconArtworkStyle.Vibrant;
    internal static InventoryBadgeStyle Badge {get;private set;} = InventoryBadgeStyle.Chest;
    internal static DamageEmblem Emblem {get;private set;}=DamageEmblem.Fire;
    private static bool initialized;private static int revision;
    internal static readonly DependencyProperty RevisionProperty=DependencyProperty.RegisterAttached("Revision",typeof(int),typeof(IconArtwork),new FrameworkPropertyMetadata(0,FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly Dictionary<(string,int),BitmapFrame?> painted=[];
    private static readonly Dictionary<DamageEmblem,DrawingImage> cleanDamage=[];
    private static readonly int[] sizes=[14,16,20,24,32,40,48,64,128,256];
    internal static int CachedImages=>painted.Count;
    internal static void Observe(FrameworkElement element)=>element.SetResourceReference(RevisionProperty,"IconArtworkRevision");
    internal static bool Apply(CharacterProfile profile)
    {
        var style=Enum.IsDefined(profile.IconStyle)?profile.IconStyle:IconArtworkStyle.Vibrant;
        var emblem=Enum.IsDefined(profile.DamageEmblem)?profile.DamageEmblem:DamageEmblem.Fire;
        if(initialized&&Style==style&&Emblem==emblem&&Badge==profile.InventoryBadge)return false;
        initialized=true;Style=style;Emblem=emblem;Badge=profile.InventoryBadge;
        if(Application.Current!=null)Application.Current.Resources["IconArtworkRevision"]=++revision;
        return true;
    }
    internal static BitmapFrame? Painted(string role,double devicePixels=256)
    {
        int size=sizes.FirstOrDefault(s=>s>=devicePixels,256);var key=(role,size);
        if(painted.TryGetValue(key,out var existing))return existing;
        try
        {
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.UriSource=new("pack://application:,,,/EQLTracker;component/Assets/Painted/"+role+"-256.png");bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=size;bitmap.EndInit();bitmap.Freeze();
            var frame=BitmapFrame.Create(bitmap);frame.Freeze();painted[key]=frame;return frame;
        }
        catch(Exception error) when(error is IOException or NotSupportedException or UriFormatException or System.IO.FileFormatException) {painted[key]=null;return null;}
    }
    internal static bool DrawPainted(DrawingContext dc,string role,Rect bounds,double dpi,IconArtworkStyle? style=null)
    {
        if((style??Style)!=IconArtworkStyle.Vibrant||Painted(role,Math.Max(bounds.Width,bounds.Height)*dpi)is not{}image)return false;
        dc.DrawImage(image,bounds);return true;
    }
    internal static string AppRole(string id)=>id switch{"blue"=>"brand-blue-orb","gold-elements"=>"brand-gold-elements",_=>id};
    internal static string ActorRole(string actor,string element)=>actor switch{"Group"=>"actor-group","Player"=>"actor-self","Combined"=>"actor-total",_=>element switch{"Earth"=>"pet-earth","Air"=>"pet-air","Fire"=>"pet-fire","Water"=>"pet-water",_=>"pet-generic"}};
    internal static string GlyphRole(string shape)=>"glyph-"+shape.ToLowerInvariant();
    internal static void DrawDamage(DrawingContext dc,Rect bounds,double dpi,IconArtworkStyle? style=null,DamageEmblem? emblem=null)
    {
        var selected=DamageChoices.FirstOrDefault(c=>c.Id==(emblem??Emblem))??DamageChoices[0];
        if(DrawPainted(dc,selected.Role,bounds,dpi,style))return;
        dc.DrawImage(CleanDamage(selected.Id),bounds);
    }
    private static DrawingImage CleanDamage(DamageEmblem emblem)
    {
        if(cleanDamage.TryGetValue(emblem,out var existing))return existing;
        var group=new DrawingGroup();using(var dc=group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent,null,new(0,0,16,16));
            var gold=new SolidColorBrush(Color.FromRgb(225,184,89));var blue=new SolidColorBrush(Color.FromRgb(101,196,248));var violet=new SolidColorBrush(Color.FromRgb(183,131,246));var whiteGold=new SolidColorBrush(Color.FromRgb(255,229,161));var fire=new SolidColorBrush(Color.FromRgb(244,116,62));
            void Line(string path,Brush color,double width=1.4)=>dc.DrawGeometry(null,new Pen(color,width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},Geometry.Parse(path));
            switch(emblem)
            {
                case DamageEmblem.Fire:Line("M2,2L10,10 M4,1L12,9",fire);dc.DrawEllipse(fire,null,new(12,11),2.1,1.9);dc.DrawEllipse(whiteGold,null,new(12.4,11.4),.8,.8);break;
                case DamageEmblem.ArcaneOrb:Line("M2,2Q4,7 11,11 M3,1Q9,3 12,10",blue);dc.DrawEllipse(blue,new Pen(whiteGold,.6),new(12,11.5),2.1,2.1);break;
                case DamageEmblem.Lightning:Line("M1,1L6,5L5,7L10,8L9,10L15,14 M4,4L1,6 M10,8L13,7",blue,1.6);break;
                case DamageEmblem.ArcaneSigil:Line("M2,1L4,4 M5,5L7,7",violet);dc.DrawGeometry(violet,null,Geometry.Parse("M9,7L14,12L10,12L8,9Z M12,12L15,15L12,14Z"));break;
                case DamageEmblem.MagicMissiles:foreach(int offset in new[]{0,2,4})Line($"M1,{1+offset}L{11-offset},12 M{9-offset},9L{12-offset},13L{9-offset},12",whiteGold,1.1);break;
                case DamageEmblem.ElementalVortex:Line("M1,1Q4,6 11,10",fire);Line("M4,1Q5,6 13,11",blue);dc.DrawEllipse(violet,new Pen(whiteGold,.7),new(12,12),2.2,2.2);break;
            }
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(205,221,231)),new Pen(gold,.65),Geometry.Parse("M2,14L4,10L12,2L14,1L13,4L5,12Z"));Line("M2,10L6,14 M1,15L3,13",gold,1.5);
        }
        group.Freeze();var image=new DrawingImage(group);image.Freeze();cleanDamage[emblem]=image;return image;
    }
}
internal sealed class DamageEmblemPreview:FrameworkElement
{
    internal DamageEmblem? Emblem{get;init;}internal IconArtworkStyle? StyleOverride{get;init;}
    internal DamageEmblemPreview(){Width=32;Height=32;IconArtwork.Observe(this);}
    protected override void OnRender(DrawingContext dc){double size=Math.Min(ActualWidth,ActualHeight);if(size>0)IconArtwork.DrawDamage(dc,new(0,0,size,size),VisualTreeHelper.GetDpi(this).PixelsPerDip,StyleOverride,Emblem);}
}
