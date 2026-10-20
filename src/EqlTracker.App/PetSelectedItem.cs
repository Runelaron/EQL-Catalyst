using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private SelectedItemPreviewState petPreview = new("", null, 0);
    private InventoryEntry? petInspectedCopy;
    private readonly Dictionary<string, GearComparison?> petRankCache = [];
    private CancellationTokenSource? petPreviewCancel;
    private string petPreviewKey = "", petSessionKey = "";
    private LoadoutEvaluationSession? petSelectedSession;
    private List<EquipmentPosition> petSelectedPositions = [];
    private GearComparison? petDisplayedComparison;
    private Grid? petInnerSplit, petOuterSplit;
    private readonly DispatcherTimer petPreviewDelay = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private InventoryEntry? PetInspectedInstance(PetSearchRow row) => petPreview.Identity == row.ToString() ? petInspectedCopy ?? row.Instance : row.Instance;
    private int PetInspectedRank(PetSearchRow row) => petPreview.Identity == row.ToString() ? petPreview.Rank ?? PetInspectedInstance(row)?.Rank ?? row.Rank : row.Rank;
    private void ChangePetPreviewRank(int? rank)
    {
        using var measurement = UpdateDiagnostics.Measure("Rank input apply");
        if (petResults.SelectedItem is not PetSearchRow row) return;
        petPreview = new(row.ToString(), rank, petPreview.Revision + 1); petPreviewKey = ""; petPreviewCancel?.Cancel();
        DisplaySelectedPet(row, null, $"Updating +{PetInspectedRank(row)}…");
        if (!petPreviewDelay.IsEnabled) petPreviewDelay.Start();
    }
    private void ShowPetComparison()
    {
        using var measurement = UpdateDiagnostics.Measure("Selected item comparison");
        petPreviewDelay.Stop();
        if (petResults.SelectedItem is not PetSearchRow row) { petPreviewCancel?.Cancel(); petPreviewKey = ""; petComparison.Clear(); return; }
        if (!Controller.Demo && row.Item.MetadataVersion < PetItemRules.MetadataVersion) Controller.RequestGearMetadata(row.Item.Name);
        if (petPreview.Identity != row.ToString()) { petPreview = new(row.ToString(), null, petPreview.Revision + 1); petInspectedCopy = row.Instance; }
        int rank = PetInspectedRank(row);
        var instance = PetInspectedInstance(row);
        long equipmentRevision = Controller.EquipmentRevision, petRevision = Controller.PetRevision;
        string key = $"{Controller.EquipmentRevision}|{Controller.PetRevision}|{petEditRevision}|{petTheory?.Revision}|{SelectedPetInventory?.Id}|{petPreview.Identity}|{petPreview.Revision}|{PhysicalItemChoices.Identity(instance)}|{petPreferredPosition}";
        if (key == petPreviewKey) return; petPreviewKey = key;
        petPreviewCancel?.Cancel(); petPreviewCancel?.Dispose(); petPreviewCancel = new(); var token = petPreviewCancel.Token;
        if (rank == row.Rank && ReferenceEquals(instance, row.Instance)) { if (!Controller.Demo && petRowsKey != petKey) { petPreviewKey = ""; DisplaySelectedPet(row, null, $"Updating +{rank}…"); } else DisplaySelectedPet(row, row.Comparison, ""); return; }
        DisplaySelectedPet(row, null, $"Updating +{rank}…");
        if (SelectedPetInventory is not { } selected) return;
        string baselineKey = $"{Controller.EquipmentRevision}|{Controller.PetRevision}|{petEditRevision}|{petTheory?.Revision}|{selected.Id}";
        if (petSelectedSession == null || petSessionKey != baselineKey)
        {
            var owner = EquipmentPositions.Copy(Controller.EquipmentView?.Profile.ActiveLoadout ?? Controller.Profile.ActiveLoadout);
            var plan = EquipmentPositions.Copy(selected); var context = GuidedPetBuilds.Context(owner, plan);
            petSelectedPositions = PetEquipmentInference.Build(plan, owner, Controller.Catalog).Positions;
            petRankCache.Clear();
            var reference = GuidedPetBuilds.Positions(PetInventoryEditing.Confirmed(owner, Controller.Catalog), owner, Controller.Catalog);
            var weights = Controller.Profile.Weights.GetValueOrDefault(SelectedPetPreset) ?? GearScorer.Defaults["Pet DPS"];
            petSelectedSession = new(Controller.Catalog, context, true, new(weights), petSelectedPositions, reference, plan.IsTheorycraft); petSessionKey = baselineKey;
        }
        var session = petSelectedSession; string preferred = petPreferredPosition;
        string cacheKey = row.Item.Key + "|" + PhysicalItemChoices.Identity(instance) + "|" + rank + "|" + preferred;
        var positions = petSelectedPositions;
        GearComparison? Calculate() { token.ThrowIfCancellationRequested(); lock (session) { token.ThrowIfCancellationRequested(); return PetEquipmentInference.Best(row.Item, rank, instance, session, positions, preferred == "*" ? null : preferred); } }
        void Apply(GearComparison? result) { if (!token.IsCancellationRequested && equipmentRevision == Controller.EquipmentRevision && petRevision == Controller.PetRevision && petPreviewKey == key && petResults.SelectedItem is PetSearchRow active && active.ToString() == row.ToString() && SelectedTab == TrackerTab.Pet) { petRankCache[cacheKey] = result; DisplaySelectedPet(active, result, ""); } }
        if (petRankCache.TryGetValue(cacheKey, out var cached)) { Apply(cached); return; }
        if (Controller.Demo && !previewAsyncForReview) Apply(Calculate());
        else _ = Task.Run(Calculate, token).ContinueWith(task => Dispatcher.InvokeAsync(() => { if (task.IsCompletedSuccessfully) Apply(task.Result); }), TaskScheduler.Default);
    }
    private void DisplaySelectedPet(PetSearchRow row, GearComparison? comparison, string reason)
    {
        int rank = PetInspectedRank(row); if (!reason.StartsWith("Updating")) petDisplayedComparison = comparison;
        ItemPairComparison? pair = null;
        if (reason.Length == 0 && comparison == null && (petItems.SelectedItem as PetItemRow)?.Item is { ItemKey.Length: > 0 } current)
        {
            var context = SelectedPetInventory is { } build ? GuidedPetBuilds.Context(Controller.Profile.ActiveLoadout, build) : Controller.Profile.ActiveLoadout;
            pair = ItemPairComparison.Create(Controller.Catalog.FindKey(current.ItemKey), current.Entry(), row.Item, rank, row.Instance, context, Controller.Catalog);
        }
        petComparison.AnimateValues = Controller.Profile.SmoothMeterMotion;
        petComparison.ShowItem(row.Item, rank, row.Rank, petPreview.Rank.HasValue, PetInspectedInstance(row), comparison, SelectedPetPreset, pair, reason, ChangePetPreviewRank, OpenPetFormula);
        petComparison.SetOwnership(row.Item, Controller.Inventory);
        petComparison.SetCopies(PhysicalItemChoices.Ordered(Controller.Inventory.Evidence(row.Item).Where(i => !i.IsAugment && !i.IsCollection && i.Count > 0)), PetInspectedInstance(row), copy => { petInspectedCopy = copy; petPreview = petPreview with { Rank = null, Revision = petPreview.Revision + 1 }; petPreviewKey = ""; ShowPetComparison(); });
    }
    private void OpenPetFormula()
    {
        var weights = Controller.Profile.Weights.GetValueOrDefault(SelectedPetPreset) ?? GearScorer.Defaults["Pet DPS"];
        new RatingFormulaDialog(SelectedPetPreset, new(weights), petDisplayedComparison) { Owner = this }.ShowDialog();
    }
    private void ResetPetPanels()
    {
        if (petOuterSplit != null) PaneLayout.Cancel(petOuterSplit);
        Controller.Profile.PanelSplits["pet-focused-items"] = .56; Controller.Profile.PanelSplits["pet-focused-comparison"] = .25;
        if (petInnerSplit != null) { petInnerSplit.ColumnDefinitions[0].Width = new(.44, GridUnitType.Star); petInnerSplit.ColumnDefinitions[2].Width = new(.56, GridUnitType.Star); }
        if (petOuterSplit != null && ActualWidth >= 1080) { petOuterSplit.ColumnDefinitions[0].Width = new(.75, GridUnitType.Star); petOuterSplit.ColumnDefinitions[2].Width = new(.25, GridUnitType.Star); }
        Controller.Save();
    }
}
