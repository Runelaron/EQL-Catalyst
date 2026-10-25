using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class ToolbarHeaderPolishReview
{
    internal static void Run(TrackerController controller,WorkspaceToolbarWindow bar,QuestWindow tracker,DpsWindow lab,string folder,Action<bool,string> check)
    {
        void Flush(Window w){w.UpdateLayout();w.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);w.UpdateLayout();}
        Rect Bounds(FrameworkElement e,Visual ancestor)=>e.TransformToAncestor(ancestor).TransformBounds(new Rect(e.RenderSize));
        var close=Usability.Tree(bar).OfType<Button>().Single(b=>Equals(b.Tag,"toolbar-close"));
        bar.Reveal();Flush(bar);var grip=bar.OuterResize!.Grip;
        check(Bounds(close,bar).Right+3.9<=Bounds(grip,bar).Left&&grip.Width==14,"Toolbar grip follows Close with a separate four-DIP gap");
        check(close.Content is FrameworkElement art&&art.Width==12&&close.Width==24,"Themed Close artwork shrinks by a quarter inside its unchanged 24-DIP button");
        var hover=(DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        var focus=(DependencyPropertyKey)typeof(UIElement).GetField("IsKeyboardFocusedPropertyKey",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        var press=(DependencyPropertyKey)typeof(ButtonBase).GetField("IsPressedPropertyKey",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        var samples=new List<object>();
        foreach(string theme in new[]{"coldain","sunstrider","shadowman"})
        {
            Appearance.Apply(theme);
            foreach(var action in new[]{bar.SettingsButton,close})foreach(var state in new[]{"Idle","Hover","Focus","Pressed"})
            {
                var button=new Button{Template=action.Template,Width=24,Height=24,Content=new ToolbarActionArtwork(action==close){Width=action==close?12:18,Height=action==close?12:18}};
                button.Measure(new(24,24));button.Arrange(new(0,0,24,24));button.ApplyTemplate();button.UpdateLayout();
                button.SetValue(hover,state=="Hover");button.SetValue(focus,state=="Focus");button.SetValue(press,state=="Pressed");button.UpdateLayout();
                var face=(Border)button.Template.FindName("Face",button);
                check(face.Background is SolidColorBrush fill&&fill.Color.A==0&&button.ActualHeight==24,theme+" "+(action==close?"Close":"Settings")+" "+state+" stays clear with fixed bounds");
            }
            foreach(var window in new FrostWindow[]{tracker,lab})foreach(double width in new[]{915d,640d})
            {
                bar.Open(window==tracker?"Tracker":"CombatLab");window.Width=width;window.Height=650;
                window.Subtitle.Text=window==tracker?"ExampleHero · Lake Nerius · Updated 09:15":"Monster pet · Conjuration · Nektropos Castle";
                window.Subtitle.Visibility=Visibility.Visible;Flush(window);
                var band=Usability.Tree(window).OfType<WorkspaceHeaderBand>().Single();var title=Bounds(window.HeadingTitle,band);var context=Bounds(window.Subtitle,band);
                check(context.Width>0&&context.Right<=band.ActualWidth-7.5,theme+" "+window.GetType().Name+" "+width+" keeps context right-aligned with eight-DIP control separation");
                check(context.Top>=title.Bottom-.5||context.Left>=title.Right+11.5,theme+" "+window.GetType().Name+" "+width+" keeps title and context clear of each other");
                if(width==915)check(context.Left>=title.Right&&band.ActualHeight<40,theme+" "+window.GetType().Name+" shares a compact title line");
                foreach(double scale in new[]{1d,1.5,2d,3d})
                {
                    // Crop the actual native window's rendered header, not a reconstruction.
                    var capture=PreviewRenderer.Capture((FrameworkElement)window.Content,width,650,scale);
                    var crop=new CroppedBitmap(capture,new(0,0,capture.PixelWidth,Math.Min(capture.PixelHeight,(int)Math.Ceiling(86*scale))));
                    string name=$"header-{(window==tracker?"tracker":"lab")}-{theme}-{(width==915?"normal":"minimum")}-{scale:0.0}x.png";
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(crop));using(var file=File.Create(Path.Combine(folder,name)))png.Save(file);samples.Add(new{File=name,Theme=theme,Width=width,Scale=scale});
                }
            }
        }
        var longTitle=UI.Text("EQL Catalyst — A deliberately long workspace title",17);var detail=UI.Text("ExampleHero · Long zone name",12);
        var narrow=new WorkspaceHeaderBand(longTitle,detail);narrow.Children.Add(longTitle);narrow.Children.Add(detail);narrow.Measure(new(300,200));narrow.Arrange(new(0,0,300,narrow.DesiredSize.Height));
        check(Bounds(detail,narrow).Top>=Bounds(longTitle,narrow).Bottom,"Long headings wrap context below the title without overlap");
        bar.Open("CombatLab");bar.ClosePanel();bar.TryHide();bar.Reveal();Flush(bar);
        check(bar.IsVisible&&bar.State.Active==""&&!lab.IsVisible,"Catalyst Toolbar reveals the collapsed bar without choosing a workspace");
        bar.Open("Tracker");bar.TryHide();bar.Reveal();Flush(bar);
        check(bar.State.Active=="Tracker"&&tracker.IsVisible,"Catalyst Toolbar restores the selected workspace without switching contexts");
        Json.Write(Path.Combine(folder,"toolbar-header-polish.json"),new{Passed=true,SyntheticStates=true,PhysicalPointerAcceptance=false,HeaderImages=samples});
        Appearance.Apply(controller.Profile.Theme);
    }
}
