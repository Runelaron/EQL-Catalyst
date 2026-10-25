using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EqlTracker.Core;

namespace EqlTracker.App;

/// <summary>Isolated WPF state/rendering checks; these do not manufacture native pointer input.</summary>
internal static class ToolbarButtonPresentationReview
{
    internal static void Run(TrackerController controller,WorkspaceToolbarWindow bar,string folder,Action<bool,string> check)
    {
        var images=new List<string>();var samples=new List<object>();
        DependencyPropertyKey Key(Type owner,string name)=>(DependencyPropertyKey)(owner.GetField(name,BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null)??throw new InvalidOperationException("Missing WPF state key "+name));
        var hoverKey=Key(typeof(UIElement),"IsMouseOverPropertyKey");
        var focusKey=Key(typeof(UIElement),"IsKeyboardFocusedPropertyKey");
        var pressedKey=Key(typeof(ButtonBase),"IsPressedPropertyKey");
        var states=new[]{("Idle",false,false,false,false),("Hover",true,false,false,false),("Selected",false,true,false,false),("Selected + hover",true,true,false,false),("Keyboard focus",false,false,true,false),("Pressed",true,false,false,true)};
        try
        {
            foreach(string theme in new[]{"coldain","sunstrider","shadowman"})
            {
                Appearance.Apply(theme);
                foreach(bool minimum in new[]{false,true})
                {
                    var board=new StackPanel();
                    // Subtle continuous stripes reveal transparency without obscuring the labels.
                    var backing=new DrawingGroup();
                    backing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(41,61,72)),null,new RectangleGeometry(new Rect(0,0,32,32))));
                    backing.Children.Add(new GeometryDrawing(null,new Pen(new SolidColorBrush(Color.FromRgb(58,78,89)),1),Geometry.Parse("M0 0L32 32")));
                    board.Background=new DrawingBrush(backing){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new(0,0,32,32)};
                    foreach(var (name,hover,selected,focus,pressed) in states)
                    {
                        var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(6)};
                        row.Children.Add(new TextBlock{Text=name,Width=minimum?105:135,Foreground=UI.Brush("Foreground"),VerticalAlignment=VerticalAlignment.Center});
                        foreach(var id in new[]{"Tracker","CombatLab","Overview"})
                        {
                            var source=bar.Buttons[id];
                            var label=new TextBlock{Text=id=="CombatLab"?"Combat Lab":id,FontSize=11,FontWeight=FontWeights.Bold,Foreground=UI.Brush("Foreground")};
                            var button=new ToggleButton{Style=source.Style,Content=UI.Row(new ToolbarGlyph(id){Width=16,Height=16,Margin=new(0,0,4,0)},label),Height=24,Width=minimum?source.ActualWidth:source.ActualWidth+10,Padding=source.Padding,Margin=new(0,0,3,0),IsChecked=selected};
                            button.ApplyTemplate();button.Measure(new Size(1000,24));button.Arrange(new Rect(0,0,button.Width,24));button.UpdateLayout();var before=button.RenderSize;
                            button.SetValue(hoverKey,hover);button.SetValue(focusKey,focus);button.SetValue(pressedKey,pressed);button.UpdateLayout();
                            var face=(Border)button.Template.FindName("Face",button);var cue=(Grid)button.Template.FindName("HoverCue",button);
                            check(face.Background is SolidColorBrush fill&&fill.Color.A==0&&button.Opacity==1,theme+" "+id+" "+name+" keeps its face transparent and artwork opaque");
                            check(face.BorderThickness==new Thickness(1)&&button.RenderSize==before&&button.ActualHeight==24,theme+" "+id+" "+name+" reserves identical 24-DIP geometry");
                            check(face.BorderBrush is SolidColorBrush edge&&(selected?edge.Color==UI.Brush("Spell").Color:edge.Color.A==0),theme+" "+id+" "+name+" shows the blue border only for selection");
                            check(cue.Opacity==((hover||focus||pressed)?1:0)&&!cue.IsHitTestVisible&&cue.Children.Count==2,theme+" "+id+" "+name+" uses two noninteractive corner brackets for intent");
                            var bitmap=PreviewRenderer.Capture(button,button.Width,24,1);var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
                            check(pixels[(12*bitmap.PixelWidth+2)*4+3]==0,theme+" "+id+" "+name+" has no dark interior fill in the rendered pixels");
                            row.Children.Add(button);samples.Add(new{Theme=theme,Minimum=minimum,Workspace=id,State=name,Width=button.ActualWidth,Height=button.ActualHeight,FaceAlpha=0});
                        }
                        board.Children.Add(row);
                    }
                    double width=minimum?450:510,height=220;
                    foreach(double scale in new[]{1d,1.5,2d,3d})
                    {
                        string name="toolbar-buttons-"+theme+"-"+(minimum?"minimum":"normal")+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x.png";
                        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(board,width,height,scale)));using(var file=File.Create(Path.Combine(folder,name)))png.Save(file);images.Add(name);
                    }
                }
            }
            Json.Write(Path.Combine(folder,"toolbar-button-presentation.json"),new{Passed=true,SyntheticStates=true,RealPointerInputClaimed=false,HeightDIP=24,FaceAlpha=0,SelectedBorder="Spell blue",HoverAndFocus="Two gold corner brackets",Samples=samples,Images=images});
        }
        finally{Appearance.Apply(controller.Profile.Theme);}
    }
}
