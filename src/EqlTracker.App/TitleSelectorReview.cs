using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class OverlayWindow
{
    internal void ReviewTitleSelector(string folder)
    {
        if (!Controller.Demo || !IsDps) throw new InvalidOperationException("Synthetic DPS review only");
        Directory.CreateDirectory(Path.Combine(folder,"qa"));
        var checks=new List<string>(); var timings=new List<object>();
        void Check(bool ok,string name) { if(!ok) throw new InvalidOperationException(name);checks.Add(name); }
        void Flush() { UpdateLayout(); Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle); UpdateLayout(); }
        void Capture(string name,FrameworkElement element,double width,double height)
        {
            foreach(double scale in new[]{1d,1.5,2d,3d})
            {
                var watch=Stopwatch.StartNew();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(element,width,height,scale)));
                using var file=File.Create(Path.Combine(folder,"qa",name+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x.png"));encoder.Save(file);
                timings.Add(new{Name=name,Scale=scale,Milliseconds=watch.Elapsed.TotalMilliseconds});
            }
        }
        GearBuildFixtures.Apply(Controller);Controller.Profile.SmoothMeterMotion=false;
        var button=titleDisplaySelector!;var menu=button.ContextMenu!;menu.Opacity=0;
        void ClickTitle()
        {
            // End the previous synthetic anchor gesture, as a real mouse release
            // does, before starting another direct Click event.
            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent});
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        Check(menu.Items.OfType<MenuItem>().Select(i=>i.Header?.ToString()).SequenceEqual(new[]{"Pet","Self","Combined","Group"}),"Title dropdown has exactly four direct choices");
        Check(menu.Items.OfType<MenuItem>().All(i=>i.Icon is ActorBadge&&i.IsCheckable&&i.Items.Count==0),"Choices have role icons and checked state, without nesting");
        string labBefore=JsonSerializer.Serialize(Controller.Profile.Meter);string filterBefore=JsonSerializer.Serialize(Controller.Profile.OverlayMeter!);string footerBefore=JsonSerializer.Serialize(Controller.Profile.OverlayFooterDisplay);
        var actors=new[]{"Pet","Player","Combined","Group"};
        foreach(string actor in actors)
        {
            var item=menu.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,actor=="Player"?"Self":actor));
            ClickTitle();Check(menu.IsOpen,"Title icon opens choices · "+actor);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));menu.IsOpen=false;Refresh();Flush();
            var selection=Controller.Profile.OverlayMeter!;
            Check(actor=="Group"?selection.DisplayMode==OverlayCombatMode.Group:selection.DisplayMode==OverlayCombatMode.Personal&&selection.Actor==actor,"Selection applies immediately · "+actor);
            ClickTitle();
            Check(menu.Items.OfType<MenuItem>().Count(i=>i.IsChecked)==1&&item.IsChecked,"Reopened dropdown checks only selected actor · "+actor);menu.IsOpen=false;Flush();
            Check(AutomationProperties.GetName(button)=="DPS display: "+(actor=="Player"?"Self":actor),"Accessible title follows selected actor · "+actor);
            Check(footerBefore==JsonSerializer.Serialize(Controller.Profile.OverlayFooterDisplay),"Title choice leaves secondary display unchanged · "+actor);
        }
        var persisted=JsonSerializer.Deserialize<DpsSelection>(JsonSerializer.Serialize(Controller.Profile.OverlayMeter!))!;
        Check(persisted.DisplayMode==OverlayCombatMode.Group,"Selection uses existing persisted overlay setting");
        Check(labBefore==JsonSerializer.Serialize(Controller.Profile.Meter),"Title selection leaves Combat Lab selection unchanged");
        var original=JsonSerializer.Deserialize<DpsSelection>(filterBefore)!;var current=Controller.Profile.OverlayMeter!;
        current.DisplayMode=original.DisplayMode;current.Actor=original.Actor;
        Check(filterBefore==JsonSerializer.Serialize(current),"Scope, sorting and filters remain unchanged");SelectDisplay("Group");
        foreach(Key key in new[]{Key.Down,Key.F4})
        {
            button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,Environment.TickCount,key){RoutedEvent=Keyboard.PreviewKeyDownEvent});
            Check(menu.IsOpen,"Keyboard opens choices · "+key);
            menu.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
            Check(!menu.IsOpen&&!Usability.GetIsPressed(button),"Escape clears dropdown and pressed state · "+key);Flush();
        }
        var peer=new ButtonAutomationPeer(button);((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();Flush();Check(menu.IsOpen,"Title dropdown supports accessible Invoke");menu.IsOpen=false;Flush();
        Check(!Header.IsMouseCaptured,"Title selector does not start header dragging");
        foreach(string style in new[]{"Minimal","Frost","Luminous"})
        {
            Controller.Profile.MeterStyle=style;SetMeterSize(128,72);Flush();
            Check(button.IsVisible&&HeaderIcon.IsVisible&&button.ActualWidth>=18&&HeadingTitle.ActualWidth>10,"Minimum overlay retains title selector and title · "+style);
            ClickTitle();Check(menu.IsOpen,"Minimum title selector opens · "+style);menu.IsOpen=false;Flush();
        }
        ToggleCollapse();Flush();Check(button.IsVisible&&button.ActualWidth>=18,"Collapsed title keeps usable selector");ToggleCollapse();Flush();
        foreach(string theme in new[]{"coldain","sunstrider","shadowman"})
        {
            Appearance.Apply(theme);Controller.Profile.IconStyle=IconArtworkStyle.Vibrant;IconArtwork.Apply(Controller.Profile);Controller.Profile.MeterStyle="Frost";
            SetMeterSize(300,260);Refresh();Flush();Capture(theme+"-title",(FrameworkElement)Content,300,260);
            SetMeterSize(128,72);Flush();Capture(theme+"-minimum",(FrameworkElement)Content,128,72);
            var board=UI.Stack(UI.Text("DPS display",14,"Accent",true));board.Background=UI.Brush("SolidSurface");
            var items=menu.Items.OfType<MenuItem>().ToArray();menu.Items.Clear();
            var menuPreview=new StackPanel { Resources=menu.Resources };
            foreach(var item in items)menuPreview.Children.Add(item);board.Children.Add(menuPreview);
            foreach(var style in Enum.GetValues<IconArtworkStyle>())
            {
                Controller.Profile.IconStyle=style;IconArtwork.Apply(Controller.Profile);
                var groupBadge=new ActorBadge{Width=48,Height=48};groupBadge.Set("Group",false,"Group icon");
                // Freeze each review sample to its own style using a render capture.
                var snapshot=PreviewRenderer.Capture(groupBadge,48,48,2);var picture=new Image{Source=snapshot,Width=48,Height=48};
                board.Children.Add(UI.Row(UI.Text(style+" Group",12),picture));
                Check(snapshot.PixelWidth==96,theme+": "+style+" Group renders at native device pixels");
            }
            Controller.Profile.IconStyle=IconArtworkStyle.Vibrant;IconArtwork.Apply(Controller.Profile);
            Capture(theme+"-choices",board,240,330);menuPreview.Children.Clear();foreach(var item in items)menu.Items.Add(item);
            Check(IconArtwork.Painted("actor-group",64)!=null,theme+": detailed Group uses packaged painted artwork");
        }
        var art=IconArtwork.Painted("actor-group",256)!;Check(art.PixelWidth==256&&art.PixelHeight==256,"Group production resource has intended 256px dimensions");
        string Sha(string path)=>Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        Json.Write(Path.Combine(folder,"title-selector-checks.json"),new{Passed=checks.Count,Checks=checks,Timings=timings,AppSha256=Sha(typeof(App).Assembly.Location),CoreSha256=Sha(typeof(CharacterProfile).Assembly.Location),Scope="Synthetic WPF control/menu/keyboard events, accessible Invoke, existing persistence round-trip and component renders; no live profile or physical game input."});
    }
}
