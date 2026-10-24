using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private StackPanel? petDetailsEditor;
    private PetDetailsPopup? petDetailsPopup;
    private void ClosePetDetails()=>petDetailsPopup?.Close();
    private void OpenPetDetails()
    {
        if(SelectedPetInventory is not {} build||petDetailsEditor==null)return;
        if(petDetailsPopup!=null){petDetailsPopup.Activate();return;}
        bool current=PetDestination==PetInventoryDestination.Confirmed;
        if(current)Controller.RefreshDetectedPetContext();
        LoadPetContext();
        var popup=new PetDetailsPopup(current?"Current pet details":"Pet details · "+build.Name,petDetailsEditor,petContextNote){Owner=this};petDetailsPopup=popup;
        string workspace=build.Id,character=Controller.Profile.Character+"|"+Controller.Profile.Server;
        bool loading=false;string key="",contextKey="";
        bool Valid()=>SelectedTab==TrackerTab.Pet&&SelectedPetInventory?.Id==workspace&&character==Controller.Profile.Character+"|"+Controller.Profile.Server;
        void Mode(bool follow)
        {
            if(loading||!Valid()||SelectedPetInventory is not {} selected)return;
            if(selected.FollowDetectedSummon!=follow){PetBuildEditing.Remember(selected,follow?"Use detected pet":"Override build details");selected.FollowDetectedSummon=follow;}
            if(follow)PetDetectedContext.Apply(Controller.Profile,selected,PetContextCatalog.Bundled);
            key="";contextKey="";LoadPetContext();PetEdited();
        }
        popup.Override.Click+=(_,_)=>Mode(popup.Override.IsChecked!=true);
        popup.UseDetected.Click+=(_,_)=>Mode(true);
        void RefreshPopup()
        {
            if(!Valid()||!IsVisible){popup.Close();return;}
            var selected=SelectedPetInventory!;
            bool follow=current&&selected.FollowDetectedSummon;
            loading=true;popup.Override.Visibility=popup.UseDetected.Visibility=current?Visibility.Visible:Visibility.Collapsed;popup.Override.IsChecked=!selected.FollowDetectedSummon;popup.UseDetected.IsEnabled=!follow;loading=false;
            foreach(var input in new Control[]{petElement,petSpell,petSummonTier,petFocusName,petLevel})input.IsEnabled=!follow;
            string nextContext=PetComparisonEngine.Key(new{selected.PetType,selected.SummonSpell,selected.SummonClass,selected.SummonRank,selected.SummonRankKnown,selected.PetLevel,selected.LevelMode,selected.FollowDetectedSummon,selected.Focus});
            if(nextContext!=contextKey){if(follow||!petDetailsEditor.IsKeyboardFocusWithin)LoadPetContext();contextKey=nextContext;}
            var identity=PetDetectedContext.Latest(Controller.Profile);
            popup.Sheet.SetContext(selected,current&&identity?.Element==selected.PetType?identity.Name:current?"":selected.Name);
            popup.ModeSummary.Text=current?(follow?"Following last confirmed owned pet":"Current build override")+(Controller.Profile.PetElementOverride.Length>0?" · identity override: "+Controller.Profile.PetElementOverride:""):"Independent planned summon";
            popup.Source.Text=current?(follow?"Following last confirmed owned pet":"Current build override · detected identity and DPS icons unchanged")+(identity==null?" · no owned-pet evidence":$"\n{identity.Name} · {identity.RawSummonSpell} · {identity.SummonedAt:MMM dd HH:mm:ss}\n{identity.SummonEvidence}")+(Controller.Profile.PetElementOverride.Length>0?"\nExisting identity override: "+Controller.Profile.PetElementOverride:""):"Independent planned summon · +0 is a planning assumption until changed. Valid edits use this workspace's save/Undo rules.";
            string next=nextContext+"|"+Controller.EquipmentRevision+"|"+Controller.PetRevision+"|"+Controller.PetResearchRevision+"|"+petEditRevision+"|"+Controller.StatProfileRevision+"|"+Controller.ConnectedSpellRevision+"|"+Controller.StandardPetBuffRevision+"|"+PetSheetLoadout.Version;
            if(next==key)return;key=next;
            if(selected.SummonSpell.Length==0){popup.RequireSelection();return;}
            PetBuild snapshot;LoadoutProfile owner;Catalog catalog;PetResearchState? research;
            lock(Controller.Sync){snapshot=EquipmentPositions.Copy(selected);owner=EquipmentPositions.Copy(Controller.Profile.ActiveLoadout);catalog=Controller.EquipmentView?.Catalog??Controller.Catalog;research=TrackerController.PetResearchEnabled?Controller.PetResearchSnapshot():null;}
            popup.QueueStatistics(snapshot,owner,catalog,research,()=>Valid()&&SelectedPetInventory?.Id==workspace,Controller.PetSheetEvidenceSnapshot(),Controller.StandardPetBuffSnapshot());
        }
        popup.RefreshRequested=RefreshPopup;
        popup.Sheet.ReviewBuffs.Click+=(_,_)=>
        {
            var setup=Controller.StandardPetBuffSnapshot();var catalog=PetBuffCatalog.References();
            foreach(var learned in setup.Buffs)if(!catalog.Buffs.Any(b=>b.Name==learned.Spell))catalog.Buffs.Add(new(){Id="learned:"+learned.Spell,Name=learned.Spell,Source=learned.Evidence,Classes=new(){["MAG"]=0}});
            var preferences=new PetComparisonPreferences{ExternalBuffs=true,Buffs=setup.Buffs.Select(b=>new PetBuffChoice{Id=catalog.Buffs.First(d=>d.Name==b.Spell).Id,Enabled=!b.Excluded,Rank=b.EffectiveRank,OwnershipConfirmed=true}).ToList()};
            var dialog=new PetBuffSetupDialog(Controller.Profile.ActiveLoadout,catalog,preferences,true,setup){Owner=popup};
            if(dialog.ShowDialog()==true)
            {
                foreach(var learned in setup.Buffs){var choice=preferences.Buffs.FirstOrDefault(c=>catalog.Find(c.Id)?.Name==learned.Spell);learned.Excluded=choice?.Enabled!=true;if(choice!=null){learned.OverrideRank=choice.Rank!=learned.Rank;learned.SelectedRank=choice.Rank;}}
                Controller.SaveStandardPetBuffs(setup);key="";RefreshPopup();
            }
        };
        void OwnerVisibilityChanged(object? sender,DependencyPropertyChangedEventArgs args){if(!IsVisible)popup.Close();}
        IsVisibleChanged+=OwnerVisibilityChanged;
        popup.Closed+=(_,_)=>{IsVisibleChanged-=OwnerVisibilityChanged;popup.ReleaseEditor(petDetailsEditor,petContextNote);petDetailsPopup=null;foreach(var input in new Control[]{petElement,petSpell,petSummonTier,petFocusName,petLevel})input.IsEnabled=true;};
        popup.Show();RefreshPopup();
    }
}

internal sealed class PetDetailsPopup : FrostDialog
{
    internal readonly CheckBox Override=new(){Content="Override build details",ToolTip="Change Current build assumptions only. Detected pet identity, DPS icons and history stay unchanged."};
    internal readonly Button UseDetected=UI.Button("Use detected pet",()=>{});
    internal readonly TextBlock Source=UI.Text("",11,"SecondaryText");
    internal readonly TextBlock ModeSummary=UI.Text("",11,"SecondaryText"),Updating=UI.Text("",11,"SecondaryText"),Related=UI.Text("",11,"SecondaryText");
    internal readonly PetCharacterSheet Sheet=new();
    internal readonly Expander Evidence;
    internal TextBlock Capture=>Sheet.Capture;
    internal Action? RefreshRequested;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(300)};
    private CancellationTokenSource? work;
    private long generation;
    private bool closed;
    internal PetDetailsPopup(string title,FrameworkElement editor,TextBlock contextEvidence):base(title,740,800)
    {
        MinWidth=500;MinHeight=420;
        var modes=new WrapPanel();modes.Children.Add(Override);modes.Children.Add(UseDetected);
        modes.Margin=new(0,2,0,5);Override.VerticalAlignment=VerticalAlignment.Center;UseDetected.Height=28;UseDetected.Margin=new(10,0,0,0);
        var settings=UI.Stack(ModeSummary,modes,editor);settings.Margin=new(0,0,0,8);
        var evidence=Evidence=FocusedLayout.Disclosure("Evidence",UI.Stack(Source,contextEvidence,Sheet.CaptureEvidence,Sheet.AdditionalEvidence,Related),"Summon evidence, level assumptions, capture conditions and other recorded snapshots. Observed values are not live telemetry or recalculated totals.");
        Updating.Visibility=Visibility.Collapsed;
        Editor.Children.Add(UI.Stack(Sheet.IdentityHeader,settings,Sheet,Updating,evidence));
        Actions.Children.Add(UI.Button("Close",Close));
        foreach(var button in Usability.Tree((DependencyObject)Content).OfType<Button>().Where(b=>Equals(b.Content,"×")))button.ToolTip="Close Pet Details. Valid edits retain the workspace's save and Undo behavior.";
        timer.Tick+=(_,_)=>RefreshRequested?.Invoke();Loaded+=(_,_)=>timer.Start();Closed+=(_,_)=>{closed=true;generation++;timer.Stop();work?.Cancel();work?.Dispose();};
    }
    internal void ReleaseEditor(params FrameworkElement[] editors){foreach(var editor in editors)if(editor.Parent is Panel parent)parent.Children.Remove(editor);}
    internal void RequireSelection(){generation++;work?.Cancel();Sheet.Clear();Related.Text="";Updating.Text="Select a summon to show its observed snapshot and equipment contributions.";Updating.Visibility=Visibility.Visible;}
    internal async void QueueStatistics(PetBuild build,LoadoutProfile owner,Catalog catalog,PetResearchState? research,Func<bool> valid,PetSheetEvidenceInputs? evidence=null,StandardPetBuffState? standard=null)
    {
        work?.Cancel();work?.Dispose();work=new();var token=work.Token;long request=++generation;
        Updating.Text="Updating snapshot…";Updating.Visibility=Visibility.Visible;
        try
        {
            var completed=await Task.Run(()=>
            {
                token.ThrowIfCancellationRequested();var stats=PetDetailsStatistics.Project(build,owner,catalog,research);
                var abilities=ConnectedSpellEvidence.PetAbilities(build,evidence?.Profile??new(),evidence?.History??[],evidence?.State??new(),evidence?.Index);
                string buffs=evidence==null?"No retained buff history for this view.":ConnectedSpellEvidence.PetBuffHistory(evidence.State,build,evidence.Profile);
                token.ThrowIfCancellationRequested();return (stats,abilities,buffs);
            },token);
            if(closed||token.IsCancellationRequested||request!=generation||!valid())return;
            var result=completed.stats;Sheet.SetStandardContext(build,standard??new(),research);Sheet.Present(result);Updating.Visibility=Visibility.Collapsed;
            Sheet.SetAbilities(completed.abilities,completed.buffs);
            Related.Text=result.Related.Count>0?string.Join("\n",result.Related):"No additional applicable captures.";
        }
        catch(OperationCanceledException){}
        catch(Exception error) when(error is InvalidOperationException or ArgumentException)
        {if(!closed&&request==generation&&valid()){Updating.Text="Snapshot could not be refreshed; previous values are retained.";Updating.ToolTip=error.Message;}}
    }
}
