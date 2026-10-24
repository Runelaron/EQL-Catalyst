using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class PetResultCard : Border
{
    public PetResultCard()
    {
        Width = 268; MinHeight = 190; VerticalAlignment = VerticalAlignment.Stretch;
        DataContextChanged += (_, _) => Render();
        SetResourceReference(BackgroundProperty,"DetailSurface"); SetResourceReference(BorderBrushProperty,"Border");
        BorderThickness=new(1);Padding=new(9);Margin=new(0,0,8,6);CornerRadius=new(4);
    }
    private void Render()
    {
        if(DataContext is not PetComparisonResult row){Child=null;return;}
        string N(double? n,string format)=>n?.ToString(format,CultureInfo.CurrentCulture)??"Unknown";
        string Delta(double? n)=>n?.ToString("+0.0;-0.0;0",CultureInfo.CurrentCulture)??"Unknown";
        var badge=new ActorBadge{Width=28,Height=28};badge.Set("Pet",true,row.Pet,row.Pet);
        var title=UI.Row(badge,UI.Text("Pet Type: "+row.Pet,15,"Accent",true));
        var numbers=new StackPanel();
        var measured=row.Research;
        numbers.Children.Add(UI.Text("Measured DPS: "+N(measured?.Dps??row.DisplayDps,"N1"),16,"Mint",true));
        numbers.Children.Add(UI.Text("Observed HP: "+N(measured?.Hp??row.Hp,"N0"),14,"Foreground",true));
        numbers.Children.Add(UI.Text("Observed AC: "+(measured?.Ac??"Unknown"),13,"Foreground",true));
        if(measured!=null)numbers.Children.Add(UI.Text(measured.StatConditions,11,"SecondaryText"));
        numbers.Children.Add(UI.Text(measured is {Fights:>0} ? measured.Fights+" fights · "+measured.Seconds.ToString("N0")+" encounter seconds" : "No matching combat sample",11,"SecondaryText"));
        numbers.Children.Add(UI.Text(measured?.Conditions??"Record a benchmark with its exact equipment and buff context.",11,"SecondaryText"));
        numbers.Children.Add(UI.Text("Measured history; no DPS/defense winner inferred. Stat captures and combat runs may have different conditions.",11,"SecondaryText"));
        var equipment=UI.Stack(
            UI.Text("Equipment damage points: "+N(row.DamagePreference,"N1")+" · Change "+Delta(row.DamagePreferenceDelta),12,"Foreground",true),
            UI.Text("Equipment defense points: "+N(row.DefensePreference,"N1")+" · Change "+Delta(row.DefensePreferenceDelta),12,"Foreground",true),
            UI.Text("Gear HP: "+N(row.GearHp,"N0")+" · Change: "+Delta(row.GearHpDelta),12),
            UI.Text(row.PartialEquipment ? "Known contributions · coverage limited" : "Same gear can produce identical equipment points for different pet families.",11,"SecondaryText"));
        foreach(string weapon in row.Equipment.Totals.Weapons) equipment.Children.Add(UI.Text(weapon,11,"Mint"));
        if(row.Equipment.Totals.Notes.Count>0) equipment.Children.Add(UI.Text("Conditional equipment evidence",11,"Melee"));
        numbers.Children.Add(FocusedLayout.Disclosure("Equipment points & ratios",equipment,"Equipment points guide your saved goals and optimization. They do not measure innate pet abilities or predict DPS."));
        foreach(FrameworkElement number in numbers.Children)number.Margin=new(0,0,0,3);
        Child=UI.Stack(title,numbers,UI.Text(row.Summon+" · Level: "+row.Level,11,"SecondaryText"));
        ToolTip="Equipment preference points are not DPS. Gear HP excludes innate HP and buffs. Changes compare with Current effective equipment. "+row.Plan.LevelEvidence;
        System.Windows.Automation.AutomationProperties.SetName(this,"Pet Type: "+row.Pet+"; Measured DPS "+N(measured?.Dps??row.DisplayDps,"N1")+"; observed HP "+N(measured?.Hp??row.Hp,"N0"));
    }
}

internal sealed class PetBenchmarkDialog : FrostDialog
{
    internal PetBenchmark? Benchmark {get;private set;}
    internal TextBox DpsInput=>dps;internal TextBox HpInput=>hp;internal CheckBox ConfirmedInput=>confirmed;
    private readonly PetComparisonResult row;
    private readonly TextBox name=UI.Input("",230),dps=UI.Input("",100),hp=UI.Input("",100),notes=UI.Input("",420);
    private readonly ListBox fights=new(){Height=145,SelectionMode=SelectionMode.Extended};
    private readonly CheckBox confirmed=new(){Content="I confirm the shown level, gear, ranks, buffs and test conditions"};
    private readonly CheckBox currentPatch=new(){Content="Measured after the October 22 update",ToolTip="Confirm the game was patched for this measurement. Earlier or uncertain samples remain historical."};
    private readonly TextBlock error=UI.Text("",11,"Violet");
    private sealed record FightChoice(Encounter Fight){public override string ToString()=>Fight.Start.ToString("MMM dd HH:mm:ss")+" · "+Fight.PetName+" · "+string.Join(", ",Fight.Events.Where(e=>e.Owner=="Pet").Select(e=>e.Target).Distinct().Take(3))+" · "+Fight.Duration.ToString("0.0")+"s · "+(Fight.PetDamage/Fight.Duration).ToString("N1")+" DPS";}
    internal PetBenchmarkDialog(PetComparisonResult row,IReadOnlyList<Encounter> encounters):base("Record Pet benchmark",650,620)
    {
        this.row=row;name.Text=row.Pet+" test";fights.ItemsSource=encounters.Select(f=>new FightChoice(f)).ToList();
        name.ToolTip="Name this measured sample. Stored only in this character's local profile.";dps.ToolTip="Measured DPS, not preference points. Selecting encounters calculates total damage / summed encounter duration.";hp.ToolTip="Measured total pet HP for this exact level, gear and buff context. Leave blank if unknown.";notes.ToolTip="Describe target, positioning, buffs, weapon changes and uncertainties.";
        fights.ToolTip="Optional: select finished encounters. Recorded summon and pet-ownership evidence identifies the pet; target names do not establish identity.";
        confirmed.ToolTip="Leave unchecked when gear, rank, level or buffs are uncertain. Unconfirmed samples remain historical evidence and do not populate comparison-card numbers.";
        fights.SelectionChanged+=(_,_)=>{var selected=fights.SelectedItems.Cast<FightChoice>().ToList();dps.IsReadOnly=selected.Count>0;if(selected.Count>0)dps.Text=(selected.Sum(f=>f.Fight.PetDamage)/selected.Sum(f=>f.Fight.Duration)).ToString("R",CultureInfo.InvariantCulture);};
        Editor.Children.Add(UI.Stack(UI.Text("Pet: "+row.Pet+" · Level: "+row.Level+" · "+row.Summon,14,"Accent",true),UI.Field("Benchmark name",name),UI.Text("Equipment: "+string.Join(" · ",row.Equipment.Positions.Where(p=>p.Item!=null).Select(p=>p.Slot+": "+p.Item!.Name)),11),UI.Field("Finished encounters (optional)",fights),UI.Row(UI.Field("Measured DPS",dps),UI.Field("Measured HP",hp)),UI.Field("Test notes",notes),confirmed,currentPatch,UI.Text("Measurements stay separate from predictions. Confirmed samples apply only to the matching mechanics, summon, level, equipment, buffs and encounter assumptions.",11,"SecondaryText"),error));
        Actions.Children.Add(UI.Button("Save benchmark",Save));Actions.Children.Add(UI.Button("Cancel",Close));
    }
    private void Save()
    {
        try
        {
            double? Number(TextBox box){if(box.Text.Trim().Length==0)return null;if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n))throw new ArgumentException("Enter a valid number or leave blank for Unknown.");return n;}
            var selected=fights.SelectedItems.Cast<FightChoice>().Select(f=>f.Fight).ToList();
            var benchmark=selected.Count>0?PetBenchmarks.FromFights(row,selected,name.Text,confirmed.IsChecked==true):new PetBenchmark{Label=name.Text,Pet=row.Pet,Level=row.Plan.PetLevel,Summon=row.Plan.SummonSpell,Rank=row.Plan.SummonRank,ContextKey=PetBenchmarks.Context(row),Dps=Number(dps),ContextConfirmed=confirmed.IsChecked==true,Equipment=EquipmentPositions.Fingerprint(row.Equipment.Positions),Buffs=PetComparisonEngine.Key(row.Scenario.Buffs),PetIdentityEvidence="User-recorded measurement"};
            if(currentPatch.IsChecked!=true || selected.Any(f=>f.Start.Date<OctoberContent.PatchDate))benchmark.MechanicsVersion="Historical / patch unconfirmed";
            benchmark.Hp=Number(hp);benchmark.Conditions+=" "+notes.Text.Trim();benchmark.Validate();Benchmark=benchmark;DialogResult=true;
        }
        catch(ArgumentException e){error.Text=e.Message;}
    }
}
