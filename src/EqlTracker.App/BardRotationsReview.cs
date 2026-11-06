using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;
internal static class BardRotationsReview
{
    internal static async Task Run(string folder,TrackerController controller,DpsWindow lab,bool whyOnly=false)
    {
        if(!controller.Demo)throw new InvalidOperationException("Synthetic review only");Directory.CreateDirectory(folder);var checks=new List<string>();var failures=new List<string>();var geometry=new List<object>();
        void Check(bool value,string name){if(value)checks.Add(name);else failures.Add(name);}
        void Flush(){lab.UpdateLayout();lab.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);lab.UpdateLayout();}
        controller.SeedIntegratedReview();controller.SeedBardRotationsReview();controller.Profile.SmoothMeterMotion=false;
        lab.Placement!.ReviewEnabled=true;lab.Opacity=0;lab.ShowActivated=false;lab.ShowInTaskbar=false;lab.Left=lab.Top=-15000;lab.Show();var panel=lab.OpenSongsReview();panel.Refresh();await panel.Pending;Flush();
        if(whyOnly)
        {
            foreach(var theme in ThemeCatalog.Choices.Skip(1))foreach(double scale in new[]{1d,1.5,2d,3d})foreach(var size in new[]{(Name:"normal",W:915d,H:650d),(Name:"minimum",W:640d,H:420d),(Name:"short",W:915d,H:420d)})
            {
                controller.Profile.Theme=theme.Id;Appearance.Apply(theme.Id);lab.Width=size.W;lab.Height=size.H;Flush();
                foreach(var toggle in Usability.Tree(panel.Rows).OfType<ToggleButton>().Where(t=>Equals(t.Content,"Why?")))
                {
                    var presenter=Usability.Tree(toggle).OfType<ContentPresenter>().First();double minimum=presenter.DesiredSize.Width+20+2+toggle.Padding.Left+toggle.Padding.Right;var priorBounds=toggle.RenderSize;toggle.Focus();toggle.IsChecked=true;Flush();toggle.IsChecked=false;Flush();
                    Check(toggle.ActualWidth>=minimum&&toggle.RenderSize==priorBounds&&toggle.ActualHeight==28,$"Complete Why label and stable activation {theme.Id}/{size.Name}/{scale}");
                }
                if(scale==1){var image=PreviewRenderer.Capture((FrameworkElement)lab.Content,size.W,size.H,scale);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(folder,$"{theme.Id}-songs-{size.Name}-{scale:0.0}x.png"));encoder.Save(file);}
            }
            Json.Write(Path.Combine(folder,"why-checks.json"),new{Passed=failures.Count==0,Checks=checks,Failures=failures,OnlyAffectedDisclosure=true});if(failures.Count>0)throw new InvalidOperationException(string.Join("\n",failures));lab.Hide();return;
        }
        Check(lab.ActiveTab==CombatLabTab.Comparison&&panel.View?.Rows.Count==5,"Songs occupy Loadout Comparison and display the five-song set");
        Check(panel.View?.Rows.Any(r=>r.Options.Count>0)==true,"Group-aware higher-rank overlap produces a supported replacement");
        Check(panel.Strength.IsChecked==true,"Effective strength defaults on");
        var before=panel.Rows.Children.Count>1?panel.Rows.Children[1]:null;controller.AdvanceBardReviewCounter();panel.Refresh();await panel.Pending;Check(ReferenceEquals(before,panel.Rows.Children[1]),"Recurrence bookkeeping retains stable comparison rows");
        var common=controller.BardResearchSnapshot().Common.First();controller.SelectBardSet(common.Id);panel.Refresh();await panel.Pending;controller.ChangeBardReviewCurrent();panel.Refresh();await panel.Pending;Check(panel.View?.SetId==common.Id,"A common set stays pinned while detection changes");
        controller.SelectBardSet("detected");panel.Refresh();await panel.Pending;Check(panel.View?.SetId!=common.Id,"Detected resumes automatic following");controller.SeedBardRotationsReview();panel.Refresh();await panel.Pending;
        panel.Strength.IsChecked=false;panel.Strength.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await panel.Pending;Check(!controller.BardResearchSnapshot().Preferences.EffectiveStrength,"Strength preference persists independently of rank priority");panel.Strength.IsChecked=true;panel.Strength.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await panel.Pending;
        foreach(var theme in ThemeCatalog.Choices.Skip(1))
        {
            controller.Profile.Theme=theme.Id;Appearance.Apply(theme.Id);
            foreach(double scale in new[]{1d,1.5,2d,3d})foreach(var size in new[]{(Name:"normal",W:915d,H:650d),(Name:"minimum",W:640d,H:420d),(Name:"short",W:915d,H:420d)})
            {
                lab.Width=size.W;lab.Height=size.H;Flush();panel.Refresh();await panel.Pending;Flush();
                var frame=(FrameworkElement)lab.Content;var image=PreviewRenderer.Capture(frame,size.W,size.H,scale);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(folder,$"{theme.Id}-songs-{size.Name}-{scale:0.0}x.png")))png.Save(file);Flush();
                Check(panel.Sets.ActualHeight==28&&panel.Strength.ActualHeight==28,$"28-DIP controls {theme.Id}/{size.Name}/{scale}");
                var bounds=panel.Sets.TransformToAncestor((Visual)lab.Content).TransformBounds(new(panel.Sets.RenderSize));panel.Sets.Focus();Flush();Check(bounds==panel.Sets.TransformToAncestor((Visual)lab.Content).TransformBounds(new(panel.Sets.RenderSize)),$"Stable focus bounds {theme.Id}/{size.Name}/{scale}");
                Check(image.PixelWidth==(int)Math.Ceiling(size.W*scale)&&image.PixelHeight==(int)Math.Ceiling(size.H*scale),$"Render dimensions {theme.Id}/{size.Name}/{scale}");
                foreach(var toggle in Usability.Tree(panel.Rows).OfType<ToggleButton>().Where(t=>Equals(t.Content,"Why?"))){Check(toggle.ActualHeight==28,$"Why disclosure geometry {theme.Id}/{size.Name}/{scale}");}
                geometry.Add(new{Theme=theme.Id,Scale=scale,size.Name,Width=panel.ActualWidth,Height=panel.ActualHeight,Rows=panel.View?.Rows.Count});
            }
        }
        panel.Setup.IsExpanded=true;panel.Setup.BringIntoView();Flush();Check(Usability.Tree(panel.Setup).OfType<BardSongEditor>().Count()==1,"One lazy Setup editor replaces the nested member inspector");
        Check(!Usability.Tree(panel.Setup).OfType<Button>().Where(b=>Equals(b.Content,"Save modifiers")).Any(b=>b.IsEnabled),"Unchanged modifier Save starts disabled");
        Save("setup-expanded",915,650);panel.Setup.IsExpanded=false;
        lab.ShowView(CombatLabMode.Damage,CombatLabTab.Group);lab.OpenSongCoverageReview();await panel.Pending;Flush();Check(lab.ActiveTab==CombatLabTab.Comparison&&panel.IsVisible,"Existing Song coverage action navigates to the new comparison");
        controller.SelectBardSet("missing");panel.Refresh();await panel.Pending;Check(panel.View?.Rows.Count==0,"Unavailable pinned set rejects stale completed rows");controller.SelectBardSet("detected");
        Json.Write(Path.Combine(folder,"bard-rotations-checks.json"),new{Passed=failures.Count==0,Checks=checks,Failures=failures,Geometry=geometry,Synthetic=true,PhysicalDpi="Not exercised",LiveGameAcceptance="Not exercised"});
        if(failures.Count>0)throw new InvalidOperationException(string.Join("\n",failures));lab.Hide();
        void Save(string name,double w,double h){var image=PreviewRenderer.Capture((FrameworkElement)lab.Content,w,h,1);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(folder,name+".png"));encoder.Save(file);}
    }
}

