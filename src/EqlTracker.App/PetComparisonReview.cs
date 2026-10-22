using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewPetComparison(string folder,bool render)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Synthetic review only.");Directory.CreateDirectory(Path.Combine(folder,"qa"));var checks=new List<string>();
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        void Show(Window w){w.Left=w.Top=-15000;w.ShowActivated=false;w.Show();w.UpdateLayout();}
        void Capture(string name,Window w,double width,double height)
        {
            if(!render)return;foreach(double scale in new[]{1d,1.5,2d,3d}){var bitmap=PreviewRenderer.Capture((FrameworkElement)w.Content,width,height,scale);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(folder,"qa",name+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x.png"));encoder.Save(file);}
        }
        GearBuildFixtures.Apply(Controller);var owner=Controller.Profile.ActiveLoadout;owner.Classes=["MAG","NEC","BST"];owner.Level=50;
        var definition=Controller.Catalog.Find("Sunderfury")!;var armor=Controller.Catalog.Find("Indicolite Breastplate")!;
        var inventory=new PetBuild{Name="Synthetic intended inventory",PetType="Earth",SummonSpell="Greater Conjuration: Earth",SummonClass="MAG",SummonRank=10,PetLevel=50,Items=[new(){Id="fixture:weapon",Name=definition.Name,ItemKey=definition.Key,Position=1,Rank=4,Slot="Primary"},new(){Id="fixture:armor",Name=armor.Name,ItemKey=armor.Key,Position=2,Rank=5,Slot="Chest"},new(){Id="fixture:stored",Name="Cloak of Flames",ItemKey=Controller.Catalog.Find("Cloak of Flames")!.Key,Position=3,Rank=2,Slot="Storage"}]};
        owner.ConfirmedPetInventory=inventory;Controller.Save();var baseline=System.Text.Json.JsonSerializer.Serialize(inventory);
        var window=new PetComparisonWindow(Controller,inventory,()=>{}){Owner=this};Show(window);await window.LoadReferences();await window.Recalculate();window.StopReviewPolling();
        Check(window.Rows.Any(r=>r.Pet=="Air")&&window.Rows.Any(r=>r.Pet=="Warder")&&window.Rows.Any(r=>r.Pet=="Undead"),"Real comparison resolves Mage Necromancer Beastlord families");
        Check(window.Rows.All(r=>r.Outcome==null&&r.PredictedDps=="Unavailable"),"Incomplete real-data mechanics never fabricate DPS");
        Check(window.Rows.All(r=>r.Fits.Single(f=>f.Id=="fixture:stored").Equipped),"Current comparison reuses the Pet tab inferred equipment projection");
        Check(System.Text.Json.JsonSerializer.Serialize(inventory)==baseline,"Opening comparison preserves source inventory ranks and equipment");
        Check(Usability.Tree(window).OfType<Button>().Any(b=>b.Content?.ToString()=="Save as Pet plan…"),"Named-plan action is accessible without Current activation");
        Check(window.Results.Items.Cast<object>().OfType<PetComparisonResult>().Select(r=>r.Pet).Distinct().Count()==window.Rows.Count,"Stable rows retain distinct pet identities");
        window.Goal.SelectedItem="Balanced";await window.Recalculate();Check(window.Rows.All(r=>r.Scenario.Goal=="Balanced"&&!r.StrongTradeoff),"Balanced avoids dominance claims with missing pet baselines");
        window.Results.SelectedItem=window.Rows.First(r=>r.Pet=="Water");window.ShowSelected();Check(Usability.Tree(window.Details).OfType<Expander>().Any(e=>e.Header?.ToString()?.Contains("From behind")==true),"Water has an additional positional scenario without baseline backstab credit");
        window.Results.SelectedItem=window.Rows.First(r=>r.Pet=="Air");window.ShowSelected();Check(!Usability.Tree(window.Details).OfType<Expander>().Any(e=>e.Header?.ToString()?.Contains("From behind")==true),"Air does not receive a meaningless behind-position option");
        window.SetMode(PetEquipmentMode.Reassign);await window.Recalculate();Check(window.Rows.Any(r=>r.Equipment.Selections.Any(s=>s.EntryId=="fixture:stored")),"Explicit reassignment can preview the stored cloak");Check(System.Text.Json.JsonSerializer.Serialize(inventory)==baseline,"Reassignment remains isolated from Current");window.SetMode(PetEquipmentMode.IntendedSlots);await window.Recalculate();
        window.SetMode(PetEquipmentMode.Inventory);await window.Recalculate();Check(window.Rows.All(r=>r.Scenario.EquipmentMode==PetEquipmentMode.Inventory),"Inventory optimization uses completed revisioned snapshots");
        Check(window.Rows.All(r=>r.Equipment.Selections.Count<=PetEquipmentPlanner.Capacity(owner.Classes)),"Inventory optimized assignments stay within capacity");
        window.SetMode(PetEquipmentMode.BestPossible);await window.Recalculate();Check(window.Rows.All(r=>r.Plan.Items.All(i=>i.Rank==10)&&r.Plan.IsTheorycraft),"Best Possible stays hypothetical at catalog +10");
        window.SetMode(PetEquipmentMode.IntendedSlots);await window.Recalculate();
        var task1=window.Recalculate();window.Goal.SelectedItem="Tank";var task2=window.Recalculate();await Task.WhenAll(task1,task2);Check(window.Rows.All(r=>r.Scenario.Goal=="Tank"),"Obsolete comparison requests cannot overwrite the final goal");
        var record=EquipmentPositions.Copy(Controller.Profile);var weights=new PetGoalWeightsDialog(record,"Balanced"){Owner=this};Show(weights);Check(Usability.Tree(weights).OfType<TextBlock>().Any(t=>t.Text.Contains("no combined point score")),"Pet weight editor explains independent Balanced dimensions");Check(weights.Inputs.ContainsKey("Mana")&&weights.Inputs.ContainsKey("Endurance"),"Pet weight editor preserves resource and custom-stat editing");weights.Close();
        foreach(string theme in new[]{"coldain","sunstrider","shadowman"})
        {
            Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme));Appearance.Apply(theme);
            window.Width=1180;window.Height=820;window.Results.SelectedItem=window.Rows.First(r=>r.Pet=="Earth");window.ShowSelected();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            Capture(theme+"-comparison",window,1180,820);window.Width=660;Capture(theme+"-comparison-narrow",window,660,800);window.Width=1180;
            window.SetMode(PetEquipmentMode.Inventory);await window.Recalculate();Capture(theme+"-inventory-optimized",window,1180,820);
            window.SetMode(PetEquipmentMode.BestPossible);await window.Recalculate();Capture(theme+"-catalog-optimized",window,1180,820);
            window.SetMode(PetEquipmentMode.IntendedSlots);await window.Recalculate();
            window.Results.SelectedItem=window.Rows.First(r=>r.Pet=="Water");window.ShowSelected();var behind=Usability.Tree(window.Details).OfType<Expander>().First(e=>e.Header?.ToString()?.Contains("From behind")==true);window.SelectedDetails.IsExpanded=true;behind.IsExpanded=true;window.UpdateLayout();behind.BringIntoView();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Capture(theme+"-position",window,1180,820);window.SelectedDetails.IsExpanded=false;Usability.Tree(window).OfType<ScrollViewer>().First().ScrollToTop();
            var setup=new PetBuffSetupDialog(owner,PetBuffCatalog.References(),new(){Buffs=[new(){Id="reference:burnout3",Rank=0,Enabled=true},new(){Id="reference:augmentdeath",Rank=0,Enabled=true},new(){Id="reference:quickening",Enabled=true}]}){Owner=this};Show(setup);Capture(theme+"-buffs",setup,660,620);setup.Close();
            var editor=new PetGoalWeightsDialog(record,"Tank"){Owner=this};Show(editor);Capture(theme+"-weights",editor,740,620);editor.Close();
            var benchmark=new PetBenchmarkDialog(window.Rows.First(),[]){Owner=this};Show(benchmark);Capture(theme+"-benchmark",benchmark,650,620);benchmark.Close();
            var first=window.Rows.First();var named=PetComparisonEngine.SavePlan(first,"Synthetic saved review");Check(named.IsTheorycraft&&named.Id!=inventory.Id&&named.ComparisonScenario!=null,theme+" saved plan preserves unsupported assumptions without activating");
        }
        for(int n=1;n<=5;n++){var sample=new ItemDefinition{Name="Example wardplate "+n,Key="example wardplate "+n,Classes=["ALL"],Slots=["CHEST"],Stats=new(){["AC"]=40+n*15},MetadataComplete=true,IntroducedExpansion="Classic"};Controller.Catalog.Items.Add(sample);Controller.Inventory.Entries.Add(new(){Name=sample.Name,Location="Observed synthetic stock "+n,Count=1});}Controller.Catalog.InvalidateIndexes();Controller.Inventory.Invalidate();
        window.SetMode(PetEquipmentMode.IntendedSlots);await window.Recalculate();window.Results.SelectedItem=window.Rows.First(r=>r.Pet=="Earth");window.ShowSelected();window.SelectedDetails.IsExpanded=true;window.UpdateLayout();
        _=Dispatcher.BeginInvoke(new Action(()=>{var modal=Application.Current.Windows.OfType<PetBenchmarkDialog>().Last();modal.DpsInput.Text="123.4";modal.HpInput.Text="5000";modal.ConfirmedInput.IsChecked=true;Usability.Tree(modal).OfType<Button>().Single(b=>b.Content?.ToString()=="Save benchmark").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}),DispatcherPriority.Loaded);
        Usability.Tree(window.Details).OfType<Button>().Single(b=>b.Content?.ToString()=="Record benchmark…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await window.Recalculate();
        Check(window.Rows.First(r=>r.Pet=="Earth").DisplayDps==123.4&&window.Rows.First(r=>r.Pet=="Earth").Hp==5000,"Routed benchmark save populates only matching-context measured metrics");
        Check(window.Rows.Where(r=>r.Pet!="Earth").All(r=>r.DisplayDps==null),"Earth benchmark cannot fabricate values for other pet families");
        window.SelectedDetails.IsExpanded=false;
        await window.ReviewUpgrades(true);Check(window.UpgradeCount is >0 and <=5,"Whole-build rank helper shows at most five supported owned replacements");
        await window.ReviewUpgrades(false);Check(window.UpgradeCount<=5,"Selected-item rank helper remains bounded and isolated");await window.ReviewUpgrades(true);
        foreach(string theme in new[]{"coldain","sunstrider","shadowman"}){Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme));Appearance.Apply(theme);window.Width=1180;window.Height=820;Capture(theme+"-measured-card",window,1180,820);window.UpgradeDetails.IsExpanded=true;window.UpdateLayout();window.UpgradeDetails.BringIntoView();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Capture(theme+"-rank-upgrades",window,1180,820);window.UpgradeDetails.IsExpanded=false;Usability.Tree(window).OfType<ScrollViewer>().First().ScrollToTop();}
        int plansBefore=owner.PetBuilds.Count;
        _ = Dispatcher.BeginInvoke(new Action(()=>{var dialog=Application.Current.Windows.OfType<EntryDialog>().Last();Usability.Tree(dialog).OfType<TextBox>().First().Text="Synthetic routed plan";Usability.Tree(dialog).OfType<Button>().Single(b=>b.Content?.ToString()=="Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}),DispatcherPriority.Loaded);
        Usability.Tree(window).OfType<Button>().Single(b=>b.Content?.ToString()=="Save as Pet plan…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(owner.PetBuilds.Count==plansBefore+1&&owner.PetBuilds.Last().Name=="Synthetic routed plan","Routed save command commits only a new named Pet plan");Check(System.Text.Json.JsonSerializer.Serialize(inventory)==baseline,"Routed save leaves Current inventory and ranks byte-equivalent");window.Close();
        Controller.Profile.PetGoalWeights.Clear();owner.PetPreset="Balanced";ShowView(TrackerTab.Pet);Refresh();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        Check(petRoleChoice?.Items.Cast<string>().Take(3).SequenceEqual(PetRoleRatings.Goals)==true,"Pet tab exposes DPS Balanced Tank before custom goals");
        Check(Usability.Tree(Body).OfType<Button>().Any(b=>b.Content?.ToString()=="Compare pets…"),"Pet tab exposes Compare pets without an extra menu layer");
        petSearch.Text="Slime Blood";Refresh();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        petResults.SelectedItem=petResults.Items.Cast<object>().OfType<PetSearchRow>().FirstOrDefault();ShowPetComparison();
        Check(petResults.Items.Cast<object>().OfType<PetSearchRow>().Any(r=>r.DefenseDelta.HasValue&&r.Rating.Contains("Damage")&&r.Rating.Contains("Defense")),"Pet Balanced candidate rows show both preference dimensions");
        Check(Usability.Tree(petComparison).OfType<TextBlock>().Any(t=>t.Text=="Damage preference")&&Usability.Tree(petComparison).OfType<TextBlock>().Any(t=>t.Text=="Defense preference"),"Balanced inspector gives both dimensions equal summary cards");
        foreach(string theme in new[]{"coldain","sunstrider","shadowman"}){Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme));Appearance.Apply(theme);Width=1440;Height=900;Refresh();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Capture(theme+"-pet-goals",this,1440,900);}
        owner.Classes=["SHD","MAG","BRD"];owner.Preset="Balanced";Controller.Save();
        var blood=Controller.Catalog.Find("Bloodmoon")!;
        InventoryParser.Apply(Controller.Inventory,new(DateTime.Now.AddMinutes(1),blood.Name,"Looted",1,"Synthetic boss"),"synthetic:loot");
        Controller.Save();ShowView(TrackerTab.Pet);petOwnership.SelectedIndex=1;petSlot.SelectedIndex=0;petSearch.Text="Bloodmoon";petRank.SelectedItem="Default";petKey="";RefreshPet();
        Check(petResults.Items.OfType<PetSearchRow>().Any(r=>r.Item.Key==blood.Key),"Looted Bloodmoon appears in owned Pet candidates through owner-class access");
        petRank.SelectedItem="10";RefreshPet();Check(petResults.Items.OfType<PetSearchRow>().Where(r=>r.Item.Key==blood.Key).All(r=>r.Rank==10),"Explicit Pet list rank previews owned rows");
        var physical=Controller.Inventory.Evidence(blood).First(e=>!e.IsCollection&&e.Count>0);int savedRank=physical.Rank;
        ShowView(TrackerTab.Gear);gearSearch.Text="Bloodmoon";gearRankChoice.SelectedIndex=11;gearOwnership.SelectedIndex=0;gearEligibility.SelectedIndex=0;gearPositive.SelectedIndex=0;GearSelectedPosition="*";gearFingerprint=null;RefreshGear();
        Check(gearTable.Items.OfType<GearRow>().Any(r=>r.Item.Key==blood.Key&&r.Rank==10),"Gear rank dropdown updates searched owned Bloodmoon candidates");
        var gearRow=gearTable.Items.OfType<GearRow>().First(r=>r.Item.Key==blood.Key);gearTable.SelectedItem=gearRow;ShowSelectedGear();
        Check(GearInspectedRank(gearRow)==10&&physical.Rank==savedRank,"Inspector honors list preview without altering physical rank");
        Check(Usability.Tree(gearInspector).OfType<TextBlock>().Any(t=>t.Text=="Obtain From"),"Shared item inspector includes acquisition information");
        int popupRank=0;ItemPairComparison Pair()=>ItemPairComparison.Create(null,null,blood,popupRank,physical,owner,Controller.Catalog,false);
        var livePair=new PetPairComparisonView(Pair(),Pair,()=>popupRank.ToString());var pairHost=new FrostWindow(Controller,"Synthetic live comparison","",750,600){Owner=this};pairHost.Body.Children.Add(UI.Scroll(livePair));Show(pairHost);livePair.RefreshNow();var cardBefore=Usability.Tree(livePair).OfType<ClassicItemCard>().Single();popupRank=10;livePair.RefreshNow();Check(ReferenceEquals(cardBefore,Usability.Tree(livePair).OfType<ClassicItemCard>().Single())&&Usability.Tree(livePair).OfType<TextBlock>().Any(t=>t.Text=="Bloodmoon +10"),"Open comparison updates preview rank in place without rebuilding its card");pairHost.Close();

        foreach(string theme in new[]{"coldain","sunstrider","shadowman"})
        {
            Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme));Appearance.Apply(theme);Width=1440;Height=900;ShowView(TrackerTab.Gear);Refresh();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Capture(theme+"-gear-live",this,1440,900);
            var formula=new RatingFormulaDialog("Balanced",SelfRoleRatings.Resolve(Controller.Profile,owner,"Balanced").Weights,gearRow.Comparison,false,gearRow.Name){Owner=this};Show(formula);Capture(theme+"-formula",formula,740,700);formula.Close();
            var pair=ItemPairComparison.Create(null,null,blood,10,physical,owner,Controller.Catalog,false);var comparison=new FrostWindow(Controller,"Item comparison","",750,600){Owner=this};comparison.Body.Children.Add(UI.Scroll(new PetPairComparisonView(pair)));Show(comparison);Capture(theme+"-item-sources",comparison,750,600);comparison.Close();
        }
        petRank.SelectedItem="Default";ShowView(TrackerTab.Progress);progressAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(progressAll.IsChecked==true&&!progressTracked.IsChecked.GetValueOrDefault(),"All Progress choice is exclusive without tracking every quest");progressTracked.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(progressTracked.IsChecked==true&&!progressAll.IsChecked.GetValueOrDefault(),"Tracked Progress choice remains mutually exclusive");
        Json.Write(Path.Combine(folder,"pet-comparison-checks.json"),new{Passed=checks.Count,Checks=checks,Performance=PetComparisonWindow.Timings,Diagnostics=UpdateDiagnostics.Drain(),SyntheticOnly=true,NoLiveGameOrPhysicalDpi=true});
    }
}
