using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed record PetSheetEvidenceInputs(CharacterProfile Profile,ConnectedSpellState State,ConnectedSpellIndex Index,IReadOnlyList<Encounter> History);

public sealed partial class TrackerController
{
    private ConnectedSpellState connectedSpells=new();
    private ConnectedSpellIndex? connectedSpellIndex;
    private long connectedReferenceRevision=-1;
    private long connectedIndexRevision;
    private FileCheckpoint? connectedCheckpoint;
    internal long ConnectedSpellRevision { get { lock(Sync)return connectedSpells.Revision+referenceRevision+historyRevision+connectedIndexRevision+(Published?.Revision??0); } }
    private void InitializeConnectedSpells()
    {
        var saved=store.Get<ConnectedSpellState>("connectedSpellEvidence-v1");
        connectedSpells=saved??new();
        standardPetBuffs=store.Get<StandardPetBuffState>("standardPetBuffs-v1")??new();
        connectedCheckpoint=store.Get<FileCheckpoint>("connectedSpellCheckpoint-v1");
        // A new projection version invalidates views without rewinding this independent reader.
        if(saved==null||connectedCheckpoint==null)connectedCheckpoint=ConnectedSpellEvidence.InitialCheckpoint(statCheckpoint);
        connectedSpells.Version=ConnectedSpellIndex.Version;
    }
    internal ConnectedSpellIndex SpellIndexSnapshot()
    {
        lock(Sync)
        {
            connectedSpellIndex??=new(spellReferences.Values.DistinctBy(s=>s.Name),referenceAliases);
            return connectedSpellIndex;
        }
    }
    private void RefreshConnectedSpellClient(string path)
    {
        ConnectedSpellIndex next;long revision;
        lock(Sync){next=new(spellReferences.Values.DistinctBy(s=>s.Name),referenceAliases);revision=referenceRevision;}
        if(File.Exists(path))next.WithClientFile(path,cancel.Token);
        lock(Sync){if(cancel.IsCancellationRequested||revision!=referenceRevision)return;connectedSpellIndex=next;connectedReferenceRevision=revision;connectedIndexRevision++;}
    }
    internal PetSheetEvidenceInputs PetSheetEvidenceSnapshot()
    {
        lock(Sync)return new(new(){Character=Profile.Character,PetIdentities=Profile.PetIdentities.Select(EquipmentPositions.Copy).ToList()},EquipmentPositions.Copy(connectedSpells),SpellIndexSnapshot(),Published?.History??[]);
    }
    internal string ReceivedSpellEvidence(bool pet)
    {
        lock(Sync)
        {
            var identity=PetDetectedContext.Latest(Profile);
            return pet?identity==null?"No confirmed owned-pet context. Buff coverage is unresolved.":ConnectedSpellEvidence.PetBuffHistory(connectedSpells,new(){PetType=identity.Element,SummonSpell=identity.CanonicalSummonSpell,SummonRank=identity.SummonRank??0,SummonRankKnown=identity.SummonRank.HasValue},Profile):ConnectedSpellEvidence.BuffHistory(connectedSpells,Profile.Character);
        }
    }
    private CombatInspectionSnapshot ConnectSpellInspection(CombatInspectionSnapshot snapshot,DpsViewState damage,HealingViewState? healing)
    {
        LoadoutProfile owner;Catalog catalog;ConnectedSpellIndex index;string recipient;
        lock(Sync){owner=EquipmentPositions.Copy(Profile.ActiveLoadout);catalog=EquipmentView?.Catalog??Catalog;index=SpellIndexSnapshot();recipient=damage.Filter.Owner=="Pet"?PetDetectedContext.Latest(Profile)?.Name??"":Profile.Character;}
        string buffs;lock(Sync)buffs=damage.Filter.Owner=="Pet"?ConnectedSpellEvidence.PetBuffHistory(connectedSpells,Profile,damage.Fights):ConnectedSpellEvidence.BuffHistory(connectedSpells,recipient);
        var abilities=snapshot.Abilities.ToDictionary(p=>p.Key,p=>
        {
            var identity=index.Resolve(p.Value.Ability);var lines=new List<InspectionLine>{new("Spell identity",identity.Canonical+" · upgrade rank "+(identity.Rank?.ToString()??"Unknown")+(identity.Definition?.Id is {} id?" · client ID "+id:"")),new("Buff evidence",buffs)};
            if(identity.Definition is {} definition)lines.Add(new("Spell parameters",(definition.DirectHeal is {} heal?"Static direct-heal parameter: "+heal.ToString("0.###")+" HP. Caster modifiers and actual healing remain separate.":"Numerical power is unresolved at this rank/context; sourced effect descriptions remain available.")+"\n"+definition.Source));
            if(healing!=null)
            {
                var heals=healing.Events.Where(e=>e.Ability.Equals(p.Value.Ability,StringComparison.OrdinalIgnoreCase)).ToArray();
                var owners=heals.Select(e=>e.HealerOwner).Distinct().ToArray();
                if(owners.Length==1&&owners[0] is "Player" or "Pet")
                {
                    bool petCaster=owners[0]=="Pet";
                    var casterPositions=petCaster?PetEquipmentInference.Build(PetEquipmentProjection.Prepare(PetInventoryEditing.Confirmed(owner,catalog),owner,catalog),owner,catalog).Positions:owner.EquipmentPositions;
                    lines.Add(new("Focus evidence",HealingFocusEvidence.Explain(index,p.Value.Ability,casterPositions,catalog,petCaster,petCaster?owner.PetLevel??0:owner.Level,heals.Any(e=>e.OverTime),petCaster?PetDefinition.For(owner.PetType).Classes:owner.Classes)));
                }
                else lines.Add(new("Focus evidence","Caster equipment is unavailable or mixed. Recipient equipment and a mismatched delta do not establish a focus modifier."));
            }
            var original=p.Value.Lines.Select(line=>line.Label=="Logged spell rank"?new InspectionLine(line.Label,identity.Rank?.ToString()??"Unknown; a spell-line numeral is not an upgrade rank"):line);
            return p.Value with {Lines=original.Concat(lines).ToArray()};
        });
        return snapshot with {Abilities=abilities};
    }
}
