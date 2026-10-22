using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class QuestWindow
{
    internal void ReviewGearControlsFinal(string folder,bool render=true)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Isolated preview required");
        var checks=new List<string>();void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks.Add(label);}
        void Capture(string name,FrameworkElement component,double width,double height){if(!render){component.Measure(new(width,height));component.Arrange(new(0,0,width,height));component.UpdateLayout();return;}foreach(double scale in new[]{1d,1.5,2,3}){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(component,width,height,scale)));using var output=File.Create(Path.Combine(folder,"qa",$"gear-controls-{name}-{scale:0.0}x.png"));encoder.Save(output);}}
        var prompt=new CurrentBuildBackupDialog("Raid build","Before raid",true);Capture("backup-prompt",(FrameworkElement)prompt.Content,520,230);
        var text=Usability.Tree((DependencyObject)prompt.Content).OfType<TextBlock>().ToList();
        Check(text.Any(t=>t.Text.StartsWith("Current changed")),"Refreshed backup prompt explains that Current changed");
        Check(Usability.Tree((DependencyObject)prompt.Content).OfType<Button>().Single(b=>b.Content?.ToString()=="Save & use build").IsEnabled,"Named backup enables Save & use build");
        var targets=new[]{new GearPlacementTarget("PRIMARY:1","Primary","Bloodmoon",true,"Eligible",[],[]),new GearPlacementTarget("SECONDARY:1","Secondary","Empty",true,"Eligible",[],[])};
        var picker=new GearSlotPickerDialog("Sword +7",targets);Capture("slot-picker",(FrameworkElement)picker.Content,500,330);
        var choices=Usability.Tree((DependencyObject)picker.Content).OfType<ListBox>().Single();choices.SelectedIndex=1;
        Check(picker.PositionId=="SECONDARY:1"&&Usability.Tree((DependencyObject)picker.Content).OfType<Button>().Single(b=>b.Content?.ToString()=="Add to build").IsEnabled,"Picker retains explicit destination identity and enables Add");
        var heading=Usability.Tree((DependencyObject)picker.Content).OfType<TextBlock>().Single(t=>t.Text=="Sword +7");Check(heading.TranslatePoint(new(),(UIElement)picker.Content).Y<choices.TranslatePoint(new(),(UIElement)picker.Content).Y,"Picker title and choices occupy separate layout rows");
        var changes=new List<BuildContribution>();foreach(var (stat,delta) in new[]{("AC",5d),("STR",3d),("DEX",-2d),("HP",20d),("Mana",-10d),("ResistFire",4d),("ResistMagic",-3d)})changes.Add(new(stat,delta,10,10+delta,delta,1,false,"Illustrative presentation fixture"));
        ContextRating Context(string name)=>new(){Name=name,CurrentScore=100,CandidateScore=117,Changes=changes,Quality="Illustrative component fixture · not measured character data"};
        var result=new LoadoutComparison{Pet=false,Unbuffed=Context("Unbuffed"),Buffed=Context("Usual buffs")};var comparison=new GearComparison("Candidate","Chest","Current",117,100,17,"","","Eligible","Fixture"){Evaluation=result};var view=new BuildComparisonView();view.Show(comparison,"Balanced Tank");Capture("categorized-changes",view,340,660);
        var lines=Usability.Tree(view).OfType<TextBlock>().Where(t=>t.Visibility==Visibility.Visible).Select(t=>t.Text).Where(t=>ItemStatGroups.Categories.Any(c=>t.StartsWith(c+":"))).ToList();Check(lines.Select(l=>l.Split(':')[0]).SequenceEqual(new[]{"AC","Resources","Stats","Resists","Resources","Stats","Resists"}),"Gains and losses each follow AC Resources Stats Resists order");Check(lines.Any(l=>l.Contains("STR +3"))&&lines.Any(l=>l.Contains("DEX -2"))&&lines.Any(l=>l.Contains("HP +20"))&&lines.Any(l=>l.Contains("Magic -3")),"Signed gains and losses remain exact within each category");
        GearBuildFixtures.Apply(Controller);ShowView(TrackerTab.Gear);Width=1220;Height=740;Refresh();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();
        Capture("gear",(FrameworkElement)Content,1220,740);Capture("gear-narrow",(FrameworkElement)Content,720,470);Width=1220;Height=740;UpdateLayout();
        var info=Usability.Tree((DependencyObject)preset.Parent).OfType<Button>().First();Check(info.ActualHeight==20&&info.ActualWidth==20&&info.Margin==new Thickness(0),"Role information glyph fits its label row without raised-button clipping");
        Check(AdornerLayer.GetAdornerLayer((UIElement)Content)!=null,"Pointer glyph has a window-local adorner layer");
        Check(!ItemDragFeedback.ShouldCancel(false,new(1),new(1),true)&&ItemDragFeedback.ShouldCancel(true,new(1),new(1),true)&&ItemDragFeedback.ShouldCancel(false,new(1),new(2),true)&&ItemDragFeedback.ShouldCancel(false,new(1),new(1),false),"Escape foreground and workspace changes cancel drag feedback");
        var item=new ItemDefinition{Key="drag review",Name="Sword",Classes=["ALL"],Slots=["PRIMARY","SECONDARY"],MetadataComplete=true,Display=new(){Skill="1H Slashing"}};
        gearDragTargets=PrepareGearPlacement(new("Sword","Primary","",0,"","","",""){Item=item,Rank=7}).ToDictionary(r=>r.Target.Id);
        Usability.Tree(gearEquipment).OfType<ScrollViewer>().First().ScrollToBottom();UpdateLayout();foreach(var visual in Usability.Tree(gearEquipment).OfType<DataGridRow>())GearDragRows.State(visual,visual.Item is EquipmentPosition p&&gearDragTargets.GetValueOrDefault(p.Id)?.Target.Allowed==true?2:1);
        Capture("drag-guidance",(FrameworkElement)Content,1220,740);gearDragTargets=null;foreach(var visual in Usability.Tree(gearEquipment).OfType<DataGridRow>())GearDragRows.State(visual,0);
        Check(!Usability.Tree((DependencyObject)Content).OfType<Button>().Any(b=>b.Content?.ToString()=="Workspace ▾"),"Workspace actions have moved to Settings menu");
        foreach(var tab in new[]{TrackerTab.Queue,TrackerTab.Items,TrackerTab.Quests}){ShowView(tab);Refresh();UpdateLayout();string label=tab==TrackerTab.Queue?"NEXT ACTION & SOURCES":tab==TrackerTab.Items?"ITEM":"QUEST";var titles=Usability.Tree((DependencyObject)Content).OfType<TextBlock>().Where(t=>t.Text==label&&t.IsVisible).ToList();Check(titles.Count==1,tab+" uses its concise floating inspector heading");}
        var settings=new SettingsWindow(Controller){ShowActivated=false,Left=-15000,Top=-15000};settings.Show();settings.SelectSection(SettingsSection.Sync);settings.UpdateLayout();var modes=Usability.Tree(settings).OfType<ComboBox>().Single(c=>c.Items.Contains("Daily"));modes.SelectedItem="Manual";Check(Controller.Profile.WikiRefresh==WikiRefreshMode.Manual,"Wiki refresh mode saves immediately per character");modes.SelectedItem="Daily";settings.Close();
        Json.Write(Path.Combine(folder,"gear-controls-final-checks.json"),new{Passed=checks.Count,Checks=checks,AppSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(QuestWindow).Assembly.Location))).ToLowerInvariant(),CoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(GearPlacementSession).Assembly.Location))).ToLowerInvariant(),Scope="Only repaired dialogs, drag guidance and newly added stat/wiki/header behavior; unchanged tab renders retained"});
    }
}
