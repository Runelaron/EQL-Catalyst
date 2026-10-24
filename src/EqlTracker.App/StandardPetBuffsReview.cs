using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewStandardPetBuffs(string folder)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Synthetic preview required.");
        Directory.CreateDirectory(folder);var checks=new List<string>();var clock=System.Diagnostics.Stopwatch.StartNew();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        void Flush(){Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
        var build=new PetBuild{PetType="Monster",SummonSpell="Monster Summoning II",SummonRank=9,SummonRankKnown=true,PetLevel=50};var owner=new LoadoutProfile{Classes=["MAG"],Level=50};var catalog=new Catalog();
        string fingerprint=EquipmentPositions.Fingerprint(PetDetailsStatistics.Project(build,owner,catalog).Equipment.Positions);
        var baseline=new PetStatSheet{Id="synthetic-base",Label="Synthetic unbuffed capture",Reviewed=true,CapturedAt=new(2026,10,23,9,0,0,TimeSpan.FromHours(-7)),Context=new(){Pet="Monster",Summon=build.SummonSpell,Rank=9,Level=50,Equipment=fingerprint,Patch=PetDefinition.Version,Buffs="Unbuffed",Complete=true,FocusAndAA="None"},Values=new(){["HP"]=8000,["HPCurrent"]=8000,["Mana"]=0,["ManaCurrent"]=0,["STR"]=200,["STA"]=150,["INT"]=50,["WIS"]=50,["AGI"]=85,["DEX"]=100,["CHA"]=20,["ACLeft"]=400,["ACRight"]=500,["AttackLeft"]=300,["AttackRight"]=310,["AttackSpeedPercent"]=140,["HPRegen"]=1,["ManaRegen"]=4}};
        var buffed=EquipmentPositions.Copy(baseline);buffed.Id="synthetic-buffed";buffed.Label="Synthetic Burnout capture";buffed.Context.Buffs="Burnout III +4";buffed.Values["STR"]=254;buffed.Values["ACLeft"]=404;buffed.Values["AttackLeft"]=336;buffed.Values["AttackSpeedPercent"]=171;
        var research=new PetResearchState{Sheets=[baseline,buffed]};var setup=new StandardPetBuffState{Buffs=[new(){Spell="Burnout III",Rank=4,Evidence="Synthetic confirmed landing"}],Pairs=[new(){Baseline=baseline.Id,Buffed=buffed.Id,Spell="Burnout III",Rank=4,SpeedCeiling=171,Evidence="Synthetic reviewed pair"}]};
        var popup=new PetDetailsPopup("Pet Details",UI.Text("Monster Summoning II · Rank 9 · Level Auto 50",12),UI.Text("Synthetic summon context",11)){Owner=this,Left=-15000,Top=-15000,ShowActivated=false};popup.Show();popup.Sheet.SetContext(build,"Example pet");
        popup.QueueStatistics(build,owner,catalog,research,()=>true,standard:setup);
        for(int i=0;i<100&&!popup.Sheet.HasResult;i++){await Task.Delay(30);Flush();}
        Check(popup.Sheet.HasResult,"Revisioned statistics completed");Check(popup.Sheet.Standard.IsChecked!=true&&popup.Sheet.Added.IsChecked!=true,"Both presentation checkboxes start unchecked");
        Check(popup.Sheet.Values["STR"].Text=="200"&&popup.Sheet.Values["HP"].Text=="8000 / 8000","Unchecked view prefers the unbuffed equipped snapshot");
        popup.Sheet.Standard.Focus();popup.Sheet.Standard.IsChecked=true;Flush();Check(popup.Sheet.Values["STR"].Text=="254"&&popup.Sheet.Values["ACLeft"].Text=="404 | 500","Checking standard buffs displays the reviewed paired changes");Check(popup.Sheet.Values["AttackSpeedPercent"].Text.Contains("Capped"),"Cap hint stays inline with the stat");
        popup.Sheet.Added.IsChecked=true;Flush();Check(popup.Sheet.Values["STR"].Text=="254 (+0)","Added remains gear-only and does not repeat the buff bonus");
        string before=PetResearch.Hash(build);popup.Sheet.Standard.IsChecked=false;Flush();Check(popup.Sheet.Values["STR"].Text=="200 (+0)"&&PetResearch.Hash(build)==before,"Presentation toggles preserve the build and restore the captured baseline");
        popup.Sheet.Added.IsChecked=false;popup.Sheet.Standard.IsChecked=true;
        foreach(var (width,height,name) in new[]{(740d,800d,"normal"),(500d,600d,"minimum")})
        {popup.Width=width;popup.Height=height;popup.UpdateLayout();Flush();var image=PreviewRenderer.Capture((FrameworkElement)popup.Content,width,height,1);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(Path.Combine(folder,"standard-buffs-"+name+".png"));encoder.Save(output);Check(popup.Sheet.Standard.ActualWidth>0&&popup.Sheet.ReviewBuffs.IsVisible,"Checkbox and review action remain accessible at "+name+" width");}
        setup.Buffs[0].Rank=5;popup.Sheet.SetStandardContext(build,setup,research);popup.Sheet.Present(PetDetailsStatistics.Project(build,owner,catalog,research));Check(popup.Sheet.Values["STR"].Text=="255"&&popup.Sheet.Values["ACLeft"].Text.Contains("Baseline"),"Uncaptured rank uses scoped estimates and labels unresolved conversions");
        popup.RequireSelection();popup.QueueStatistics(build,owner,catalog,research,()=>true,standard:setup);popup.QueueStatistics(build,owner,catalog,research,()=>false,standard:setup);await Task.Delay(100);Flush();Check(!popup.Sheet.HasResult,"Obsolete statistics requests cannot publish");popup.Close();
        Json.Write(Path.Combine(folder,"standard-buffs-checks.json"),new{Passed=checks.Count,Checks=checks,PrivateResearch=TrackerController.PetResearchEnabled,Milliseconds=clock.Elapsed.TotalMilliseconds,AppSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),CoreSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(Catalog).Assembly.Location)))});
    }
}
