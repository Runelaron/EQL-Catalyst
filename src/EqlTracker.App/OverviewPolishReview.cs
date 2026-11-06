using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class OverviewPolishReview
{
    internal static void Gear(string folder,TrackerController controller,QuestWindow tracker)
    {
        if(!controller.Demo)throw new InvalidOperationException();Directory.CreateDirectory(folder);var checks=new List<string>();
        tracker.Placement!.ReviewEnabled=true;tracker.Opacity=0;tracker.ShowActivated=false;tracker.Left=tracker.Top=-15000;tracker.Show();tracker.ShowView(TrackerTab.Gear);tracker.Refresh();
        foreach(var theme in ThemeCatalog.Choices.Skip(1))foreach(double scale in new[]{1d,1.5,2d,3d})foreach(bool narrow in new[]{false,true})
        {
            controller.Profile.Theme=theme.Id;Appearance.Apply(theme.Id);double w=narrow?640:915,h=narrow?420:650;tracker.Width=w;tracker.Height=h;tracker.UpdateLayout();tracker.OverviewPolishControls(TrackerTab.Gear,true);tracker.UpdateLayout();
            if(!tracker.OverviewPolishGearAccessibility())throw new InvalidOperationException("Gear filter/footer accessibility");
            var image=PreviewRenderer.Capture((FrameworkElement)tracker.Content,w,h,scale);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(folder,$"{theme.Id}-gear-{(narrow?"minimum":"normal")}-{scale:0.0}x.png"));png.Save(file);checks.Add($"{theme.Id}/{w}/{scale}");
        }
        Json.Write(Path.Combine(folder,"gear-checks.json"),new{Passed=true,Checks=checks,MeasuredControls=true});tracker.Hide();
    }
    internal static async Task Run(string folder,TrackerController controller,QuestWindow tracker,DpsWindow lab,bool affectedRetry=false)
    {
        if(!controller.Demo)throw new InvalidOperationException("Synthetic data required");Directory.CreateDirectory(folder);
        var checks=new List<string>();var failures=new List<string>();var geometry=new List<object>();
        void Check(bool value,string name){if(value)checks.Add(name);else failures.Add(name);}
        void Flush(FrameworkElement e){e.UpdateLayout();e.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);e.UpdateLayout();}
        controller.SeedIntegratedReview();controller.SeedOverviewPolishReview();controller.Profile.SmoothMeterMotion=false;
        var overview=new OverviewWindow(controller){Opacity=0,ShowActivated=false,ShowInTaskbar=false,Left=-15000,Top=-15000};
        foreach(var w in new FrostWindow[]{overview,tracker,lab}){w.Placement!.ReviewEnabled=true;w.Opacity=0;w.ShowActivated=false;w.ShowInTaskbar=false;w.Left=w.Top=-15000;w.Show();Flush(w);}
        overview.Refresh();await overview.Pending;Flush(overview);
        Check(overview.Snapshot?.Recap.Earned==1234&&overview.Snapshot.Recap.Spent==65,"Income combines receipts and sales; spending is a batch total");
        Check(!Usability.Tree(overview).OfType<TextBlock>().Any(t=>t.Text.StartsWith("Retained history")),"No retained-history banner");
        var initial=overview.LootRows.Children[0];overview.Refresh();await overview.Pending;Check(ReferenceEquals(initial,overview.LootRows.Children[0]),"Equivalent refresh retains completed loot rows");
        var owner=controller.Profile.ActiveLoadout;owner.Mechanics??=new();var context=controller.OverviewRatingContext(overview.Snapshot!.Recap.Loot);
        owner.Mechanics.Revision="unrelated-pet-research";owner.Mechanics.PetUsualBuffs.Add(new(){Family="Pet only"});
        Check(context.Key==controller.OverviewRatingContext(overview.Snapshot.Recap.Loot).Key,"Pet research bookkeeping does not invalidate player loot ratings");
        owner.Mechanics.PetUsualBuffs.Clear();owner.Mechanics.Revision="";
        overview.UpgradeRating.IsChecked=true;overview.UpgradeRating.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));overview.Refresh();await overview.RatingsPending;Flush(overview);
        Check(controller.OverviewRatings&&Usability.Tree(overview.LootRows).OfType<TextBlock>().Any(t=>t.Text.StartsWith("U ")&&t.Text.Contains(" / B ")),"Optional U/B ratings are displayed together");
        var ratingBefore=PetComparisonEngine.Key(owner);overview.Refresh();await overview.RatingsPending;Check(ratingBefore==PetComparisonEngine.Key(owner),"Ratings never edit saved Current gear");
        var label=Usability.Tree(overview.LootRows).OfType<TextBlock>().First(t=>t.Inlines.OfType<Hyperlink>().Any());var receipt=overview.Snapshot.Recap.Loot.First();
        overview.ItemPeek.Show(receipt,label);await Task.Delay(20);Flush(overview.ItemPeek.Child as FrameworkElement??overview);
        Check(overview.ItemPeek.IsOpen&&Usability.Tree(overview.ItemPeek.Child).OfType<ClassicItemCard>().Any(),"Loot popup uses the shared ranked item card");
        Check(Usability.Tree(overview.ItemPeek.Child).OfType<TextBlock>().Any(t=>t.Text.Contains("Recorded upgrade rank · "+receipt.Receipts.First().Rank)),"Popup retains recorded upgrade rank");
        Save("item-peek",(FrameworkElement)overview.ItemPeek.Child,420,450,1);
        overview.ItemPeek.Child.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(label),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        Check(!overview.ItemPeek.IsOpen,"Escape dismisses reduced-motion popup immediately");
        overview.ItemPeek.Show(receipt,label);overview.Hide();Check(!overview.ItemPeek.IsOpen,"Workspace hiding dismisses popup");overview.Show();overview.Refresh();await overview.Pending;
        overview.SetupToggle.IsChecked=overview.ConditionsToggle.IsChecked=true;
        foreach(var theme in ThemeCatalog.Choices.Skip(1))
        {
            controller.Profile.Theme=theme.Id;Appearance.Apply(theme.Id);
            foreach(double scale in new[]{1d,1.5,2d,3d})foreach(var size in new[]{(Name:"normal",Width:915d,Height:650d),(Name:"minimum",Width:640d,Height:420d)})
            {
                overview.Width=size.Width;overview.Height=size.Height;Flush(overview);overview.ArrangeSections();
                if(!affectedRetry||scale==1)Save($"{theme.Id}-overview-{size.Name}-{scale:0.0}x",(FrameworkElement)overview.Content,size.Width,size.Height,scale);Flush(overview);
                Check(Grid.GetRow(overview.Sections.Children[1])==(overview.Sections.ActualWidth>=760?0:1),$"Overview section breakpoint {theme.Id}/{size.Name}/{scale}");
                foreach(var e in Usability.Tree(overview).OfType<Expander>().Where(e=>Equals(e.Header,"Evidence")))
                {
                    var toggle=Usability.Tree(e).OfType<ToggleButton>().First();var before=toggle.RenderSize;toggle.Focus();Flush(overview);toggle.IsChecked=true;Flush(overview);
                    Check(toggle.ActualHeight==14&&toggle.RenderSize==before,$"Evidence 14 DIP and stable focus {theme.Id}/{size.Name}/{scale}");toggle.IsChecked=false;
                }
                var root=(Visual)overview.Content;var footer=overview.Status.TransformToAncestor(root).TransformBounds(new(overview.Status.RenderSize));
                Check(footer.Bottom<=size.Height+1&&footer.Right<=size.Width+1,$"Footer remains accessible {theme.Id}/{size.Name}/{scale}");
                var setupBounds=overview.SetupToggle.TransformToAncestor(root).TransformBounds(new(overview.SetupToggle.RenderSize));Check(setupBounds.Top>=footer.Top-30&&setupBounds.Bottom<=size.Height,$"Bottom-left actions share footer {theme.Id}/{size.Name}/{scale}");
                geometry.Add(new{Theme=theme.Id,Scale=scale,size.Name,Footer=footer.ToString(),SessionHeight=overview.SessionPicker.ActualHeight});
                foreach(var tab in affectedRetry?new[]{TrackerTab.Pet}:new[]{TrackerTab.Gear,TrackerTab.Pet})
                {
                    tracker.ShowView(tab);tracker.Width=size.Width;tracker.Height=size.Height;Flush(tracker);tracker.Refresh();tracker.OverviewPolishControls(tab,true);Flush(tracker);
                    Save($"{theme.Id}-{tab.ToString().ToLowerInvariant()}-{size.Name}-{scale:0.0}x",(FrameworkElement)tracker.Content,size.Width,size.Height,scale);
                    Check(tracker.OverviewPolishControls(tab,false),$"{tab} filter widths, placement and 28-DIP controls {theme.Id}/{size.Name}/{scale} · {tracker.OverviewPolishControlFacts()}");
                }
                foreach(var mode in new[]{CombatLabMode.Damage,CombatLabMode.Healing})
                {
                    lab.Width=size.Width;lab.Height=size.Height;lab.ShowView(mode,CombatLabTab.Group);lab.Refresh();Flush(lab);
                    Save($"{theme.Id}-{mode.ToString().ToLowerInvariant()}-group-{size.Name}-{scale:0.0}x",(FrameworkElement)lab.Content,size.Width,size.Height,scale);
                    Check(Usability.Tree(lab).OfType<TextBlock>().Any(t=>t.Text=="RECENT GROUPS")&&Usability.Tree(lab).OfType<Button>().Any(b=>Equals(b.Content,"Song coverage")),$"Group heading retains coverage actions {mode}/{theme.Id}/{size.Name}/{scale}");
                }
            }
        }
        controller.SelectOverviewSession("missing-session");overview.Refresh();await overview.Pending;Check(overview.Snapshot==null&&overview.LootRows.Children.Count==0,"Missing session rejects stale rows and rating results");controller.SelectOverviewSession("");overview.Refresh();await overview.Pending;
        Json.Write(Path.Combine(folder,"overview-polish-checks.json"),new{Passed=failures.Count==0,Checks=checks,Failures=failures,Geometry=geometry,Synthetic=true,PhysicalMultiMonitor="Not exercised",LiveGameAcceptance="Not exercised"});
        overview.Close();tracker.Hide();lab.Hide();controller.Profile.Theme="coldain";Appearance.Apply("coldain");
        if(failures.Count>0)throw new InvalidOperationException(string.Join("\n",failures.Distinct()));
        void Save(string name,FrameworkElement element,double width,double height,double scale){var bitmap=PreviewRenderer.Capture(element,width,height,scale);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);Check(bitmap.PixelWidth==(int)Math.Ceiling(width*scale)&&bitmap.PixelHeight==(int)Math.Ceiling(height*scale),"Render size · "+name);}
    }
}

public sealed partial class QuestWindow
{
    internal bool OverviewPolishGearAccessibility()
    {
        gearPositive.Focus();gearPositive.BringIntoView();UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);var bounds=gearPositive.TransformToAncestor((Visual)Content).TransformBounds(new(gearPositive.RenderSize));
        return bounds.Bottom<=ActualHeight-8&&new[]{gearOwnership,gearEligibility,gearRankChoice,gearPositive}.All(c=>c.ActualHeight==28)&&gearSearch.Width==170;
    }
    internal string OverviewPolishControlFacts()=>$"Search {petSearch.Width}, slot {petSlot.ActualHeight}, parent {petSlot.Parent?.GetType().Name}, labels {string.Join(',',(petSlot.Parent as Grid)?.Children.OfType<TextBlock>().Select(t=>t.Text)??[])}";
    internal bool OverviewPolishControls(TrackerTab tab,bool expand)
    {
        if(tab==TrackerTab.Gear)
        {
            if(expand)foreach(var e in Usability.Tree(this).OfType<Expander>().Where(e=>Equals(e.Header,"Filter")))e.IsExpanded=true;
            return gearSearch.Width==170&&gearOwnership.Width==105&&gearEligibility.Width==105&&gearRankChoice.Width==79&&gearPositive.Width==113;
        }
        if(expand)foreach(var toggle in Usability.Tree(this).OfType<ToggleButton>().Where(t=>Equals(t.Content,"Filter")))toggle.IsChecked=true;
        var grid=petSlot.Parent as Grid;return petSearch.Width==170&&petSlot.ActualHeight==28&&grid!=null&&grid.Children.OfType<TextBlock>().Any(t=>t.Text=="SLOT");
    }
}
