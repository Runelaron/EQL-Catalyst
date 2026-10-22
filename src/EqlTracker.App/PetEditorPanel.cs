using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private readonly TextBlock petListTitle = UI.Text("Confirmed pet inventory · manual", 13, "Accent", true);
    private readonly ItemDetailsInspector petComparison = new();
    private readonly TextBlock petTotals = UI.Text("", 11, "Foreground"), petWeapons = UI.Text("", 10, "Mint");
    private readonly BuildChangesPanel petChanges = new();
    private readonly CheckBox petDualWield = new() { Content = "Dual wield", ToolTip = "Reserve Primary and Secondary positions. Turning off keeps Secondary setup inactive and frees position 2." };
    private readonly TextBlock petSlotWarning = UI.Text("", 11, "Melee");
    private ComboBox? petRoleChoice;
    private string SelectedPetPreset => PetRoleRatings.Normalize(SelectedPetInventory?.Preset is { Length: > 0 } name ? name : Controller.Profile.ActiveLoadout.PetPreset);
    private string petPreferredPosition = "*";
    private sealed record PetReplacementChoice(string Id, string Label) { public override string ToString() => Label; }
    private readonly ComboBox petReplacement = new() { Width = 155, DisplayMemberPath = "Label", SelectedValuePath = "Id" };
    private readonly TextBlock petDraftSummary = UI.Text("", 11, "Mint");
    private readonly ComboBox petSpell = EditablePetNumber([], "", 230), petSummonTier = EditablePetNumber(Enumerable.Range(0, 11).Select(i => i.ToString()), "0", 65), petFocusName = EditablePetNumber(["None"], "None", 210);
    private FrameworkElement? petFocusField;
    private PetInventoryDestination PetDestination => petTheory != null || petBuild.SelectedItem is PetBuildSelection { Destination: PetInventoryDestination.Planned } ? PetInventoryDestination.Planned : PetInventoryDestination.Confirmed;
    private CancellationTokenSource? petViewCancel;
    private string petRowsKey = "";
    private sealed record PetSearchRow(ItemDefinition Item, string Ownership)
    { public string Type => PetItemRules.Type(Item); public int Rank { get; init; } public InventoryEntry? Instance { get; init; } public GearComparison? Comparison { get; init; } public double? UnbuffedDelta => Comparison?.Evaluation?.Status=="Conditional"?null:Comparison?.Evaluation?.Unbuffed.Delta; public double? DefenseDelta=>Comparison?.Evaluation?.Status=="Conditional"?null:Comparison?.Evaluation?.PetDefense?.Delta; public bool StrongTradeoff { get; set; } public string RatingTooltip => Comparison?.Evaluation?.Status == "Conditional" ? "Conditional · " + Comparison.Confidence : PetRoleRatings.Text(Comparison?.Evaluation); public string Rating => Comparison?.Evaluation?.Status == "Conditional" ? "Cond." : PetRoleRatings.Text(Comparison?.Evaluation); public override string ToString() => Item.Key + "|" + Instance?.Location + "|" + Rank; public string Name => Item.Name + (Rank > 0 ? " +" + Rank : ""); public string SlotText => string.Join(" / ", Item.Slots); }
    private sealed class PetItemRow(PetInventoryItem item, string state, string procs = "", string type = "Unknown")
    {
        public override string ToString() => Item.Id.Length > 0 ? Item.Id : "empty:" + Item.Position;
        public PetInventoryItem Item { get; } = item; public string State { get; } = state;
        public string Type { get; } = type;
        public int Position => Item.Position; public string Name => Item.ItemKey.Length == 0 ? "Empty position" : Item.Name + (Item.Exaltations.Count > 0 ? " ✧" : "");
        public string Grip => Item.ItemKey.Length > 0 ? "⠿" : ""; public string Procs { get; } = procs; public string Slot => Item.Slot; public int Rank => Item.Rank; public string RankInput { get; set; } = item.ItemKey.Length == 0 ? "" : item.Rank.ToString();
    }
    private sealed record PetCatalogDrag(string Key, InventoryEntry? Instance);
    private sealed record PetInventoryDrag(string Build, string Item, PetInventoryDestination Destination);
    private bool petRankEditing;
    private string petSelectedId = "";
    private readonly TextBlock petFeedback = UI.Text("", 11, "Mint");
    private PetBuild? CurrentBuild => petTheory?.Working ?? Controller.Profile.ActiveLoadout.PetBuilds.FirstOrDefault(b => b.Id == petBuild.SelectedValue?.ToString());
    private PetBuild? SelectedPetInventory => PetDestination == PetInventoryDestination.Confirmed ? PetInventoryEditing.Confirmed(Controller.Profile.ActiveLoadout, Controller.Catalog) : CurrentBuild;
    internal void SelectPetInventoryPosition(int position) { petItems.SelectedItem = petItems.Items.OfType<PetItemRow>().FirstOrDefault(r=>r.Position==position); }
    internal void SelectPetDestination(PetInventoryDestination destination)
    {
        if (destination == PetInventoryDestination.Confirmed) RefreshBuildChoices("current");
        else { if (CurrentBuild != null) return; if (Controller.Profile.ActiveLoadout.PetBuilds.Count == 0) Controller.Profile.ActiveLoadout.PetBuilds.Add(new() { Name = "Planned build", PetType = Controller.Profile.EffectivePetElement }); RefreshBuildChoices(Controller.Profile.ActiveLoadout.PetBuilds[0].Id); }
    }
    private UIElement BuildPet()
    {
        if(PetLayoutDefaults.Apply(Controller.Profile)) Controller.Save();
        var root = new DockPanel(); petRank.IsEditable = false; petBuild.DisplayMemberPath = "Name"; petBuild.SelectedValuePath = "Id";
        CanHide = () => ConfirmGearWorkspaceExit() && ConfirmPetWorkspaceExit();
        petComparison.PreviewCommitted += ShowPetComparison;
        petPreviewDelay.Tick += (_, _) => { petPreviewDelay.Stop(); ShowPetComparison(); };
        petTheoryCheck.Click += (_, _) => TogglePetTheorycraft();
        petTheorySave = UI.Button("Save theorycraft build…", () => SavePetTheorycraft()); petTheorySave.Visibility = Visibility.Collapsed;
        petTheorySave.ToolTip = "Explicitly save this isolated workspace as a Theorycraft build. Current inventory is unchanged.";
        var top = new WrapPanel();
        var petRole = UI.Select(PetRoleRatings.Choices(Controller.Profile), PetRoleRatings.Normalize(Controller.Profile.ActiveLoadout.PetPreset), 145);
        petRoleChoice = petRole;
        petRole.ToolTip = "Editable role preferences. Points are not predicted DPS, procs or avoidance.";
        petRole.SelectionChanged += (_, _) => { if (initializing || petContextLoading || SelectedPetInventory is not { } build) return; string selected = petRole.SelectedItem?.ToString() ?? "Pet DPS"; if (SelectedPetPreset == selected) return; PetBuildEditing.Remember(build, "Rating preset"); build.Preset = selected; if (PetDestination == PetInventoryDestination.Confirmed) Controller.Profile.ActiveLoadout.PetPreset = selected; PetEdited(true); };
        var confirm = UI.Button("Use as current build…", ConfirmBuild); confirm.ToolTip = "Review this plan before replacing Current build. Undo restores the previous inventory and context.";
        var toolsButton = FocusedLayout.Menu("Build ▾",
            ("New build from current", CloneConfirmedPet, "Copy Current build into a separate named plan without activating it."),
            ("New empty build", NewPetBuild, "Start a new named plan without changing confirmed inventory."),
            ("Duplicate", DuplicatePetBuild, "Copy the selected inventory to a new named planned build."),
            ("Rename", RenamePetBuild, "Rename the selected saved plan."),
            ("Stat profiles…", OpenPetStats, "Review automatic evidence and confirm missing pet totals. Owner stats are never copied."),
            ("Reset Pet panels", ResetPetPanels, "Restore inventory/candidates/item details to 33% / 42% / 25%."));
        toolsButton.Width=72;toolsButton.Height=28;toolsButton.Margin=new(0); var undoPet=UI.Button("Undo",UndoPetBuild);undoPet.Width=72;undoPet.Height=28;undoPet.Margin=new(0);
        var confirmField=UI.ActionField(confirm);
        foreach (var control in new UIElement[] { UI.Field("Build", petBuild), UI.ActionField(undoPet), confirmField, UI.ActionField(toolsButton), UI.ActionField(petTheorySave) }) top.Children.Add(control);
        petFocusField = (FrameworkElement)UI.Field("Focus", petFocusName);
        var context = new WrapPanel(); foreach (var control in new UIElement[] { UI.Field("Pet Type", petElement), UI.Field("Spell", petSpell), UI.Field("Rank", petSummonTier), petFocusField, UI.Field("Level", petLevel), UI.Field("Role", petRole) }) context.Children.Add(control);
        top.Children.Add(UI.ActionField(UI.Button("Compare pets…", OpenPetComparison)));
        top.Children.Add(UI.ActionField(UI.Button("Pet weights…", OpenPetGoalWeights)));
        var header = UI.Stack(top, petTheoryNote,
            PetDetailsDisclosure( UI.Stack(context, petContextNote), "Summon and focus evidence. Valid edits save automatically; Undo restores changes."));
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        petSearch.Width=170;petSearch.Height=28;petSearch.Margin=new(0);
        var left = new DockPanel(); var searchControls = new WrapPanel(); searchControls.Children.Add(UI.Field("Search",(FrameworkElement)FocusedLayout.Search(petSearch, "Find pet equipment…"))); searchControls.Children.Add(BuildPetExpansionFilter()); petTheoryCheck.VerticalAlignment=VerticalAlignment.Center; searchControls.Children.Add(UI.ActionField(petTheoryCheck));
        var filters = new WrapPanel(); foreach (var control in new UIElement[] { UI.Field("Ownership", petOwnership), UI.Field("Slot", petSlot), UI.Field("Set Rank", petRank), UI.Field("Current item", petReplacement) }) filters.Children.Add(control);
        petCandidateFilterNote.Visibility = Visibility.Collapsed;
        var candidateHeader = UI.Stack(searchControls, petCandidateFilterNote, FocusedLayout.Disclosure("Filters & Rank", filters, "Filter catalog items by ownership or compatibility; inventory positions do not enforce wearable slots."));
        var boundedHeader = UI.BoundedHeader(candidateHeader, left); DockPanel.SetDock(boundedHeader, Dock.Top); left.Children.Add(boundedHeader);
        var ratingColumn = new DataGridTextColumn { Header = "Rating", Binding = new System.Windows.Data.Binding("Rating"), SortMemberPath = "UnbuffedDelta", Width = 78 };
        var ratingCell=new Style(typeof(DataGridCell),TryFindResource(typeof(DataGridCell)) as Style);ratingCell.Setters.Add(new Setter(ToolTipProperty,new System.Windows.Data.Binding("RatingTooltip")));ratingCell.Setters.Add(new Setter(System.Windows.Automation.AutomationProperties.NameProperty,new System.Windows.Data.Binding("RatingTooltip")));ratingColumn.CellStyle=ratingCell;
        petResults.Columns.Add(ratingColumn); ConfigurePetRatingSort(ratingColumn);
        petResults.Columns.Insert(1, new DataGridTextColumn { Header = "Type", SortMemberPath = "Type", Binding = new System.Windows.Data.Binding("Type"), Width = 85 });

        TableLayout.SetDefaultScale(petResults, petResults.Columns[0], 2d / 3);
        TableLayout.SetDefaultSample(petResults, ratingColumn, "+9999.9");
        TableLayout.SetKey(petResults, "tracker.pet.candidates"); TableLayout.SetKey(petItems, "tracker.pet.inventory");
        var gripColumn = new DataGridTextColumn { Header = "", Binding = new System.Windows.Data.Binding("Grip"), Width = 16, MinWidth = 16, MaxWidth = 16, CanUserResize = false, IsReadOnly = true };
        var gripCell = new Style(typeof(DataGridCell),TryFindResource(typeof(DataGridCell)) as Style); gripCell.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(1,3,1,3))); gripColumn.CellStyle=gripCell;
        TableLayout.SuppressCellTooltip(gripColumn); petItems.Columns.Insert(0, gripColumn);
        petItems.Columns.Insert(3, new DataGridTextColumn { Header = "Type", SortMemberPath = "Type", Binding = new System.Windows.Data.Binding("Type"), Width = 78 });

        foreach (var column in petResults.Columns.Where(c => c.SortMemberPath is "SlotText" or "Ownership")) column.Visibility = Visibility.Collapsed;
        var resultStyle = new Style(typeof(DataGridRow)); resultStyle.Setters.Add(new Setter(ToolTipProperty, new System.Windows.Data.Binding("SlotText"))); petResults.RowStyle = resultStyle;
        var add = UI.CommitButton("Add to planned build", AddPetItem); var leftActions = new WrapPanel(); leftActions.Children.Add(add); leftActions.Children.Add(UI.Button("Compare…", OpenPetComparisonPanel));
        DockPanel.SetDock(leftActions, Dock.Bottom); left.Children.Add(leftActions); left.Children.Add(petResults);
        var right = new DockPanel();
        var inventoryTools = FocusedLayout.Menu("Inventory ▾", ("Edit selected item…", () => EditPetItem((petItems.SelectedItem as PetItemRow)?.Item), "Edit position, intended slot, rank and Exaltations."), ("Remove item", RemovePetItem, "Remove from this build; Undo is available."));
        petDualWield.Click += (_, _) => { if (petContextLoading || SelectedPetInventory is not { } build) return; PetSlotPlanning.Toggle(build, petDualWield.IsChecked == true, Controller.Catalog, PetEquipmentPlanner.Capacity(Controller.Profile.ActiveLoadout.Classes)); if (PetDestination == PetInventoryDestination.Confirmed) PetInventoryEditing.Confirm(Controller.Profile.ActiveLoadout, build, DateTime.Now); PetEdited(); };
        var inventoryRow = UI.Row(inventoryTools, petDualWield); petDualWield.Margin = new(10,0,0,0); petDualWield.VerticalAlignment = VerticalAlignment.Center;
        var rightHeader = UI.Stack(petListTitle, inventoryRow, petSlotWarning); DockPanel.SetDock(rightHeader, Dock.Top); right.Children.Add(rightHeader);
        var totalsCard = UI.Card(UI.Stack(UI.Text("Gear totals", 12, "Accent", true), petTotals, petWeapons)); totalsCard.Padding = new(6, 4, 6, 4); totalsCard.ToolTip = "Inferred equipment contributions only. Unknown pet base stats and buffs are excluded; strongest worn haste is used. Edit item… corrects intended or verified slots."; DockPanel.SetDock(totalsCard, Dock.Bottom); right.Children.Add(totalsCard);
        DockPanel.SetDock(petFeedback, Dock.Bottom); right.Children.Add(petFeedback); right.Children.Add(petItems);
        foreach (var column in petItems.Columns.Where(c => c.SortMemberPath is "Slot" or "State")) column.Visibility = Visibility.Collapsed;
        petItems.Columns.Add(new DataGridTextColumn { Header = "Procs", Binding = new System.Windows.Data.Binding("Procs"), Width = 100, IsReadOnly = true });
        TableLayout.SetAutoMaximum(petItems, petItems.Columns[^1], 100);
        petItems.IsReadOnly = false; petItems.CanUserSortColumns = true;
        foreach (var column in petItems.Columns) column.CanUserSort = column.SortMemberPath == "Type";
        foreach (var column in petItems.Columns) column.IsReadOnly = true;
        var rankColumn = (DataGridTextColumn)petItems.Columns.Single(c => c.SortMemberPath == "Rank"); rankColumn.IsReadOnly = false; rankColumn.Header = "+Rank";
        rankColumn.Binding = new System.Windows.Data.Binding("RankInput") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.Explicit };
        petItems.BeginningEdit += (_, e) => { if (e.Row.Item is not PetItemRow { Item.ItemKey.Length: > 0 }) { e.Cancel = true; return; } petRankEditing = true; };
        petItems.CellEditEnding += (_, e) => {
            if (e.Row.Item is not PetItemRow row) return;
            if (e.EditAction == DataGridEditAction.Cancel) { row.RankInput = row.Item.Rank.ToString(); petRankEditing = false; return; }
            string value = (e.EditingElement as TextBox)?.Text ?? row.RankInput;
            bool valid = int.TryParse(value, out int rank) && CommitPetRank(row.Item.Id, rank);
            if (!valid) { if (!int.TryParse(value, out int ignoredRank)) PetMessage("Use a whole +Rank from 0 to 20.", true); e.Cancel = true; }
            Dispatcher.BeginInvoke(() => { if (!valid) { petItems.CancelEdit(DataGridEditingUnit.Cell); petItems.CancelEdit(DataGridEditingUnit.Row); } else petItems.CommitEdit(DataGridEditingUnit.Row, true); petRankEditing = false; row.RankInput = row.Item.Rank.ToString(); petKey = ""; RefreshPet(); });
        };
        var inventoryStyle = new Style(typeof(DataGridRow)); inventoryStyle.Setters.Add(new Setter(ToolTipProperty, new System.Windows.Data.Binding("State"))); petItems.RowStyle = inventoryStyle;
        var note = UI.Text("Pet inventory is manually confirmed · exports cannot identify it.", 11, "Muted"); note.ToolTip = "Add, right-click and drop use the next empty position. Add immediately; drag rows to reorder. +Rank edits inline. Use Edit item… for effects or verified equipment. Plans never imply actual ownership.";
        petListTitle.ToolTip = note.ToolTip;
        var footer = UI.Stack(petNote, petDraftSummary, petChanges); DockPanel.SetDock(footer, Dock.Bottom); right.Children.Insert(0, footer);
        var split = UI.Split(TrackerLeftPane(right), left, 400, Controller.Profile, "pet-focused-items", 180, 210);
        if (!Controller.Profile.PanelSplits.ContainsKey("pet-focused-items")) { split.ColumnDefinitions[0].Width = new(.44, GridUnitType.Star); split.ColumnDefinitions[2].Width = new(.56, GridUnitType.Star); }
        var comparison = FloatingInspector("Item Details", petComparison);
        var outer = UI.Split(split, comparison, 340, Controller.Profile, "pet-focused-comparison", 395, 220); root.Children.Add(UI.HorizontalWorkspace(outer, 620));
        if (!Controller.Profile.PanelSplits.ContainsKey("pet-focused-comparison")) { outer.ColumnDefinitions[0].Width = new(.75, GridUnitType.Star); outer.ColumnDefinitions[2].Width = new(.25, GridUnitType.Star); }
        petInnerSplit = split; petOuterSplit = outer;
        void Adapt()
        {
            petSearch.Width = ActualWidth < 800 ? 120 : 165;
            note.Visibility = petNote.Visibility = petDraftSummary.Visibility = ActualHeight < 540 ? Visibility.Collapsed : Visibility.Visible;
            petTotals.TextTrimming = TextTrimming.CharacterEllipsis;
            if (ActualHeight < 540) petChanges.IsExpanded = false;
        }
        SizeChanged += (_, _) => Adapt(); Loaded += (_, _) => Adapt();
        void DestinationChanged()
        {
            if (petContextLoading || petBuild.SelectedItem is not PetBuildSelection choice) return;
            if (!ChangePetWorkspace(choice)) return;
            confirm.IsEnabled = choice.Destination == PetInventoryDestination.Planned && petTheory == null;
            confirmField.Visibility=confirm.IsEnabled?Visibility.Visible:Visibility.Collapsed;
            add.Content = PetDestination == PetInventoryDestination.Planned ? "Add to planned build" : "Add manual item";
            add.ToolTip = "Add immediately at the next empty active inventory position. Undo reverses the addition; equipped slots remain inferred unless explicitly verified.";
            petBuild.ToolTip = "Current build is your saved active inventory. Named plans are edited separately and stay inactive until Use as current build….";
            if (petTheory == null) Controller.Profile.ActiveLoadout.SelectedPetBuildId = choice.Id;
            LoadPetContext(); petSelectedId = ""; petPreferredPosition = "*"; petReplacement.SelectedValue = "*";
            petListTitle.Text = petTheory != null ? "Theorycraft inventory · " + petTheory.Working.Name : PetDestination == PetInventoryDestination.Confirmed ? "Current build · manual inventory" : "Planned build · " + CurrentBuild?.Name;
            petKey = ""; if (!initializing) { if (petTheory == null) SchedulePetPersistence(); RefreshPet(); }
        }
        petSelectionChanged = DestinationChanged;
        petBuild.SelectionChanged += (_, _) => { if (!initializing) DestinationChanged(); };
        ConfigurePetContextEditing();
        petSearch.TextChanged += (_, _) => { petKey = ""; if (!initializing) { RefreshPet(); SchedulePetSearch(); } };
        petOwnership.SelectionChanged += (_, _) => { petKey = ""; if (!initializing) RefreshPet(); };
        petReplacement.SelectionChanged += (_, _) => { if (!initializing && petReplacement.SelectedValue is string id) { petPreferredPosition = id; SyncPetSelectionFromCurrentItem(id); petKey = ""; RefreshPet(); } };
        petResults.SelectionChanged += (_, _) => ShowPetComparison(); petRank.SelectionChanged += (_, _) => { if (!initializing) { petKey = ""; RefreshPet(); } };
        DataGridRow? RowAt(object source)
        {
            var node = source as DependencyObject;
            while (node != null) { if (node is DataGridRow row) return row; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node); }
            return null;
        }
        petResults.PreviewMouseRightButtonDown += (_, e) => { if (RowAt(e.OriginalSource)?.Item is PetSearchRow row) petResults.SelectedItem = row; };
        petItems.MouseDoubleClick += (_, e) => { var cell = Ancestor<DataGridCell>(e.OriginalSource); if (RowAt(e.OriginalSource)?.Item is PetItemRow row && cell?.Column != rankColumn) { EditPetItem(row.Item); e.Handled = true; } };
        petItems.PreviewMouseRightButtonDown += (_, e) => { if (RowAt(e.OriginalSource)?.Item is PetItemRow row) petItems.SelectedItem = row; };
        var catalogMenu = new ContextMenu(); var addMenu = new MenuItem { Header = "Add to selected list", ToolTip = "Add immediately to the selected inventory or plan, using the next empty position. No game items are moved." }; addMenu.Click += (_, _) => AddPetItem(); catalogMenu.Items.Add(addMenu); petResults.ContextMenu = catalogMenu;
        var inventoryMenu = new ContextMenu(); var edit = new MenuItem { Header = "Edit item…" }; edit.Click += (_, _) => EditPetItem((petItems.SelectedItem as PetItemRow)?.Item); inventoryMenu.Items.Add(edit); var remove = new MenuItem { Header = "Remove item" }; remove.Click += (_, _) => RemovePetItem(); inventoryMenu.Items.Add(remove); petItems.ContextMenu = inventoryMenu;
        Point? dragStart = null; PetSearchRow? dragItem = null;
        petResults.PreviewMouseLeftButtonDown += (_, e) => { dragItem = RowAt(e.OriginalSource)?.Item as PetSearchRow; dragStart = dragItem == null ? null : e.GetPosition(petResults); };
        petResults.PreviewMouseLeftButtonUp += (_, _) => { dragStart = null; dragItem = null; };
        petResults.PreviewMouseMove += (_, e) =>
        {
            if (dragStart is not { } point || e.LeftButton != MouseButtonState.Pressed || dragItem is not { } row) return;
            var delta = e.GetPosition(petResults) - point; if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragStart = null;var selected=SelectedPetInventory;long revision=Controller.PetRevision;
            ItemDragFeedback.Run(petResults,CatalogData(row),DragDropEffects.Copy,row.Item,row.Instance?.Rank??0,()=>SelectedTab==TrackerTab.Pet&&ReferenceEquals(selected,SelectedPetInventory)&&revision==Controller.PetRevision); e.Handled = true;
        };
        Point? inventoryStart = null; PetItemRow? inventoryDrag = null;
        petItems.PreviewMouseLeftButtonDown += (_, e) => { var cell = Ancestor<DataGridCell>(e.OriginalSource); inventoryDrag = cell?.Column == rankColumn ? null : RowAt(e.OriginalSource)?.Item as PetItemRow; inventoryStart = inventoryDrag?.Item.ItemKey.Length > 0 ? e.GetPosition(petItems) : null; };
        petItems.PreviewMouseLeftButtonUp += (_, _) => { inventoryStart = null; inventoryDrag = null; };
        petItems.PreviewMouseMove += (_, e) => {
            if (inventoryStart is not { } origin || inventoryDrag is not { } row || e.LeftButton != MouseButtonState.Pressed || SelectedPetInventory is not { } build || (e.GetPosition(petItems) - origin).Length < 8) return;
            inventoryStart = null;long revision=Controller.PetRevision;var definition=Controller.Catalog.FindKey(row.Item.ItemKey)??new ItemDefinition{Key=row.Item.ItemKey,Name=row.Item.Name};
            ItemDragFeedback.Run(petItems,new DataObject("EQL.PetInventory",new PetInventoryDrag(build.Id,row.Item.Id,PetDestination)),DragDropEffects.Move,definition,row.Rank,()=>SelectedTab==TrackerTab.Pet&&ReferenceEquals(build,SelectedPetInventory)&&revision==Controller.PetRevision);e.Handled=true;
        };
        right.AllowDrop = true; right.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent("EQL.PetInventory") ? DragDropEffects.Move : e.Data.GetDataPresent("EQL.CatalogItem") ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        right.Drop += (_, e) => {
            var target = RowAt(e.OriginalSource)?.Item as PetItemRow;
            if (e.Data.GetData("EQL.PetInventory") is PetInventoryDrag move && SelectedPetInventory is { } build) {
                if (move.Build != build.Id || move.Destination != PetDestination) { PetMessage("Choose the same destination before reordering.", true); e.Handled = true; return; }
                if (PetInventoryEditing.Reorder(build, move.Item, target?.Position ?? build.Items.Select(i => i.Position).DefaultIfEmpty(1).Max(), out string error)) { petSelectedId = move.Item; CommitPetInventory(); PetMessage("Inventory order saved · Undo is available."); } else PetMessage(error, true);
            }
            else if (e.Data.GetData("EQL.PetCatalog") is PetCatalogDrag catalogDrag && Controller.Catalog.FindKey(catalogDrag.Key) is { } definition) QuickAddPet(definition, catalogDrag.Instance, target?.Position);
            else if (e.Data.GetData("EQL.CatalogItem") is string key && Controller.Catalog.FindKey(key) is { } item) QuickAddPet(item, null, target?.Position);
            e.Handled = true;
        };
        petItems.SelectionChanged += (_, _) => { if (initializing || petItems.SelectedItem is not PetItemRow row) return; petSelectedId = row.Item.Id; SyncPetCurrentItem(row.Item); petKey = ""; RefreshPet(); ShowPetComparison(); };
        petSlot.SelectionChanged += (_, _) => { if (!initializing) { petKey = ""; RefreshPet(); } };
        petOwnership.ToolTip = "Inventory Only includes bags and bank. Confirmed pet means manually entered pet inventory. Planned items are not ownership evidence.";
        petSearch.ToolTip = "Search all cached equipment immediately, then the wiki after a short pause."; petRank.ToolTip = "Preview rank for bare catalog results. Owned copies retain recorded ranks; the Item Details slider changes only the selected preview. Edit +Rank in inventory to save a rank.";
        petElement.ToolTip = "Pet type for this saved inventory or planned build. Separate from automatic live pet identification."; petLevel.ToolTip = "Auto uses documented base pet level plus spell rank, capped at character level. Enter a number to override it; choose Auto to resume calculation.";
        RefreshBuildChoices(); DestinationChanged(); return root;
    }
    private void NewPetBuild()
    {
        if (!ConfirmPetWorkspaceExit()) return;
        var form = new EntryDialog("New planned pet build", [("Name", "Planned build")], "A plan does not change confirmed inventory. Add items from search or drag them into its list.") { Owner = this };
        if (form.ShowDialog() != true || form.Values[0].Length == 0) return;
        var build = new PetBuild { Name = form.Values[0], PetType = Controller.Profile.EffectivePetElement }; Controller.Profile.ActiveLoadout.PetBuilds.Add(build); RefreshBuildChoices(build.Id); PetEdited();
    }
    private void RefreshBuildChoices(string? id = null)
    {
        var loadout = Controller.Profile.ActiveLoadout;
        id ??= petTheory?.Working.Id ?? petBuild.SelectedValue?.ToString() ?? loadout.SelectedPetBuildId;
        if (petBuild.Items.Count == 0 && loadout.PetBuilds.Any(b => b.Id == id && b.IsTheorycraft)) id = "current";
        var choices = new[] { new PetBuildSelection("current", "Current build", PetInventoryDestination.Confirmed) }.Concat(loadout.PetBuilds.Select(b => new PetBuildSelection(b.Id, (b.IsTheorycraft ? "Theorycraft · " : "") + b.Name, PetInventoryDestination.Planned))).ToList();
        if (petTheory != null && choices.All(c => c.Id != petTheory.Working.Id)) choices.Add(new(petTheory.Working.Id, "Theorycraft · unsaved", PetInventoryDestination.Planned));
        bool was = initializing; initializing = true; petBuild.ItemsSource = choices; petBuild.SelectedValue = choices.Any(c => c.Id == id) ? id : "current"; initializing = was;
        if (petTheory == null && !loadout.PetBuilds.Any(b => b.Id == id && b.IsTheorycraft)) loadout.SelectedPetBuildId = petBuild.SelectedValue?.ToString() ?? "current";
        LoadPetContext(); petKey = "";
        if (!initializing) petSelectionChanged?.Invoke();
    }
    private void RefreshPet()
    {
        if (petRankEditing) return;
        if(petRatingColumn!=null) { petRatingColumn.Header="Rating"; TableLayout.SetDefaultSample(petResults, petRatingColumn, SelectedPetPreset=="Balanced"?"+9999 / +9999":"+9999.9"); }
        if (SelectedPetInventory is { } automaticBuild) RefreshAutomaticPetLevel(automaticBuild);
        long sourceRevision = Controller.EquipmentRevision, sourcePetRevision = Controller.PetRevision, sourceEditRevision = petEditRevision;
        if (petBuild.Items.Count == 0 || PetDestination == PetInventoryDestination.Planned && CurrentBuild == null) RefreshBuildChoices(); var build = SelectedPetInventory; if (build == null) return; PetInventoryEditing.EnsureIdentities(build);
        bool hadInventorySelection = petItems.SelectedItem is PetItemRow;
        int selectedPosition = (petItems.SelectedItem as PetItemRow)?.Position ?? 1;
        string expansionKey = ItemExpansionFilter.Key(Controller.Profile.PetExpansions);
        string key = $"{RankedItemStats.Version}|{PetItemRules.Version}|{Controller.EquipmentRevision}|{Controller.PetRevision}|{petEditRevision}|{petTheory?.Revision}|{build.IsTheorycraft}|{petSearch.Text}|{petOwnership.SelectedIndex}|{PetDestination}|{build.Id}|{selectedPosition}|{petSlot.SelectedItem}|{petRank.Text}|{petPreferredPosition}|{expansionKey}"; if (key == petKey) return; petKey = key;
        petViewCancel?.Cancel(); petViewCancel?.Dispose(); petViewCancel = new(); var token = petViewCancel.Token;
        var owner = EquipmentPositions.Copy(Controller.Profile.ActiveLoadout); var plan = EquipmentPositions.Copy(build); var confirmed = EquipmentPositions.Copy(PetInventoryEditing.Confirmed(owner, Controller.Catalog));
        var context = GuidedPetBuilds.Context(owner, plan); var reference = GuidedPetBuilds.Positions(confirmed, owner, Controller.Catalog); var inferred = PetEquipmentInference.Build(plan, owner, Controller.Catalog); var positions = inferred.Positions;
        string preferredPosition = petPreferredPosition;
        bool wasInitializing = initializing; initializing = true;
        petReplacement.ItemsSource = new[] { new PetReplacementChoice("*", "Best compatible") }.Concat(plan.Items.OrderBy(i => i.Position).Select(i => new PetReplacementChoice(inferred.Selections.FirstOrDefault(s => s.EntryId == i.Id)?.PositionId ?? "inventory:" + i.Id, $"{i.Position} · {i.Name} +{i.Rank}"))).ToList(); petReplacement.SelectedValue = preferredPosition; initializing = wasInitializing;
        if (owner.PetEquipmentConfirmedAt == null && confirmed.Items.Count == 0) foreach (var position in reference) position.Knowledge = EquipmentKnowledge.Unknown;
        var catalog = (Controller.EquipmentView?.Catalog ?? new Catalog { Items = Controller.Catalog.Items.ToList() }); var inventory = (Controller.EquipmentView?.Inventory ?? Controller.Inventory); var weights = PetRoleRatings.Weights(Controller.Profile,SelectedPetPreset);
        string query = petSearch.Text.Trim(), slotFilter = petSlot.SelectedItem?.ToString() ?? "All slots"; int ownership = petOwnership.SelectedIndex; int? rankOverride = int.TryParse(petRank.Text, out int tier) ? Math.Clamp(tier,0,10) : null; var pending = Controller.PendingGear(query).ToList();
        var expansions = Controller.Profile.PetExpansions.ToArray();
        string selectedGoal=SelectedPetPreset;var defenseWeights=PetRoleRatings.Weights(Controller.Profile,"Tank");
        BuildChangesPanel.Snapshot? changes = null;
        List<PetSearchRow> BuildRows()
        {
            var rows = new List<PetSearchRow>();
            var evaluator = new LoadoutEvaluationSession(catalog, context, true, weights, positions, reference, plan.IsTheorycraft, petGoal: selectedGoal, petDefenseWeights: defenseWeights, petInventory: plan,effectivePetEquipment:inferred);
            foreach (var item in catalog.Items.Where(i => ItemExpansionFilter.Includes(expansions, ItemPresentation.Expansion(i)) && (i.Slots.Count > 0 || query.Length >= 3) && (i.Name.Contains(query,StringComparison.OrdinalIgnoreCase) || i.Aliases.Any(a=>a.Contains(query,StringComparison.OrdinalIgnoreCase))) && (slotFilter == "All slots" || i.Slots.Any(s=>EquipmentPositions.Normalize(s)==slotFilter))))
            {
                if (!PetItemRules.ShowCandidate(item, context, catalog, plan.IsTheorycraft)) continue;
                token.ThrowIfCancellationRequested(); var stock = inventory.Evidence(item).Where(e=>!e.IsAugment && !e.IsCollection && e.Count>0).ToList(); bool petOwn = PetInventoryEditing.Has(confirmed,item), planned = owner.PetBuilds.Any(b=>PetInventoryEditing.Has(b,item));
                if (ownership==1 && stock.Count==0 || ownership==2 && !petOwn || ownership==3 && !planned) continue;
                string owned = string.Join(" · ",new[] { stock.Count>0 ? "Inventory Only" : "",petOwn ? "Pet · manual" : "",planned ? "Planned" : "" }.Where(x=>x.Length>0));
                foreach (var instance in PhysicalItemChoices.Rows(PetEquipmentInference.CandidateCopies(stock,confirmed,item,ownership==1), ownership == 1))
                {
                    int rank=CandidateRanks.Resolve(instance, rankOverride);
                    rows.Add(new(item,owned) { Rank=rank, Instance=instance,Comparison=PetEquipmentInference.Best(item, rank, instance, evaluator, positions, preferredPosition == "*" ? null : preferredPosition) });
                }
            }
            foreach (var title in pending) if (ownership==0 && ItemExpansionFilter.Includes(expansions, "Unknown") && !rows.Any(r=>r.Item.Name.Equals(title,StringComparison.OrdinalIgnoreCase))) rows.Add(new(new() {Name=title,Key=Identity.Key(title),WikiUrl="https://eqlwiki.com/"+Uri.EscapeDataString(title.Replace(' ','_'))},"Metadata pending"));
            changes = BuildChangesPanel.Prepare(reference, positions, catalog, new LoadoutEvaluationSession(catalog, context, true, weights, reference, reference, plan.IsTheorycraft, petGoal: selectedGoal, petDefenseWeights: defenseWeights));
            foreach(var row in rows.Where(r=>r.Comparison?.Evaluation?.Status != "Conditional" && r.DefenseDelta.HasValue && r.UnbuffedDelta.HasValue)) row.StrongTradeoff=!rows.Any(other=>other.Comparison?.Evaluation?.Status != "Conditional"&&other.DefenseDelta.HasValue&&other.UnbuffedDelta.HasValue&&PetRoleRatings.Dominates(other.UnbuffedDelta.Value,other.DefenseDelta.Value,row.UnbuffedDelta!.Value,row.DefenseDelta!.Value));
            return rows.OrderBy(r=>r.Comparison?.Evaluation?.Status == "Conditional" ? 1 : r.UnbuffedDelta>0 ? 0 : 2).ThenBy(r=>r.Name).ToList();
        }
        void Apply(List<PetSearchRow> rows)
        {
            if (sourceRevision != Controller.EquipmentRevision || sourcePetRevision != Controller.PetRevision || sourceEditRevision != petEditRevision || token.IsCancellationRequested || petKey!=key || expansionKey != ItemExpansionFilter.Key(Controller.Profile.PetExpansions) || SelectedTab!=TrackerTab.Pet || !Body.IsVisible) return;
            petRowsKey = key;
            petContextLoading = true; petDualWield.IsChecked = plan.DualWield; petContextLoading = false;
            petSlotWarning.Text = string.Join("\n", PetSlotPlanning.Warnings(plan, catalog, PetEquipmentPlanner.Capacity(owner.Classes))); petSlotWarning.Visibility = petSlotWarning.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            string? selected=(petResults.SelectedItem as PetSearchRow)?.ToString(); UI.SetRows(petResults,rows); ApplyPetSort(); petResults.SelectedItem=rows.FirstOrDefault(r=>r.ToString()==selected);
            var excluded = query.Length < 3 ? [] : catalog.Items.Where(i=>i.Name.Contains(query,StringComparison.OrdinalIgnoreCase) && !rows.Any(r=>r.Item.Key==i.Key)).Select(i=>i.Name+": "+(!ItemExpansionFilter.Includes(expansions,ItemPresentation.Expansion(i)) ? "excluded by Expansion" : slotFilter!="All slots" && !i.Slots.Any(s=>EquipmentPositions.Normalize(s)==slotFilter) ? "excluded by Slot" : !PetItemRules.ShowCandidate(i,context,catalog,plan.IsTheorycraft) ? GearScorer.Eligibility(i,context,true,null,catalog) : ownership!=0 ? "excluded by Ownership" : "metadata unverified")).Take(4).ToArray();
            petCandidateFilterNote.ToolTip=string.Join("\n",excluded);
            petCandidateFilterNote.Text = excluded.Length>0 ? excluded.Length+" matching items excluded · hover for reasons" : expansions.Length == 0 ? "Select an item expansion to show candidates." : rows.Count == 0 ? "No items match the selected filters." : "";
            petCandidateFilterNote.Visibility = petCandidateFilterNote.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            var all=Enumerable.Range(1,PetEquipmentPlanner.Capacity(owner.Classes)).Select(i=>plan.Items.FirstOrDefault(x=>x.Position==i) ?? new PetInventoryItem {Position=i,Slot="Storage"}).Concat(plan.Items.Where(i=>i.Position>PetEquipmentPlanner.Capacity(owner.Classes))).OrderBy(i=>i.Position);
            var items=all.Select(i=>new PetItemRow(i,i.ItemKey.Length==0 ? "Empty · choose an item" : (i.EquipmentVerified ? "Verified equipment · " : PetDestination==PetInventoryDestination.Confirmed ? "Manual inventory · intended slot · " : "Planned · ")+PetEquipmentPlanner.Validate(plan,i,owner,catalog) + "\n" + (inferred.Selections.FirstOrDefault(s => s.EntryId == i.Id)?.Evidence ?? "Not selected into effective equipment") + "\nDrag ⠿ or the name to reorder; +Rank edits inline.\n" + string.Join("\n", i.Exaltations.Select(e => e.SourceName + ": " + e.Effect + " · " + e.State)), catalog.FindKey(i.ItemKey) is { } definition ? PetProcLabel(plan, i, definition, owner, catalog, inferred) : "", i.ItemKey.Length == 0 ? string.Join("; ", plan.SlotConfigurations.FirstOrDefault(s => s.Position == i.Position)?.Effects.Select(e => e.Effect + " · inactive") ?? []) : catalog.FindKey(i.ItemKey) is { } typed ? PetItemRules.Type(typed) : "Unknown")).ToList();
            bool was=initializing; initializing=true; UI.SetRows(petItems,items); petItems.SelectedItem=items.FirstOrDefault(i=>i.Item.Id.Length > 0 && i.Item.Id == petSelectedId) ?? (hadInventorySelection ? items.FirstOrDefault(i=>i.Position==selectedPosition) : null); initializing=was;
            var warnings=GuidedPetBuilds.Warnings(plan,owner,catalog).Concat(GuidedPetBuilds.Availability(plan,confirmed,inventory,catalog)).ToList(); petNote.Text=$"{plan.Items.Count(i => i.Position <= PetEquipmentPlanner.Capacity(owner.Classes))}/{PetEquipmentPlanner.Capacity(owner.Classes)} inventory positions · {warnings.Count} equipment notes"; petNote.ToolTip=string.Join('\n',warnings);
            var draft = changes!.Total;
            petDraftSummary.Text = PetDestination == PetInventoryDestination.Planned ? "Build vs confirmed · "+PetRoleRatings.Text(draft)+" preference points" : "";
            petTotals.Text = string.Join(" · ", new[] { "AC", "HP", "STR", "DEX", "ATK", "Haste" }.Select(s => s + " " + inferred.Totals.Stats.GetValueOrDefault(s).ToString("0.##") + (s == "Haste" ? "%" : "")));
            petWeapons.Text = string.Join("\n", inferred.Totals.Weapons.Concat(inferred.Totals.Procs)); petTotals.ToolTip = string.Join("\n", inferred.Totals.Notes);
            if (ActualHeight < 540) petChanges.IsExpanded = false;
            petChanges.Show(changes!, PetDestination == PetInventoryDestination.Planned, UndoPetBuild, id => RemovePetBuildChange(id));
            petListTitle.Text=petTheory != null ? "Theorycraft inventory · " + petTheory.Working.Name : PetDestination==PetInventoryDestination.Confirmed ? "Current build · manual inventory" : "Planned build · "+plan.Name; ShowPetComparison();
        }
        if (Controller.Demo) Apply(BuildRows()); else _=Task.Run(BuildRows,token).ContinueWith(task=>Dispatcher.InvokeAsync(()=>{if(task.IsCompletedSuccessfully) Apply(task.Result);else if(task.IsFaulted && petKey==key)petNote.Text="Pet candidates unavailable; change filters to retry.";}),TaskScheduler.Default);
    }
    private async void SchedulePetSearch()
    {
        petSearchCancel?.Cancel(); petSearchCancel = new(); var token = petSearchCancel.Token; string query = petSearch.Text.Trim(); if (query.Length < 3 || Controller.Demo) return;
        try { await Task.Delay(500, token); await Controller.SearchGearAsync(query, token); if (!token.IsCancellationRequested) { petKey = ""; RefreshPet(); } } catch (OperationCanceledException) { }
    }
    private static string PetProcLabel(PetBuild build, PetInventoryItem item, ItemDefinition definition, LoadoutProfile owner, Catalog catalog, EffectivePetEquipment equipment)
    {
        string intended = equipment.Positions.FirstOrDefault(p => p.Id == equipment.Selections.FirstOrDefault(s => s.EntryId == item.Id)?.PositionId)?.Slot ?? item.Slot;
        var active = PetSlotPlanning.ActiveEffects(build, item, definition, intended, owner, catalog).Select(e => e.EffectId).ToHashSet();
        var intrinsic = ItemEffects.Active(definition, item.TransferredOutEffects).Where(e => ExaltationPlanner.EffectType(e) == "Proc").Select(e => e + " · intrinsic");
        var configured = item.Exaltations.Where(e => ExaltationPlanner.EffectType(e.Effect) == "Proc").Select(e => e.Effect + (!active.Contains(e.EffectId) ? " · inactive" : e.State == "Socketed" ? " · recorded" : " · planned"));
        return string.Join("; ", intrinsic.Concat(configured));
    }
    private void AddPetItem() { if (petResults.SelectedItem is PetSearchRow row) QuickAddPet(row.Item, PetInspectedInstance(row), (petItems.SelectedItem as PetItemRow)?.Position); else PetMessage("Select a catalog item to add.", true); }
    private void EditPetItem(PetInventoryItem? existing, ItemDefinition? item = null, int? rankOverride = null, int? requestedPosition = null)
    {
        if (SelectedPetInventory is not { } build) return;
        item ??= existing == null ? null : Controller.Catalog.FindKey(existing.ItemKey) ?? Controller.Catalog.Find(existing.Name);
        var selected=(petItems.SelectedItem as PetItemRow)?.Item;
        int? position = requestedPosition ?? existing?.Position ?? (selected==null?null:selected.ItemKey.Length==0 || item?.Slots.Any(s=>EquipmentPositions.Normalize(s)==EquipmentPositions.Normalize(selected.Slot)) == true?selected.Position:PetInventoryEditing.NextPosition(build));
        string character = Controller.Profile.Character + "|" + Controller.Profile.Server;
        var editor = new PetItemDialog(Controller, item, build, PetDestination, existing, rankOverride, position, () => Controller.Profile.Character + "|" + Controller.Profile.Server == character && SelectedPetInventory?.Id == build.Id ? SelectedPetInventory : null) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result == null) return;
        if (editor.Result.Position > PetEquipmentPlanner.Capacity(Controller.Profile.ActiveLoadout.Classes)) { PetMessage("Position exceeds current Pet inventory capacity.", true); return; }
        PetBuildEditing.Remember(build, "Edit slot");
        if (editor.Result.ItemKey.Length == 0) { build.Items.RemoveAll(i => i.Position == existing?.Position); PetSlotPlanning.SetEffects(build, editor.Result.Position, editor.Result.Exaltations, ""); if (PetDestination == PetInventoryDestination.Confirmed) PetInventoryEditing.Confirm(Controller.Profile.ActiveLoadout, build, DateTime.Now); PetEdited(); return; }
        if (existing != null && existing.Position != editor.Result.Position) PetSlotPlanning.SetEffects(build, existing.Position, editor.Result.Exaltations, existing.Id);
        if (!PetInventoryEditing.Put(build, editor.Result, existing?.Position, editor.ReplaceOccupied, out string error)) { petNote.Text = error; return; }
        if (PetDestination == PetInventoryDestination.Confirmed) PetInventoryEditing.Confirm(Controller.Profile.ActiveLoadout, build, DateTime.Now);
        PetEdited();
    }
    private void RemovePetItem()
    {
        if (SelectedPetInventory is not { } build || petItems.SelectedItem is not PetItemRow row) return;
        PetBuildEditing.Remember(build, "Remove item");
        build.Items.RemoveAll(i=>i.Position==row.Position); PetSlotPlanning.CompactArmorAfterRemoval(build, Controller.Catalog, PetEquipmentPlanner.Capacity(Controller.Profile.ActiveLoadout.Classes)); if (PetDestination == PetInventoryDestination.Confirmed) PetInventoryEditing.Confirm(Controller.Profile.ActiveLoadout, build, DateTime.Now); PetEdited();
    }
    private void ConfirmBuild()
    {
        if (CurrentBuild is not { } build) { PetMessage("Select a named plan first.", true); return; }
        string? issue = PetBuildEditing.ContextError(build.PetLevel, build.SummonRank, true);
        if (issue != null) { PetMessage(issue, true); return; }
        if(build.IsTheorycraft||petTheory!=null)return;
        string name="";bool refreshed=false;
        while(true){var owner=Controller.Profile.ActiveLoadout;string fingerprint=BuildActivation.PetFingerprint(PetInventoryEditing.Confirmed(owner,Controller.Catalog));
            var dialog=new CurrentBuildBackupDialog(build.Name,name,refreshed){Owner=this};if(dialog.ShowDialog()!=true)return;name=dialog.BackupName;
            bool applied;string error;lock(Controller.Sync)applied=BuildActivation.Pet(owner,build,Controller.Catalog,fingerprint,dialog.Choice,name,out error);
            if(!applied&&error.StartsWith("Current changed")){refreshed=true;continue;}if(!applied){PetMessage(error,true);return;}
            RefreshBuildChoices("current");PetEdited();PetMessage("Current build updated · Undo restores the previous build; saved backups remain available.");return;}
    }
}
