using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

/// <summary>A read-only sheet. Captured totals and effective equipment are never added together.</summary>
internal sealed class PetCharacterSheet : StackPanel
{
    internal readonly TextBlock Capture=UI.Text("Select a summon to show its observed snapshot.",11,"SecondaryText");
    internal readonly TextBlock CaptureEvidence=UI.Text("",11,"SecondaryText");
    internal readonly TextBlock Weapons=UI.Text("",11,"Foreground"),Warnings=UI.Text("",11,"Melee");
    internal readonly TextBlock GearSummary=UI.Text("",11,"SecondaryText"),ObservedLevel=UI.Text("Observed level: Unknown",12);
    internal readonly Dictionary<string,TextBlock> Values=[];
    internal readonly Dictionary<string,TextBlock> GearValues=[];
    internal readonly PetSheetColumns Main;
    internal readonly Expander Extra,Equipment;
    internal readonly CheckBox Added=new(){Content="Added",ToolTip="Show effective gear and active configured effects beside captured stats. Buffs and unsupported resource/stat conversions are excluded."};
    internal readonly CheckBox Standard=new(){Content="With standard buffs",ToolTip="Scenario with learned sustained buffs assumed active. Not a live buff roster or measured uptime. Frenzied Burnout remains separate."};
    internal readonly Button ReviewBuffs=UI.Button("Review buffs",()=>{});
    private readonly TextBlock snapshotTitle=UI.Text("Observed snapshot",14,"Foreground",true);
    private PetBuild buffBuild=new();
    private StandardPetBuffState buffSetup=new();
    private PetResearchState? buffResearch;
    internal readonly StackPanel AdditionalEvidence=UI.Stack();
    private readonly TextBlock name=UI.Text("Pet",19,"Accent",true),identity=UI.Text("",12,"SecondaryText"),rank=UI.Text("",11,"SecondaryText");
    private readonly ActorBadge icon=new(){Width=36,Height=36,Margin=new(0,0,10,0),VerticalAlignment=VerticalAlignment.Top};
    private readonly StackPanel gear=UI.Stack(),details=UI.Stack();
    private readonly StackPanel body;
    private readonly StackPanel abilities=UI.Stack();
    private readonly Dictionary<string,TextBlock> weaponNames=[],weaponStates=[];
    private PetDetailsStatistics? last;
    internal bool HasResult { get; private set; }
    internal string NameText=>name.Text;

    internal PetCharacterSheet()
    {
        var heading=new DockPanel{Margin=new(0,0,0,7)};
        DockPanel.SetDock(icon,Dock.Left);heading.Children.Add(icon);heading.Children.Add(UI.Stack(name,identity,UI.Row(ObservedLevel,rank)));
        IdentityHeader=heading;
        var left=UI.Stack(Section("Vitals",Resource("HP"),Resource("Mana")),
            Section("Combat",Pair("AC","ACLeft","ACRight","AC"),Pair("Attack","AttackLeft","AttackRight","ATK"),Field("Attack speed","AttackSpeedPercent"),Field("Haste","Haste")),
            Section("Regeneration",Field("HP","HPRegen"),Field("Mana","ManaRegen")));
        var right=UI.Stack(Section("Attributes",Field("Strength","STR"),Field("Stamina","STA"),Field("Intelligence","INT"),Field("Wisdom","WIS"),Field("Agility","AGI"),Field("Dexterity","DEX"),Field("Charisma","CHA")),
            Section("Resists (added)",new[]{"Fire","Cold","Magic","Disease","Poison","Void","Corruption"}.Select(s=>Field(s,"Resist"+s)).ToArray()));
        Main=new(left,right);
        var weaponSections=new PetSheetColumns(Weapon("Primary"),Weapon("Secondary"));
        Extra=FocusedLayout.Disclosure("Known spells & abilities",abilities,"Documented capabilities and owned-pet observations are separate. Cast attempts do not establish successful effects or innate spell power.");
        Equipment=FocusedLayout.Disclosure("Equipment contributions",UI.Stack(gear,Weapons),"Effective gear only. These values are already included in an equipped capture; they are not added to the observed snapshot.");
        var stats=UI.Stack(Main,Divider(),UI.Text("Loadout weapons",12,"Melee",true),weaponSections,Extra);
        var frame=UI.Card(stats);frame.Padding=new(10,7,10,4);frame.SetResourceReference(Border.BackgroundProperty,"DetailSurface");
        var snapshotHeading=new WrapPanel();snapshotHeading.Children.Add(snapshotTitle);Added.Margin=new(12,0,8,0);Standard.Margin=new(0,0,8,0);foreach(var element in new UIElement[]{Added,Standard,ReviewBuffs})snapshotHeading.Children.Add(element);ReviewBuffs.Height=28;
        body=UI.Stack(snapshotHeading,Capture,frame,GearSummary,Equipment,Warnings);
        Added.Checked+=(_,_)=>{if(last!=null)Present(last);};Added.Unchecked+=(_,_)=>{if(last!=null)Present(last);};
        Standard.Checked+=(_,_)=>{if(last!=null)Present(last);};Standard.Unchecked+=(_,_)=>{if(last!=null)Present(last);};
        AdditionalEvidence.Children.Add(details);
        Children.Add(body);body.Visibility=Visibility.Collapsed;
        rank.Margin=new(18,0,0,4);
        AutomationProperties.SetName(this,"Pet character sheet");
    }

    internal FrameworkElement IdentityHeader { get; }
    internal void SetStandardContext(PetBuild build,StandardPetBuffState setup,PetResearchState? research){buffBuild=build;buffSetup=setup;buffResearch=research;}

    internal void SetContext(PetBuild build,string petName)
    {
        name.Text=petName.Length>0?petName:build.PetType+" pet";
        var definition=PetDefinition.For(build.PetType);
        identity.Text=build.PetType+" · "+(definition.Classes.Length>0?string.Join(" / ",definition.Classes):"Classes unknown");
        rank.Text="Spell rank: "+(build.SummonRankKnown?"+"+build.SummonRank:"Unknown");
        icon.Set("Pet",false,build.PetType+" pet · "+definition.Source,build.PetType);
        AutomationProperties.SetName(icon,build.PetType+" pet");
        if(!HasResult)ObservedLevel.Text="Observed level: Unknown";
    }

    internal void Clear()
    {
        last=null;HasResult=false;body.Visibility=Visibility.Collapsed;ObservedLevel.Text="Observed level: Unknown";
        Capture.Text="Select a summon to show its observed snapshot.";
        CaptureEvidence.Text=Weapons.Text=Warnings.Text=GearSummary.Text="";gear.Children.Clear();GearValues.Clear();details.Children.Clear();
        foreach(var field in Values.Values.Distinct())field.Text="Unknown";
    }

    internal void Present(PetDetailsStatistics result)
    {
        last=result;HasResult=true;body.Visibility=Visibility.Visible;
        var scenario=StandardPetBuffs.Project(result,buffBuild,buffSetup,buffResearch,Standard.IsChecked==true);snapshotTitle.Text=scenario.Title;
        double? Read(string key)=>scenario.Values.TryGetValue(key,out var value)&&value is {} n&&double.IsFinite(n)&&(!(key is "HP" or "HPCurrent" or "Mana" or "ManaCurrent" or "Endurance" or "EnduranceCurrent")||n>=0)?n:null;
        foreach(var (key,block) in Values)
            block.Text=Number(Read(key),key is "AttackSpeedPercent" or "Haste" or "PrimaryAccuracyPercent" or "SecondaryAccuracyPercent");
        foreach(string field in new[]{"HP","Mana"})
        {
            var total=Read(field);var current=Read(field+"Current");
            Values[field].Text=current.HasValue&&total.HasValue?Number(current)+" / "+Number(total):Number(total);
            Values[field].ToolTip=$"{field} at the labeled capture. "+(current.HasValue&&total.HasValue?"Current / maximum.":"Maximum only; a current value is not inferred.");
        }
        SetPair("ACLeft","ACRight","AC");SetPair("AttackLeft","AttackRight","ATK");
        void SetPair(string first,string second,string fallback="")
        {
            var a=Read(first);var b=Read(second);
            Values[first].Text=a.HasValue||b.HasValue?Number(a)+" | "+Number(b):fallback.Length>0?Number(Read(fallback)):"Unknown";
        }
        ObservedLevel.Text="Observed level: "+(result.ObservedLevel is >0?result.ObservedLevel.Value.ToString(CultureInfo.CurrentCulture):"Unknown");
        ObservedLevel.ToolTip="Actual level recorded with this capture. Automatic level estimates and manually entered build assumptions remain in summon settings.";
        Capture.Text=result.CaptureName.Length==0?"No applicable capture · stats remain Unknown":
            string.Join(" · ",new[]{result.CaptureName,result.CapturedAt?.ToString("MMM dd HH:mm",CultureInfo.CurrentCulture)??"Date unknown",result.Conditions.Length>0?result.Conditions:"Conditions unknown",result.MatchingEquipment?"Matching equipment":"Related capture — equipment/buff context incomplete"});
        Capture.ToolTip=result.Capture;CaptureEvidence.Text=result.Capture;
        if(Standard.IsChecked==true)Capture.Text+="\nAssumed standard buffs: "+scenario.Conditions;
        details.Children.Clear();
        foreach(string note in scenario.Details)details.Children.Add(UI.Text(note,11,"SecondaryText"));
        foreach(var (scalar,first,second) in new[]{("AC","ACLeft","ACRight"),("ATK","AttackLeft","AttackRight")})
            if(Read(scalar).HasValue&&(Read(first).HasValue||Read(second).HasValue))details.Children.Add(UI.Text("Additional recorded "+scalar+": "+Number(Read(scalar)),11));
        foreach(string resource in new[]{"HP","Mana","Endurance"})
            if(!Read(resource).HasValue&&Read(resource+"Current").HasValue)details.Children.Add(UI.Text("Recorded current "+resource+": "+Number(Read(resource+"Current"))+" · maximum Unknown",11));
        if(result.Effects.Length>0)details.Children.Add(UI.Text("Recorded effects: "+result.Effects,11));
        if(result.WeaponEvidence.Length>0)details.Children.Add(UI.Text("Recorded weapons: "+result.WeaponEvidence,11));
        // Retain additional documented fields without turning them into invented totals.
        var displayed=Values.Keys.Concat(new[]{"AC","ATK","HPCurrent","ManaCurrent","EnduranceCurrent"}).ToHashSet();
        foreach(var pair in result.Observed.Where(p=>!displayed.Contains(p.Key)))details.Children.Add(UI.Text(pair.Key+": "+Number(Read(pair.Key)),11));
        foreach(var pair in result.Observed.Where(p=>p.Key.StartsWith("Resist")&&p.Value.HasValue))details.Children.Add(UI.Text("Recorded total "+pair.Key[6..]+": "+Number(pair.Value)+" · historical capture; innate component unresolved",11));
        foreach(string resist in new[]{"Fire","Cold","Magic","Disease","Poison","Void","Corruption"})
        {
            string key="Resist"+resist;Values[key].Text=Signed(result.Equipment.Totals.Stats.GetValueOrDefault(key));
            Values[key].ToolTip="Known effective gear contribution only. Innate resist is unknown."+(result.Loadout.Complete?" Equipment metadata is complete.":" Metadata coverage is incomplete; this is a known subtotal, including +0 where no contribution is documented.");
        }
        if(!result.Loadout.Complete)details.Children.Add(UI.Text("Resists (added): known subtotals only; some loadout metadata is incomplete. Innate resists are unknown.",11,"Melee"));
        foreach(var weapon in result.Loadout.Weapons)
        {
            weaponNames[weapon.Hand].Text=weapon.Name+(weapon.Rank is {} r?" +"+r:"");weaponStates[weapon.Hand].Text=weapon.State;weaponNames[weapon.Hand].ToolTip=weapon.Evidence;
            foreach(string field in new[]{"Damage","Delay","Ratio","AccuracyPercent","Weight"})
            {
                double? value=field=="AccuracyPercent"?weapon.Accuracy:field=="Weight"?weapon.Weight:weapon.Stats.TryGetValue(field,out double n)?n:null;
                string text=weapon.State is "Empty" or "Inactive"||weapon.State=="Non-weapon"&&field!="Weight"?"—":Number(value,field=="AccuracyPercent");
                if(field=="Weight"&&weapon.BaseWeight&&value.HasValue)text+=" (base)";
                Values[weapon.Hand+field].Text=text;Values[weapon.Hand+field].ToolTip=weapon.Evidence+(value==null?"\nThis field is undocumented; no value invented.":"");
            }
        }
        if(Added.IsChecked==true)
        {
            var knownStats=result.Equipment.Totals.Stats;
            foreach(string field in new[]{"HP","Mana","STR","STA","INT","WIS","AGI","DEX","CHA","Haste","HPRegen","ManaRegen"})
            {
                string contribution=knownStats.TryGetValue(field,out double value)?Signed(value,field=="Haste"):result.Loadout.Complete?"+0":"+?";
                Values[field].Text+=" ("+contribution+")";
                Values[field].ToolTip="Parentheses show direct effective gear and configured effects only; not received buffs or a recalculated snapshot. Resource additions apply to maximum capacity. STA-to-HP and INT/WIS-to-mana are unresolved and excluded.";
            }
            Values["ACLeft"].Text+=" ("+Signed(knownStats.GetValueOrDefault("AC"))+" raw gear AC)";
            Values["AttackLeft"].Text+=" ("+(knownStats.ContainsKey("ATK")||result.Loadout.Complete?Signed(knownStats.GetValueOrDefault("ATK")):"+?")+" raw gear ATK)";
            Values["ACLeft"].ToolTip=Values["AttackLeft"].ToolTip="Raw item contribution, not a claimed delta to either client display number; that mapping is unresolved.";
            Values["AttackSpeedPercent"].Text+=" (unresolved)";Values["AttackSpeedPercent"].ToolTip="Gear haste is shown separately. Haste stacking, cap and conversion to displayed attack speed are not assumed.";
        }
        GearValues.Clear();gear.Children.Clear();
        var known=result.Equipment.Totals.Stats;
        GearSummary.Text="Gear only · "+string.Join(" · ",known.Where(p=>p.Key is "AC" or "HP" or "Mana" or "Haste").Select(p=>p.Key+" "+Signed(p.Value,p.Key=="Haste")));
        if(!known.Any())GearSummary.Text="Gear only · no documented stat contributions";
        var groups=new[]{("Vitals & combat",new[]{"AC","HP","Mana","ATK","Haste","HPRegen","ManaRegen"}),("Attributes",new[]{"STR","STA","INT","WIS","AGI","DEX","CHA"}),("Resists",new[]{"ResistFire","ResistCold","ResistMagic","ResistDisease","ResistPoison","ResistVoid","ResistCorruption"})};
        var used=new HashSet<string>();
        var gearGroups=new WrapPanel();
        foreach(var (title,fields) in groups)
        {
            var rows=new List<UIElement>();
            foreach(string field in fields.Where(known.ContainsKey))
            {
                var value=UI.Text(Signed(known[field],field=="Haste"),12,"Foreground",true);GearValues[field]=value;used.Add(field);
                rows.Add(StatLine(field.StartsWith("Resist")?field[6..]:field,value));
            }
            if(rows.Count>0){var section=Section(title,rows.ToArray());section.Width=195;section.Margin=new(0,0,18,4);gearGroups.Children.Add(section);}
        }
        gear.Children.Add(UI.Text("Effective equipment only; separate from the observed snapshot. No innate or STA/INT/WIS resource conversion is applied.",11,"SecondaryText"));
        gear.Children.Add(gearGroups);
        foreach(var pair in known.Where(p=>!used.Contains(p.Key))){var block=UI.Text(Signed(pair.Value),12);GearValues[pair.Key]=block;gear.Children.Add(StatLine(pair.Key,block));}
        Weapons.Text=string.Join("\n",result.Equipment.Totals.Weapons);
        if(result.Equipment.Totals.Procs.Count>0)gear.Children.Add(UI.Text(string.Join("\n",result.Equipment.Totals.Procs),11));
        Warnings.Text=string.Join("\n",result.Equipment.Totals.Notes.Concat(result.Loadout.Complete?[]:new[]{"Incomplete loadout metadata · added values are known subtotals; innate resists remain unknown."}));Warnings.Visibility=Warnings.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        foreach(var hint in scenario.Hints.Where(p=>Values.ContainsKey(p.Key)).DistinctBy(p=>Values[p.Key])){Values[hint.Key].Text+=" · "+hint.Value;Values[hint.Key].ToolTip+="\nInformational cap/context hint. No equipment rating or removal decision is implied.";}
        foreach(var value in Values.Values.Distinct())AutomationProperties.SetName(value,value.Tag+": "+value.Text);
    }

    private static string Number(double? value,bool percent=false)=>value.HasValue?value.Value.ToString("0.###",CultureInfo.CurrentCulture)+(percent?"%":""):"Unknown";
    private static string Signed(double value,bool percent=false)=>value.ToString("+0.###;-0.###;+0",CultureInfo.CurrentCulture)+(percent?"%":"");
    private UIElement Resource(string key)
    {
        var value=Value(key,18);value.SetResourceReference(TextBlock.ForegroundProperty,key switch{"HP"=>"PetHP","Mana"=>"PetMana",_=>"PetEndurance"});
        return StatLine(key,value,5);
    }
    private UIElement Field(string label,string key)=>StatLine(label,Value(key));
    private TextBlock Value(string key,double size=12)
    {
        var value=UI.Text("Unknown",size,"Foreground",true);value.TextAlignment=TextAlignment.Right;value.Margin=new(8,0,0,0);Values[key]=value;
        value.ToolTip="Observed "+key+" at the labeled capture. Unknown means it was not established.";
        return value;
    }
    private UIElement Pair(string label,string first,string second,string fallback="")
    {
        var value=Value(first);Values[second]=value;if(fallback.Length>0)Values[fallback]=value;
        value.ToolTip="Recorded display order is preserved. The meaning of either number is not inferred.";
        return StatLine(label,value);
    }
    private StackPanel Weapon(string hand)
    {
        var name=weaponNames[hand]=UI.Text("None",12,"Accent",true);var state=weaponStates[hand]=UI.Text("",11,"SecondaryText");
        return Section(hand,name,state,Field("Damage",hand+"Damage"),Field("Delay",hand+"Delay"),Field("Ratio",hand+"Ratio"),Field("Accuracy",hand+"AccuracyPercent"),Field("Weight",hand+"Weight"));
    }
    internal void SetAbilities(IReadOnlyList<PetAbilityFact> facts,string buffs)
    {
        abilities.Children.Clear();
        foreach(var fact in facts)
        {
            var text=UI.Text(fact.Name+" · "+fact.Kind+(fact.Count>0?" · "+fact.Count+(fact.Kind.Contains("attempt")?" attempts":" records"):"")+(fact.Last.HasValue?" · "+fact.Last.Value.ToString("MMM dd HH:mm"):""),11);
            text.ToolTip=fact.Evidence;abilities.Children.Add(text);
        }
        if(facts.Count==0)abilities.Children.Add(UI.Text("No documented or owned-pet observations for this summon context.",11,"SecondaryText"));
        abilities.Children.Add(FocusedLayout.Disclosure("Received buff evidence",UI.Text(buffs,11,"SecondaryText"),"Landing/fade history is not a complete live buff roster. Added excludes these buffs."));
    }
    private static Grid StatLine(string label,TextBlock value,double bottom=3)
    {
        var line=new Grid{Margin=new(0,0,0,bottom)};line.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});line.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var title=UI.Text(label,12,"Foreground");title.VerticalAlignment=VerticalAlignment.Center;title.Margin=new(0);value.VerticalAlignment=VerticalAlignment.Center;
        value.Tag=label;
        Grid.SetColumn(value,1);line.Children.Add(title);line.Children.Add(value);return line;
    }
    private static StackPanel Section(string title,params UIElement[] fields)
    {
        var label=UI.Text(title,12,"Melee",true);label.Margin=new(0,1,0,6);
        var panel=UI.Stack(label);panel.Margin=new(0,0,0,8);foreach(var field in fields)panel.Children.Add(field);return panel;
    }
    private static Border Divider(){var line=new Border{Height=1,Margin=new(0,1,0,8)};line.SetResourceReference(Border.BackgroundProperty,"Edge");return line;}
}

internal sealed class PetSheetColumns : Grid
{
    internal bool IsWide { get; private set; }
    private readonly FrameworkElement left,right;
    private bool arranged;
    internal PetSheetColumns(FrameworkElement first,FrameworkElement second)
    {
        left=first;right=second;ColumnDefinitions.Add(new());ColumnDefinitions.Add(new());RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});
        Children.Add(left);Children.Add(right);SizeChanged+=(_,_)=>ArrangeColumns();
    }
    private void ArrangeColumns()
    {
        bool wide=ActualWidth>=580;if(arranged&&wide==IsWide)return;arranged=true;IsWide=wide;
        ColumnDefinitions[0].Width=new(1,GridUnitType.Star);ColumnDefinitions[1].Width=wide?new(1,GridUnitType.Star):new(0);
        SetColumn(right,wide?1:0);SetRow(right,wide?0:1);left.Margin=new(0,0,wide?14:0,0);right.Margin=new(wide?14:0,0,0,0);
    }
}
