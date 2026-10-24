using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewPetContributions(string folder)
    {
        await ReviewPetDetailsPopup(folder);
        var checks=new List<string>();var clock=Stopwatch.StartNew();
        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks.Add(label);}
        Check(Usability.Tree(this).OfType<Button>().Any(b=>Equals(b.Content,"Pet data…"))==TrackerController.PetResearchEnabled,"Private research action follows the explicit build gate");
        void Flush(){UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
        void Double(FrameworkElement target)
        {
            var e=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent};
            typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(e,2);target.RaiseEvent(e);
        }
        void Capture(string name,FrameworkElement content,double width,double height)
        {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(content,width,height,1)));using var file=File.Create(Path.Combine(folder,name+".png"));encoder.Save(file);}
        Width=915;Height=650;Flush();double expanded=Height;
        Double(HeadingTitle);Flush();Check(IsCollapsed&&HeadingTitle.Text=="EQL C - Tracker"&&Subtitle.Visibility==Visibility.Collapsed,"Routed header double-click collapses Tracker to the exact compact title");
        Capture("tracker-collapsed",(FrameworkElement)Content,915,28);
        Refresh();Flush();Check(HeadingTitle.Text=="EQL C - Tracker"&&Subtitle.Visibility==Visibility.Collapsed,"Ordinary refresh does not restore player metadata into the collapsed bar");
        Double(HeadingTitle);Flush();Check(!IsCollapsed&&Height==expanded&&HeadingTitle.Text=="EQL Catalyst — Quest & Gear Tracker","Double-click restores expanded dimensions and branded heading");
        Check(Grid.GetColumn(Subtitle)==1&&Subtitle.TextAlignment==TextAlignment.Right,"Expanded Tracker metadata is right-aligned beside the heading");
        Width=640;Flush();Check(Grid.GetRow(Subtitle)==1&&Subtitle.Visibility==Visibility.Visible,"Narrow Tracker metadata moves to a right-aligned second line");Capture("tracker-header-narrow",(FrameworkElement)Content,640,650);
        bool wasCollapsed=IsCollapsed;Double(HeaderActions.Children.OfType<Button>().Last());Flush();Check(IsCollapsed==wasCollapsed,"Header button double-click is excluded from the background collapse gesture");
        // The close button's own click action is not synthesized by PreviewMouseDown.
        IsLocked=true;Double(HeadingTitle);Flush();Double(HeadingTitle);Flush();Check(!IsCollapsed&&IsLocked&&ResizeMode==ResizeMode.NoResize,"Locked windows can collapse and restore without unlocking resize");IsLocked=false;
        using var labLifetime=new ReviewWindowLifetime(new DpsWindow(Controller));var lab=labLifetime.Window;lab.Opacity=0;lab.Left=lab.Top=-15000;lab.Show();lab.UpdateLayout();double labHeight=lab.Height;
        Double(lab.HeadingTitle);lab.UpdateLayout();Check(lab.IsCollapsed,"Combat Lab supports the routed double-click collapse gesture");Double(lab.HeadingTitle);lab.UpdateLayout();Check(!lab.IsCollapsed&&lab.Height==labHeight,"Combat Lab double-click restores its prior size");
        var rows=new[]{new ObjectiveRow{Class="WAR",Item="Ancient Component",Mob="Named Guardian",Zone="Example zone",Needed=2,Instructions="Full drop instructions",Sources=[new(){Zone="Example zone",Mob="Named Guardian",Method="Drop"}]},new ObjectiveRow{Class="BRD",Item="Quest reward",Mob="Traveling Bard",Zone="Example zone",Sources=[new(){Zone="Example zone",Mob="Traveling Bard",Method="Turn-in"}]},new ObjectiveRow{Class="MAG",Item="Ground component",Zone="Example zone",Sources=[new(){Zone="Example zone",Method="Ground spawn"}]}};
        var objectives=UI.Stack(rows.Select(row=>OverlayWindow.CompactObjectiveCard(row,false)).ToArray());
        var objectiveWindow=new ObjectivePreview(objectives);objectiveWindow.Attach();
        objectiveWindow.Left=objectiveWindow.Top=-15000;objectiveWindow.Opacity=0;objectiveWindow.Show();objectiveWindow.UpdateLayout();
        Check(Usability.Tree(objectives).OfType<TextBlock>().Count()==3,"Each objective renders one compact summary without repeated need/zone prose");Capture("compact-objectives",(FrameworkElement)objectiveWindow.Content,330,220);objectiveWindow.Close();
        Width=915;Height=650;Flush();Capture("tracker-header",(FrameworkElement)Content,915,650);
        Json.Write(Path.Combine(folder,"pet-contributions-window-checks.json"),new{Passed=checks.Count,Checks=checks,PrivateResearch=TrackerController.PetResearchEnabled,Milliseconds=clock.Elapsed.TotalMilliseconds});
    }
    private sealed class ReviewWindowLifetime(DpsWindow window):IDisposable
    {internal DpsWindow Window=>window;public void Dispose(){window.Hide();}}
    private sealed class ObjectivePreview(UIElement content):FrostDialog("Compact objective preview",330,220)
    {internal void Attach()=>Editor.Children.Add(content);}
}
