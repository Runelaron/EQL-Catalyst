namespace EqlTracker.Core;

public static class GearRecommendations
{
    public static List<GearComparison> For(ItemDefinition item, int rank, Catalog catalog, LoadoutProfile loadout, bool pet, Dictionary<string,double> weights, SelfRoleResolution? role = null, Dictionary<string,double>? petDefenseWeights = null)
    {
        if (pet && loadout.ConfirmedPetInventory is { } confirmed) loadout = GuidedPetBuilds.Context(loadout, confirmed);
        var positions = pet && loadout.ConfirmedPetInventory is { } inventory ? GuidedPetBuilds.Positions(inventory, loadout, catalog) : EquipmentPositions.For(loadout, pet);
        var session = new LoadoutEvaluationSession(catalog, loadout, pet, weights, positions, positions, role: role, petGoal: pet?loadout.PetPreset:"", petDefenseWeights: petDefenseWeights,petInventory:pet?loadout.ConfirmedPetInventory:null);
        return positions.Where(p => session.Fits(item,p)).Select(p => session.Present(item,rank,p.Id)).OrderBy(c=>c.Evaluation?.Status=="Conditional"?3:c.Evaluation?.Group == "Positive in both" ? 0 : c.Evaluation?.Unbuffed.Delta > 0 || c.Evaluation?.Buffed.Delta > 0 ? 1 : 2).ThenBy(c=>c.Slot).ToList();
    }
}
