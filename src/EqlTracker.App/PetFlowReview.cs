using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;
public sealed partial class QuestWindow
{
    internal async Task ReviewPetFlow(string folder)
    {
        await ReviewCompactPet(folder);
        var checks=new List<string>();var timings=new List<object>();
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
        void Flush(){UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        void Capture(string name){foreach(double scale in new[]{1d,1.5,2d,3d}){var watch=Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();var bitmap=PreviewRenderer.Capture((FrameworkElement)Content,Width,Height,scale);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(folder,"qa",name+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x.png"));encoder.Save(file);timings.Add(new{Name=name,Scale=scale,Milliseconds=watch.Elapsed.TotalMilliseconds,Bytes=GC.GetAllocatedBytesForCurrentThread()-allocated});}}
        ShowView(TrackerTab.Pet);Width=1400;Height=820;Flush();
        Check(petItems.Columns.Any(c=>c.Header?.ToString()=="Exalt")&&petItems.Columns.Any(c=>c.Header?.ToString()=="Rank"),"Inventory headers are Exalt and Rank");
        Check(!Usability.Tree(this).OfType<TextBlock>().Any(t=>t.IsVisible&&t.Text.Contains("Current build · manual inventory")),"Redundant inventory title removed");
        var filter=Usability.Tree(this).OfType<Expander>().Single(e=>e.IsVisible&&e.Header?.ToString()=="Filter");
        var details=Usability.Tree(this).OfType<Button>().Single(e=>e.Content?.ToString()=="Pet Details…");
        var weights=Usability.Tree(this).OfType<Button>().Single(b=>b.IsVisible&&b.Content?.ToString()=="Pet weights…");
        Check(Math.Abs(details.TranslatePoint(new(),this).Y-weights.TranslatePoint(new(),this).Y)<10,"Pet Details and Pet weights share toolbar row");
        var theory=petTheoryCheck.TranslatePoint(new(),this);var search=petSearch.TranslatePoint(new(),this);
        Check(theory.Y<search.Y&&theory.X>search.X,"Theorycraft is right-aligned on Search title row");
        filter.IsExpanded=true;Flush();Check(petSlot.IsVisible&&petRank.IsVisible&&petSlot.TranslatePoint(new(),this).Y>search.Y,"Expanded filters occupy their own row");filter.IsExpanded=false;
        var owner=Controller.Profile.ActiveLoadout;var build=SelectedPetInventory!;
        build.SlotConfigurations.Add(new(){Position=1,ObservedItemId=build.Items[0].Id,Effects=[new(){EffectId="review-focus",SourceName="Synthetic focus",Effect="Focus Effect: Clarity",State="Planned"},new(){EffectId="review-click",SourceName="Synthetic click",Effect="Click Effect: Gate",State="Planned"},new(){EffectId="review-worn",SourceName="Synthetic worn",Effect="Worn Effect: Endurance",State="Planned"},new(){EffectId="review-proc",SourceName="Synthetic proc",Effect="Proc: Earthquake",State="Planned"}]});
        petKey="";RefreshPet();Flush();var row=petItems.Items.OfType<PetItemRow>().Single(r=>r.Position==1);
        Check(row.Procs.Contains("Focus:")&&row.Procs.Contains("Click:")&&row.Procs.Contains("Worn:")&&row.Procs.Contains("Proc:"),"Exalt cell contains all populated kinds");
        Check(row.Procs.IndexOf("Proc:")<row.Procs.IndexOf("Focus:")&&row.Procs.IndexOf("Focus:")<row.Procs.IndexOf("Click:")&&row.Procs.IndexOf("Click:")<row.Procs.IndexOf("Worn:"),"Weapon Exalt effect order leads with Proc");
        Check(row.NameTooltip.Contains("Bloodmoon +4")&&row.NameTooltip.Contains("Weapon ratio:")&&row.NameTooltip.Contains("Adopted weapon model"),"Item-name tooltip includes full name, rank, ratio and evidence");
        bool BoundTooltip(Style? style)=>style!=null&&(style.Setters.OfType<Setter>().Any(s=>s.Property==ToolTipProperty&&s.Value is Binding b&&b.Path.Path=="NameTooltip")||BoundTooltip(style.BasedOn));
        var name=(DataGridTextColumn)petItems.Columns.Single(c=>c.SortMemberPath=="Name");Check(BoundTooltip(name.ElementStyle),"Name tooltip remains a live binding on recycled cells");
        owner.Classes=["BRD","SHD","MNK"]; // Synthetic owner route permits sash and both existing weapons; no eligibility bypass.
        petEditRevision++;petSessionKey="";petPreviewKey="";
        foreach(string theme in new[]{"Coldain","Sunstrider","Shadowman"})
        {
            Appearance.Apply(theme);Controller.Profile.Theme=theme;Width=1400;Height=820;Flush();
            var vertical=new ScrollBar{Orientation=Orientation.Vertical,Height=110,Maximum=100,ViewportSize=10};var horizontal=new ScrollBar{Orientation=Orientation.Horizontal,Width=110,Maximum=100,ViewportSize=10};
            var bars=new RatingPairReviewDialog(UI.Row(vertical,horizontal)){Owner=this,Left=-15000,Top=-15000,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};bars.Show();bars.UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            Check(vertical.ActualWidth==5&&horizontal.ActualHeight==5,"Native planning bars measure five DIP in both orientations: "+theme);bars.Close();
            foreach(var bar in Usability.Tree(petItems).OfType<ScrollBar>().Where(b=>b.IsVisible))Check((bar.Orientation==Orientation.Vertical?bar.ActualWidth:bar.ActualHeight)<=5.1,"Planning scrollbar is five DIP: "+theme);
            petSearch.Text="Golden Sash";petKey="";RefreshPet();Flush();
            var candidate=petResults.Items.OfType<PetSearchRow>().FirstOrDefault(r=>r.Item.Name=="Golden Sash of Tranquility");Check(candidate!=null,"Golden Sash remains discoverable");petResults.SelectedItem=candidate;ShowPetComparison();Flush();
            Check(!petComparison.ResetPreview.IsEnabled,"Reset disabled at catalog/list baseline: "+theme);
            petComparison.RankSlider.Value=1;ShowPetComparison();Flush();Check(petComparison.ResetPreview.IsEnabled,"Reset enabled for changed preview: "+theme);
            Check(!Usability.Tree(petComparison).OfType<TextBlock>().Any(t=>t.Text.StartsWith("Preview +")&&t.Text.Contains("list +")),"Redundant preview/list status removed: "+theme);
            Check(!Usability.Tree(petComparison).OfType<TextBlock>().Any(t=>t.Text.Contains("AC: AC")),"AC label appears once: "+theme);
            Check(Usability.Tree(petComparison).OfType<TextBlock>().Any(t=>t.Text.Contains("Monk Test of Tranquility")),"Verified quest acquisition shown offline: "+theme);
            Capture(theme+"-flow-rank");petComparison.ResetPreview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));ShowPetComparison();Flush();Check(!petComparison.ResetPreview.IsEnabled&&petComparison.RankSlider.Value==0,"Reset returns to +0 without editing inventory: "+theme);
            petRank.SelectedItem="4";petKey="";RefreshPet();Flush();petResults.SelectedItem=petResults.Items.OfType<PetSearchRow>().First(r=>r.Item.Name=="Golden Sash of Tranquility");ShowPetComparison();Flush();Check(!petComparison.ResetPreview.IsEnabled&&petComparison.RankSlider.Value==4,"Set Rank controls preview baseline: "+theme);
            petRank.SelectedIndex=0;petSearch.Text="";petKey="";RefreshPet();Flush();OpenPetDetails();Capture(theme+"-flow-details");ClosePetDetails();
            var ev=petResults.Items.OfType<PetSearchRow>().Single(r=>r.Item.Key=="evensong");petResults.SelectedItem=ev;ShowPetComparison();Flush();Check(!petComparison.ResetPreview.IsEnabled&&petComparison.RankSlider.Value==5,"Actual uses the recorded physical rank: "+theme);
            petComparison.RankSlider.Value=6;ShowPetComparison();Flush();Check(petComparison.ResetPreview.IsEnabled,"Recorded rank preview enables Reset: "+theme);petComparison.ResetPreview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));ShowPetComparison();Flush();Check(!petComparison.ResetPreview.IsEnabled&&petComparison.RankSlider.Value==5,"Reset restores recorded +5: "+theme);
            Width=680;Height=650;Flush();Capture(theme+"-flow-narrow");Check(petComparison.IsVisible,"Inspector remains available at narrow width: "+theme);
        }
        Check(build.Items[0].Rank==4&&build.Items[1].Rank==5,"Previews preserve recorded Pet inventory ranks");
        Json.Write(Path.Combine(folder,"pet-flow-checks.json"),new{Passed=checks.Count,Checks=checks,Timings=timings,Scope="Synthetic WPF routed interaction and render-scale coverage; physical DPI/live game separate"});
    }
}
