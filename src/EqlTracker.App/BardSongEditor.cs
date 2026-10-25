using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class BardSongEditor : StackPanel
{
    internal readonly Button Save;
    internal readonly StackPanel Gems=new();
    private readonly TrackerController controller;
    private BardSetup draft;
    private BardSetup savedDraft;
    private string baseline;
    private readonly IReadOnlyList<BardSong> catalog;
    private readonly ComboBox aura,count,available;
    private readonly TextBox instrument,singing,focus,level,aa,values,source;
    private readonly CheckBox complete=new(){Content="All components reviewed"};
    private readonly TextBlock selected=UI.Text("Select a song for evidence",12,"Melee",true),status=UI.Text("",11,"SecondaryText");
    private readonly StackPanel availableRows=new();
    private readonly Dictionary<int,string> invalidRanks=[];
    private BardSong? evidenceSong;
    private bool loading;
    private string evidenceBaseline="";
    internal BardSongEditor(TrackerController controller,Action saved)
    {
        this.controller=controller;draft=controller.IntegratedSnapshot().Bard;savedDraft=EquipmentPositions.Copy(draft);catalog=controller.KnownBardSongs();baseline=Key(draft);
        aura=WorkspaceForms.Control(UI.Select(["Unknown","1","2","3","4","5"],draft.ConfirmedAuraRank?.ToString()??"Unknown",90),"Confirmed Aura rank");
        count=WorkspaceForms.Control(UI.Select(Enumerable.Range(1,16).Select(n=>n.ToString()),draft.GemCount.ToString(),70),"Active gem count");
        Children.Add(WorkspaceForms.Flow(UI.Field("Aura rank",aura),UI.Field("Gems",count)));
        aura.ToolTip="Confirmed Symphonic Aura rank; Unknown does not establish capacity.";count.ToolTip="Confirmed active gem count. Selection works backwards from the final eligible gem.";
        Children.Add(Gems);BuildGems();
        instrument=Input(draft.Instrument,64);singing=Input(draft.Singing,64);focus=Input(draft.Focus,64);level=Input(draft.EffectiveCasterLevel,64);aa=WorkspaceForms.Control(UI.Input(draft.AaContext,190));
        var potency=FocusedLayout.Disclosure("Potency context",UI.Stack(WorkspaceForms.Flow(UI.Field("Instrument",instrument),UI.Field("Singing",singing),UI.Field("Focus",focus),UI.Field("Caster level",level)),UI.Field("Relevant AAs",aa)),"Unknown modifiers prevent a definitive potency comparison; client values do not establish server stacking.");
        Children.Add(potency);
        available=new ComboBox{IsEditable=true,IsTextSearchEnabled=true,DisplayMemberPath="Label",ItemsSource=catalog,Width=230};WorkspaceForms.Control(available,"Search replacement songs");
        var add=WorkspaceForms.Control(UI.Button("Add",()=>{if(available.SelectedItem is not BardSong song)return;if(!draft.Available.Any(s=>s.Identity.SpellId==song.Identity.SpellId)){var copy=EquipmentPositions.Copy(song);copy.Available=true;draft.Available.Add(copy);}BuildAvailable();SelectEvidence(draft.Available.First(s=>s.Identity.SpellId==song.Identity.SpellId));Dirty();}));
        add.IsEnabled=false;available.SelectionChanged+=(_,_)=>add.IsEnabled=available.SelectedItem is BardSong;
        Children.Add(FocusedLayout.Disclosure("Replacement songs",UI.Stack(WorkspaceForms.Flow(available,add),availableRows),"Search the client song catalog and confirm only songs available to this character. Unknown rank and effects remain unresolved."));BuildAvailable();
        values=WorkspaceForms.Control(UI.Input());values.Width=double.NaN;source=WorkspaceForms.Control(UI.Input());source.Width=double.NaN;
        var evidence=new StackPanel();evidence.Children.Add(selected);
        evidence.Children.Add(FocusedLayout.Disclosure("Reviewed components",UI.Stack(UI.Field("Stat=amount; Stat=amount",values),complete,UI.Field("Evidence & modifier context",source)),"One selected song; exact reviewed components support conservative swaps. Unreviewed components remain unknown."));
        Children.Add(evidence);
        Save=WorkspaceForms.Control(UI.Button("Save song set",()=>
        {
            if(!Validate())return;var latest=controller.IntegratedSnapshot();
            if(Key(latest.Bard)!=baseline){status.Text="Song setup changed elsewhere. Reopen review before saving.";return;}
            if(draft.ConfirmedAuraRank!=latest.Bard.ConfirmedAuraRank)draft.AuraEvidence=draft.ConfirmedAuraRank.HasValue?"Player confirmed · "+DateTime.Now.ToString("O"):"";
            draft.Planned=latest.Bard.Planned;latest.Bard=EquipmentPositions.Copy(draft);controller.SaveIntegrated(latest);baseline=Key(draft);savedDraft=EquipmentPositions.Copy(draft);Dirty();status.Text="Song set saved";saved();
        }));Save.IsEnabled=false;Children.Add(Save);Children.Add(status);Children.Add(new CompatibilityRuleEditor(controller,false));
        aura.SelectionChanged+=(_,_)=>{draft.ConfirmedAuraRank=int.TryParse(aura.SelectedItem?.ToString(),out int n)?n:null;Dirty();};
        count.SelectionChanged+=(_,_)=>{draft.GemCount=int.TryParse(count.SelectedItem?.ToString(),out int n)?n:8;BuildGems();Dirty();};
        foreach(var box in new[]{instrument,singing,focus,level,aa})box.TextChanged+=(_,_)=>Dirty();
        values.TextChanged+=(_,_)=>Dirty();source.TextChanged+=(_,_)=>Dirty();complete.Click+=(_,_)=>Dirty();
    }
    private static string Key(BardSetup setup)=>PetComparisonEngine.Key(new{setup.GemCount,setup.ConfirmedAuraRank,setup.Instrument,setup.Singing,setup.Focus,setup.EffectiveCasterLevel,setup.AaContext,Songs=setup.Songs.OrderBy(s=>s.Gem).ToArray(),Available=setup.Available.OrderBy(s=>s.Identity.SpellId).ToArray()});
    private static TextBox Input(double? n,double width)=>WorkspaceForms.Control(UI.Input(n?.ToString(CultureInfo.InvariantCulture)??"",width));
    private void BuildGems()
    {
        Gems.Children.Clear();
        Grid Row(){var row=new Grid{Margin=new(0,0,0,4)};foreach(var width in new[]{new GridLength(32),new GridLength(1,GridUnitType.Star),new GridLength(56),new GridLength(42)})row.ColumnDefinitions.Add(new(){Width=width});return row;}
        var header=Row();int col=0;foreach(string label in new[]{"Gem","Song","Rank","Keep"}){var text=UI.Label(label);Grid.SetColumn(text,col++);header.Children.Add(text);}Gems.Children.Add(header);
        for(int gem=draft.GemCount;gem>=1;gem--)
        {
            int position=gem;var chosen=draft.Songs.FirstOrDefault(s=>s.Gem==gem);
            var choices=new[]{new BardSong{Identity=new("","Empty",null)}}.Concat(catalog).Concat(chosen==null?[]:[chosen]).DistinctBy(s=>s.Identity.SpellId).ToArray();
            var song=WorkspaceForms.Control(new ComboBox{ItemsSource=choices,DisplayMemberPath="Identity.Name",IsEditable=true,IsTextSearchEnabled=true,MinWidth=100},"Song in gem "+gem);
            song.ToolTip=chosen?.Identity.Name??"Search song names; review rank separately.";
            song.SelectedItem=choices.FirstOrDefault(s=>s.Identity.SpellId==chosen?.Identity.SpellId)??choices[0];
            var rank=WorkspaceForms.Control(UI.Input(chosen?.Identity.UpgradeRank?.ToString()??"",50),"Rank in gem "+gem);
            var keep=WorkspaceForms.Control(new CheckBox{IsChecked=chosen?.Keep==true},"Keep gem "+gem);
            var row=Row();row.Children.Add(UI.Text(gem.ToString(),12));row.Children.Add(song);Grid.SetColumn(song,1);row.Children.Add(rank);Grid.SetColumn(rank,2);row.Children.Add(keep);Grid.SetColumn(keep,3);Gems.Children.Add(row);
            void Update()
            {
                if(song.SelectedItem is not BardSong selectedSong||selectedSong.Identity.SpellId.Length==0){draft.Songs.RemoveAll(s=>s.Gem==position);invalidRanks.Remove(position);Dirty();return;}
                var prior=draft.Songs.FirstOrDefault(s=>s.Gem==position);var edited=prior?.Identity.SpellId==selectedSong.Identity.SpellId?prior:EquipmentPositions.Copy(selectedSong);
                int? n=int.TryParse(rank.Text,out int value)&&value is >=0 and <=1000?value:null;
                if(rank.Text.Trim().Length>0&&!n.HasValue){invalidRanks[position]=rank.Text;Dirty();return;}invalidRanks.Remove(position);
                bool rankChanged=edited.Identity.UpgradeRank!=n;
                if(rankChanged){edited.Effects=EquipmentPositions.Copy(savedDraft.Songs.Concat(savedDraft.Available).FirstOrDefault(s=>s.Identity.SpellId==edited.Identity.SpellId&&s.Identity.UpgradeRank==n)?.Effects??[]);if(ReferenceEquals(evidenceSong,edited))evidenceSong=null;}
                edited.Identity=edited.Identity with{UpgradeRank=n};edited.Gem=position;edited.Keep=keep.IsChecked==true;edited.Available=true;draft.Songs.RemoveAll(s=>s.Gem==position);draft.Songs.Add(edited);SelectEvidence(edited);Dirty();
            }
            song.SelectionChanged+=(_,_)=>Update();rank.TextChanged+=(_,_)=>Update();keep.Click+=(_,_)=>Update();
            song.GotKeyboardFocus+=(_,_)=>SelectEvidence(draft.Songs.FirstOrDefault(s=>s.Gem==position));
        }
    }
    private void BuildAvailable()
    {
        availableRows.Children.Clear();foreach(var song in draft.Available)
        {var row=new WrapPanel();row.Children.Add(WorkspaceForms.Control(UI.Button(song.Label,()=>SelectEvidence(song))));row.Children.Add(WorkspaceForms.Control(UI.Button("Remove",()=>{draft.Available.Remove(song);BuildAvailable();if(evidenceSong==song)SelectEvidence(null);Dirty();})));availableRows.Children.Add(row);}
    }
    private void SelectEvidence(BardSong? song)
    {
        if(ReferenceEquals(evidenceSong,song))return;if(values!=null&&!StoreEvidence())return;
        evidenceSong=song;if(values==null)return;loading=true;selected.Text=song==null?"Select a song for evidence":song.Label;
        selected.ToolTip=song?.Source;values.Text=ReviewedComponents.Text(song?.Effects??[]);source.Text=song?.Effects.FirstOrDefault()?.Source??"";complete.IsChecked=song?.Effects.Count>0&&song.Effects.All(e=>e.Amount.HasValue);evidenceBaseline=PetComparisonEngine.Key(new{values.Text,Source=source.Text,Complete=complete.IsChecked});loading=false;
    }
    private bool StoreEvidence()
    {
        if(evidenceSong==null||values==null)return true;
        string input=PetComparisonEngine.Key(new{values.Text,Source=source.Text,Complete=complete.IsChecked});if(input==evidenceBaseline)return true;
        try{evidenceSong.Effects=ReviewedComponents.Parse(values.Text,evidenceSong.Identity,source.Text.Trim(),complete.IsChecked==true&&source.Text.Trim().Length>0);evidenceBaseline=input;return true;}
        catch(InvalidDataException error){status.Text=error.Message;return false;}
    }
    private bool Validate()
    {
        if(!StoreEvidence()||invalidRanks.Count>0){status.Text="Rank must be 0–1000 or blank for Unknown.";return false;}
        bool Number(TextBox box,out double? value){value=null;if(box.Text.Trim().Length==0)return true;if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||!double.IsFinite(n)||n<0||n>10000)return false;value=n;return true;}
        if(!Number(instrument,out var i)||!Number(singing,out var s)||!Number(focus,out var f)||!Number(level,out var l)||l.HasValue&&(l<1||l>100||l!=Math.Floor(l.Value))){status.Text="Use nonnegative modifiers; caster level 1–100. Blank remains Unknown.";return false;}
        draft.Instrument=i;draft.Singing=s;draft.Focus=f;draft.EffectiveCasterLevel=l.HasValue?(int)l:null;draft.AaContext=aa.Text.Trim();return true;
    }
    private void Dirty(){if(loading||Save==null)return;bool valid=Validate();Save.IsEnabled=valid&&Key(draft)!=baseline;if(valid)status.Text="";}
}
