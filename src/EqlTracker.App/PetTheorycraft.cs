using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private PetWorkspaceDraft? petDraft;
    private PetWorkspaceDraft? petTheory {get=>petDraft?.Working.IsTheorycraft==true?petDraft:null;set=>petDraft=value;}
    private bool petTheoryChanging,petWorkspaceChanging;
    private string petWorkspaceSelection="current",petDraftKey="";
    private readonly CheckBox petTheoryCheck=new(){Content="Theorycraft",ToolTip="Create an isolated Theorycraft draft. Save explicitly; Current is unchanged."};
    private readonly TextBlock petTheoryNote=UI.Text("",11,"Melee",true);
    private Button petWorkspaceUndo=null!,petWorkspaceSave=null!;
    private PetBuild? SavedPetSource(string key)=>key=="current"?PetInventoryEditing.Confirmed(Controller.Profile.ActiveLoadout,Controller.Catalog):Controller.Profile.ActiveLoadout.PetBuilds.FirstOrDefault(b=>b.Id==key);
    private PetBuild? WorkingPetBuild()
    {
        string key=petBuild.SelectedValue?.ToString()??"current";
        if(petDraft!=null&&petDraftKey==key)
        {
            var source=SavedPetSource(key);
            if(source!=null){if(!petDraft.Dirty&&!petDraft.CanCommit(source))petDraft=new(source,false,key=="current");else petDraft.Follow(source);}
            return petDraft.Working;
        }
        var saved=SavedPetSource(key);if(saved==null)return null;
        petDraft=new(saved,false,key=="current");petDraftKey=key;return petDraft.Working;
    }
    private void BeginPetDraft(PetBuild build,bool isNew)
    {build.FollowDetectedSummon=false;petDraft=new(build,isNew);petDraftKey=petWorkspaceSelection=build.Id;RefreshBuildChoices(build.Id);UpdatePetDraftActions();}
    private static void CopyPetAssumptions(PetBuild from,PetBuild to)
    {to.PetType=from.PetType;to.PetLevel=from.PetLevel;to.LevelMode=from.LevelMode;to.SummonSpell=from.SummonSpell;to.SummonClass=from.SummonClass;to.SummonRank=from.SummonRank;to.SummonRankKnown=from.SummonRankKnown;to.Focus=from.Focus;to.FollowDetectedSummon=false;to.Preset=from.Preset;to.Items=from.Items;to.SlotConfigurations=from.SlotConfigurations;to.SlotConfigurationVersion=from.SlotConfigurationVersion;to.DualWield=from.DualWield;to.DormantSecondary=from.DormantSecondary;to.DormantSecondaryConfiguration=from.DormantSecondaryConfiguration;to.StatProfiles=from.StatProfiles;to.ComparisonScenario=from.ComparisonScenario;to.LevelEvidence=from.LevelEvidence;}
    private void SetTheoryCheck(bool value){petTheoryChanging=true;petTheoryCheck.IsChecked=value;petTheoryChanging=false;}
    private void TogglePetTheorycraft()
    {
        if(initializing||petTheoryChanging)return;
        if(petTheoryCheck.IsChecked==true)
        {
            string sourceKey=petDraftKey;
            if(!ConfirmPetWorkspaceExit()){SetTheoryCheck(false);return;}
            var original=SavedPetSource(sourceKey)??SavedPetSource("current");var source=original==null?null:EquipmentPositions.Copy(original);
            if(source!=null){source.Id=Guid.NewGuid().ToString("N");source.Name="Theorycraft · "+source.Name;source.IsTheorycraft=true;source.EditHistory.Clear();source.Undo.Clear();BeginPetDraft(source,true);}
        }
        else if(ConfirmPetWorkspaceExit())RefreshBuildChoices("current");else SetTheoryCheck(true);
        UpdateTheorycraftNote();
    }
    private bool ChangePetWorkspace(PetBuildSelection choice)
    {
        if(petWorkspaceChanging||petDraftKey==choice.Id){petWorkspaceSelection=choice.Id;UpdatePetDraftActions();return true;}
        if(petDraft!=null&&!ConfirmPetWorkspaceExit())
        {bool before=initializing;initializing=true;petBuild.SelectedValue=petDraftKey;initializing=before;return false;}
        petWorkspaceSelection=choice.Id;petDraftKey=choice.Id;var saved=SavedPetSource(choice.Id);petDraft=saved==null?null:new(saved,false,choice.Id=="current");
        SetTheoryCheck(petTheory!=null);UpdateTheorycraftNote();return true;
    }
    internal bool ConfirmPetWorkspaceExit()
    {
        if(petDraft?.Dirty==true)
        {
            var dialog=new TheorycraftExitDialog{Owner=this};dialog.ShowDialog();
            if(dialog.Choice==TheorycraftExitChoice.Cancel)return false;
            if(dialog.Choice==TheorycraftExitChoice.Save&&!SavePetWorkspace(false))return false;
        }
        petDraft=null;petDraftKey="";SetTheoryCheck(false);UpdatePetDraftActions();return true;
    }
    private bool SavePetTheorycraft()=>SavePetWorkspace();
    private bool SavePetWorkspace(bool refresh=true)
    {
        if(petDraft==null||!petDraft.Dirty)return true;
        var session=petDraft;var draft=session.Working;
        if(draft.IsTheorycraft&&session.IsNew)
        {
            var name=new EntryDialog("Save Theorycraft build",[("Name",draft.Name)],"Keep this plan separate from Current."){Owner=this};
            if(name.ShowDialog()!=true||string.IsNullOrWhiteSpace(name.Values[0]))return false;draft.Name=name.Values[0].Trim();
        }
        lock(Controller.Sync)
        {
            if(!session.CanCommit(SavedPetSource(petDraftKey))){PetMessage("The saved build changed. Your draft is retained. Discard it to reload the saved build before saving.",true);return false;}
            if(draft.IsTheorycraft){draft.TheorycraftRulesVersion=PetItemRules.Version;draft.TheorycraftAssumptions=PetEquipmentInference.Build(draft,Controller.Profile.ActiveLoadout,Controller.Catalog).Totals.Notes.Where(n=>n.StartsWith("Hypothetical assumption")).ToList();}
            var saved=session.Commit();var owner=Controller.Profile.ActiveLoadout;
            if(petDraftKey=="current")PetInventoryEditing.Confirm(owner,saved,DateTime.Now);
            else {int index=owner.PetBuilds.FindIndex(b=>b.Id==saved.Id);if(index<0)owner.PetBuilds.Add(saved);else owner.PetBuilds[index]=saved;}
            if(petDraftKey=="current")petDraft=new(SavedPetSource("current")!,false,true);
            Controller.Save();
        }
        UpdatePetDraftActions();PetMessage("Build saved.");
        if(refresh){petWorkspaceChanging=true;try{RefreshBuildChoices(petDraftKey);LoadPetContext();petKey="";RefreshPet();}finally{petWorkspaceChanging=false;}}
        return true;
    }
    private void UpdatePetDraftActions()
    {if(petWorkspaceSave==null)return;bool dirty=petDraft?.Dirty==true;petWorkspaceSave.IsEnabled=petWorkspaceUndo.IsEnabled=dirty;petWorkspaceSave.ToolTip="Commit this build's draft.";petWorkspaceUndo.ToolTip="Discard all changes since the last save.";}
    private void UpdateTheorycraftNote()
    {petTheoryNote.Text=petTheory==null?"":"Theorycraft · "+(petDraft!.Dirty?"unsaved":"saved")+" · "+petDraft.Working.Name;petTheoryNote.Visibility=petTheory==null?Visibility.Collapsed:Visibility.Visible;UpdatePetDraftActions();}
}

internal enum TheorycraftExitChoice { Cancel,Save,Discard }
internal sealed class TheorycraftExitDialog:FrostDialog
{
    internal TheorycraftExitChoice Choice{get;private set;}
    internal TheorycraftExitDialog():base("Unsaved Pet build",480,275)
    {
        Editor.Children.Add(UI.Text("Save this build before leaving? Discard removes the unsaved draft; Cancel keeps editing.",13));
        Actions.Children.Add(UI.Button("Save",()=>{Choice=TheorycraftExitChoice.Save;DialogResult=true;}));
        Actions.Children.Add(UI.Button("Discard",()=>{Choice=TheorycraftExitChoice.Discard;DialogResult=true;}));Actions.Children.Add(UI.Button("Cancel",Close));
    }
}
