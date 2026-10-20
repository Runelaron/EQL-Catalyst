using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

public enum TrackerTab { Queue = 0, Items = 1, Quests = 2, Gear = 3, Pet = 4, Exaltations = 5, Progress = 6 }
public static class TrackerNavigation
{ public static IReadOnlyList<TrackerTab> Order { get; } = new[] { TrackerTab.Queue, TrackerTab.Items, TrackerTab.Quests, TrackerTab.Progress, TrackerTab.Gear, TrackerTab.Exaltations, TrackerTab.Pet }; }
public sealed record QuestLibraryRow(QuestDefinition Quest, string Tracking, string State, string Suggestion, string Explanation)
{ public GearComparison? Comparison { get; init; } public string Preset { get; init; } = ""; public bool IsSuggested => Comparison?.Evaluation is { } e ? e.Unbuffed.Delta > 0 || e.Buffed.Delta > 0 : Comparison?.CanCompare == true && Comparison.Delta > 0; public string Name => (IsSuggested ? "✦ " : "") + Quest.Name; public string Class => string.Join("/", QuestTracking.Classes(Quest)); public string Expansion => Quest.Expansion; public string Set => Quest.SetName; }
public sealed partial class QuestWindow
{
    private readonly Dictionary<TrackerTab, TabItem> tabById = [];
    private TrackerTab SelectedTab => tabs.SelectedItem is TabItem { Tag: TrackerTab id } ? id : TrackerTab.Queue;
    private StackPanel questSearchArea = null!, itemsEmptyPanel = null!;
    private bool fittingColumns;
    private readonly TextBlock queueNoteState = UI.Text("", 12, "Mint");
    private readonly TextBox librarySearch = UI.Input("", 220);
    private readonly ComboBox libraryClasses = UI.Select(["My classes", "Pet compatible", "All classes"], "My classes", 140);
    private readonly ComboBox libraryExpansion = UI.Select(["All expansions", "Classic", "Kunark", "Velious"], "All expansions", 130);
    private readonly ComboBox libraryState = UI.Select(["All quests", "Tracked", "Untracked", "Owned / completed", "Suggested"], "All quests", 150);
    private readonly DataGrid libraryTable = UI.Table(("Quest / reward", "Name", 0, null), ("Cls", "Class", 85, null), ("Expansion", "Expansion", 85, null), ("Tracking", "Tracking", 85, null), ("Recommendation", "Suggestion", 150, null));
    private readonly StackPanel libraryDetail = new();
    private readonly TextBox questQuantity = UI.Input("1", 42);
    private string libraryKey = "", petKey = "", exaltKey = "";
    private readonly TextBox petSearch = UI.Input("", 220), petPosition = UI.Input("1", 42);
    private readonly ComboBox petRank = EditablePetNumber(Enumerable.Range(0, 11).Select(i => i.ToString()), "0", 60), petLevel = EditablePetNumber(new[] { "Auto", "Unknown" }.Concat(Enumerable.Range(1, 50).Select(i => i.ToString())), "Unknown", 80);
    private readonly ComboBox petBuild = UI.Select([], "", 170), petElement = UI.Select(["Unknown", "Earth", "Water", "Air", "Fire", "Undead", "Animation", "Warder", "Spirit"], "Unknown", 95);
    private readonly ComboBox petSlot = UI.Select(new[] { "All slots" }.Concat(EquipmentPositions.Names.Where(n => n != "Any Slot")), "All slots", 110);
    private readonly ComboBox petOwnership = UI.Select(["All items", "Inventory Only", "Confirmed pet", "Planned items"], "All items", 130);
    private readonly DataGrid petResults = UI.Table(("Item", "Name", 0, null), ("Slots", "SlotText", 150, null), ("Ownership", "Ownership", 130, null));
    private readonly DataGrid petItems = UI.Table(("#", "Position", 32, null), ("Item", "Name", 0, null), ("Slot", "Slot", 85, null), ("+rank", "Rank", 50, null), ("Status", "State", 230, null));
    private readonly TextBlock petNote = UI.Text("", 11, "Mint"), petCompare = UI.Text("", 12);
    private readonly TextBox exaltSearch = UI.Input("", 220);
    private readonly ComboBox exaltType = UI.Select(["All effects", "Focus", "Click", "Worn", "Proc", "Ornamentation", "Unknown"], "All effects", 100), exaltOwnership = UI.Select(["All", "Owned effects", "Have source item", "Missing"], "All", 135), exaltExpansion = UI.Select(["All expansions", "Classic", "Kunark", "Velious", "Unknown"], "All expansions", 130), exaltTarget = UI.Select(["Any", "Self", "Pet"], "Any", 75);
    private readonly DataGrid exaltTable = UI.Table(("Source / effect", "Name", 0, null), ("Type", "Type", 65, null), ("Unlock Rank", "UnlockRank", 85, null), ("Ownership", "State", 190, null));
    private readonly StackPanel exaltDetail = new();
    private readonly ComboBox exaltCompatibility = UI.Select(["All", "Current Loadout", "Incompatible"], "All", 180);
    private readonly ComboBox exaltPurpose = UI.Select(new[] { "All" }.Concat(Enum.GetNames<EffectPurpose>()), "All", 100), exaltContent = UI.Select(["Current", "Future", "All"], "Current", 95);
    private readonly ComboBox exaltDestination = UI.Select([], "", 230);
    private List<ExaltationOption> exaltOptions = [];
    private string exaltCatalogKey = "";
    private string exaltRequestedKey = "";
    private CancellationTokenSource? petSearchCancel;

    private void AddTab(TrackerTab id, string label, UIElement content)
    { var tab = new TabItem { Header = label.Length > 1 ? char.ToUpper(label[0]) + label[1..].ToLowerInvariant() : label, Content = content, Tag = id, ToolTip = id switch { TrackerTab.Queue => "Your next goals in saved order", TrackerTab.Items => "Items to collect and keep", TrackerTab.Quests => "Discover and track quest rewards", TrackerTab.Progress => "Nested checklist and acquisition sources", TrackerTab.Gear => "Find replacements for a selected equipment position", TrackerTab.Pet => "Confirmed inventory and separate planned builds", _ => "Discover effects and plan compatible assignments" } }; tabById[id] = tab; tabs.Items.Add(tab); }
    internal void ShowView(TrackerTab id, string? item = null)
    {
        bool wasInitializing = initializing; initializing = true;
        if (item != null) { if (id == TrackerTab.Gear) { gearSearch.Text = item; GearSelectedPosition = "*"; } if (id == TrackerTab.Progress) questSelection.SelectedItem = item; if (id == TrackerTab.Quests) librarySearch.Text = item; if (id == TrackerTab.Pet) petSearch.Text = item; if (id == TrackerTab.Exaltations) exaltSearch.Text = item; }
        tabs.SelectedItem = tabById[id]; initializing = wasInitializing;
        if (id != TrackerTab.Gear) { gearViewCancel?.Cancel(); gearFingerprint = null; }
        if (id != TrackerTab.Pet) { petViewCancel?.Cancel(); petKey = ""; }
        if (id != TrackerTab.Quests) { libraryViewCancel?.Cancel(); libraryKey = ""; }
        if (id != TrackerTab.Items) { questFilterKey = ""; fingerprint = null; }
        Refresh(); if (item != null && id == TrackerTab.Gear && !initializing) ScheduleGearSearch(); if (item != null && id == TrackerTab.Pet && !initializing) SchedulePetSearch(); UpdateLayout();
    }
    private void ConfigurePlanningTabs()
    {
        TableLayout.SetKey(libraryTable, "tracker.quests"); TableLayout.SetKey(exaltTable, "tracker.exaltations"); TableLayout.SetKey(gearTable, "tracker.gear.candidates");
        AddTab(TrackerTab.Quests, "QUESTS", BuildLibrary()); AddTab(TrackerTab.Pet, "PET", BuildPet()); AddTab(TrackerTab.Exaltations, "EXALTATIONS", BuildExaltations());
        tabs.Items.Clear(); foreach (var id in TrackerNavigation.Order) tabs.Items.Add(tabById[id]); tabs.SelectedItem = tabById[TrackerTab.Queue];
        ConfigureFocusedQueueTemplate();
    }
    private void RefreshPlanningTab()
    { if (SelectedTab == TrackerTab.Quests) RefreshLibrary(); if (SelectedTab == TrackerTab.Pet) RefreshPet(); if (SelectedTab == TrackerTab.Exaltations) RefreshExaltations(); }
    private void Changed()
    { Controller.Save(); fingerprint = gearFingerprint = stepFingerprint = null; libraryKey = petKey = exaltKey = queueSignature = ""; Refresh(); }
    private void PinQuest(QuestDefinition quest)
    {
        int quantity = int.TryParse(questQuantity.Text, out int q) ? Math.Clamp(q, 1, 99) : 1;
        QuestTracking.Track(Controller.Profile, quest, true, quantity);
        AddPin(new() { Kind = "Quest", QuestId = quest.Id, ItemKey = quest.RewardKey, Name = quest.Name });
    }
    private void PinItem(QuestDefinition? quest, string key)
    {
        if (quest == null) return; QuestTracking.Track(Controller.Profile, quest, true);
        AddPin(new() { QuestId = quest.Id, ItemKey = key, Name = Controller.Catalog.FindKey(key)?.Name ?? key });
    }
    private void AddPin(QueuedGoal goal)
    { if (!Controller.Profile.GoalQueue.Any(g => g.Id == goal.Id)) Controller.Profile.GoalQueue.Add(goal); Changed(); }
    private void PinWishlist(WishlistGoal wish) => AddPin(new() { Kind = "Gear", QuestId = wish.Id, ItemKey = Controller.Catalog.Find(wish.Name)?.Key ?? Identity.Key(wish.Name), Name = wish.Name });
    private void PinGear(ItemDefinition item, string? target = null)
    {
        target ??= gearTarget.SelectedItem?.ToString() ?? "Pet";
        var wish = Controller.Profile.Wishlist.FirstOrDefault(w => Identity.Key(w.Name) == Identity.Key(item.Name) && w.Loadout == target);
        if (wish == null) { wish = new() { Name = item.Name, Loadout = target, Zone = item.Sources.FirstOrDefault()?.Zone ?? "", Source = item.Sources.FirstOrDefault()?.Mob ?? "" }; Controller.Profile.Wishlist.Add(wish); }
        AddPin(new() { Kind = "Gear", QuestId = wish.Id, ItemKey = item.Key, Name = item.Name });
    }
    private UIElement BuildLibrary()
    {
        foreach (var column in libraryTable.Columns.OfType<DataGridTextColumn>().Where(c => c.SortMemberPath is "Name" or "Suggestion"))
        { var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(ToolTipProperty, new Binding("Explanation"))); style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis)); column.ElementStyle = style; }
        var root = new DockPanel(); var controls = new WrapPanel();
        foreach (var c in new UIElement[] { FocusedLayout.Search(librarySearch, "Find a quest or reward…"), libraryClasses, libraryState,
            UI.Button("Track / untrack", () => { if (libraryTable.SelectedItem is QuestLibraryRow r) { QuestTracking.Track(Controller.Profile, r.Quest, !QuestTracking.Selected(r.Quest, Controller.Profile, Controller.Inventory, Controller.Catalog), int.TryParse(questQuantity.Text, out int n) ? n : 1); Changed(); } }),
            }) controls.Children.Add(c);
        var heading = UI.Stack(controls, FocusedLayout.Disclosure("More filters", UI.Row(libraryExpansion), "Filter quest expansion independently of item-source availability."));
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        root.Children.Add(UI.Split(TrackerLeftPane(libraryTable), FocusedLayout.FloatingInspector("Quest", libraryDetail), 300, Controller.Profile, "library", 280, 180));
        foreach (var c in new[] { libraryClasses, libraryExpansion, libraryState }) c.SelectionChanged += (_, _) => { libraryKey = ""; if (!initializing) RefreshLibrary(); };
        librarySearch.TextChanged += (_, _) => { libraryKey = ""; if (!initializing) RefreshLibrary(); };
        libraryTable.SelectionChanged += (_, _) => ShowLibraryDetail(); return root;
    }
    private QuestLibraryRow LibraryRow(QuestDefinition q, CharacterProfile p, Catalog catalog, InventorySnapshot inventory, bool pet)
    {
        var item = catalog.FindKey(q.RewardKey); string suggestion = "Baseline needed", explanation = "Confirm equipped items before recommending an upgrade."; GearComparison? selectedComparison = null; string selectedPreset = "";
        if (item != null)
        {
            var loadout = p.ActiveLoadout;
            selectedPreset = pet ? loadout.PetPreset : loadout.Preset; var weights = p.Weights.GetValueOrDefault(selectedPreset, GearScorer.Defaults["Tank"]);
            var comparisons = GearRecommendations.For(item, 0, catalog, loadout, pet, weights);
            var best = comparisons.FirstOrDefault(c => c.Evaluation is { } e && (e.Unbuffed.Delta > 0 || e.Buffed.Delta > 0)) ?? comparisons.FirstOrDefault(c=>c.CanCompare);
            selectedComparison = best ?? comparisons.FirstOrDefault();
            if (best != null) { suggestion = pet ? $"{selectedPreset} · {best.Evaluation?.Unbuffed.Delta?.ToString("+0.0;-0.0;0") ?? "—"} pts" : $"{selectedPreset} · U {best.Evaluation?.Unbuffed.Delta?.ToString("+0.0;-0.0;0") ?? "—"} / B {best.Evaluation?.Buffed.Delta?.ToString("+0.0;-0.0;0") ?? "—"} pts"; explanation = best.Slot + " · " + best.Replaces + "\n" + best.Contributions + "\n" + best.Effects; }
            else if (comparisons.FirstOrDefault() is { Eligibility: not "Eligible" } restricted) { suggestion = "Future / restricted"; explanation = restricted.Eligibility; }
        }
        bool done = QuestTracking.Fulfilled(q, p, inventory, catalog);
        return new(q, QuestTracking.Selected(q, p, inventory, catalog) ? "✓ Tracked" : "Not tracked", done ? "Owned / completed" : "Incomplete", suggestion, explanation) { Comparison = selectedComparison, Preset = selectedPreset };
    }
    private CancellationTokenSource? libraryViewCancel;
    private void RefreshLibrary()
    {
        string key = $"{Controller.QuestRevision}|{Controller.EquipmentRevision}|{librarySearch.Text}|{libraryClasses.SelectedItem}|{libraryExpansion.SelectedItem}|{libraryState.SelectedItem}"; if (key == libraryKey) return; libraryKey = key;
        libraryViewCancel?.Cancel();libraryViewCancel?.Dispose();libraryViewCancel=new();var token=libraryViewCancel.Token;
        var planningInput = Controller.PlanningProfile ?? EquipmentPositions.Copy(Controller.Profile); var equipmentInput = Controller.EquipmentView?.Profile; var inventory = Controller.EquipmentView?.Inventory ?? Controller.Inventory; var catalog = Controller.EquipmentView?.Catalog ?? new Catalog { Items = Controller.Catalog.Items.ToList(), Quests = Controller.Catalog.Quests.ToList() };
        bool pet=libraryClasses.SelectedItem?.ToString()=="Pet compatible",allClasses=libraryClasses.SelectedItem?.ToString()=="All classes"; string expansion=libraryExpansion.SelectedItem?.ToString()??"All expansions",state=libraryState.SelectedItem?.ToString()??"All quests",query=librarySearch.Text;
        List<QuestLibraryRow> Build()
        {
            var p = EquipmentPositions.Copy(planningInput);
            if (equipmentInput != null) { p.Loadouts = equipmentInput.Loadouts; p.Weights = equipmentInput.Weights; p.SelectedLoadout = equipmentInput.SelectedLoadout; }
            var eligible=pet?GearScorer.EligibleClasses(p.ActiveLoadout,true):p.EnabledClasses;
            var rows=new List<QuestLibraryRow>();foreach(var q in catalog.Quests.Where(q=>(allClasses || QuestTracking.Relevant(q,eligible)) && (expansion=="All expansions" || q.Expansion==expansion) && (q.Name+q.SetName+q.Class).Contains(query,StringComparison.OrdinalIgnoreCase))) {token.ThrowIfCancellationRequested();rows.Add(LibraryRow(q,p,catalog,inventory,pet));}
            return rows.Where(r=>state switch {"Tracked"=>r.Tracking.StartsWith("✓"),"Untracked"=>!r.Tracking.StartsWith("✓"),"Owned / completed"=>r.State=="Owned / completed","Suggested"=>r.IsSuggested,_=>true}).OrderBy(r=>r.Quest.Kind!="Epic").ThenBy(r=>r.Set).ThenBy(r=>r.Name).ToList();
        }
        void Apply(List<QuestLibraryRow> rows)
        {
            if(token.IsCancellationRequested || libraryKey!=key || SelectedTab!=TrackerTab.Quests || !Body.IsVisible)return;
            string? selected=(libraryTable.SelectedItem as QuestLibraryRow)?.Quest.Id;UI.SetRows(libraryTable,rows);libraryTable.SelectedItem=rows.FirstOrDefault(r=>r.Quest.Id==selected)??rows.FirstOrDefault();ShowLibraryDetail();
        }
        if(Controller.Demo)Apply(Build());else _=Task.Run(Build,token).ContinueWith(task=>Dispatcher.InvokeAsync(()=>{if(task.IsCompletedSuccessfully)Apply(task.Result);}),TaskScheduler.Default);
    }
    private void ShowLibraryDetail()
    {
        libraryDetail.Children.Clear(); if (libraryTable.SelectedItem is not QuestLibraryRow r) { libraryDetail.Children.Add(UI.Text("No quests match these filters.")); return; }
        questQuantity.Text = Controller.Profile.QuestQuantities.GetValueOrDefault(r.Quest.Id, 1).ToString();
        if (questQuantity.Parent is Panel oldQuantityRow) oldQuantityRow.Children.Remove(questQuantity);
        libraryDetail.Children.Add(UI.Card(UI.Stack(UI.Label(r.Quest.SetName), UI.Text(r.Quest.Name, 16, "Accent", true), UI.Text(r.State + " · " + r.Tracking, 12, "Mint"))));
        var action = UI.CommitButton("Add to queue", () => PinQuest(r.Quest)); action.ToolTip = "Track this reward and pin its remaining requirements. Repeated additions do not duplicate it.";
        libraryDetail.Children.Add(UI.Row(action, UI.Label("Qty"), questQuantity));
        libraryDetail.Children.Add(UI.Button("View progress", () => ShowView(TrackerTab.Progress, r.Quest.Name)));
        var comparison = new ComparisonPanel(); comparison.Show(r.Comparison, r.Preset); libraryDetail.Children.Add(FocusedLayout.Disclosure("Reward comparison · " + r.Suggestion, comparison, "Compare both stat contexts; effects and unscored losses are preserved."));
        libraryDetail.Children.Add(UI.Text(r.Quest.RecommendedLevel > 0 ? $"Quest recommended level {r.Quest.RecommendedLevel}. Equip level and +rank are separate." : "Quest recommended level not documented.", 11, "Muted"));
        if (Controller.Catalog.FindKey(r.Quest.RewardKey) is { } reward) AddFocusedSources(libraryDetail, reward.Sources);
        var steps = new StackPanel(); steps.Children.Add(UI.Text(r.Quest.Coverage, 11, "Muted"));
        foreach (var step in r.Quest.Steps) steps.Children.Add(UI.Card(UI.Stack(UI.Text(step.Name, 12, "Foreground", true), UI.Text(step.Instructions, 11), UI.Text(string.Join("\n", step.Inputs.Select(i => $"{i.Count} × {Controller.Catalog.FindKey(i.ItemKey)?.Name ?? i.ItemKey}")), 11, "Mint"))));
        libraryDetail.Children.Add(FocusedLayout.Disclosure("Full checklist & coverage", steps, "Ingredients, intermediate rewards and alternate routes. View Progress to navigate a specific branch.")); libraryDetail.Children.Add(UI.Button("Open walkthrough", () => UI.OpenUrl(r.Quest.WikiUrl)));
        Usability.Tooltips(libraryDetail);
    }
    private void ConfigureGearControls(WrapPanel controls)
    {
        var menu = new ContextMenu(); var button = OverlayMenus.Button(menu, "Exact expansion selections include clearly marked zone-based assumptions; unresolved metadata stays Unknown.", "Expansion Filter ▾"); button.Width = double.NaN; button.Padding = new(6, 1, 6, 1);
        void Update()
        {
            var selected = Controller.Profile.GearExpansions; var names = new[] { "Classic", "Kunark", "Velious", "Unknown" };
            string shortNames = selected.Count == 4 ? "All" : selected.Count == 0 ? "None" : string.Join(" / ", names.Where(selected.Contains).Select(n => n == "Unknown" ? "?" : n[..1]));
            button.Content = "Expansion Filter · " + shortNames + " ▾"; button.ToolTip = "Item introduction expansions: " + (selected.Count == 0 ? "None" : string.Join(", ", selected)) + ". Zone-based assumptions are marked in Item Details; missing evidence remains Unknown. Independent of Pet filters.";
        }
        foreach (string name in new[] { "Classic", "Kunark", "Velious", "Unknown" }) {
            var choice = new MenuItem { Header = name, IsCheckable = true, IsChecked = Controller.Profile.GearExpansions.Contains(name), StaysOpenOnClick = true };
            choice.Click += (_, _) => { if (choice.IsChecked) { if (!Controller.Profile.GearExpansions.Contains(name)) Controller.Profile.GearExpansions.Add(name); } else Controller.Profile.GearExpansions.Remove(name); Update(); GearEdited(); }; menu.Items.Add(choice);
        }
        Update(); controls.Children.Add(button);
    }
    private void LayoutQuestColumns()
    {
        if (fittingColumns || questSearchArea == null || table.ActualWidth < 1) return; fittingColumns = true;
        try {
            double gutter = Descendants(table).OfType<ScrollBar>().Where(b => b.Orientation == Orientation.Vertical && b.IsVisible).Select(b => b.ActualWidth).DefaultIfEmpty(0).Max();
            double width = Math.Max(0, table.ActualWidth - table.BorderThickness.Left - table.BorderThickness.Right - gutter);
            questSearchArea.Width = width;
        } finally { fittingColumns = false; }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var d in Descendants(child)) yield return d; } }

    private UIElement BuildExaltations()
    {
        exaltSearch.Height=28;exaltSearch.Margin=new(0);foreach(var control in new[]{exaltType,exaltPurpose,exaltOwnership,exaltContent,exaltExpansion,exaltTarget,exaltCompatibility})control.Height=28;
        var root = new DockPanel(); var filters = new WrapPanel(); foreach (var c in new UIElement[] { UI.Field("Search", (FrameworkElement)FocusedLayout.Search(exaltSearch, "Find an effect or source…")), UI.Field("Socket", exaltType), UI.Field("Purpose", exaltPurpose), UI.Field("Ownership", exaltOwnership), UI.Field("Content", exaltContent) }) filters.Children.Add(c);
        var advanced = new WrapPanel(); foreach (var c in new UIElement[] { UI.Field("Expansion", exaltExpansion), UI.Field("Target", exaltTarget), UI.Field("Restrictions", exaltCompatibility) }) advanced.Children.Add(c);
        var header = UI.Stack(filters, FocusedLayout.Disclosure("Expansion & compatibility", advanced, "Filter exact item expansion, intended loadout and class compatibility.")); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        root.Children.Add(UI.Split(TrackerLeftPane(exaltTable), FloatingEffectInspector(exaltDetail), 340, Controller.Profile, "exaltations", 280, 200));
        exaltTable.SelectionChanged += (_, _) => ShowExaltation(); exaltSearch.TextChanged += (_, _) => { exaltKey = ""; if (!initializing) RefreshExaltations(); };
        foreach (var c in new[] { exaltType, exaltOwnership, exaltExpansion, exaltTarget, exaltCompatibility, exaltPurpose, exaltContent }) c.SelectionChanged += (_, _) => { exaltKey = ""; if (!initializing) RefreshExaltations(); }; return root;
    }
    private void RefreshExaltations()
    {
        string catalogKey = $"{Controller.EquipmentRevision}|{Controller.QuestRevision}|{Controller.Catalog.Items.Count}|{Controller.Inventory.Revision}";
        if (catalogKey != exaltCatalogKey)
        {
            if (Controller.Demo) { exaltOptions = ExaltationPlanner.Build(Controller.Catalog, Controller.Inventory); exaltCatalogKey = catalogKey; }
            else
            {
                if (exaltRequestedKey != catalogKey)
                {
                    exaltRequestedKey = catalogKey;
                    var catalog = Controller.EquipmentView?.Catalog ?? new Catalog { Items = Controller.Catalog.Items.ToList() };
                    var inventory = Controller.EquipmentView?.Inventory ?? Controller.Inventory;
                    _ = Task.Run(() => ExaltationPlanner.Build(catalog, inventory)).ContinueWith(task => Dispatcher.InvokeAsync(() => {
                        if (task.IsCompletedSuccessfully && exaltRequestedKey == catalogKey) { exaltOptions = task.Result; exaltCatalogKey = catalogKey; exaltKey = ""; if (SelectedTab == TrackerTab.Exaltations) RefreshExaltations(); }
                    }), TaskScheduler.Default);
                }
                return;
            }
        }
        RefreshExaltationCompatibilityLabel();
        string key = catalogKey + exaltSearch.Text + exaltType.SelectedItem + exaltOwnership.SelectedItem + exaltExpansion.SelectedItem + exaltTarget.SelectedItem + exaltCompatibility.SelectedItem + exaltPurpose.SelectedItem + exaltContent.SelectedItem; if (key == exaltKey) { RefreshSelectedEffectReference(); return; } exaltKey = key;
        var rows = exaltOptions.Where(e => e.Name.Contains(exaltSearch.Text, StringComparison.OrdinalIgnoreCase)
            && (exaltType.SelectedIndex == 0 || e.Type == exaltType.SelectedItem?.ToString())
            && (exaltPurpose.SelectedIndex == 0 || e.Purpose.ToString() == exaltPurpose.SelectedItem?.ToString())
            && (exaltOwnership.SelectedIndex switch { 1 => e.Owned, 2 => e.SourceOwned, 3 => !e.Owned && !e.SourceOwned, _ => true })
            && (exaltExpansion.SelectedIndex == 0 || ItemPresentation.Expansion(e.Source) == exaltExpansion.SelectedItem?.ToString())
            && (exaltContent.SelectedIndex switch { 0 => e.Content != "Future", 1 => e.Content == "Future", _ => true })).ToList();
        if (exaltCompatibility.SelectedIndex > 0) {
            var owner = Controller.Profile.ActiveLoadout; string target = exaltTarget.SelectedItem?.ToString() ?? "Any";
            var classes = target == "Any" ? GearScorer.EligibleClasses(owner, false).Concat(GearScorer.EligibleClasses(owner, true)).Distinct().ToList() : GearScorer.EligibleClasses(owner, target == "Pet");
            rows = rows.Where(e => e.Source.Classes.Count > 0 && e.Source.Slots.Count > 0 && (e.Source.Classes.Contains("ALL") || e.Source.Classes.Intersect(classes).Any()) == (exaltCompatibility.SelectedIndex == 1)).ToList();
        }
        string? selected = (exaltTable.SelectedItem as ExaltationOption)?.Id; UI.SetRows(exaltTable, rows.OrderBy(e => e.Source.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Effect, StringComparer.OrdinalIgnoreCase).ToList());
        var displayed = exaltTable.Items.OfType<ExaltationOption>().ToList(); exaltTable.SelectedItem = displayed.FirstOrDefault(e => e.Id == selected) ?? displayed.FirstOrDefault(); ShowExaltation();
    }
    private void PinExaltation()
    { if (exaltTable.SelectedItem is not ExaltationOption e) return; AddPin(new() { Kind = "Exaltation", QuestId = "exaltation", ItemKey = e.Source.Key, EffectId = e.Id, Name = e.Name }); }
    private void ShowExaltation()
    {
        selectedEffectReferencePanel=null;selectedEffectReferenceKey="";
        exaltDetail.Children.Clear(); if (exaltTable.SelectedItem is not ExaltationOption e) { exaltDetail.Children.Add(UI.Text("No matching effects. Unknown effect types remain visible.")); return; }
        var icons = new ItemHeaderIcons(UI.Text(e.Source.Name, 16, "Accent", true)); icons.Show(e.Source, e.Owned ? new(true, e.OwnershipEvidence.Details, false, null) : ItemOwnership.For(e.Source, Controller.Inventory));
        selectedEffectReferencePanel=new();
        exaltDetail.Children.Add(UI.Card(UI.Stack(icons, UI.Text(e.Effect, 13, "Mint"), selectedEffectReferencePanel, UI.Text(e.Type == "Unknown" ? "Unlock rank unverified" : $"{e.Type} · extract/socket at +{e.UnlockRank}" + (e.Purpose == EffectPurpose.Unknown ? "" : " · " + e.Purpose), 12), UI.Text(e.State, 12), UI.Text("Class: " + string.Join(" / ", e.Source.Classes) + "\nSlots: " + ItemPresentation.Slots(e.Source.Slots), 11), UI.Text((e.Content == "Unverified" ? "Availability: unverified" : e.Content) + (e.Source.AvailabilityEvidence.Length > 0 ? " · " + e.Source.AvailabilityEvidence : ""), 10, "Muted"), UI.Text(ItemPresentation.ExpansionNote(e.Source), 10, "Muted"))));
        RefreshSelectedEffectReference();
        exaltDetail.Children.Add(FocusedLayout.Disclosure("Ownership evidence", UI.Text(e.OwnershipEvidence.Details, 11), "Distinguishes source equipment, extracted storage, socketed effects and collection/keyring presence. No copies are invented."));
        exaltDetail.Children.Add(UI.CommitButton("Add to queue", PinExaltation));
        exaltDetail.Children.Add(FocusedLayout.Disclosure("Unlock rules", UI.Text("Ornamentation +0 · Focus +1 · Click +2 · Worn +3 · Proc +4. Source ownership is not an extracted effect. Collection entries prove presence, not quantity. Intrinsic effects remain separate from socketed and planned effects.", 11, "SecondaryText"), "Effect types unlock at different source and destination ranks; source ownership is not effect ownership."));
        AddFocusedSources(exaltDetail, e.Source.Sources.DistinctBy(s => (s.Zone, s.Mob, s.Method, s.Availability)));
        exaltDetail.Children.Add(UI.Button("Open source wiki", () => UI.OpenUrl(e.Source.WikiUrl)));
        if (exaltDestination.Parent is Panel prior) prior.Children.Remove(exaltDestination);
        var assignment = new StackPanel();
        if (petBuild.Items.Count == 0) RefreshBuildChoices();
        if (exaltTarget.SelectedItem?.ToString() == "Pet") exaltDestination.ItemsSource = CurrentBuild?.Items.ToList() ?? [];
        else exaltDestination.ItemsSource = Controller.Profile.ActiveLoadout.Equipment.ToList();
        exaltDestination.DisplayMemberPath = "Name"; exaltDestination.SelectedIndex = 0;
        if (exaltTarget.SelectedItem?.ToString() == "Pet")
        {
            assignment.Children.Add(UI.Label("PLAN ON PET BUILD · " + CurrentBuild?.Name)); assignment.Children.Add(exaltDestination);
            var note = UI.Text(CurrentBuild == null ? "Choose a named plan in Pet's Build selector first." : "Choose a destination to validate rank, wearable-slot and class intersection.", 11, "Muted"); assignment.Children.Add(note);
            var replaceEffect = new CheckBox { Content = "Replace existing effect of this type", ToolTip = "Explicitly replace the effect occupying this type slot in the selected plan. Intrinsic effects remain separate." }; assignment.Children.Add(replaceEffect);
            void AssignEffect(string state)
            {
                if (CurrentBuild is not { } build || exaltDestination.SelectedItem is not PetInventoryItem item || Controller.Catalog.FindKey(item.ItemKey) is not { } definition) return;
                var proposed = EquipmentPositions.Copy(item);
                if (!ExaltationPlanner.Assign(proposed,e,definition,item.Rank,item.Slot,GuidedPetBuilds.Context(Controller.Profile.ActiveLoadout,build),Controller.Catalog,state,replaceEffect.IsChecked==true,out string issue)) { note.Text=issue; return; }
                PetBuildEditing.Remember(build,"Exaltation assignment"); item.Exaltations=proposed.Exaltations; PetEdited(); note.Text=state+" effect saved in “"+build.Name+"” · Undo available in Pet.";
            }
            assignment.Children.Add(UI.Button("Assign to planned item", () => AssignEffect("Planned")));
            var socket = UI.Button("Record socketed effect", () => AssignEffect("Socketed")); socket.ToolTip = "Record an observed socketed effect in this plan. Current build is unchanged until you explicitly use this plan."; assignment.Children.Add(socket);
            assignment.Children.Add(UI.Button("Remove planned / socketed effect", () => { if (exaltDestination.SelectedItem is PetInventoryItem item) { if (CurrentBuild is { } build) PetBuildEditing.Remember(build,"Remove effect"); item.Exaltations.RemoveAll(a => e.MatchesId(a.EffectId)); PetEdited(); note.Text = "Build assignment removed. Confirmed equipped baseline is unchanged."; } }));
            assignment.Children.Add(UI.Button("Mark intrinsic effect transferred out", () => { if (exaltDestination.SelectedItem is PetInventoryItem item && item.ItemKey == e.Source.Key) { if (CurrentBuild is { } build) PetBuildEditing.Remember(build,"Transferred effect"); if (!item.TransferredOutEffects.Contains(e.Effect)) item.TransferredOutEffects.Add(e.Effect); PetEdited(); note.Text = "Intrinsic effect excluded in this build. Confirm equipment separately."; } else note.Text = "Select the source item itself to mark its intrinsic effect removed."; }));
        }
        else
        {
            assignment.Children.Add(UI.Label("PLAN ON SELF EQUIPMENT")); assignment.Children.Add(exaltDestination);
            var note = UI.Text("Plans preserve confirmed equipment. Choose an owned baseline or queue the effect for a future item.", 11, "Mint"); assignment.Children.Add(note);
            assignment.Children.Add(UI.Button("Assign to planned item", () => { if (exaltDestination.SelectedItem is not InventoryEntry item || Controller.Catalog.Find(item.Name) is not { } destination) return;
                string result = ExaltationPlanner.Validate(e, destination, item.Rank, item.Location, Controller.Profile.ActiveLoadout, false, Controller.Catalog); note.Text = result;
                if (result != "Compatible") return; var plans = Controller.Profile.ActiveLoadout.PlannedExaltations;
                if (!plans.Any(p => e.MatchesId(p.EffectId) && p.DestinationKey == destination.Key && p.DestinationSlot == item.Location)) plans.Add(new() { Target = "Self", EffectId = e.Id, SourceName = e.Source.Name, Effect = e.Effect, DestinationKey = destination.Key, DestinationSlot = item.Location, DestinationRank = item.Rank }); Controller.Save(); note.Text = "Saved Self effect plan · confirmed equipment unchanged."; }));
            foreach (var plan in Controller.Profile.ActiveLoadout.PlannedExaltations.Where(p => e.MatchesId(p.EffectId))) assignment.Children.Add(UI.Text($"Planned: {Controller.Catalog.FindKey(plan.DestinationKey)?.Name} +{plan.DestinationRank} · {plan.DestinationSlot}", 11));
        }
        if (exaltTarget.SelectedItem?.ToString() != "Any") exaltDetail.Children.Add(FocusedLayout.Disclosure("Plan an assignment", assignment, "Choose a destination only when planning or manually recording an effect. Confirmed equipment is separate."));
        Usability.Tooltips(exaltDetail);
    }
}
