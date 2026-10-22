using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class PetGoalWeightsDialog : FrostDialog
{
    internal readonly Dictionary<string,TextBox> Inputs=[];
    private readonly CharacterProfile profile;
    private readonly ComboBox dimension;
    private readonly TextBlock error=UI.Text("",11,"Violet");
    private readonly StackPanel fields=new();
    private readonly Dictionary<string,Dictionary<string,double>> draft=[];
    private string current="";
    private bool changing;
    internal PetGoalWeightsDialog(CharacterProfile profile,string goal):base("Pet preference weights",740,620)
    {
        this.profile=profile;draft["DPS"]=PetRoleRatings.Weights(profile,"DPS");draft["Tank"]=PetRoleRatings.Weights(profile,"Tank");
        dimension=UI.Select(["DPS","Tank"],goal=="Tank"?"Tank":"DPS",140);
        Editor.Children.Add(UI.Stack(UI.Text("Pet goals use independent editable preference priorities. Balanced retains both dimensions; it has no combined point score. These weights never predict DPS or survival time.",12),UI.Field("Dimension",dimension),fields,error));
        dimension.SelectionChanged+=(_,_)=>{if(changing)return;if(Store())Load();else{changing=true;dimension.SelectedItem=current;changing=false;}};Load();
        Actions.Children.Add(UI.Button("Reset weights to default",()=>{draft[current]=new(current=="Tank"?PetRoleRatings.Tank:GearScorer.Defaults["Pet DPS"]);Load();}));
        Actions.Children.Add(UI.Button("Save weights",Save));Actions.Children.Add(UI.Button("Cancel",Close));
    }
    private bool Store()
    {
        if(current.Length==0)return true;var values=new Dictionary<string,double>();
        foreach(var (stat,input) in Inputs){if(!double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)||!double.IsFinite(value)){error.Text="Enter a finite weight for "+stat+". Previous settings retained.";input.Focus();return false;}if(value!=0)values[stat]=value;}draft[current]=values;return true;
    }
    private void Load()
    {
        current=dimension.SelectedItem?.ToString()??"DPS";fields.Children.Clear();Inputs.Clear();error.Text="";var wrap=new WrapPanel();
        string[][] groups=[["AC","HP","Mana","Endurance","HPRegen","ManaRegen"],["STR","DEX","STA","CHA","INT","WIS","AGI","ATK","Ratio","Haste"],["ResistFire","ResistCold","ResistMagic","ResistDisease","ResistPoison"]];string[] titles=["AC & Resources","Stats & weapons","Resists"];
        for(int n=0;n<groups.Length;n++)
        {
            var column=UI.Stack(UI.Text(titles[n],13,"Melee",true));column.Width=220;column.Margin=new(0,0,8,0);
            foreach(string stat in groups[n].Concat(n==1?draft[current].Keys.Except(groups.SelectMany(g=>g)):[]))
            {
                var row=new Grid{Margin=new(0,0,0,6)};row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new(80)});
                var label=UI.Text(RatingExplanation.Label(stat),12);label.VerticalAlignment=VerticalAlignment.Center;var input=UI.Input(draft[current].GetValueOrDefault(stat).ToString("R",CultureInfo.InvariantCulture));input.Height=28;input.Margin=new(0);input.ToolTip="Editable preference points per effective "+stat+". No damage/resource conversion is inferred.";
                System.Windows.Automation.AutomationProperties.SetName(input,stat+" Pet weight");Inputs[stat]=input;Grid.SetColumn(input,1);row.Children.Add(label);row.Children.Add(input);column.Children.Add(row);
            }wrap.Children.Add(column);
        }fields.Children.Add(wrap);
    }
    internal void Save(){if(!Store())return;foreach(var pair in draft)profile.PetGoalWeights[pair.Key]=new(pair.Value);DialogResult=true;}
}

internal sealed class PetBuffSetupDialog : FrostDialog
{
    private readonly PetComparisonPreferences target;
    private readonly PetBuffCatalog catalog;
    private readonly LoadoutProfile owner;
    private readonly List<PetBuffChoice> working;
    private readonly ListBox list=new(){Height=170};
    private readonly ComboBox add;
    private readonly CheckBox enabled=new(){Content="Include this buff"},owned=new(){Content="Learning / source ownership confirmed"},external=new(){Content="External source"};
    private readonly TextBox rank=UI.Input("",65),uptime=UI.Input("",75);
    private readonly TextBlock detail=UI.Text("",11),error=UI.Text("",11,"Violet");
    private PetBuffChoice? selected;
    private bool loading;
    private sealed record Row(PetBuffChoice Choice,string Name){public override string ToString()=>Name;}
    internal PetBuffSetupDialog(LoadoutProfile owner,PetBuffCatalog catalog,PetComparisonPreferences preferences):base("Pet buff setup",660,620)
    {
        this.catalog=catalog;this.owner=EquipmentPositions.Copy(owner);target=preferences;working=EquipmentPositions.Copy(preferences.Buffs);
        var choices=catalog.Choices(owner,preferences.ExternalBuffs).ToList();add=UI.Select([],"",360);add.ItemsSource=choices;add.DisplayMemberPath="Name";if(choices.Count>0)add.SelectedIndex=0;
        var fields=new WrapPanel();foreach(var c in new UIElement[]{UI.Field("Spell / AA",add),UI.ActionField(UI.Button("Add",Add)),UI.ActionField(UI.Button("Remove",Remove))})fields.Children.Add(c);
        var settings=new WrapPanel();foreach(var c in new UIElement[]{UI.Field("Rank",rank),UI.Field("Uptime %",uptime),UI.ActionField(enabled),UI.ActionField(owned),UI.ActionField(external)})settings.Children.Add(c);
        rank.ToolTip="Confirmed spell/AA rank, 0–10; blank remains Unknown.";uptime.ToolTip="Configured uptime 0–100%; blank remains Unknown. Cooldowns are not evidence of actual uptime.";
        list.SelectionChanged+=(_,_)=>{if(loading)return;if(!Store()){loading=true;list.SelectedItem=list.Items.Cast<Row>().FirstOrDefault(r=>ReferenceEquals(r.Choice,selected));loading=false;return;}Load();};
        Editor.Children.Add(UI.Stack(UI.Text(catalog.Status,11,"SecondaryText"),fields,list,settings,detail,error,UI.Text("Effect-slot checks are computed evidence. Complete conflicting buffs are excluded; bonuses are never cherry-picked. No active buff roster, proc rate or uptime is inferred.",11,"SecondaryText")));
        Actions.Children.Add(UI.Button("Save setup",()=>{if(!Store())return;target.Buffs=EquipmentPositions.Copy(working);DialogResult=true;}));Actions.Children.Add(UI.Button("Cancel",Close));Refresh();
    }
    private void Refresh(){loading=true;list.ItemsSource=working.Select(c=>new Row(c,(catalog.Find(c.Id)?.Name??c.Id)+(c.External?" · external":""))).ToList();list.SelectedIndex=working.Count>0?0:-1;loading=false;Load();}
    private void Add(){if(!Store()||add.SelectedItem is not PetBuffDefinition value)return;var found=working.FirstOrDefault(c=>c.Id==value.Id);if(found==null)working.Add(new(){Id=value.Id,Enabled=true,External=value.SourceItem.Length==0&&!(value.Aa&&value.ConfirmedRank>0)&&!value.Classes.Any(c=>owner.Classes.Select(CastingPriority.Code).Contains(c.Key)&&c.Value<=owner.Level),Rank=value.ConfirmedRank,OwnershipConfirmed=value.ConfirmedRank.HasValue||value.SourceItem.Length>0});Refresh();list.SelectedIndex=working.FindIndex(c=>c.Id==value.Id);}
    private void Remove(){if(selected==null)return;working.Remove(selected);selected=null;Refresh();}
    private void Load()
    {
        selected=(list.SelectedItem as Row)?.Choice;loading=true;enabled.IsChecked=selected?.Enabled==true;owned.IsChecked=selected?.OwnershipConfirmed==true;external.IsChecked=selected?.External==true;rank.Text=selected?.Rank?.ToString()??"";uptime.Text=selected?.Uptime is {} up?(up*100).ToString("0.###",CultureInfo.InvariantCulture):"";
        var d=selected==null?null:catalog.Find(selected.Id);detail.Text=d==null?"Select or add a buff.":d.Name+" · "+string.Join(" / ",d.Classes.Select(c=>c.Key+" "+(c.Value>0?c.Value.ToString():"level unverified")))+"\n"+d.Source+"\n"+(d.ParametersComplete?$"Client: cast {d.CastMs}ms · recovery {d.RecoveryMs}ms · reuse {d.RecastMs}ms · mana {d.Mana}. Server formula and rank response remain unverified.":"Reference only: exact effects, target restrictions and stacking remain unresolved.");loading=false;
    }
    private bool Store()
    {
        if(loading||selected==null)return true;int? r=null;double? u=null;
        if(rank.Text.Trim().Length>0){if(!int.TryParse(rank.Text,out int n)||(catalog.Find(selected.Id)?.Aa==true?n is <1 or >100:n is <0 or >10)){error.Text="Spell rank must be 0–10; confirmed AA rank 1–100; blank remains Unknown.";return false;}r=n;}
        if(uptime.Text.Trim().Length>0){if(!double.TryParse(uptime.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||!double.IsFinite(n)||n is <0 or >100){error.Text="Uptime must be 0–100% or blank for Unknown.";return false;}u=n/100;}
        selected.Rank=r;selected.Uptime=u;selected.Enabled=enabled.IsChecked==true;selected.OwnershipConfirmed=owned.IsChecked==true;selected.External=external.IsChecked==true;error.Text="";return true;
    }
}
