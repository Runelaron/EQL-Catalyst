namespace EqlTracker.Core;

internal sealed class PetWorkspaceDraft
{
    internal PetBuild Baseline { get; private set; }
    internal PetBuild Working { get; private set; }
    internal bool IsNew { get; private set; }
    internal string SavedId=>IsNew?"":Working.Id;
    private readonly bool followsDetected;
    internal string SourceKey { get; private set; }
    internal long Revision { get; private set; }
    internal void Changed()=>Revision++;
    internal bool Dirty=>IsNew||Key(Working,followsDetected)!=Key(Baseline,followsDetected);
    internal PetWorkspaceDraft(PetBuild source,bool isNew=false,bool followsDetected=false)
    {this.followsDetected=followsDetected;Baseline=EquipmentPositions.Copy(source);bool dual=Baseline.DualWield;PetSlotPlanning.Migrate(Baseline);Baseline.DualWield=dual;Working=EquipmentPositions.Copy(Baseline);SourceKey=Key(source,followsDetected);IsNew=isNew;}
    internal static string Key(PetBuild b,bool ignoreDetected=false)=>PetComparisonEngine.Key(new{
        b.Id,b.Name,b.IsTheorycraft,b.Preset,b.FollowDetectedSummon,
        Context=ignoreDetected&&b.FollowDetectedSummon?null:new{b.PetType,Level=b.LevelMode==PetLevelMode.Manual?b.PetLevel:null,Mode=b.LevelMode==PetLevelMode.Unspecified&&!b.PetLevel.HasValue?PetLevelMode.Automatic:b.LevelMode,b.SummonSpell,b.SummonClass,b.SummonRank,b.SummonRankKnown,b.Focus},
        b.DualWield,Items=b.Items.OrderBy(i=>i.Position).Select(i=>new{i.Id,i.Position,i.ItemKey,i.Name,i.Rank,i.Slot,i.EquipmentVerified,i.Exaltations,i.TransferredOutEffects}),
        Slots=b.SlotConfigurations.OrderBy(s=>s.Position),b.DormantSecondary,b.DormantSecondaryConfiguration,b.ComparisonScenario,b.StatProfiles});
    internal void Follow(PetBuild source)
    {
        if(!followsDetected||!source.FollowDetectedSummon)return;
        void Apply(PetBuild target){target.PetType=source.PetType;target.PetLevel=source.PetLevel;target.LevelMode=source.LevelMode;target.LevelEvidence=source.LevelEvidence;target.SummonSpell=source.SummonSpell;target.SummonClass=source.SummonClass;target.SummonRank=source.SummonRank;target.SummonRankKnown=source.SummonRankKnown;target.Focus=source.Focus;}
        if(Baseline.FollowDetectedSummon)Apply(Baseline);if(Working.FollowDetectedSummon)Apply(Working);
    }
    internal bool CanCommit(PetBuild? source)=>IsNew?source==null:source!=null&&Key(source,followsDetected)==SourceKey;
    internal PetBuild Commit(){var result=EquipmentPositions.Copy(Working);result.Undo.Clear();result.EditHistory.Clear();Baseline=EquipmentPositions.Copy(result);Working=EquipmentPositions.Copy(result);SourceKey=Key(result,followsDetected);IsNew=false;return result;}
    internal void Undo(){Working=EquipmentPositions.Copy(Baseline);IsNew=false;Revision++;}
}

internal static class PetPositionEditing
{
    internal static string Validate(PetBuild build,ItemDefinition item,int position,int capacity,LoadoutProfile? owner=null,Catalog? catalog=null)
    {
        if(position<1||position>capacity)return "Position exceeds this build's inventory capacity.";
        if(owner!=null&&catalog!=null){string eligible=GearScorer.Eligibility(item,GuidedPetBuilds.Context(owner,build),true,null,catalog);if(eligible!="Eligible"&&!(build.IsTheorycraft&&PetItemRules.CanAssume(eligible)))return eligible;}
        var slots=item.Slots.Select(EquipmentPositions.Normalize).ToArray();
        if(position==1)return slots.Contains("Primary")?"":"Position 1 requires a Primary-compatible item.";
        if(position==2&&build.DualWield)return slots.Contains("Secondary")?"":"Position 2 requires a Secondary-compatible item while Dual wield is enabled.";
        return slots.Any(s=>s is not ("Primary" or "Secondary"))?"":"Weapons belong in Primary or enabled Secondary; any-order equipment is not enabled.";
    }
    internal static void Assign(PetBuild build,PetInventoryItem value)
    {value.Slot=value.Position==1?"Primary":value.Position==2&&build.DualWield?"Secondary":"Storage";value.EquipmentVerified=value.Slot!="Storage";}
    internal static bool Replace(PetBuild build,ItemDefinition item,InventoryEntry? instance,int position,int capacity,out string error,LoadoutProfile? owner=null,Catalog? catalog=null)
    {
        error=Validate(build,item,position,capacity,owner,catalog);if(error.Length>0)return false;
        var old=build.Items.FirstOrDefault(i=>i.Position==position);
        if(instance?.PhysicalId=="pet:"+old?.Id)return true;
        if(instance?.PhysicalId is {Length:>0} physical&&build.Items.Any(i=>"pet:"+i.Id==physical)){error="This copy is already in the Pet inventory. Drag its row to swap it.";return false;}
        PetSlotPlanning.Migrate(build);
        var value=new PetInventoryItem{Id=Guid.NewGuid().ToString("N"),Position=position,ItemKey=item.Key,Name=item.Name,Rank=instance?.Rank??0,
            Exaltations=EquipmentPositions.Copy(build.SlotConfigurations.FirstOrDefault(s=>s.Position==position)?.Effects??[]),TransferredOutEffects=instance?.TransferredOutEffects.ToList()??[]};
        if(value.Exaltations.Count==0&&instance is {IsCollection:false})value.Exaltations=PetEffectPolicy.Observed(instance.Augments);
        foreach(var effect in value.Exaltations)effect.State="Planned";
        Assign(build,value);
        build.Items.RemoveAll(i=>i.Position==position);build.Items.Add(value);
        // Retained conflicting/locked configurations stay available for review, never become transfer evidence.
        var slot=PetSlotPlanning.Slot(build,position);slot.Effects=EquipmentPositions.Copy(value.Exaltations);
        if(old==null&&slot.Effects.Count>0)slot.ObservedItemId="";
        PetSlotPlanning.Sync(build);return true;
    }
    internal static bool Swap(PetBuild build,string id,int position,Catalog catalog,int capacity,out string error,LoadoutProfile? owner=null)
    {
        error="";var item=build.Items.FirstOrDefault(i=>i.Id==id);if(item==null){error="The dragged item is no longer in this build.";return false;}
        if(item.Position==position)return true;var other=build.Items.FirstOrDefault(i=>i.Position==position);
        foreach(var (entry,target) in new[]{(item,position),(other,item.Position)})if(entry!=null){var def=catalog.FindKey(entry.ItemKey)??catalog.Find(entry.Name);if(def==null){error="Item metadata is unavailable.";return false;}error=Validate(build,def,target,capacity,owner,catalog);if(error.Length>0)return false;}
        PetSlotPlanning.Migrate(build);int from=item.Position;item.Position=position;Assign(build,item);if(other!=null){other.Position=from;Assign(build,other);}PetSlotPlanning.Sync(build);return true;
    }
    internal static bool Remove(PetBuild build,string id){var item=build.Items.FirstOrDefault(i=>i.Id==id);if(item==null)return false;PetSlotPlanning.Migrate(build);build.Items.Remove(item);PetSlotPlanning.Sync(build);return true;}
}
