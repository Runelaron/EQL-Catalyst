using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private SelectedItemPreviewState gearPreview = new("", null, 0);
    private InventoryEntry? gearInspectedCopy;
    private string gearSelectedKey = "", gearBaselineKey = "";
    private LoadoutEvaluationSession? gearSelectedSession;
    private GearComparison? gearDisplayedComparison;
    private string gearDisplayedIdentity = "";
    private CancellationTokenSource? gearPreviewCancel;
    private readonly Dictionary<string, GearComparison> gearRankCache = [];
    private readonly DispatcherTimer gearPreviewDelay = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private string GearIdentity(GearRow row) => row.Item.Key + "|" + row.PositionId + "|" + PhysicalItemChoices.Identity(row.Instance);
    private InventoryEntry? GearInspectedInstance(GearRow row) => gearPreview.Identity == GearIdentity(row) ? gearInspectedCopy ?? row.Instance : row.Instance;
    private int GearInspectedRank(GearRow row) => gearPreview.Identity == GearIdentity(row) ? gearPreview.Rank ?? (gearRankChoice.SelectedIndex == 0 && gearInspectedCopy != null ? gearInspectedCopy.Rank : row.Rank) : row.Rank;
    private void ChangeGearPreviewRank(int? rank)
    {
        if (gearTable.SelectedItem is not GearRow row) return;
        gearPreview = gearPreview with { Rank = rank, Revision = gearPreview.Revision + 1 }; gearSelectedKey = ""; gearPreviewCancel?.Cancel();
        DisplaySelectedGear(row, null, $"Updating +{GearInspectedRank(row)}…");
        if (!gearPreviewDelay.IsEnabled) gearPreviewDelay.Start();
    }
    private void ShowSelectedGear()
    {
        gearPreviewDelay.Stop();
        if (gearTable.SelectedItem is not GearRow row) { gearInspector.Clear(); gearPreviewCancel?.Cancel(); gearSelectedKey = ""; return; }
        if (!Controller.Demo && row.Item.MetadataVersion < PetItemRules.MetadataVersion) Controller.RequestGearMetadata(row.Item.Name);
        string identity = GearIdentity(row);
        if (gearPreview.Identity != identity) { gearPreview = new(identity, null, gearPreview.Revision + 1); gearInspectedCopy = row.Instance; }
        int rank = GearInspectedRank(row); var instance = GearInspectedInstance(row);
        if (!Controller.Demo && gearRowsKey != gearFingerprint) { gearSelectedKey = ""; DisplaySelectedGear(row, null, $"Updating +{rank}…"); return; }
        string baseline = Controller.EquipmentRevision + "|" + Controller.PetRevision + "|" + gearFingerprint;
        string key = baseline + "|" + identity + "|" + gearPreview.Revision + "|" + PhysicalItemChoices.Identity(instance);
        if (key == gearSelectedKey) return; gearSelectedKey = key;
        gearPreviewCancel?.Cancel(); gearPreviewCancel?.Dispose(); gearPreviewCancel = new(); var token = gearPreviewCancel.Token;
        if (rank == row.Rank && ReferenceEquals(instance, row.Instance)) { DisplaySelectedGear(row, row.Comparison, ""); return; }
        DisplaySelectedGear(row, null, $"Updating +{rank}…");
        if (gearSelectedSession == null || gearBaselineKey != baseline)
        {
            gearRankCache.Clear(); gearBaselineKey = baseline;
            var loadout = gearEvaluationLoadout ?? EquipmentPositions.Copy(Controller.Profile.ActiveLoadout);
            var reference = gearEvaluationReference.Count > 0 ? gearEvaluationReference : GearReference(loadout, false);
            if (GearPet) loadout = GuidedPetBuilds.Context(loadout, gearDestination.SelectedIndex == 1 && CurrentBuild != null ? CurrentBuild : PetInventoryEditing.Confirmed(loadout, Controller.Catalog));
            var resolvedRole = GearPet ? null : SelfRoleRatings.Resolve(Controller.Profile, loadout, preset.SelectedItem?.ToString());
            var weights = resolvedRole?.Weights ?? Controller.Profile.Weights.GetValueOrDefault(loadout.PetPreset) ?? GearScorer.Defaults["Pet DPS"];
            gearSelectedSession = new(Controller.EquipmentView?.Catalog ?? Controller.Catalog, loadout, GearPet, new(weights), EquipmentPositions.Copy(visibleGearPositions), reference, gearTheory != null, resolvedRole);
        }
        var session = gearSelectedSession; bool aa = gearWithoutAa.IsChecked != true;
        string cache = identity + "|" + PhysicalItemChoices.Identity(instance) + "|" + rank;
        GearComparison Calculate() { token.ThrowIfCancellationRequested(); lock (session) return session.Present(row.Item, rank, row.PositionId, instance, aa); }
        void Apply(GearComparison result)
        {
            if (token.IsCancellationRequested || key != gearSelectedKey || baseline != Controller.EquipmentRevision + "|" + Controller.PetRevision + "|" + gearFingerprint || SelectedTab != TrackerTab.Gear || gearTable.SelectedItem is not GearRow active || GearIdentity(active) != identity) return;
            gearRankCache[cache] = result; DisplaySelectedGear(active, result, "");
        }
        if (gearRankCache.TryGetValue(cache, out var cached)) { Apply(cached); return; }
        if (Controller.Demo && !previewAsyncForReview) Apply(Calculate());
        else _ = Task.Run(Calculate, token).ContinueWith(task => Dispatcher.InvokeAsync(() => { if (task.IsCompletedSuccessfully) Apply(task.Result); }), TaskScheduler.Default);
    }
    private void DisplaySelectedGear(GearRow row, GearComparison? comparison, string reason)
    {
        if (!reason.StartsWith("Updating")) { gearDisplayedComparison = comparison; gearDisplayedIdentity = GearIdentity(row); }
        int rank = GearInspectedRank(row); var current = visibleGearPositions.FirstOrDefault(p => p.Id == row.PositionId)?.Item;
        ItemPairComparison? pair = comparison?.Evaluation == null && reason.Length == 0 ? ItemPairComparison.Create(current == null ? null : Controller.Catalog.FindKey(current.ConfirmedItemKey) ?? Controller.Catalog.Find(current.Name), current, row.Item, rank, GearInspectedInstance(row), Controller.Profile.ActiveLoadout, Controller.Catalog, GearPet) : null;
        gearInspector.AnimateValues = Controller.Profile.SmoothMeterMotion;
        gearInspector.ShowItem(row.Item, rank, row.Rank, gearPreview.Rank.HasValue, GearInspectedInstance(row), comparison, GearPet ? Controller.Profile.ActiveLoadout.PetPreset : preset.SelectedItem?.ToString() ?? "Balanced Tank", pair, reason, ChangeGearPreviewRank, OpenGearFormula);
        gearInspector.SetOwnership(row.Item, Controller.Inventory);
        gearInspector.SetCopies(PhysicalItemChoices.Ordered(Controller.Inventory.Evidence(row.Item).Where(i => !i.IsAugment && !i.IsCollection && i.Count > 0)), GearInspectedInstance(row), copy => { gearInspectedCopy = copy; gearPreview = gearPreview with { Rank = null, Revision = gearPreview.Revision + 1 }; gearSelectedKey = ""; ShowSelectedGear(); });
        if (gearAddToBuild != null) { gearAddToBuild.Content = "Add to build (+" + rank + ")"; gearAddToBuild.ToolTip = "Add this inspected +" + rank + " candidate and its effects to the selected build. Current is editable; inventory ownership evidence stays unchanged."; }
        gearDetails.Text = Controller.Inventory.Ownership(row.Item) + "\n" + string.Join("\n", row.Item.Sources.Select(s => s.ToString()).Distinct()) + "\nItem introduction: " + ItemPresentation.Expansion(row.Item) + "\n" + ItemPresentation.ExpansionNote(row.Item) + "\n" + row.Item.ExpansionEvidence;
    }
    private void OpenGearFormula()
    {
        string role = GearPet ? Controller.Profile.ActiveLoadout.PetPreset : preset.SelectedItem?.ToString() ?? "Balanced";
        var resolved = GearPet ? null : SelfRoleRatings.Resolve(Controller.Profile, Controller.Profile.ActiveLoadout, role);
        var weights = resolved?.Weights ?? Controller.Profile.Weights.GetValueOrDefault(role) ?? GearScorer.Defaults["Pet DPS"];
        var row = gearTable.SelectedItem as GearRow;
        var comparison = row != null && gearDisplayedIdentity == GearIdentity(row) ? gearDisplayedComparison : null;
        new RatingFormulaDialog(role, new(weights), comparison, GearPet, row == null ? "" : row.Item.Name + " +" + GearInspectedRank(row), () => OpenGearStats()) { Owner = this }.ShowDialog();
    }
}
