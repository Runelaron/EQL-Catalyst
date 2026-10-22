using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewCorrectPetRatings(string folder)
    {
        if (!Controller.Demo) throw new InvalidOperationException("Synthetic review only.");
        Directory.CreateDirectory(Path.Combine(folder,"qa"));var checks=new List<string>();var timings=new List<object>();
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
        void Flush(){UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
        string[] Texts(DependencyObject view)=>Usability.Tree(view).OfType<TextBlock>().Select(t=>t.Text).ToArray();
        void Capture(string name,Window window,double width,double height)
        {
            window.Width=width;window.Height=height;window.UpdateLayout();
            foreach(double scale in new[]{1d,1.5,2d,3d}){var clock=Stopwatch.StartNew();var bitmap=PreviewRenderer.Capture((FrameworkElement)window.Content,width,height,scale);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(folder,"qa",name+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x.png")))encoder.Save(output);timings.Add(new{Name=name,Scale=scale,Milliseconds=clock.Elapsed.TotalMilliseconds});}
        }
        GearBuildFixtures.Apply(Controller);var owner=Controller.Profile.ActiveLoadout;owner.Classes=["MAG","BRD","SHD"];owner.Level=50;owner.PetType="Earth";owner.PetLevel=50;owner.PetPreset="DPS";Controller.Profile.CurrentClasses=["MAG","BRD","SHD"];SelfRoleRatings.Apply(Controller.Profile);
        var blood=Controller.Catalog.Find("Bloodmoon")!;var evensong=Controller.Catalog.Find("Evensong")!;var whip=Controller.Catalog.Find("Revultant Whip")!;
        var build=new PetBuild{Preset="DPS",Name="Current build",PetType="Earth",PetLevel=50,SlotConfigurationVersion=PetSlotPlanning.Version,DualWield=true,Items=[new(){Id="primary",Position=1,Slot="Storage",ItemKey=blood.Key,Name=blood.Name,Rank=4},new(){Id="secondary",Position=2,Slot="Storage",ItemKey=evensong.Key,Name=evensong.Name,Rank=5}]};
        owner.ConfirmedPetInventory=build;Controller.Save();ShowView(TrackerTab.Pet);RefreshBuildChoices("current");petSearch.Text="";RefreshPet();Flush();
        var same=petResults.Items.OfType<PetSearchRow>().Single(r=>r.Item.Key==evensong.Key);
        Check(same.Comparison?.Slot=="Secondary"&&same.Comparison.Evaluation?.Unbuffed.Delta==0,"Best compatible recognizes the equipped Evensong and reports zero");
        petPreferredPosition="SECONDARY:1";petRank.SelectedItem="4";petKey="";RefreshPet();var replacement=petResults.Items.OfType<PetSearchRow>().Single(r=>r.Item.Key==whip.Key);petResults.SelectedItem=replacement;ShowPetComparison();Flush();
        Check(Math.Abs((replacement.Comparison?.Evaluation?.Unbuffed.Delta??999)+9.5)<1e-9,"Whip +4 versus Evensong +5 reconciles to -9.5");
        var equipment=PetEquipmentInference.Build(build,owner,Controller.Catalog);var evaluator=new LoadoutEvaluationSession(Controller.Catalog,owner,true,new(GearScorer.Defaults["Pet DPS"]),equipment.Positions,equipment.Positions,petInventory:build,effectivePetEquipment:equipment);
        for(int rank=0;rank<=10;rank++){var clock=Stopwatch.StartNew();var value=evaluator.Compare(whip,rank,"SECONDARY:1");timings.Add(new{Name="rank-preview-"+rank,Milliseconds=clock.Elapsed.TotalMilliseconds,Status=value.Status});}
        previewAsyncForReview=true;ChangePetPreviewRank(5);ShowPetComparison();ChangePetPreviewRank(4);ShowPetComparison();await Task.Delay(120);Flush();previewAsyncForReview=false;
        Check(petDisplayedComparison?.Candidate==whip.Name+" +4"&&Math.Abs((petDisplayedComparison.Evaluation?.Unbuffed.Delta??999)+9.5)<1e-9,"Rapid preview changes reject the obsolete unverified +5 result");
        var pair=ItemPairComparison.Create(evensong,build.Items[1].Entry(),whip,4,null,owner,Controller.Catalog);
        Check(pair.Current.Stats["Ratio"]==.64&&pair.Candidate.Stats["Ratio"]==.5,"Raw comparison shares the observed weapon ratios");
        var grouped=new BuildComparisonView();var changes=new List<BuildContribution>{new("DEX",9,0,9,9,.5,false,""),new("Ratio",.14,0,.14,.14,100,false,""),new("ResistDisease",9,0,9,9,0,false,""),new("ResistPoison",9,0,9,9,0,false,"")};
        var context=new ContextRating{Changes=changes,CurrentScore=0,CandidateScore=18.5};grouped.Show(new("Example","Secondary","Example",18.5,0,18.5,"","","Eligible",""){Evaluation=new(){Pet=true,Unbuffed=context}},"DPS");
        var lines=Texts(grouped);Check(Array.FindIndex(lines,t=>t.StartsWith("Weapons:"))<Array.FindIndex(lines,t=>t.StartsWith("Stats:")),"Weapon ratio has a separate line above Stats");
        Check(lines.Any(t=>t=="Resists: Disease +9 · Poison +9"),"Grouped resists omit the repeated resist suffix");
        var current=replacement.Comparison!.Evaluation!;Check(current.WeaponNote.StartsWith("Current: Primary")&&current.WeaponNote.Contains("Secondary 0.64")&&!current.WeaponNote.Contains("planned"),"Inspector current ratios contain active hands only");
        var card=new ClassicItemCard(whip,4);Check(Texts(card).Any(t=>t.Contains("DMG: 7")&&t.Contains("Ratio: 0.5"))&&Texts(card).Any(t=>t.Contains("WT: 0.7")),"Classic card displays observed damage ratio and weight");
        Check(Texts(card).Any(t=>t.Contains("SV VOID: +4"))&&Texts(card).Any(t=>t.Contains("Accuracy: +10%")),"Observed Void and accuracy stay visible without invented conversions");
        var unknown=new ClassicItemCard(whip,5);Check(Texts(unknown).Any(t=>t.Contains("Conditional")),"Unverified ranked weapons remain visibly Conditional");
        Controller.Profile.QuestTracking["brd-epic"]=true;Controller.Save();ShowView(TrackerTab.Progress,"Bard Epic Quest");RefreshSteps();Flush();
        string firstInstruction=Controller.Catalog.Quests.First(q=>q.Id=="brd-epic").Steps.First(s=>s.Instructions.Contains("Konia Swiftfoot")).Instructions;
        var rootText=Texts(howTo);Check(rootText.Count(t=>QuestPresentation.Normalize(t)==QuestPresentation.Normalize(firstInstruction))==1,"Bard Epic root instruction occurs once instead of as heading plus body");
        var quest=Controller.Catalog.Quests.First(q=>q.Id=="brd-epic");foreach(var step in quest.Steps){var parts=QuestPresentation.Instruction(quest,step);Check(parts.Heading.Length==0||QuestPresentation.Normalize(parts.Heading)!=QuestPresentation.Normalize(parts.Instruction),"Distinct instruction presentation: "+step.Id);}
        var rootNode=selectedStep;var child=progressNodes.SelectMany(n=>n.Descendants()).FirstOrDefault(n=>n.Node.Kind!=QuestNodeKind.Quest&&n.Node.Name.Contains("Torch of Misty"));
        for(int themeIndex=0;themeIndex<3;themeIndex++)
        {
            string theme=new[]{"coldain","sunstrider","shadowman"}[themeIndex];Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme));Appearance.Apply(theme);
            ShowView(TrackerTab.Progress,"Bard Epic Quest");selectedStep=rootNode;ShowProgressDetails();Flush();Capture(theme+"-progress",this,1220,800);Capture(theme+"-progress-narrow",this,760,720);
            if(child!=null){selectedStep=child.Node;ShowProgressDetails();Check(Texts(howTo).Count(t=>QuestPresentation.Normalize(t)==QuestPresentation.Normalize(child.Node.Name))==1,"Substep title appears once in "+theme);}
            ShowView(TrackerTab.Pet);petResults.SelectedItem=petResults.Items.OfType<PetSearchRow>().First(r=>r.Item.Key==whip.Key);ShowPetComparison();Flush();Capture(theme+"-pet",this,1400,820);Capture(theme+"-pet-narrow",this,820,720);
            var formula=new RatingFormulaDialog("DPS",new(GearScorer.Defaults["Pet DPS"]),replacement.Comparison,true,whip.Name+" +4"){Owner=this,Left=-15000,Top=-15000,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};formula.Show();formula.UpdateLayout();Check(Texts(formula).Any(t=>t.Contains("= -9.5 points")),"Formula final equation reconciles in "+theme);Capture(theme+"-formula",formula,820,720);Capture(theme+"-formula-narrow",formula,500,680);formula.Close();
            var raw=new RatingPairReviewDialog(new PetPairComparisonView(pair)){Owner=this,Left=-15000,Top=-15000,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};raw.Show();raw.UpdateLayout();Capture(theme+"-pair",raw,850,620);raw.Close();
            var compare=new PetComparisonWindow(Controller,EquipmentPositions.Copy(build),()=>{}){Owner=this,Left=-15000,Top=-15000,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};compare.Show();await compare.Recalculate();for(int wait=0;wait<100&&compare.Results.Items.Count==0;wait++)await Task.Delay(25);compare.StopReviewPolling();Check(compare.Results.Items.Count>0,"Comparison cards populated in "+theme);Capture(theme+"-pet-cards",compare,1180,800);compare.Close();
        }
        Check(build.Items[0].Rank==4&&build.Items[1].Rank==5,"Review and rank calculations leave recorded build ranks unchanged");
        Json.Write(Path.Combine(folder,"correct-pet-checks.json"),new{Passed=checks.Count,Checks=checks,Timings=timings,RankedStats=RankedItemStats.Version,Scope="Synthetic WPF assertions and 100/150/200/300 percent renders across three themes. Physical DPI and live game are separate."});
    }
    private sealed class RatingPairReviewDialog : FrostDialog
    {
        internal RatingPairReviewDialog(UIElement content):base("Item comparison",850,620){Editor.Children.Add(content);}
    }
}
