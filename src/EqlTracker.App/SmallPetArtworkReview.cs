using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EqlTracker.Core;

namespace EqlTracker.App;

// Retained comparison: reference vectors beside the approved painted treatment.
internal static class SmallPetArtworkReview
{
    internal static void Run(TrackerController controller,string folder,Action<bool,string> check)
    {
        var roles=new[]{("Monster A","pet-monster"),("Dragon C · overhead","pet-monster-dragon-c"),("Aviak C · overhead","pet-monster-aviak-c"),("Dragon B · diagonal","pet-monster-dragon-b"),("Aviak B · diagonal","pet-monster-aviak-b"),("Air · Monk fist","pet-air"),("Water · Rogue","pet-water"),("Fire · Wizard staff","pet-fire"),("Fire · Wizard crystal","pet-fire-crystal"),("Earth · Classic","pet-earth"),("Warder · Lion","pet-warder"),("Animation","pet-animation"),("Skeleton","pet-undead-skeleton"),("Wraith A","pet-undead-wraith-a"),("Wraith B","pet-undead-wraith-b")};
        var images=new List<string>();int[] sizes=[13,14,16,20,24,32,64];
        try
        {
            foreach(string theme in new[]{"coldain","sunstrider","shadowman"})foreach(double scale in new[]{1d,1.5,2d,3d})
            {
                Appearance.Apply(theme);var board=new StackPanel{Background=UI.Brush("InnerSurface")};
                var labels=new StackPanel{Orientation=Orientation.Horizontal,Height=24};labels.Children.Add(UI.Text("Simple | painting",12,"Melee",true));labels.Children[0].SetValue(FrameworkElement.WidthProperty,170d);
                foreach(int size in sizes)labels.Children.Add(new TextBlock{Text=size+" DIP",Width=152,Foreground=UI.Brush("Melee"),TextAlignment=TextAlignment.Center});board.Children.Add(labels);
                foreach(var (name,role) in roles)
                {
                    check(IconArtwork.Painted(role,64*scale)!=null,"Direct painting is available · "+role+" / "+scale);
                    var row=new StackPanel{Orientation=Orientation.Horizontal,Height=84};row.Children.Add(new TextBlock{Text=name,Width=170,Foreground=UI.Brush("Foreground"),VerticalAlignment=VerticalAlignment.Center});
                    foreach(int size in sizes)
                    {
                        var cell=new StackPanel{Orientation=Orientation.Horizontal,Width=152,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
                        foreach(bool direct in new[]{false,true})
                        {
                            var icon=new Artwork(role,direct,scale){Width=size,Height=size};
                            var button=new Button{Template=ButtonTemplate(),Width=size+4,Height=Math.Max(20,size+4),Padding=new(1),Content=icon,Margin=new(3,0,3,0),ToolTip=(direct?"Painting":"Simplified")+" · "+name};cell.Children.Add(button);
                        }
                        row.Children.Add(cell);
                    }
                    board.Children.Add(row);
                }
                string file=$"pet-small-painted-{theme}-{scale:0.0}x.png";
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(board,1234,1284,scale)));using(var output=File.Create(Path.Combine(folder,file)))png.Save(output);images.Add(file);
            }
            Json.Write(Path.Combine(folder,"small-painted-review.json"),new{Passed=true,ReviewOnly=true,VibrantPaintingsAtEverySize=true,Themes=3,Scales=4,Images=images,Sizes=sizes,Roles=roles.Select(r=>r.Item2)});
        }
        finally{Appearance.Apply(controller.Profile.Theme);}
    }
    private static ControlTemplate ButtonTemplate()=>(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
    <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button"><Border Background="Transparent" BorderThickness="1" BorderBrush="{DynamicResource Accent}" CornerRadius="2" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border></ControlTemplate>
    """);
    private sealed class Artwork(string role,bool direct,double scale):FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            var bounds=new Rect(0,0,ActualWidth,ActualHeight);
            if(direct&&IconArtwork.Painted(role,Math.Max(ActualWidth,ActualHeight)*scale) is{} painting)dc.DrawImage(painting,bounds);
            else ApprovedPetArtwork.Vector(dc,role,bounds,true);
        }
    }
}
