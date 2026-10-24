using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewLocalIteration(string folder, OverlayWindow overlay)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Synthetic smoke check only");
        Directory.CreateDirectory(folder);var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        void Flush(){UpdateLayout();overlay.UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
        void Capture(string name,FrameworkElement element,double width,double height){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(element,width,height,1)));using var file=File.Create(Path.Combine(folder,name+".png"));encoder.Save(file);}
        GearBuildFixtures.Apply(Controller);var owner=Controller.Profile.ActiveLoadout;owner.Classes=["MNK","MAG","SHD"];owner.Level=50;
        owner.ConfirmedPetInventory=new(){PetType="Earth",PetLevel=50,SlotConfigurationVersion=PetSlotPlanning.Version};
        ShowView(TrackerTab.Pet);RefreshBuildChoices("current");petRank.SelectedIndex=0;petSearch.Text="Wu's Fist of Mastery";petKey="";RefreshPet();Flush();
        petResults.SelectedItem=petResults.Items.OfType<PetSearchRow>().First(r=>r.Item.Key=="wu's fist of mastery");ShowPetComparison();Flush();
        string inventory=JsonSerializer.Serialize(SelectedPetInventory);
        Check(!petComparison.ResetPreview.IsEnabled,"Catalog +0 starts with reset disabled");
        petComparison.RankSlider.Value=4;await Task.Delay(100);Flush();
        Check(petDisplayedComparison?.Evaluation?.Status!="Conditional"&&petDisplayedComparison?.Evaluation?.Unbuffed.Delta.HasValue==true,"Live Wu slider produces a numeric rating");
        var facts=Usability.Tree(petComparison).OfType<TextBlock>().Select(t=>t.Text).ToArray();
        Check(facts.Any(t=>t.Contains("DMG: 22")&&t.Contains("Ratio: 1")),"Card and rating share Wu +4 inputs");
        Check(facts.Any(t=>t.Contains("WT (base): 0.5"))&&!facts.Any(t=>t.Contains("catalog +0; upgraded weight unverified")||t.Contains("Conditional · ranked weapon")),"Card uses concise base-weight label without blanket rank warnings");
        Check(petComparison.ResetPreview.IsEnabled,"Moved slider enables reset");
        petComparison.ResetPreview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);Flush();
        Check(petComparison.RankSlider.Value==0&&!petComparison.ResetPreview.IsEnabled,"Reset returns to catalog +0 baseline");
        Check(inventory==JsonSerializer.Serialize(SelectedPetInventory),"Slider and reset leave inventory unchanged");
        foreach(double width in new[]{915d,640d})
        {
            Width=width;Height=650;Flush();
            Check(!Usability.Tree(this).OfType<TextBlock>().Any(t=>t.IsVisible&&(t.Text.Contains("inventory positions")||t.Text.Contains("equipment notes"))),"Inventory count row absent at width "+width);
            Check(petItems.ActualHeight>200&&petTotals.IsVisible,"Inventory fills available height and keeps totals at width "+width);
        }
        PetMessage("Synthetic actionable error",true);Check(petFeedback.IsVisible&&petFeedback.Text.Contains("actionable"),"Actionable errors retain visible feedback");PetMessage("");
        Width=915;petComparison.RankSlider.Value=4;await Task.Delay(100);Flush();Capture("pet-local",(FrameworkElement)Content,915,650);
        var selector=Usability.Tree(overlay).OfType<Button>().Single(b=>Equals(b.Tag,"overlay-title-display-selector"));
        foreach(var (width,height,buttonWidth,iconWidth) in new[]{(440d,300d,24d,20d),(128d,72d,20d,14d)})
        {
            overlay.SetMeterSize(width,height);Flush();
            Check(selector.Content is ActorBadge icon&&icon.Width==iconWidth&&Math.Abs(selector.ActualWidth-buttonWidth)<.1,"Arrowless centered selector sizing at width "+width);
        }
        selector.ContextMenu!.Opacity=0;selector.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(selector.ContextMenu.IsOpen&&selector.ContextMenu.Items.OfType<MenuItem>().Count()==4,"Arrowless selector retains four direct choices");selector.ContextMenu.IsOpen=false;Flush();
        Capture("dps-local",(FrameworkElement)overlay.Content,128,72);
        string Sha(string path)=>Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        Json.Write(Path.Combine(folder,"local-smoke-checks.json"),new{Passed=checks.Count,Checks=checks,AppSha256=Sha(typeof(App).Assembly.Location),CoreSha256=Sha(typeof(CharacterProfile).Assembly.Location),Scope="Focused synthetic smoke only; broad regressions and theme/DPI review deferred pending user confirmation."});
    }
}
