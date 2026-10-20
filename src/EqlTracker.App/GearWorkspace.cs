using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private readonly DataGrid gearEquipment = UI.Table(("Slot", "Label", 86, null), ("Equipment", "Name", 170, null), ("Type", "Type", 100, null));
    private readonly ComboBox gearPosition = new() { Width = 210, DisplayMemberPath = "Label", SelectedValuePath = "Id" };
    private readonly ComboBox gearOwnership = UI.Select(["All", "Inventory Only", "Have Drops", "Have Ingredients", "Quest rewards"], "All", 140);
    private readonly ComboBox gearEligibility = UI.Select(["All", "Build", "Incompatible"], "All", 140);
    private readonly ComboBox gearPositive = UI.Select(["All comparisons", "Positive in either", "Positive in both"], "All comparisons", 150);
    private readonly ComboBox gearRankChoice = UI.Select(new[] { "Default" }.Concat(Enumerable.Range(0, 11).Select(r => "+" + r)), "Default", 105);
    private readonly ComboBox gearDestination = UI.Select(["Confirmed", "Planned build"], "Confirmed", 125);
    private readonly CheckBox gearWithoutAa = new() { Content = "Compare without AAs" };
    private readonly TextBlock gearDraftSummary = UI.Text("", 11, "Mint"), gearBreakEven = UI.Text("", 11, "Muted");
    private readonly BuildChangesPanel gearChanges = new();
    private Button? gearAddToBuild;
    private bool gearSelecting;
    private string gearPositionsKey = "";
    private string gearRowsKey = "";
    private LoadoutProfile? gearEvaluationLoadout;
    private List<EquipmentPosition> gearEvaluationReference = [];
    private List<EquipmentPosition> visibleGearPositions = [];
    internal DataGrid GearEquipmentTable => gearEquipment;
    private string GearSelectedPosition { get => GearPet ? Controller.Profile.GearPetPositionId : Controller.Profile.GearPositionId; set { if (GearPet) Controller.Profile.GearPetPositionId = value; else Controller.Profile.GearPositionId = value; } }
    internal void SelectGearPosition(string id) { GearSelectedPosition = id; gearPositionsKey = ""; gearFingerprint = null; RefreshGear(); }
    internal void SetGearTarget(bool pet) => gearTarget.SelectedItem = "Self";
    private bool GearPet => false;

    private DataGrid CreateGearTable()
    {
        var grid = UI.Table(("Item", "Name", 140, null), ("Type", "Type", 100, null), ("Replaces", "Slot", 78, null), ("Unbuffed Δ", "UnbuffedDelta", 84, "+0.0;-0.0;0"), ("Buffed Δ", "BuffedDelta", 78, "+0.0;-0.0;0"), ("Result", "Verdict", 150, null), ("Ownership", "Ownership", 150, null), ("Item score", "ItemScore", 85, "0.0"), ("Data quality", "Confidence", 160, null));
        foreach (DataGridTextColumn column in grid.Columns.Where(c => c.SortMemberPath is "ItemScore" or "UnbuffedDelta" or "BuffedDelta"))
        {
            ((Binding)column.Binding).TargetNullValue = "—";
            var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(ToolTipProperty, new Binding(column.SortMemberPath == "ItemScore" ? "ItemScoreTooltip" : "ScoreTooltip"))); column.ElementStyle = style;
        }
        grid.SelectionChanged += (_, _) => { gearBreakEven.Text = ""; ShowGearDetail(); };
        grid.ToolTip = "Both contexts are equally important. Select a physical replacement position; sort either delta column. Points express preferences, never predicted DPS.";
        foreach (var column in grid.Columns.Where(c => c.SortMemberPath is "Slot" or "Verdict" or "ItemScore" or "Confidence")) column.Visibility = Visibility.Collapsed;
        TableLayout.SetKey(grid, "gear-v2"); return grid;
    }
    private UIElement BuildGearWorkspace()
    {
        gearPreviewDelay.Tick += (_, _) => { gearPreviewDelay.Stop(); ShowSelectedGear(); };
        gearInspector.PreviewCommitted += ShowSelectedGear;
        Closed += (_, _) => { gearPreviewDelay.Stop(); gearPreviewCancel?.Cancel(); petPreviewDelay.Stop(); petPreviewCancel?.Cancel(); };
        gearBuildChoice.ToolTip = "Current is your directly editable equipment baseline. Named builds edit separately; Theorycraft saves explicitly and cannot become Current.";
        preset.ToolTip = "Choose editable stat preferences for Self equipment ratings; use ⓘ for every role's formula.";
        gearEligibility.ToolTip = "All includes unverified items. Build requires compatibility with current classes, level and slot; Incompatible shows known restrictions.";
        var root = new DockPanel(); var controls = new WrapPanel();
        var roleInfo = UI.Button("ⓘ", OpenGearRoles); roleInfo.ToolTip = "Select role for rating, click for more"; roleInfo.Padding = new(6, 2, 6, 2);
        var builds=GearCommandMenu(true);var tools=GearCommandMenu(false);
        gearUseCurrent = UI.Button("Use as current build", ConfirmGearDraft);
        gearUseCurrent.ToolTip = "Choose whether to save Current before replacing it with this ordinary build. Undo remains available.";
        gearSaveTheory = UI.Button("Save theorycraft build…", () => SaveGearTheorycraft());
        gearUseCurrentField = GearAction(gearUseCurrent);
        foreach (var child in new UIElement[] { GearField("Build",gearBuildChoice),GearField("Role",preset,roleInfo),GearAction(builds),gearUseCurrentField,GearAction(UI.Button("Undo",UndoGearDraft)),GearAction(tools),GearAction(gearSaveTheory) }) controls.Children.Add(child);
        gearUpdateCurrent.IsChecked = Controller.Profile.UpdateCurrentBuildFromInventory;
        gearUpdateCurrent.Click += (_, _) => { Controller.Profile.UpdateCurrentBuildFromInventory = gearUpdateCurrent.IsChecked == true; if (gearUpdateCurrent.IsChecked == true) { lock (Controller.Sync) GearBuildEditing.ApplyExport(Controller.Profile, Controller.Inventory); } GearEdited(); };
        var header = UI.Stack(controls, gearUpdateCurrent, gearTheoryNote);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var search = new WrapPanel(); search.Children.Add(FocusedLayout.Search(gearSearch, "Find a replacement…")); ConfigureGearControls(search); search.Children.Add(gearTheoryCheck); search.Children.Add(gearPosition);
        var filters = new WrapPanel(); foreach (var child in new UIElement[] { UI.Field("Items", gearOwnership), UI.Field("Compatibility", gearEligibility), UI.Field("Rank", gearRankChoice), UI.Field("Rating", gearPositive) }) filters.Children.Add(child);
        var candidatePane = new DockPanel();
        var candidateHeader = UI.Stack(gearFindLabel, search, FocusedLayout.Disclosure("Filters & Rank", filters, "Filter candidates without editing inventory or other saved builds."), gearSearchStatus);
        DockPanel.SetDock(candidateHeader, Dock.Top); candidatePane.Children.Add(candidateHeader);
        DockPanel.SetDock(gearChanges, Dock.Bottom); candidatePane.Children.Add(gearChanges);
        var actions = new WrapPanel(); gearAddToBuild = UI.Button("Add to build", () => TryGearDraft());
        gearAddToBuild.ToolTip="Replace a compatible slot in the selected build using the inspected rank. Uses the selected equipment slot, or offers a compatible-slot picker. Undo is available; ownership is unchanged.";
        foreach (var button in new[] { gearAddToBuild, UI.CommitButton("Add to queue", () => { if (gearTable.SelectedItem is GearRow row) PinGear(row.Item); }), UI.Button("Compare…", OpenGearComparison) }) actions.Children.Add(button);
        DockPanel.SetDock(actions, Dock.Bottom); candidatePane.Children.Add(actions);
        var future = new Expander { Header = "Future gear / wishlist", Content = UI.Stack(UI.Row(UI.Button("+ Add item", AddWishlist), UI.Button("Complete / reopen", ToggleWishlist), UI.Button("Remove goal", RemoveWishlist)), wishlist) };
        DockPanel.SetDock(future, Dock.Bottom); candidatePane.Children.Add(future); candidatePane.Children.Add(gearTable);
        SizeChanged += (_, _) => future.Visibility = ActualHeight < 540 ? Visibility.Collapsed : Visibility.Visible;
        var detailCard = FloatingInspector("Item Details", UI.Stack(gearInspector, gearBreakEven, new Expander { Header = "Sources & ownership", Content = gearDetails }));
        gearInnerSplit = UI.Split(candidatePane, detailCard, 400, Controller.Profile, "gear-candidates", 260, 230);
        if (!Controller.Profile.PanelSplits.ContainsKey("gear-candidates")) { gearInnerSplit.ColumnDefinitions[0].Width = new(.627, GridUnitType.Star); gearInnerSplit.ColumnDefinitions[2].Width = new(.373, GridUnitType.Star); }
        var equipmentPane = new DockPanel(); var title = gearEquipmentTitle; DockPanel.SetDock(title, Dock.Top); equipmentPane.Children.Add(title); equipmentPane.Children.Add(gearEquipment);
        gearOuterSplit = UI.Split(TrackerLeftPane(equipmentPane), gearInnerSplit, 300, Controller.Profile, "gear-workspace", 170, 495);
        if (!Controller.Profile.PanelSplits.ContainsKey("gear-workspace")) { gearOuterSplit.ColumnDefinitions[0].Width = new(.33, GridUnitType.Star); gearOuterSplit.ColumnDefinitions[2].Width = new(.67, GridUnitType.Star); }
        bool lastWide = true, lastDetails = true;
        gearOuterSplit.SizeChanged += (_, _) => {
            bool wide = ActualWidth >= 980; equipmentPane.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
            gearOuterSplit.Children.OfType<GridSplitter>().First().Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
            if (!wide) { gearOuterSplit.ColumnDefinitions[0].MinWidth = 0; gearOuterSplit.ColumnDefinitions[0].Width = new(0); gearOuterSplit.ColumnDefinitions[1].Width = new(0); }
            else if (!lastWide) { double fraction = Controller.Profile.PanelSplits.GetValueOrDefault("gear-workspace", .67); gearOuterSplit.ColumnDefinitions[0].MinWidth = 170; gearOuterSplit.ColumnDefinitions[0].Width = new(1-fraction, GridUnitType.Star); gearOuterSplit.ColumnDefinitions[2].Width = new(fraction, GridUnitType.Star); gearOuterSplit.ColumnDefinitions[1].Width = new(5); }
            bool details = ActualWidth >= 1080 && ActualHeight >= 540;
            detailCard.Visibility = details ? Visibility.Visible : Visibility.Collapsed; gearInnerSplit.Children.OfType<GridSplitter>().First().Visibility = details ? Visibility.Visible : Visibility.Collapsed;
            if (!details) { gearInnerSplit.ColumnDefinitions[2].MinWidth = 0; gearInnerSplit.ColumnDefinitions[2].Width = new(0); gearInnerSplit.ColumnDefinitions[1].Width = new(0); gearInnerSplit.ColumnDefinitions[0].MinWidth = 0; }
            else if (!lastDetails) { double fraction = Controller.Profile.PanelSplits.GetValueOrDefault("gear-candidates", .373); gearInnerSplit.ColumnDefinitions[0].MinWidth = 260; gearInnerSplit.ColumnDefinitions[0].Width = new(1-fraction, GridUnitType.Star); gearInnerSplit.ColumnDefinitions[2].MinWidth = 230; gearInnerSplit.ColumnDefinitions[2].Width = new(fraction, GridUnitType.Star); gearInnerSplit.ColumnDefinitions[1].Width = new(5); }
            gearFindLabel.Visibility = ActualHeight < 540 ? Visibility.Collapsed : Visibility.Visible;
            gearSearch.Width = ActualWidth < 800 ? 140 : 220;
            lastDetails = details; lastWide = wide; UpdateGearBuildControls();
        };
        root.Children.Add(gearOuterSplit);
        gearEquipment.SelectionChanged += (_, _) => { if (!gearSelecting && gearEquipment.SelectedItem is EquipmentPosition position) { GearSelectedPosition = position.Id; gearFingerprint = null; RefreshGear(); } };
        gearPosition.SelectionChanged += (_, _) => { if (!gearSelecting && !initializing && gearPosition.SelectedItem is EquipmentPosition position) { GearSelectedPosition = position.Id; gearFingerprint = null; RefreshGear(); } };
        foreach (var control in new[] { gearOwnership, gearEligibility, gearPositive, gearRankChoice }) control.SelectionChanged += (_, _) => { if (!initializing) { gearFingerprint = null; RefreshGear(); } };
        gearPosition.ToolTip = "Choose a replacement slot. Paired and Any Slot positions stay separate. Unknown equipment differs from a known-empty slot.";
        gearRankChoice.ToolTip = "Default uses recorded physical ranks, or +0 for catalog items. A selected +0–10 previews unowned candidates only. Saved item ranks stay unchanged.";
        gearOwnership.ToolTip = "All groups copies; Inventory Only lists physical copies. Have Drops shows quest rewards with some components owned; Have Ingredients shows sourced crafted outputs with some ingredients owned.";
        ConfigureGearDrag();
        InitializeGearBuilds(); return root;
    }
    private void ShowGearSources()
    {
        if (gearTable.SelectedItem is not GearRow row) return;
        var panel = new FrostWindow(Controller, row.Item.Name + " · sources", "Acquisition requirements and availability", 620, 430);
        var content = new StackPanel(); AddFocusedSources(content, row.Item.Sources); content.Children.Add(UI.Button("Open wiki", () => UI.OpenUrl(row.WikiUrl)));
        panel.Body.Children.Add(UI.Scroll(content)); Native.ShowPanel(panel);
    }
    private List<EquipmentPosition> GearReference(LoadoutProfile loadout, bool pet) => pet ? GuidedPetBuilds.Positions(PetInventoryEditing.Confirmed(loadout, Controller.Catalog), loadout, Controller.Catalog) : EquipmentPositions.For(loadout, false);
    private void RefreshGear()
    {
        gearUpdateCurrent.ToolTip=NativeSyncPolicy.AutoUpdateHelp(Controller.Profile.NativeSync);
        if (gearBuildLoading) return;
        if (gearOwnerName != Controller.Profile.SelectedLoadout) {
            gearOwnerName = Controller.Profile.SelectedLoadout; GearBuildEditing.Migrate(Controller.Profile.ActiveLoadout); RefreshGearBuildChoices(Controller.Profile.ActiveLoadout.SelectedGearBuildId);
            bool previous = initializing; initializing = true; preset.SelectedItem = Controller.Profile.ActiveLoadout.Preset; initializing = previous; gearPositionsKey = ""; gearSelectedKey = ""; gearWorkspaceRevision++;
        }
        long sourceRevision = Controller.EquipmentRevision; string id = GearSelectedPosition;
        var selected = SelectedGearBuild; bool theory = gearTheory != null;
        string signature = $"{sourceRevision}|{Controller.Catalog.Items.Count}|{Controller.Inventory.Revision}|{gearWorkspaceRevision}|{gearSearch.Text}|{preset.SelectedItem}|{gearBuildChoice.SelectedValue}|{gearRankChoice.SelectedIndex}|{gearOwnership.SelectedIndex}|{gearEligibility.SelectedIndex}|{gearPositive.SelectedIndex}|{gearWithoutAa.IsChecked}|{id}|{string.Join(',',Controller.Profile.GearExpansions)}|{theory}";
        if (gearFingerprint == signature) return; gearFingerprint = signature; gearViewCancel?.Cancel(); gearViewCancel?.Dispose(); gearViewCancel = new(); var token = gearViewCancel.Token;
        wishlist.DisplayMemberPath = "Display"; wishlist.ItemsSource = Controller.Profile.Wishlist.Select(x => new WishlistRow(x)).ToList();
        var loadout = EquipmentPositions.Copy(Controller.EquipmentView?.Profile.ActiveLoadout ?? Controller.Profile.ActiveLoadout); loadout.EquipmentPositions = EquipmentPositions.Copy(Controller.Profile.ActiveLoadout.EquipmentPositions); loadout.Equipment = EquipmentPositions.Copy(Controller.Profile.ActiveLoadout.Equipment);
        var reference = GearReference(loadout, false); var positions = selected == null ? reference : EquipmentPositions.Copy(selected.Positions);
        if (id != "*" && !positions.Any(p => p.Id == id)) { id = "*"; GearSelectedPosition = id; }
        string positionKey = sourceRevision + "|" + gearWorkspaceRevision + "|" + gearBuildChoice.SelectedValue + "|" + id;
        gearEvaluationLoadout = loadout; gearEvaluationReference = reference;
        if (gearPositionsKey != positionKey)
        {
            gearPositionsKey = positionKey; gearSelecting = true; visibleGearPositions = positions;
            foreach (var p in positions) p.Type = p.Item == null ? "" : (Controller.Catalog.FindKey(p.Item.ConfirmedItemKey) ?? Controller.Catalog.Find(p.Item.Name)) is { } definition ? PetItemRules.Type(definition) : "Unknown";
            UI.SetRows(gearEquipment, positions); gearPosition.ItemsSource = new[] { new EquipmentPosition { Id = "*", Slot = "All slots" } }.Concat(positions).ToList(); gearEquipment.SelectedItem = gearEquipment.Items.OfType<EquipmentPosition>().FirstOrDefault(p => p.Id == id); gearPosition.SelectedValue = id; gearSelecting = false;
        }
        var weights = new Dictionary<string,double>(Controller.Profile.Weights.GetValueOrDefault(preset.SelectedItem?.ToString() ?? "Balanced Tank", GearScorer.Defaults["Balanced Tank"]));
        var catalog = Controller.EquipmentView?.Catalog ?? new Catalog { Items = Controller.Catalog.Items.ToList(), Quests = Controller.Catalog.Quests.ToList() }; var inventory = Controller.EquipmentView?.Inventory ?? Controller.Inventory;
        string query = gearSearch.Text.Trim(); int ownedFilter = gearOwnership.SelectedIndex, eligibleFilter = gearEligibility.SelectedIndex, positiveFilter = gearPositive.SelectedIndex, rankOverride = gearRankChoice.SelectedIndex - 1; bool aa = gearWithoutAa.IsChecked != true;
        var expansions = Controller.Profile.GearExpansions.ToHashSet(); var pending = Controller.PendingGear(query).ToList(); var profile = new CharacterProfile { Routes = new(Controller.Profile.Routes), CompletedSteps = new(Controller.Profile.CompletedSteps) };
        (List<GearRow> Rows, LoadoutComparison Draft, BuildChangesPanel.Snapshot Changes) Build()
        {
            var session = new LoadoutEvaluationSession(catalog, loadout, false, weights, positions, reference, theory);
            var referenceSession = new LoadoutEvaluationSession(catalog, loadout, false, weights, reference, reference, theory);
            var materials = ownedFilter is 2 or 3 ? new MaterialIndex(catalog, inventory, profile) : null;
            var questRewards = catalog.Quests.Select(q => q.RewardKey).ToHashSet();
            var candidates = catalog.Items.Where(i => expansions.Contains(ItemPresentation.Expansion(i)) && (i.Slots.Count > 0 || query.Length >= 3) && (i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || i.Aliases.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase)))).ToList();
            foreach (string title in pending) if (expansions.Contains("Unknown") && !candidates.Any(i => i.Name.Equals(title, StringComparison.OrdinalIgnoreCase))) candidates.Add(new() { Key = Identity.Key(title), Name = title, WikiUrl = "https://eqlwiki.com/" + Uri.EscapeDataString(title.Replace(' ', '_')) });
            var rows = new List<GearRow>();
            foreach (var item in candidates)
            {
                token.ThrowIfCancellationRequested(); var matches = inventory.Evidence(item); var physical = matches.Where(e => !e.IsCollection && e.Count > 0).ToList(); bool collection = matches.Any(e => e.IsCollection);
                var material = ownedFilter is 2 or 3 ? materials!.For(item.Key, ownedFilter == 2 ? RecipeKind.Quest : RecipeKind.Tradeskill) : null;
                if (ownedFilter == 1 && physical.Count == 0 || material is { HasAny: false } || ownedFilter == 4 && !questRewards.Contains(item.Key)) continue;
                foreach (var position in positions.Where(p => (id == "*" ? p.Slot != "Any Slot" : p.Id == id) && (item.Slots.Count == 0 || EquipmentPositions.Fits(item, p, false))))
                foreach (var instance in PhysicalItemChoices.Rows(physical, ownedFilter == 1))
                {
                    int rank = instance?.Rank ?? (rankOverride >= 0 ? rankOverride : 0); var result = session.Present(item, rank, position.Id, instance, aa); var evaluation = result.Evaluation!;
                    bool eligible = result.Eligibility == "Eligible" && evaluation.Status != "Eligibility unconfirmed";
                    bool incompatible = !eligible && !result.Eligibility.Contains("nknown", StringComparison.OrdinalIgnoreCase) && !result.Eligibility.Contains("pending", StringComparison.OrdinalIgnoreCase) && !result.Eligibility.StartsWith("Confirm");
                    if (eligibleFilter == 1 && !eligible || eligibleFilter == 2 && !incompatible || positiveFilter == 1 && !(evaluation.Unbuffed.Delta > 0 || evaluation.Buffed.Delta > 0) || positiveFilter == 2 && evaluation.Group != "Positive in both") continue;
                    rows.Add(new(result.Candidate, position.Label, "Replaces: " + result.Replaces, result.CanCompare ? result.Delta : null, evaluation.Group, result.Confidence, result.Contributions + "\n" + result.Effects, item.WikiUrl) { Item = item, Rank = rank, Comparison = result, PositionId = position.Id, Instance = instance, Ownership = material?.Label ?? (instance != null ? instance.Location + (instance.Estimated ? " · log estimate" : " · physical") : collection ? "Collection · quantity unknown" : "Not owned") });
                }
            }
            if (id == "*") rows = rows.GroupBy(r => r.Item.Key + "|" + PhysicalItemChoices.Identity(r.Instance)).Select(g => g.OrderByDescending(r => Math.Min(r.UnbuffedDelta ?? double.NegativeInfinity, r.BuffedDelta ?? double.NegativeInfinity)).ThenBy(r => r.PositionId, StringComparer.Ordinal).First()).ToList();
            return (rows.OrderBy(r => r.Verdict == "Positive in both" ? 0 : r.Verdict == "Context-dependent / estimate" ? 1 : r.Verdict == "Data needed" ? 3 : 2).ThenBy(r => r.Name).ThenBy(r => r.Slot).ToList(), referenceSession.CompareBuild(positions), BuildChangesPanel.Prepare(reference, positions, catalog, referenceSession));
        }
        void Apply((List<GearRow> Rows, LoadoutComparison Draft, BuildChangesPanel.Snapshot Changes) view)
        {
            if (sourceRevision != Controller.EquipmentRevision || token.IsCancellationRequested || gearFingerprint != signature || SelectedTab != TrackerTab.Gear || !Body.IsVisible) { SuppressedGearBuilds++; return; }
            gearRowsKey = signature; if (ActualHeight < 540) gearChanges.IsExpanded = false; gearChanges.Show(view.Changes, selected != null, UndoGearDraft, RemoveGearBuildChange);
            gearDraftSummary.Text = selected == null ? "Current build · Unbuffed and Usual buffs · preference points" : $"{selected.Name} vs Current · Unbuffed {view.Draft.Unbuffed.Delta:+0.0;-0.0;0} · Usual buffs {view.Draft.Buffed.Delta:+0.0;-0.0;0} points";
            var previous = gearTable.SelectedItem as GearRow; UI.SetRows(gearTable, view.Rows); var displayed = gearTable.Items.OfType<GearRow>().ToList(); gearTable.SelectedItem = displayed.FirstOrDefault(r => r.Item.Key == previous?.Item.Key && r.PositionId == previous.PositionId && r.Instance?.Location == previous.Instance?.Location) ?? displayed.FirstOrDefault();
            gearSearchStatus.Visibility = ActualHeight < 540 && view.Rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            gearSearchStatus.Text = view.Rows.Count + " comparisons · " + (view.Rows.Count == 0 ? "Choose All slots or clear filters." : Controller.GearSearchStatus); UpdateGearBuildControls(); ShowGearDetail();
        }
        if (Controller.Demo) Apply(Build());
        else { gearSearchStatus.Text = "Comparing local equipment…"; _ = Task.Run(Build, token).ContinueWith(task => Dispatcher.InvokeAsync(() => { if (task.IsCanceled || token.IsCancellationRequested) { SuppressedGearBuilds++; return; } if (task.IsCompletedSuccessfully) Apply(task.Result); else gearSearchStatus.Text = "Comparison unavailable; change filters to retry."; }), TaskScheduler.Default); }
    }
    private void OpenGearStats()
    {
        var loadout = Controller.Profile.ActiveLoadout; bool pet = GearPet; var reference = GearReference(loadout, pet);
        Native.ShowPanel(new StatProfileDialog(Controller, loadout, pet, reference, () => { gearFingerprint = null; libraryKey = ""; petKey = ""; Refresh(); }, aaCompare: value => { gearWithoutAa.IsChecked = value; GearEdited(); }, withoutAAs: gearWithoutAa.IsChecked == true) { Owner = this });
    }
    private void TryGearDraft(GearRow? selected = null, string? positionId = null)
    {
        var row = selected ?? gearTable.SelectedItem as GearRow; if (row == null) return;
        if (selected == null) row = row with { Rank = GearInspectedRank(row), Instance = GearInspectedInstance(row), Comparison = gearDisplayedComparison ?? row.Comparison };
        var prepared=PrepareGearPlacement(row);string? id=positionId;
        if(id==null){id=prepared.FirstOrDefault(p=>p.Target.Id==GearSelectedPosition&&p.Target.Allowed)?.Target.Id;var valid=prepared.Where(p=>p.Target.Allowed).ToArray();if(id==null&&valid.Length==1)id=valid[0].Target.Id;
            if(id==null&&valid.Length>1){var picker=new GearSlotPickerDialog(row.Name,valid.Select(p=>p.Target)){Owner=this};if(picker.ShowDialog()!=true)return;id=picker.PositionId;}}
        if(id==null){gearSearchStatus.Text=prepared.FirstOrDefault(p=>p.Target.Reason!="Eligible"&&!p.Target.Reason.StartsWith("Does not fit"))?.Target.Reason??"No compatible replacement slot is available.";return;}
        var result=PrepareGearPlacement(row).FirstOrDefault(p=>p.Target.Id==id);if(result==null){gearSearchStatus.Text="Choose a compatible equipment row.";return;}
        bool applied;string error;lock(Controller.Sync)applied=GearPlacementSession.Commit(Controller.Profile.ActiveLoadout,SelectedGearBuild,result,Controller.Catalog,out error);
        if(!applied){gearSearchStatus.Text=error;return;}
        var build=SelectedGearBuild;
        GearEdited(); gearBreakEven.Text = row.Item.Name + " +" + row.Rank + " added to " + (build?.Name ?? "Current build") + " · Undo available.";
    }
    private IReadOnlyList<GearPlacementResult> PrepareGearPlacement(GearRow row)
    {var owner=Controller.Profile.ActiveLoadout;var positions=SelectedGearBuild?.Positions??EquipmentPositions.For(owner,false);return new GearPlacementSession(Controller.Catalog,owner,new(Controller.Profile.Weights.GetValueOrDefault(preset.SelectedItem?.ToString()??"Balanced Tank",GearScorer.Defaults["Balanced Tank"])),positions,GearReference(owner,false),gearTheory!=null).Prepare(row.Item,row.Rank,row.Instance);}
    private void UndoGearDraft()
    {
        if (gearRemovedBuild != null) { Controller.Profile.ActiveLoadout.GearBuilds.Add(gearRemovedBuild); string id = gearRemovedBuild.Id; gearRemovedBuild = null; RefreshGearBuildChoices(id); GearEdited(); return; }
        bool undone; lock (Controller.Sync) undone = GearBuildEditing.Undo(Controller.Profile.ActiveLoadout, SelectedGearBuild); if (undone) { RefreshGearBuildChoices(gearWorkspaceId); GearEdited(); }
    }
    private void ConfirmGearDraft()
    {
        if (SelectedGearBuild is not {IsTheorycraft:false} build||gearTheory!=null) return;
        string name="";bool refreshed=false;
        while(true){var owner=Controller.Profile.ActiveLoadout;string fingerprint=BuildActivation.GearFingerprint(owner);var dialog=new CurrentBuildBackupDialog(build.Name,name,refreshed){Owner=this};if(dialog.ShowDialog()!=true)return;name=dialog.BackupName;
            bool applied;string reason;lock(Controller.Sync)applied=BuildActivation.Gear(owner,build,fingerprint,dialog.Choice,name,out reason);
            if(!applied&&reason.StartsWith("Current changed")){refreshed=true;continue;}if(!applied){gearSearchStatus.Text=reason;return;}RefreshGearBuildChoices("current");GearEdited();gearSearchStatus.Text=reason;return;}
    }
    private async void CalculateBreakEven()
    {
        if (gearTable.SelectedItem is not GearRow row) return; gearBreakEven.Text = "Checking preference break-even +0…+10…";
        var token = gearViewCancel?.Token ?? CancellationToken.None; var owner = EquipmentPositions.Copy(Controller.Profile.ActiveLoadout); var positions = EquipmentPositions.Copy(visibleGearPositions); var catalog = Controller.EquipmentView?.Catalog ?? Controller.Catalog; var weights = new Dictionary<string,double>(Controller.Profile.Weights.GetValueOrDefault(preset.SelectedItem?.ToString() ?? "Balanced Tank", GearScorer.Defaults["Balanced Tank"])); bool theory = gearTheory != null; bool aa = gearWithoutAa.IsChecked != true;
        try { int? rank = await Task.Run(() => { var session = new LoadoutEvaluationSession(catalog, owner, false, weights, positions, GearReference(owner, false), theory); for (int r = 0; r <= 10; r++) { token.ThrowIfCancellationRequested(); var c = session.Compare(row.Item, r, row.PositionId, GearInspectedInstance(row), aa); if (c.Unbuffed.Delta > 0 && c.Buffed.Delta > 0) return (int?)r; } return null; }, token);
            if (!token.IsCancellationRequested && ReferenceEquals(row, gearTable.SelectedItem)) gearBreakEven.Text = rank.HasValue ? $"Estimated preference break-even: +{rank}." : "No positive result in both contexts through +10; check eligibility and profile evidence.";
        } catch (OperationCanceledException) { }
    }
}
