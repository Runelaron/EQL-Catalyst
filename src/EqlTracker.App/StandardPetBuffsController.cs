using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    private StandardPetBuffState standardPetBuffs=new();
    internal long StandardPetBuffRevision {get{lock(Sync)return standardPetBuffs.Revision;}}
    internal StandardPetBuffState StandardPetBuffSnapshot(){lock(Sync)return EquipmentPositions.Copy(standardPetBuffs);}
    private void RefreshStandardPetBuffs()
    {
        if(StandardPetBuffs.Learn(standardPetBuffs,connectedSpells,SpellIndexSnapshot(),Profile))
        {var saved=EquipmentPositions.Copy(standardPetBuffs);QueuePersistence(()=>store.Set("standardPetBuffs-v1",saved));}
        if(PetResearchEnabled&&!researchUnavailable&&PetResearch.ReprojectBuffMetadata(research,connectedSpells,Profile))
        {researchSource="";SavePetResearch();}
        RefreshMechanicalSetup();
    }
    internal void SaveStandardPetBuffs(StandardPetBuffState edited)
    {
        lock(Sync)
        {
            // Merge only review choices; newer automatic evidence and private capture links survive.
            foreach(var change in edited.Buffs)
            {
                if(change.OverrideRank&&change.SelectedRank is <0)throw new ArgumentException("Rank must be nonnegative or Unknown.");
                var current=standardPetBuffs.Buffs.FirstOrDefault(b=>b.Spell==change.Spell);if(current==null)continue;
                current.Excluded=change.Excluded;current.OverrideRank=change.OverrideRank;current.SelectedRank=change.SelectedRank;
            }
            standardPetBuffs.Revision++;var saved=EquipmentPositions.Copy(standardPetBuffs);QueuePersistence(()=>store.Set("standardPetBuffs-v1",saved));
            RefreshMechanicalSetup();
        }
    }
}
