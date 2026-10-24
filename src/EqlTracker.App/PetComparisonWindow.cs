using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private PetComparisonWindow? petComparisonWindow;
    private void OpenPetComparison()
    {
        if(petComparisonWindow!=null){petComparisonWindow.Activate();return;}
        if(SelectedPetInventory is not {} build)return;
        petComparisonWindow=new(Controller,EquipmentPositions.Copy(build),()=>{petKey="";RefreshBuildChoices();Refresh();},()=>SelectedPetInventory?.Id==build.Id?SelectedPetInventory:null,()=>petEditRevision+(petTheory?.Revision??0)){Owner=this};
        petComparisonWindow.Closed+=(_,_)=>petComparisonWindow=null;petComparisonWindow.Show();
    }
    private void OpenPetGoalWeights()
    {
        if(!PetRoleRatings.Goals.Contains(SelectedPetPreset)){PetMessage("Custom Pet weights are preserved. Choose DPS, Balanced or Tank to edit a shared goal.");return;}
        var draft=EquipmentPositions.Copy(Controller.Profile);var editor=new PetGoalWeightsDialog(draft,SelectedPetPreset){Owner=this};
        if(editor.ShowDialog()!=true)return;
        lock(Controller.Sync){Controller.Profile.PetGoalWeights=draft.PetGoalWeights;Controller.Save();}petKey=petSessionKey=petPreviewKey="";Refresh();
    }
}

internal sealed class PetComparisonWindow : FrostDialog
{
    private readonly TrackerController controller;
    private readonly PetBuild openingBuild;
    private readonly Func<PetBuild?>? workspace;
    private readonly Func<long>? workspaceRevision;
    private readonly Action saved;
    private readonly string character;
    private readonly PetContextCatalog summons;
    private PetBuffCatalog buffs=PetBuffCatalog.References();
    private PetComparisonPreferences preferences;
    private readonly ComboBox source=UI.Select([],"",200);
    internal readonly ComboBox Goal=UI.Select(PetRoleRatings.Goals,"DPS",120);
    internal readonly ListBox Results=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,BorderThickness=new(0),Padding=new(0),Margin=new(0)};
    private readonly CheckBox optimized=new(){Content="Optimized gear"};
    private FrameworkElement? gearPoolField;
    private readonly ComboBox gearPool=UI.Select(["Inventory","Best Possible"],"Inventory",135);
    private readonly ComboBox upgradeScope=UI.Select(["Selected item","Whole build"],"Selected item",135),upgradeItem=UI.Select([],"",220);
    private readonly StackPanel upgradeResults=new();
    private readonly WrapPanel summonControls=new();
    private InventorySnapshot evaluatedInventory=new();
    private Catalog evaluatedCatalog=new();
    private PetBuild evaluatedBuild=new();
    private CharacterProfile evaluatedProfile=new();
    private PetComparisonResult? currentPet;
    private CancellationTokenSource? upgradeCancel;
    private long upgradeRequest;
    internal readonly Button UpgradeButton=UI.Button("Find up to 5 upgrades",()=>{});
    internal readonly StackPanel Details=new();
    internal Expander SelectedDetails { get; }
    internal Expander UpgradeDetails { get; }
    internal int UpgradeCount { get; private set; }
    internal Task ReviewUpgrades(bool whole){if(!controller.Demo)throw new InvalidOperationException("Synthetic review only");upgradeScope.SelectedIndex=whole?1:0;return FindUpgrades();}
    private string detailPet="";
    internal readonly TextBlock Status=UI.Text("",11,"SecondaryText");
    private readonly ComboBox spell=UI.Select([],"",260),rank=UI.Select(Enumerable.Range(0,11).Select(n=>n.ToString()),"0",60);
    private readonly TextBox focus=UI.Input("",190);
    private readonly CheckBox learned=new(){Content="Learning confirmed"},rankKnown=new(){Content="Rank confirmed"};
    private readonly TextBox duration=UI.Input("",74),defense=UI.Input("",74),resist=UI.Input("",74),incoming=UI.Input("",74),target=UI.Input("",145);
    private readonly CheckBox external=new(){Content="Include external buffs"};
    private readonly Dictionary<string,List<PetComparisonResult>> cache=[];
    private List<PetMeasuredSample> history=[];
    private long historyRevision;
    private IReadOnlyList<Encounter> lastHistory=[];
    private bool historyReady;
    private readonly Button saveButton;
    private readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private readonly DispatcherTimer revisionTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private CancellationTokenSource? cancellation;
    private readonly CancellationTokenSource lifetime=new();
    private long request;
    private string revision="",evaluatedContext="";
    private readonly DispatcherTimer updatingDelay=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private bool pending;
    private string requestedContext="";
    private PetBuild ComparisonSource()
    {
        var owner=controller.Profile.ActiveLoadout;string choice=(source.SelectedItem as SourceChoice)?.Id??"workspace";
        return choice=="workspace"?workspace?.Invoke()??openingBuild:choice=="current"?PetInventoryEditing.Confirmed(owner,controller.Catalog):owner.PetBuilds.FirstOrDefault(b=>b.Id==choice)??openingBuild;
    }
    private string ContextVersion
    {
        get
        {
            var profile=controller.Profile;var owner=profile.ActiveLoadout;var build=ComparisonSource();var catalog=controller.EquipmentView?.Catalog??controller.Catalog;
            var keys=build.Items.Select(i=>i.ItemKey).Concat(build.SlotConfigurations.SelectMany(s=>s.Effects).Select(e=>catalog.Find(e.SourceName)?.Key??e.SourceName)).Concat(owner.Equipment.Select(e=>catalog.Find(e.Name)?.Key??e.Name)).Distinct().ToHashSet();
            return PetComparisonEngine.Key(new{profile.Character,profile.Server,Build=PetWorkspaceDraft.Key(build),build.PetLevel,owner.Classes,owner.Level,owner.Race,owner.RaceLevel,owner.Deity,
                OwnerEquipment=EquipmentPositions.Fingerprint(EquipmentPositions.For(owner,false)),PetAAs=owner.PetStatProfiles.Select(p=>p.AAs),preferences,DpsWeights=PetRoleRatings.Weights(profile,"DPS"),DefenseWeights=PetRoleRatings.Weights(profile,"Tank"),mode,buffs.Version,
                Metadata=catalog.Items.Where(i=>mode==PetEquipmentMode.BestPossible||mode==PetEquipmentMode.Inventory||keys.Contains(i.Key)).OrderBy(i=>i.Key).Select(i=>new{i.Key,i.Name,i.Classes,i.Races,i.Slots,i.Deity,i.RequiredLevel,i.EffectActivationLevel,i.Stats,i.Effects,i.EffectEvidence,i.Display,i.MetadataComplete,i.MetadataVersion,i.TwoHanded,i.IntroducedExpansion,i.ContentAvailability}),
                Inventory=mode==PetEquipmentMode.Inventory?controller.Inventory.Entries:null,
                History=(controller.Published?.History??[]).Select(f=>new{f.Id,f.Start,f.End,f.Zone,f.PetType,f.PetName,f.Weapons,f.BuffNotes,f.PetDamage,PetEvents=f.Events.Where(e=>e.Owner=="Pet").Select(e=>new{e.Time,e.Actor,e.Target,e.Ability,e.Damage,e.Outcome})}),controller.PetResearchRevision});
        }
    }
    internal long CompletedRequests {get;private set;}
    internal bool IsUpdating=>pending;
    private bool loading=true,closed;
    private PetEquipmentMode mode;
    internal List<PetComparisonResult> Rows { get; private set; }=[];
    internal void StopReviewPolling(){if(!controller.Demo)throw new InvalidOperationException("Synthetic review only.");revisionTimer.Stop();debounce.Stop();}
    internal static readonly List<object> Timings=[];
    private sealed record SourceChoice(string Id,string Name){public override string ToString()=>Name;}
    internal PetComparisonWindow(TrackerController controller,PetBuild build,Action saved,Func<PetBuild?>? workspace=null,Func<long>? workspaceRevision=null) : base("Compare pets",1180,820)
    {
        this.workspace=workspace;this.workspaceRevision=workspaceRevision;this.controller=controller;openingBuild=EquipmentPositions.Copy(build);this.saved=saved;character=controller.Profile.Character+"|"+controller.Profile.Server;preferences=EquipmentPositions.Copy(controller.Profile.PetComparison);
        string path=Path.Combine(AppContext.BaseDirectory,"data","pet-context.json");summons=File.Exists(path)?PetContextCatalog.Load(path):new();
        source.ItemsSource=new[]{new SourceChoice("workspace","Selected workspace · "+build.Name),new SourceChoice("current","Current build")}.Concat(controller.Profile.ActiveLoadout.PetBuilds.Select(b=>new SourceChoice(b.Id,b.Name+(b.IsTheorycraft?" · Theorycraft":"")))).ToArray();source.SelectedIndex=0;
        Goal.SelectedItem=PetRoleRatings.Normalize(preferences.Goal);external.IsChecked=preferences.ExternalBuffs;
        duration.Text=N(preferences.Encounter.Seconds);defense.Text=N(preferences.Encounter.TargetDefense);resist.Text=N(preferences.Encounter.TargetResist);incoming.Text=N(preferences.Encounter.IncomingDps);target.Text=preferences.Encounter.Target;
        duration.ToolTip="Encounter duration in seconds, or blank for Unknown. Unknown cadence does not become a fabricated DPS estimate.";defense.ToolTip="Known target defense, or blank. Not inferred from a target's name.";resist.ToolTip="Known target resistance, or blank; no default resistance is invented.";incoming.ToolTip="Known incoming damage per second, or blank. This alone is not a verified pet time-to-live formula.";
        optimized.ToolTip="Preview a new equipment assignment. Current inventory, ranks and equipment are unchanged.";
        gearPool.ToolTip="Inventory: recorded equippable copies at their current ranks. Best Possible: compatible catalog items at +10 in selected expansions.";
        gearPool.Visibility=Visibility.Visible;
        optimized.Click+=(_,_)=>{if(gearPoolField!=null)gearPoolField.Visibility=optimized.IsChecked==true?Visibility.Visible:Visibility.Collapsed;SetMode(optimized.IsChecked!=true?PetEquipmentMode.IntendedSlots:gearPool.SelectedIndex==1?PetEquipmentMode.BestPossible:PetEquipmentMode.Inventory);};
        gearPool.SelectionChanged+=(_,_)=>{if(!loading&&optimized.IsChecked==true)SetMode(gearPool.SelectedIndex==1?PetEquipmentMode.BestPossible:PetEquipmentMode.Inventory);};
        gearPoolField=(FrameworkElement)UI.Field("Equipment",gearPool);gearPoolField.Visibility=Visibility.Collapsed;
        var expansions=ExpansionSelector();
        var settings=new WrapPanel();foreach(var c in new UIElement[]{UI.Field("Pet inventory",source),UI.Field("Goal",Goal),UI.ActionField(optimized),gearPoolField,expansions})settings.Children.Add(c);
        var encounter=new WrapPanel();foreach(var c in new UIElement[]{UI.Field("Target",target),UI.Field("Duration (s)",duration),UI.Field("Target defense",defense),UI.Field("Target resist",resist),UI.Field("Incoming DPS",incoming)})encounter.Children.Add(c);
        var root=UI.Stack(settings,Status);
        var columns = new FrameworkElementFactory(typeof(StackPanel)); columns.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); Results.ItemsPanel = new ItemsPanelTemplate(columns);
        void FitCards() => Results.MaxWidth = Math.Max(240, ActualWidth - 60); SizeChanged += (_,_) => FitCards(); Loaded += (_,_) => FitCards();
        Results.ItemTemplate=new DataTemplate{VisualTree=new FrameworkElementFactory(typeof(PetResultCard))};
        var itemStyle=new Style(typeof(ListBoxItem));itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));Results.ItemContainerStyle=itemStyle;
        ScrollViewer.SetVerticalScrollBarVisibility(Results,ScrollBarVisibility.Disabled);ScrollViewer.SetHorizontalScrollBarVisibility(Results,ScrollBarVisibility.Auto);
        Results.SelectionChanged+=(_,_)=>ShowSelected();
        source.ToolTip="Compare Current or a named Pet plan. The selected workspace refreshes with equipment evidence; source builds remain unchanged.";
        root.Children.Add(Results);
        var selection=summonControls;spell.DisplayMemberPath="Label";spell.SelectedValuePath="Id";
        foreach(var c in new UIElement[]{UI.Field("Summon",spell),UI.Field("Rank",rank),UI.Field("Focus (documented only)",focus),UI.ActionField(learned),UI.ActionField(rankKnown)})selection.Children.Add(c);
        SelectedDetails=FocusedLayout.Disclosure("Selected pet · setup, equipment & evidence",Details,"Select a pet card. Expand to review summon settings, equipment, buffs, weights and measured benchmarks.");root.Children.Add(SelectedDetails);
        var advanced=UI.Stack(encounter,UI.Row(UI.Button("Buff setup",EditBuffs),UI.Button("Weights",EditWeights),UI.Button("Measurement checklist",()=>{new MeasurementGuideDialog{Owner=this}.ShowDialog();})),external);
        root.Children.Add(FocusedLayout.Disclosure("Encounter & buff assumptions",advanced,"Unknown inputs stay unknown. Preference points are not predicted DPS."));
        UpgradeButton.Click+=async(_,_)=>await FindUpgrades();
        upgradeScope.ToolTip="Compare owned replacements for one recorded Pet item, or the whole effective build. At most five results, ordered by additional ranks needed.";
        upgradeItem.ToolTip="Choose the Pet inventory item to replace. Storage items are not treated as equipped.";upgradeItem.DisplayMemberPath="Name";upgradeItem.SelectedValuePath="Id";
        upgradeScope.SelectionChanged+=(_,_)=>{upgradeItem.Visibility=upgradeScope.SelectedIndex==0?Visibility.Visible:Visibility.Collapsed;CancelUpgrades();};upgradeItem.SelectionChanged+=(_,_)=>CancelUpgrades();
        UpgradeDetails=FocusedLayout.Disclosure("Rank-efficient inventory upgrades",UI.Stack(UI.Row(upgradeScope,upgradeItem),UpgradeButton,upgradeResults),"Find up to five preference improvements at recorded rank or +0–10. No motes or inventory changes are performed.");root.Children.Add(UpgradeDetails);
        if(TrackerController.PetResearchEnabled) root.Children.Add(UI.Button("Pet data",()=>new PetResearchWindow(controller){Owner=this}.Show()));
        Editor.Children.Add(root);
        saveButton=UI.Button("Save as Pet plan",SaveSelected);saveButton.IsEnabled=false;Actions.Children.Add(saveButton);Actions.Children.Add(UI.Button("Close",Close));
        source.SelectionChanged+=(_,_)=>Queue();Goal.SelectionChanged+=(_,_)=>{preferences.Goal=Goal.SelectedItem?.ToString()??"DPS";Queue();};
        external.Click+=(_,_)=>{preferences.ExternalBuffs=external.IsChecked==true;Queue();};
        spell.SelectionChanged+=(_,_)=>{if(loading||Results.SelectedItem is not PetComparisonResult row||spell.SelectedItem is not PetSummonOption value)return;preferences.SelectedSummons[row.Pet]=value.Id;Queue();};
        rank.SelectionChanged+=(_,_)=>SetupChanged();focus.LostKeyboardFocus+=(_,_)=>SetupChanged();learned.Click+=(_,_)=>SetupChanged();rankKnown.Click+=(_,_)=>SetupChanged();
        foreach(var t in new[]{duration,defense,resist,incoming,target})t.TextChanged+=(_,_)=>{if(loading)return;try{var candidate=new PetEncounterAssumptions{Seconds=Number(duration),TargetDefense=Number(defense),TargetResist=Number(resist),IncomingDps=Number(incoming),Target=target.Text.Trim()};candidate.Validate();preferences.Encounter=candidate;Queue();}catch(ArgumentException e){Status.Text=e.Message+" Previous results retained.";}};
        debounce.Tick+=async(_,_)=>{debounce.Stop();await Recalculate();};
        updatingDelay.Tick+=(_,_)=>{updatingDelay.Stop();if(pending)Status.Text="Updating comparison · previous completed results retained.";};
        revisionTimer.Tick+=(_,_)=>{string next=ContextVersion;if(next!=revision){Queue(false);}};
        Loaded+=async(_,_)=>{loading=false;revisionTimer.Start();await LoadReferences();await Recalculate();};
        Closed+=(_,_)=>{closed=true;lifetime.Cancel();request++;debounce.Stop();revisionTimer.Stop();updatingDelay.Stop();cancellation?.Cancel();cancellation?.Dispose();upgradeCancel?.Cancel();upgradeCancel?.Dispose();};
    }
    private static string N(double? number)=>number?.ToString("0.##",CultureInfo.InvariantCulture)??"";
    private static double? Number(TextBox input){if(input.Text.Trim().Length==0)return null;if(!double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||!double.IsFinite(n))throw new ArgumentException("Enter a finite number, or leave blank for Unknown.");return n;}
    private bool SameCharacter=>controller.Profile.Character+"|"+controller.Profile.Server==character;
    private void Queue(bool persist=true)
    {
        if(loading||closed)return;if(!SameCharacter){Close();return;}
        string next=ContextVersion;if(next==requestedContext&&(pending||next==evaluatedContext))return;
        requestedContext=revision=next;request++;cancellation?.Cancel();saveButton.IsEnabled=false;
        if(!pending){pending=true;updatingDelay.Start();}
        if(persist){lock(controller.Sync){controller.Profile.PetComparison=EquipmentPositions.Copy(preferences);controller.Save();}}
        debounce.Stop();debounce.Start();
    }
    internal void SetMode(PetEquipmentMode value){mode=value;bool previous=loading;loading=true;optimized.IsChecked=value is PetEquipmentMode.Inventory or PetEquipmentMode.BestPossible;gearPool.SelectedIndex=value==PetEquipmentMode.BestPossible?1:0;if(gearPoolField!=null)gearPoolField.Visibility=optimized.IsChecked==true?Visibility.Visible:Visibility.Collapsed;loading=previous;Queue(false);}
    internal async Task LoadReferences()
    {
        string folder=controller.Profile.GameFolder;
        var loaded=await Task.Run(()=>{string file=Path.Combine(folder,"spells_us.txt");try{return folder.Length>0&&File.Exists(file)?PetClientSpellAdapter.ParseFile(file,lifetime.Token):PetBuffCatalog.References();}catch(IOException){return PetBuffCatalog.References();}catch(UnauthorizedAccessException){return PetBuffCatalog.References();}catch(OperationCanceledException){return PetBuffCatalog.References();}});
        var relevant=GearMechanicsEvidence.RelevantAAs(true).Select(a=>a.Name).ToHashSet();
        foreach(var aa in controller.StatEvidenceSnapshot().AAs.Values.Where(a=>a.Rank>0&&relevant.Contains(a.Name)))
            if(!loaded.Buffs.Any(b=>b.Name==aa.Name||b.RelatedAa==aa.Name))loaded.Buffs.Add(new(){Id="aa:"+Identity.Key(aa.Name),Name=aa.Name,Aa=true,ConfirmedRank=aa.Rank,Source=GearMechanicsEvidence.AaSource+" · recorded AA rank observation"});
        foreach(var aa in controller.Profile.ActiveLoadout.PetStatProfiles.SelectMany(p=>p.AAs).Where(a=>a.Target=="Pet"&&a.Confirmed&&a.Rank>0))
            if(!loaded.Buffs.Any(b=>b.Name==aa.Name||b.RelatedAa==aa.Name))loaded.Buffs.Add(new(){Id="aa:"+Identity.Key(aa.Name),Name=aa.Name,Aa=true,ConfirmedRank=aa.Rank,Source=aa.Source+" · confirmed Pet stat profile; numerical response remains separate"});
        var evidence=controller.StatEvidenceSnapshot();foreach(var buff in loaded.Buffs.Where(b=>b.Aa)){var observed=evidence.AAs.Values.FirstOrDefault(a=>a.Name==buff.Name||a.Name==buff.RelatedAa);if(observed?.Rank is {} rank)buff.ConfirmedRank=rank;}
        List<ItemDefinition> items;lock(controller.Sync){items=controller.Inventory.Entries.Where(e=>!e.IsAugment).Select(e=>controller.Catalog.Find(e.Name,e.GameId==0?null:e.GameId)).Where(i=>i!=null).Cast<ItemDefinition>().DistinctBy(i=>i.Key).ToList();}
        foreach(var item in items)foreach(string effect in item.Effects.Where(e=>ExaltationPlanner.EffectType(e)=="Click")){var spell=loaded.Buffs.Where(b=>effect.Contains(b.Name,StringComparison.OrdinalIgnoreCase)).OrderByDescending(b=>b.Name.Length).FirstOrDefault();if(spell!=null)spell.SourceItem=item.Name;}
        loaded.Version+=":"+PetComparisonEngine.Key(loaded.Buffs);buffs=loaded;
    }
    internal async Task Recalculate()
    {
        if(closed||!SameCharacter)return;if(!pending){pending=true;updatingDelay.Start();}requestedContext=revision=ContextVersion;saveButton.IsEnabled=false;debounce.Stop();cancellation?.Cancel();cancellation?.Dispose();cancellation=new();var token=cancellation.Token;long id=++request;
        CharacterProfile profile;Catalog catalog;PetBuild build;string expectedContext;InventorySnapshot inventory;var frozenBuffs=buffs;
        lock(controller.Sync)
        {
            expectedContext=ContextVersion;inventory=EquipmentPositions.Copy(controller.EquipmentView?.Inventory??controller.Inventory);profile=EquipmentPositions.Copy(controller.Profile);profile.PetComparison=EquipmentPositions.Copy(preferences);
            catalog=controller.EquipmentView?.Catalog??new Catalog{Items=controller.Catalog.Items.Select(EquipmentPositions.Copy).ToList(),Quests=controller.Catalog.Quests.ToList()};
            string choice=(source.SelectedItem as SourceChoice)?.Id??"workspace";
            if (choice == "workspace" && workspace != null && workspace() == null) { Status.Text = "Source workspace changed. Choose Current or a saved build to continue."; pending=false;updatingDelay.Stop();return; }
            build=EquipmentPositions.Copy(choice=="workspace"&&workspace!=null?workspace()!:choice=="workspace"?(profile.ActiveLoadout.ConfirmedPetInventory?.Id==openingBuild.Id?profile.ActiveLoadout.ConfirmedPetInventory:profile.ActiveLoadout.PetBuilds.FirstOrDefault(b=>b.Id==openingBuild.Id)??openingBuild):choice=="current"?PetInventoryEditing.Confirmed(profile.ActiveLoadout,catalog):profile.ActiveLoadout.PetBuilds.FirstOrDefault(b=>b.Id==choice)??openingBuild);
            // Historical aggregation is revision-driven, never part of pointer/slider updates.

        }
        var historicalInputs=controller.Published?.History??[];if(!ReferenceEquals(historicalInputs,lastHistory)){lastHistory=historicalInputs;historyRevision++;history=[];historyReady=false;}
        bool ready=historyReady;List<PetMeasuredSample>? hydratedHistory=null;var observed=history.ToList();string key=expectedContext;var clock=Stopwatch.StartNew();
        try
        {
            var rows=cache.TryGetValue(key,out var cached)?cached:await Task.Run(()=>{token.ThrowIfCancellationRequested();var measurements=ready?observed:PetComparisonEngine.History(historicalInputs);hydratedHistory=measurements;return PetComparisonEngine.Evaluate(profile,build,catalog,summons,frozenBuffs,measurements,mode,token,inventory:inventory);},token);
            if(closed||token.IsCancellationRequested||id!=request||!SameCharacter)return;
            if(ContextVersion!=expectedContext){Queue(false);return;}evaluatedContext=expectedContext;
            if(hydratedHistory!=null){history=hydratedHistory;historyReady=true;}
            if(cache.Count>=8)cache.Clear();cache[key]=rows;Rows=rows;
            evaluatedProfile=profile;evaluatedInventory=inventory;evaluatedCatalog=catalog;evaluatedBuild=PetEquipmentProjection.Prepare(build,profile.ActiveLoadout,catalog);
            currentPet=(mode==PetEquipmentMode.IntendedSlots?rows:await Task.Run(()=>PetComparisonEngine.Evaluate(profile,build,catalog,summons,frozenBuffs,observed,PetEquipmentMode.IntendedSlots,token),token)).FirstOrDefault(r=>r.Pet==build.PetType);
            if(closed||token.IsCancellationRequested||id!=request||ContextVersion!=expectedContext)return;
            foreach(var row in rows){row.CurrentDps=currentPet?.DisplayDps;row.CurrentHp=currentPet?.Hp;}
            if(TrackerController.PetResearchEnabled) { var research=controller.PetResearchSnapshot(); foreach(var row in rows) row.Research=PetResearch.Summary(research,row.Pet); }
            var previousItem=upgradeItem.SelectedValue;loading=true;upgradeItem.ItemsSource=evaluatedBuild.Items;upgradeItem.SelectedValue=previousItem??evaluatedBuild.Items.FirstOrDefault()?.Id;loading=false;
            CancelUpgrades();saveButton.IsEnabled=rows.Count>0;string selected=(Results.SelectedItem as PetComparisonResult)?.Pet??build.PetType;
            loading=true;Results.ItemsSource=rows;Results.SelectedItem=rows.FirstOrDefault(r=>r.Pet==selected)??rows.FirstOrDefault();loading=false;ShowSelected();
            Status.Text=rows.Count==0?"No documented summons match the character's confirmed classes and level. Confirm those facts in the active build.":(mode==PetEquipmentMode.IntendedSlots?"Same effective gear · attainable pet levels":"Best supported preference assignments · preview only")+" · "+profile.PetComparison.Goal+" · "+rows.Count+" options. "+(rows.All(r=>r.Outcome==null)?"Insufficient evidence for a predicted DPS winner.":"Supported outcomes are separate from historical observations.");
            CompletedRequests++;
            Timings.Add(new{Milliseconds=clock.Elapsed.TotalMilliseconds,Goal=profile.PetComparison.Goal,Mode=mode.ToString(),Rows=rows.Count,CacheHit=ReferenceEquals(rows,cached)});
        }
        catch(OperationCanceledException){}
        catch(Exception e) when(e is ArgumentException or InvalidOperationException){if(id==request&&!closed)Status.Text=e.Message+" Previous completed results retained.";}
        finally{if(id==request){pending=false;updatingDelay.Stop();}}
    }
    private void SetupChanged()
    {
        if(loading||Results.SelectedItem is not PetComparisonResult row)return;
        int value=int.TryParse(rank.SelectedItem?.ToString(),out int n)?n:row.Plan.SummonRank;
        preferences.Summons[row.Spell.Id]=new(){SpellId=row.Spell.Id,Rank=value,LearningConfirmed=learned.IsChecked==true,RankConfirmed=rankKnown.IsChecked==true,Focus=focus.Text.Trim()};Queue();
    }
    internal void ShowSelected()
    {
        if(loading)return;if(LogicalTreeHelper.GetParent(summonControls) is ContentControl parent)parent.Content=null;
        var expanded=Details.Children.OfType<Expander>().ToDictionary(e=>e.Header?.ToString()??"",e=>e.IsExpanded);Details.Children.Clear();if(Results.SelectedItem is not PetComparisonResult row)return;if(detailPet!=row.Pet)expanded.Clear();detailPet=row.Pet;
        CancelUpgrades();
        loading=true;try{spell.ItemsSource=PetComparisonEngine.Eligible(summons,controller.Profile.ActiveLoadout).Where(s=>s.PetType==row.Pet).ToList();spell.SelectedValue=row.Spell.Id;string value=row.Plan.SummonRank.ToString();if(!rank.Items.Contains(value))rank.Items.Add(value);rank.SelectedItem=value;learned.IsChecked=row.Scenario.Summon.LearningConfirmed;rankKnown.IsChecked=row.Scenario.Summon.RankConfirmed;focus.Text=row.Plan.Focus;}finally{loading=false;}
        Details.Children.Add(FocusedLayout.Disclosure("Summon settings",summonControls,"Separate summon/rank settings per pet family; missing learning remains unverified."));
        var headline=UI.Text(row.Pet+" · "+string.Join('/',row.Definition.Classes),18,"Accent",true);var badge=new ActorBadge{Width=32,Height=32};badge.Set("Pet",true,row.Pet+" · "+string.Join("/",row.Definition.Classes),row.Pet);Details.Children.Add(UI.Row(badge,headline));
        Details.Children.Add(UI.Text(row.Summon+" · level "+row.Level+" · "+row.Definition.Abilities,12));
        Details.Children.Add(UI.Text(row.Plan.LevelEvidence,11,"SecondaryText"));
        Details.Children.Add(FocusedLayout.Disclosure("Innate baselines & mechanics",UI.Stack(row.Definition.BaseStats.Select(p=>(UIElement)UI.Text(p.Key+": "+(p.Value.Value?.ToString("0.##")??"Unresolved")+" · "+p.Value.Evidence+" · "+p.Value.Explanation,11)).ToArray()),"Innate stats are distinct from equipment totals, spell rank, summon level and owner stats."));
        if(row.Definition.Backstab)Details.Children.Add(FocusedLayout.Disclosure("Additional result · From behind",UI.Text(row.FromBehind+". Baseline grants no positional damage. Use this comparison to decide whether managing position is worthwhile.",12),"Only backstab-capable pets receive this additional scenario."));
        Details.Children.Add(UI.Text(row.Scenario.Goal=="Balanced"?(row.StrongTradeoff?"Supported damage/survival tradeoff":"No supported cross-pet dominance claim; compare strengths and missing evidence"):PetRoleRatings.Explain(row.Scenario.Goal),12,"Melee",true));
        Details.Children.Add(UI.Text("Equipment preference: "+row.Guidance,12));
        Details.Children.Add(FocusedLayout.Disclosure("Weights & arithmetic",UI.Stack(UI.Text("Damage weights: "+RatingExplanation.Formula(PetRoleRatings.Weights(controller.Profile,"DPS")),11),UI.Text("Defense weights: "+RatingExplanation.Formula(PetRoleRatings.Weights(controller.Profile,"Tank")),11),UI.Text("Equipment totals: "+string.Join(" · ",row.Equipment.Totals.Stats.Select(p=>p.Key+": "+p.Value.ToString("0.##"))),11),UI.Button("Edit weights",EditWeights)),"Editable preference priorities, separate from measured DPS and HP."));
        if(row.EquipmentRequirements.Count>0)Details.Children.Add(FocusedLayout.Disclosure("Equipment needed",UI.Stack(row.EquipmentRequirements.Select(n=>(UIElement)UI.Text(n,11)).Append(UI.Text(row.AssignmentSearchComplete?"Assignment search complete.":"Bounded preference search; global optimum is not established.",11,"SecondaryText")).ToArray()),"Required items, ranks and transfers; saving creates no ownership."));
        if(row.EquipmentTradeoffs.Count>0)Details.Children.Add(FocusedLayout.Disclosure("Balanced equipment tradeoffs",UI.Stack(row.EquipmentTradeoffs.Select(p=>(UIElement)UI.Text(string.Join(" · ",p.Items.Select(i=>i.Slot+": "+i.Name+" +"+i.Rank)),11)).ToArray()),"Other nondominated preference assignments. No combined Balanced score or predicted winner."));
        Details.Children.Add(FocusedLayout.Disclosure("October 22 mechanics", UI.Stack(OctoberContent.Mechanics.Select(m => (UIElement)UI.Text(m.Name+": "+m.Description,11)).Append(UI.Text(OctoberContent.PatchNotes,11)).ToArray()), "Current patch evidence; unpublished coefficients do not become predictions."));
        Details.Children.Add(UI.Button("Record benchmark",RecordBenchmark));
        foreach(var benchmark in preferences.Benchmarks.Where(b=>b.Pet==row.Pet).OrderByDescending(b=>b.RecordedAt).Take(5))Details.Children.Add(UI.Text("Benchmark: "+benchmark.Label+" · DPS "+(benchmark.Dps?.ToString("N1")??"Unknown")+" · HP "+(benchmark.Hp?.ToString("N0")??"Unknown")+" · "+(benchmark.MechanicsVersion!=PetDefinition.Version?"Historical / other patch":benchmark.ContextConfirmed?"Confirmed test context":"Context unverified"),11,"SecondaryText"));
        var fit=UI.Stack(row.Fits.Select(f=>(UIElement)UI.Text(f.Item+" · "+f.Slot+" · "+PetComparisonEngine.AccessText(f.Access)+"\n"+(f.Equipped?"In effective equipment · ":"Not equipped · ")+f.Reason,11,f.Reason=="Eligible"?"Foreground":"SecondaryText")).ToArray());
        Details.Children.Add(FocusedLayout.Disclosure("Equipment fit & assignments",fit,"Pet classes and owner classes are separate valid routes; bows, levels, slots and effect constraints still apply.",true));
        Details.Children.Add(FocusedLayout.Disclosure("Buffs & stacking",UI.Stack(row.Buffs.Select(b=>(UIElement)UI.Text(b.Name+" · "+b.State+"\n"+b.Detail+"\n"+b.Source,11)).ToArray()),buffs.Status));
        Details.Children.Add(FocusedLayout.Disclosure("Historical measured DPS",row.Observed.Count==0?UI.Text("No retained pet observations for this type. Missing samples are not zero DPS.",11):UI.Stack(row.Observed.Select(h=>(UIElement)UI.Text($"{h.Dps:N1} observed DPS · {h.Damage:N0} damage / {h.Seconds:N1}s · {h.Fights} fights · {h.Zone}\n{h.Targets}\n{h.Conditions}",11)).ToArray()),"Historical observations have their recorded conditions; unmatched samples never predict the current build."));
        if(TrackerController.PetResearchEnabled)
        {
            var phases=controller.PetResearchSnapshot().Samples.Values.Where(s=>s.Context.Pet==row.Pet).OrderByDescending(s=>s.Start).Take(5);
            Details.Children.Add(FocusedLayout.Disclosure("Measured buff phases",UI.Stack(phases.Select(s=>(UIElement)UI.Text(s.Start.ToString("MMM dd HH:mm")+" · "+s.Targets+"\n"+string.Join("\n",s.BuffPeriodSeconds.Where(p=>p.Value>0).Select(p=>p.Key+": "+(s.BuffPeriodDamage.GetValueOrDefault(p.Key)/p.Value).ToString("0.0")+" DPS · "+s.BuffPeriodDamage.GetValueOrDefault(p.Key)+" damage / "+p.Value.ToString("0.0")+"s")),11)).Append(UI.Text("Targets and durations can differ. Historical phase DPS does not establish causal buff multipliers or a predicted winner.",11,"SecondaryText")).ToArray()),"Original encounter totals and rate denominators are preserved. Uncertain transitions remain unresolved."));
        }
        Details.Children.Add(FocusedLayout.Disclosure("Evidence & missing inputs",UI.Stack(row.Missing.Select(m=>(UIElement)UI.Text("• "+m,11,"SecondaryText")).Concat(new[]{(UIElement)UI.Text(row.Definition.Source+"\n"+row.Spell.Url+" revision "+row.Spell.Revision+"\n"+PetDefinition.Version+"\n"+row.Scenario.Encounter.Explanation,11)}).ToArray()),"Documented kits; computed equipment; assumptions and unresolved mechanics remain separate.",false));foreach(var disclosure in Details.Children.OfType<Expander>())if(expanded.TryGetValue(disclosure.Header?.ToString()??"",out bool open))disclosure.IsExpanded=open;Usability.Tooltips(Details);
    }
    private void EditBuffs(){var dialog=new PetBuffSetupDialog(controller.Profile.ActiveLoadout,buffs,preferences){Owner=this};if(dialog.ShowDialog()==true)Queue();}
    private FrameworkElement ExpansionSelector()
    {
        var menu=new ContextMenu();var button=OverlayMenus.Button(menu,"Comparison expansion filter","All ▾");button.Width=110;button.Height=28;
        void Update(){var chosen=ItemExpansionFilter.Choices.Where(preferences.Expansions.Contains).ToList();button.Content=(chosen.Count==4?"All":chosen.Count==0?"None":chosen.Count==1?chosen[0]:string.Join(" / ",chosen.Select(n=>n=="Unknown"?"?":n[..1])))+" ▾";button.ToolTip="Compare equipment from: "+string.Join(", ",preferences.Expansions)+". Classic is current; Kunark and Velious are future scenarios.";}
        foreach(string expansion in new[]{"Classic","Kunark","Velious","Unknown"})
        {
            var entry=new MenuItem{Header=expansion+(expansion is "Kunark" or "Velious"?" · Future":""),IsCheckable=true,IsChecked=preferences.Expansions.Contains(expansion),StaysOpenOnClick=true};
            entry.Click+=(_,_)=>{if(entry.IsChecked){if(!preferences.Expansions.Contains(expansion))preferences.Expansions.Add(expansion);}else preferences.Expansions.Remove(expansion);Update();Queue();};menu.Items.Add(entry);
        }
        Update();return (FrameworkElement)UI.Field("Expansion",button);
    }
    private void CancelUpgrades(){upgradeRequest++;upgradeCancel?.Cancel();upgradeResults.Children.Clear();UpgradeCount=0;UpgradeButton.IsEnabled=Rows.Count>0;}
    private async Task FindUpgrades()
    {
        if(Results.SelectedItem is not PetComparisonResult row)return;
        CancelUpgrades();upgradeCancel?.Dispose();upgradeCancel=new();var token=upgradeCancel.Token;long id=++upgradeRequest;string version=ContextVersion;
        var profile=EquipmentPositions.Copy(evaluatedProfile);profile.PetComparison.Goal=Goal.SelectedItem?.ToString()??"DPS";
        var build=EquipmentPositions.Copy(evaluatedBuild);build.PetType=row.Pet;build.PetLevel=row.Plan.PetLevel;
        string? selected=upgradeScope.SelectedIndex==0?upgradeItem.SelectedValue?.ToString():null;
        if(upgradeScope.SelectedIndex==0&&selected==null){upgradeResults.Children.Add(UI.Text("Choose a recorded Pet item first.",11));return;}
        UpgradeButton.IsEnabled=false;upgradeResults.Children.Add(UI.Text("Checking owned copies and +0–10…",11,"SecondaryText"));
        try
        {
            var options=await Task.Run(()=>{var pool=PetEquipmentOptions.Pool(build,profile.ActiveLoadout,evaluatedCatalog,evaluatedInventory,PetEquipmentMode.Inventory,preferences.Expansions);return PetEquipmentOptions.Upgrades(build,profile.ActiveLoadout,evaluatedCatalog,profile,pool,selected,token);},token);
            if(closed||token.IsCancellationRequested||id!=upgradeRequest||version!=ContextVersion)return;
            upgradeResults.Children.Clear();
            UpgradeCount=options.Count;
            if(options.Count==0)upgradeResults.Children.Add(UI.Text("No supported preference improvement found through +10 for this scope. Conditional mechanics can prevent a confirmed result.",11,"SecondaryText"));
            foreach(var option in options)upgradeResults.Children.Add(UI.Card(UI.Stack(UI.Text(option.Name+" +"+option.RequiredRank+" · "+(option.RankDelta==0?"Ready at recorded rank":"+"+option.RankDelta+" ranks needed"),12,"Accent",true),UI.Text("Replaces: "+option.Replaces,11),UI.Text("Owned: +"+option.CurrentRank+" · Required: +"+option.RequiredRank+" · Preference: "+option.DamagePoints.ToString("+0.##;-0.##;0")+(profile.PetComparison.Goal=="Balanced"?" damage / "+option.DefensePoints.ToString("+0.##;-0.##;0")+" defense":" points"),11,"Mint"),UI.Text(option.Evidence,10,"SecondaryText"))));
        }
        catch(OperationCanceledException){}
        finally{if(id==upgradeRequest&&!closed)UpgradeButton.IsEnabled=true;}
    }
    private void RecordBenchmark()
    {
        if(Results.SelectedItem is not PetComparisonResult row)return;
        var fights=(controller.Published?.History??controller.Combat.Encounters).Where(f=>f.Finished&&f.PetType==row.Pet).OrderByDescending(f=>f.Start).Take(100).Select(CombatSnapshots.Copy).ToList();
        var dialog=new PetBenchmarkDialog(row,fights){Owner=this};
        if(dialog.ShowDialog()!=true||dialog.Benchmark==null)return;
        preferences.Benchmarks.Add(dialog.Benchmark);Queue();
    }
    private void EditWeights(){var draft=EquipmentPositions.Copy(controller.Profile);var dialog=new PetGoalWeightsDialog(draft,Goal.SelectedItem?.ToString()??"DPS"){Owner=this};if(dialog.ShowDialog()!=true)return;lock(controller.Sync){controller.Profile.PetGoalWeights=draft.PetGoalWeights;controller.Save();}Queue(false);}
    private void SaveSelected()
    {
        if(!saveButton.IsEnabled||Results.SelectedItem is not PetComparisonResult row)return;
        if(ContextVersion!=evaluatedContext){Queue(false);return;}
        var dialog=new EntryDialog("Save as Pet plan",[("Name",row.Pet+" · "+preferences.Goal)],"Creates a new named plan. Current equipment, inventory ownership and combat records remain unchanged. Unsupported assumptions retain Theorycraft designation."){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        if(ContextVersion!=evaluatedContext){Queue(false);Status.Text="Build context changed while naming the plan. Review the refreshed result before saving.";return;}
        try{var plan=PetComparisonEngine.SavePlan(row,dialog.Values[0]);lock(controller.Sync){if(!SameCharacter)return;controller.Profile.ActiveLoadout.PetBuilds.Add(plan);controller.Save();}saved();Status.Text="Saved “"+plan.Name+"”"+(plan.IsTheorycraft?" · Theorycraft":" · named plan")+". Current unchanged.";}
        catch(ArgumentException e){Status.Text=e.Message;}
    }
}
