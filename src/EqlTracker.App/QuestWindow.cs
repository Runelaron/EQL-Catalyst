using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed record StepRow(string Id, string Quest, string Step, string Inputs, string Outputs, string Progress, string Instructions, string WikiUrl);
public sealed record GearRow(string Name, string Slot, string Replaces, double? Delta, string Verdict, string Confidence, string Details, string WikiUrl)
{ public ItemDefinition Item { get; init; } = new(); public int Rank { get; init; } public GearComparison? Comparison { get; init; }
  public string PositionId { get; init; } = ""; public InventoryEntry? Instance { get; init; }
  public string Ownership { get; init; } = ""; public string Type => PetItemRules.Type(Item);
  public double? UnbuffedDelta => Comparison?.Evaluation?.Unbuffed.Delta ?? (Comparison?.Evaluation == null ? Delta : null);
  public double? BuffedDelta => Comparison?.Evaluation?.Buffed.Delta;
  public double? ItemScore => Comparison?.ItemScore;
  public string ItemScoreTooltip => "Item score sums this item's stats × selected preset weights. It can be shown without an equipped baseline. Effects are separate; this is not measured DPS.";
  public string ScoreTooltip => "Preference score change = sum of stat changes × selected weights. Effects are separate; this is not measured DPS.\n" + (Comparison?.CanCompare == true ? Details : "Eligibility or equipment baseline is incomplete."); }
public sealed partial class QuestWindow : FrostWindow
{
    private readonly TextBox search = UI.Input();
    private readonly ComboBox zone;
    private readonly ComboBox status = UI.Select(["Needed", "All", "Owned", "Unavailable"], "Needed", 115);
    private readonly ComboBox era;
    private readonly ComboBox planThrough;
    private Grid questSplit = null!;
    private readonly TextBlock checklistNotice = UI.Text("", 11, "Violet");
    private readonly TextBlock count = UI.Text("", 12, "Mint");
    private readonly DataGrid table;
    private readonly StackPanel detail = new() { Margin = new(0) };
    private readonly WrapPanel classes = new() { Margin = new(0, 0, 0, 4) };
    private readonly TextBox gearSearch = UI.Input("", 260);
    private readonly ComboBox gearTarget = UI.Select(["Self", "Pet"], "Self", 85);
    private readonly ComboBox preset;
    private readonly DataGrid gearTable;
    private readonly TextBlock gearDetails = UI.Text("Select equipment to see score contributions and effect changes.", 12, "Muted");
    private readonly ListBox wishlist = new() { Height = 130 };
    private readonly ComboBox questSelection;
    private readonly TextBox progressSearch = UI.Input("", 165);
    private readonly CheckBox progressTracked = new() { Content = "Tracked only", IsChecked = false };
    private string progressQuestFilter = "";
    private readonly ComboBox route = UI.Select(["Default", "Include optional steps"], "Default", 180);
    private readonly TreeView progressTree = new() { Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new(0) };
    private QuestNode? selectedStep;
    private string? progressDetailNodeId;
    private readonly ObservableCollection<QuestProgressNode> progressNodes = [];
    private readonly StackPanel howTo = new();
    private readonly TextBlock questEmpty = UI.Text("No goals match these filters", 12, "Muted");
    private readonly TextBlock breadcrumb = UI.Text("", 11, "SecondaryText"), gearSearchStatus = UI.Text("", 11, "Muted");
    private readonly WrapPanel chips = new();
    private readonly ItemDetailsInspector gearInspector = new();
    private ComparisonPanel comparisonView => gearInspector.Rating;
    private CancellationTokenSource? gearSearchCancel;
    private CancellationTokenSource? gearViewCancel;
    internal int SuppressedGearBuilds { get; private set; }
    private string lastSearch = "";
    private readonly TextBlock stepDetails = UI.Text("Select a quest step. Manual corrections are reversible.", 12, "Muted");
    private readonly TextBlock coverage = UI.Text("", 12, "Violet");
    private readonly TabControl tabs = new();
    private readonly ListBox queueList = new();
    private string queueSignature = "", questFilterKey = "";
    private string? fingerprint, classFingerprint, stepFingerprint, gearFingerprint;
    private bool initializing = true;
    public QuestWindow(TrackerController controller) : base(controller, "Quest & Gear Tracker", "Prepare now. Know what to keep. Find the next upgrade.", 1220, 800, surface: WindowSurface.Theme)
    {
        Appearance.RaisedButtons(Body);
        CatalystHeading.Apply(HeadingTitle, "Quest & Gear Tracker");
        era = UI.Select(ExpansionRules.Names, ExpansionRules.Current(controller.Profile), 100);
        search.Margin = new(0, 0, 0, 4);
        planThrough = UI.Select(ExpansionRules.Names, controller.Profile.PlanThrough, 100);
        preset = UI.Select(controller.Profile.Weights.Keys, "Pet damage", 165);
        zone = UI.Select(new[] { "All zones", "Current zone" }.Concat(controller.Catalog.Zones.Order()), "Current zone", 215);
        table = UI.Table(("Item", "Item", 0, null), ("H/N", "Quantity", 50, null), ("Cls", "Class", 42, null), ("Zone", "Zone", 150, null), ("Status", "Availability", 125, null));
        table.MinWidth = 280; TableLayout.SetKey(table, "quest");
        ScrollViewer.SetVerticalScrollBarVisibility(table, ScrollBarVisibility.Visible);
        table.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => { if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) Dispatcher.BeginInvoke(LayoutQuestColumns, System.Windows.Threading.DispatcherPriority.Loaded); }));
        foreach (var (column, tooltip) in new[] { (table.Columns[1], "Allocated Have / Need for this item and class goal. Shared stock is allocated once by goal priority."), (table.Columns[2], "Class required by this quest goal") })
        { var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), (Style)Application.Current.FindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader))); style.Setters.Add(new Setter(ToolTipProperty, tooltip)); column.HeaderStyle = style; }
        table.SelectionChanged += (_, _) => ShowDetail();
        var controls = new WrapPanel(); foreach (var control in new UIElement[] { UI.Field("Zone", zone), UI.Field("Status", status), UI.Field("Current expansion", era), UI.Field("Plan through", planThrough) }) { if (control is StackPanel panel) panel.Margin = new(0, 0, 6, 0); controls.Children.Add(control); }
        count.VerticalAlignment = VerticalAlignment.Bottom; count.ToolTip = "Goals are item/class requirements. Remaining sums outstanding quantities after ownership allocation; one item can be required by several classes.";
        era.ToolTip = "The server expansion now. Source availability and observed drops use this setting."; planThrough.ToolTip = "Include quest goals through this expansion. Classic sources for future quests remain collectable now.";
        var advancedItems = FocusedLayout.Disclosure("Filters · classes & expansions", UI.Stack(controls, classes, checklistNotice), "Choose classes, source expansion and planning horizon. Collection still uses source availability.");
        itemFilters = advancedItems;
        var filter = UI.Stack(count, advancedItems, chips);
        var questGrid = new Grid(); questGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); questGrid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); questGrid.Children.Add(filter);
        var listPane = new Grid(); listPane.RowDefinitions.Add(new() { Height = GridLength.Auto }); listPane.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); questSearchArea = UI.Stack(UI.Label("Search items, mobs, quests, or zones"), search); questSearchArea.HorizontalAlignment = HorizontalAlignment.Left; listPane.Children.Add(questSearchArea); Grid.SetRow(table, 1); listPane.Children.Add(table);
        var emptyPanel = UI.Stack(questEmpty, UI.Row(UI.Button("Browse quests", () => ShowView(TrackerTab.Quests)), UI.Button("Clear filters", () => { search.Clear(); zone.SelectedItem = "All zones"; status.SelectedItem = "All"; }))); emptyPanel.Margin = new(8, 48, 8, 0); emptyPanel.VerticalAlignment = VerticalAlignment.Top; emptyPanel.Visibility = Visibility.Collapsed; itemsEmptyPanel = emptyPanel; Grid.SetRow(emptyPanel, 1); listPane.Children.Add(emptyPanel);
        table.SizeChanged += (_, _) => LayoutQuestColumns(); table.Loaded += (_, _) => LayoutQuestColumns();
        ConfigureGoalGroups();
        var split = questSplit = UI.Split(TrackerLeftPane(listPane), ItemFloatingInspector(UI.Stack(breadcrumb, detail)), 295, controller.Profile, "quest", 280, 180); Grid.SetRow(split, 1); questGrid.Children.Add(split);
        AddTab(TrackerTab.Items, "ITEMS", questGrid);
        gearTarget.SelectedItem = "Self";
        preset.ItemsSource = controller.Profile.Weights.Keys.Where(k => !k.StartsWith("Pet", StringComparison.OrdinalIgnoreCase)).ToList(); preset.SelectedItem = controller.Profile.ActiveLoadout.Preset;
        gearTable = CreateGearTable();
        AddTab(TrackerTab.Gear, "GEAR", BuildGearWorkspace());
        questSelection = UI.Select(controller.Catalog.Quests.Select(x => x.Name), "Warrior Epic Quest", 200);
        questSelection.ToolTip = "Choose a quest. Select an ingredient for sources, an intermediate reward for its process, or the quest root for all steps.";
        VirtualizingPanel.SetIsVirtualizing(progressTree, true); VirtualizingPanel.SetScrollUnit(progressTree, ScrollUnit.Pixel); ScrollViewer.SetCanContentScroll(progressTree, true);
        ConfigureProgressTree();
        progressTree.SelectedItemChanged += (_, e) => { selectedStep = (e.NewValue as QuestProgressNode)?.Node; ShowProgressDetails(); };
        var stepsGrid = new Grid(); stepsGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); stepsGrid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); stepsGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Button ProgressButton(string label, Action action, string tooltip) { var button = UI.Button(label, action); button.ToolTip = tooltip; return button; }
        var stepControls = new WrapPanel();
        foreach (var control in new UIElement[] { UI.Field("Find quest / reward", progressSearch), UI.Field("Quest", questSelection), UI.ActionField(progressTracked), UI.ActionField(ProgressButton("Open walkthrough", OpenQuest, "Open the source wiki walkthrough for this quest.")), UI.ActionField(FocusedLayout.Menu("More ▾", ("Expand all", () => ExpandProgress(true), "Expand every step and ingredient."), ("Collapse all", () => ExpandProgress(false), "Collapse the quest branches."), ("Complete / reopen step", ToggleStep, "Reversible correction; does not create inventory."), ("Complete / reopen quest", ToggleQuest, "Reversible manual completion of this quest."))) }) stepControls.Children.Add(control);
        progressSearch.ToolTip = "Search quest names and reward names in the cached library."; progressTracked.ToolTip = "Limit the selector to tracked quests. Turn off to browse every available quest.";
        progressSearch.TextChanged += (_, _) => { FilterProgressQuests(); RefreshSteps(); }; progressTracked.Checked += (_, _) => { FilterProgressQuests(); RefreshSteps(); }; progressTracked.Unchecked += (_, _) => { FilterProgressQuests(); RefreshSteps(); };

        stepsGrid.Children.Add(UI.Stack(stepControls, FocusedLayout.Disclosure("Routes & checklist coverage", UI.Stack(route, coverage), "Alternative routes and source coverage; leaves the main checklist unchanged until you choose a route.")));
        var howPanel = UI.Card(UI.Scroll(UI.Stack(UI.Text("How to obtain", 13, "Melee", true), howTo))); howPanel.SetResourceReference(Border.BackgroundProperty, "DetailSurface");
        var statusCard = UI.Card(UI.Stack(UI.Label("STEP STATUS"), stepDetails)); statusCard.SetResourceReference(Border.BackgroundProperty, "DetailSurface"); stepDetails.SetResourceReference(TextBlock.ForegroundProperty, "Foreground");
        statusCard.MaxHeight = 120; statusCard.Child = UI.Scroll(statusCard.Child); statusCard.Margin = new(0, 4, 0, 0);
        var progressLeft = new DockPanel(); DockPanel.SetDock(statusCard, Dock.Bottom); progressLeft.Children.Add(statusCard); progressLeft.Children.Add(progressTree);
        var progressSplit = UI.Split(TrackerLeftPane(progressLeft), howPanel, 320, Controller.Profile, "progress-details", 240, 180); Grid.SetRow(progressSplit, 1); Grid.SetRowSpan(progressSplit, 2); stepsGrid.Children.Add(progressSplit);
        AddTab(TrackerTab.Progress, "PROGRESS", stepsGrid);
        AddTab(TrackerTab.Queue, "QUEUE", BuildFocusedQueue()); ConfigureQueueDrag();
        ConfigurePlanningTabs();
        TrackerTabChrome.Install(tabs); Body.Children.Add(tabs);
        foreach (var control in new[] { zone, status, era, planThrough, gearTarget, questSelection }) control.SelectionChanged += (_, _) => { if (!initializing) { lock (Controller.Sync) { if (control == era || control == planThrough) { Controller.Profile.Era = Controller.Profile.CurrentExpansion = era.SelectedItem?.ToString() ?? "Classic"; Controller.Profile.PlanThrough = planThrough.SelectedItem?.ToString() ?? "Velious"; } if (control == gearTarget) preset.SelectedItem = gearTarget.SelectedItem?.ToString() == "Pet" ? Controller.Profile.ActiveLoadout.PetPreset : Controller.Profile.ActiveLoadout.Preset; if (control == preset) { if (gearTarget.SelectedItem?.ToString() == "Pet") Controller.Profile.ActiveLoadout.PetPreset = preset.SelectedItem?.ToString() ?? "Pet damage"; else Controller.Profile.ActiveLoadout.Preset = preset.SelectedItem?.ToString() ?? "Tank"; } fingerprint = stepFingerprint = gearFingerprint = null; if (control == era || control == planThrough || control == preset) Controller.Save(); Refresh(); } } };
        preset.SelectionChanged += (_, _) => { if (initializing) return; lock (Controller.Sync) Controller.Profile.ActiveLoadout.Preset = preset.SelectedItem?.ToString() ?? "Balanced Tank"; fingerprint = stepFingerprint = gearFingerprint = null; Controller.Save(); Refresh(); };
        search.TextChanged += (_, _) => { fingerprint = null; if (!initializing) Refresh(); };
        gearSearch.TextChanged += (_, _) => { gearFingerprint = null; if (!initializing) { Refresh(); ScheduleGearSearch(); } };
        tabs.SelectionChanged += (_, e) => { if (e.Source == tabs && !initializing) { if (SelectedTab != TrackerTab.Gear && gearTheory != null && !ConfirmGearWorkspaceExit()) { bool previous = initializing; initializing = true; tabs.SelectedItem = tabById[TrackerTab.Gear]; initializing = previous; return; } if (SelectedTab != TrackerTab.Pet && petTheory != null) { if (!ConfirmPetWorkspaceExit()) { bool previous = initializing; initializing = true; tabs.SelectedItem = tabById[TrackerTab.Pet]; initializing = previous; return; } RefreshBuildChoices("current"); } if (SelectedTab != TrackerTab.Gear) { gearViewCancel?.Cancel(); gearFingerprint = null; } if (SelectedTab != TrackerTab.Pet) { petViewCancel?.Cancel(); petPreviewCancel?.Cancel(); petKey = ""; } if (SelectedTab != TrackerTab.Quests) { libraryViewCancel?.Cancel(); libraryKey = ""; } if (SelectedTab != TrackerTab.Items) { questFilterKey = ""; fingerprint = null; } Refresh(); } };
        Body.IsVisibleChanged += (_, _) => { if (!Body.IsVisible) { gearViewCancel?.Cancel(); petViewCancel?.Cancel(); libraryViewCancel?.Cancel(); petKey = libraryKey = ""; gearFingerprint = null; questFilterKey = ""; fingerprint = null; } };
        route.SelectionChanged += (_, _) => { if (initializing) return; lock (Controller.Sync) { var q = Controller.Catalog.Quests.FirstOrDefault(x => x.Name == questSelection.SelectedItem?.ToString()); if (q == null) return; Controller.Profile.Routes[q.Id] = route.SelectedIndex == 1 ? "Alternative" : "Default"; Controller.Save(); stepFingerprint = fingerprint = null; Refresh(); } };
        initializing = false; Refresh();
    }
    public void Refresh()
    {
        if (Monitor.IsEntered(Controller.Sync)) { Dispatcher.BeginInvoke(Refresh); return; }
        using (UpdateDiagnostics.Measure("QuestWindow refresh"))
        {
            var profile = Controller.Profile;
            Subtitle.Text = $"{profile.Character} · {profile.Zone} · {(Controller.Inventory.CapturedAt == default ? "Inventory not imported" : "Updated " + Controller.Inventory.CapturedAt.ToString("MMM/dd HH:mm"))}";
            string classState = string.Join(',', profile.EnabledClasses) + string.Join(',', profile.CurrentClasses) + string.Join(',', profile.Level50Classes);
            if (classFingerprint != classState)
            {
                classFingerprint = classState; classes.Children.Clear();
                foreach (var c in Controller.Catalog.Quests.Where(x => x.Kind == "Epic").Select(x => x.Class).Distinct().Order())
                {
                    var q = Controller.Catalog.Quests.First(x => x.Class == c && x.Kind == "Epic");
                    var checkbox = new CheckBox { Content = c + (profile.CurrentClasses.Contains(c) ? " •" : profile.Level50Classes.Contains(c) ? " 50" : ""), IsChecked = profile.EnabledClasses.Contains(c), ToolTip = q.Steps.Count == 0 ? "Epic checklist unavailable. Manual wishlist goals are available. " + q.Coverage : profile.CurrentClasses.Contains(c) ? "Current class" : profile.Level50Classes.Contains(c) ? "Previously reached level 50" : "Optional goal" };
                    checkbox.Click += (_, _) => { lock (Controller.Sync) { if (checkbox.IsChecked == true) { profile.OptionalClasses.Add(c); profile.ExcludedClasses.Remove(c); } else { profile.OptionalClasses.Remove(c); profile.ExcludedClasses.Add(c); } Controller.Save(); fingerprint = null; Refresh(); } }; classes.Children.Add(checkbox);
                }
            }
            if (SelectedTab == TrackerTab.Items) RefreshObjectives();
            if (SelectedTab == TrackerTab.Queue) RefreshQueue();
            if (SelectedTab == TrackerTab.Gear) RefreshGear();
            if (SelectedTab == TrackerTab.Progress) RefreshSteps();
            RefreshPlanningTab();
        }
    }
    private void RefreshObjectives()
    {
        var p = Controller.Profile;
        string selectedZone = zone.SelectedItem?.ToString() ?? "All zones", selectedStatus = status.SelectedItem?.ToString() ?? "Needed", query = search.Text, currentZone = p.Zone, currentEra = p.CurrentExpansion;
        string key = $"{Controller.QuestRevision}|{selectedZone}|{selectedStatus}|{query}|{currentZone}";
        UpdateChips();
        checklistNotice.Text = string.Join(" / ", Controller.Catalog.Quests.Where(q => q.Steps.Count == 0 && p.EnabledClasses.Contains(q.Class)).Select(q => q.Class + ": Epic checklist unavailable · manual wishlist available"));
        if (questFilterKey == key && fingerprint != null) return;
        questFilterKey = key; fingerprint = key;
        var objectives = Controller.Objectives;
        List<ObjectiveRow> Filter() => objectives.Where(x => (selectedStatus switch { "Needed" => x.Remaining > 0, "Owned" => x.Remaining == 0, "Unavailable" => x.Availability is "Unavailable" or "Future / source unknown", _ => true }) &&
            (selectedZone switch { "All zones" => true, "Current zone" => QuestPlanner.InZone(x, currentZone, currentEra), _ => x.Sources.Any(s => Identity.ZoneKey(s.Zone) == Identity.ZoneKey(selectedZone)) || Identity.ZoneKey(x.Zone) == Identity.ZoneKey(selectedZone) }) &&
            (x.Item + x.Mob + x.Zone + x.Quest + x.Class).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        void Apply(List<ObjectiveRow> filtered)
        {
            if (questFilterKey != key || SelectedTab != TrackerTab.Items) return;
            var previous = table.SelectedItem as ObjectiveRow;
            UI.SetRows(table, filtered); var grouped = CollectionViewSource.GetDefaultView(table.ItemsSource); if (grouped.GroupDescriptions.Count == 0) { using var groupMeasure = UpdateDiagnostics.Measure("Quest grouping"); grouped.GroupDescriptions.Add(new PropertyGroupDescription("Group")); }
            FitShortColumns(objectives); LayoutQuestColumns(); itemsEmptyPanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed; questEmpty.Text = objectives.Count == 0 ? "No tracked goals. Browse quests to choose rewards." : "No goals match these filters."; table.HeadersVisibility = DataGridHeadersVisibility.Column;
            table.SelectedItem = filtered.FirstOrDefault(x => x.Key == previous?.Key && x.QuestId == previous.QuestId) ?? filtered.FirstOrDefault();
            count.Text = $"{filtered.Count} goals · {filtered.Sum(x => x.Remaining)} remaining";
            if (filtered.Count == 0) { breadcrumb.Text = ""; detail.Children.Clear(); detail.Children.Add(UI.Text("No goals match these filters", 12, "Muted")); }
        }
        if (Controller.Demo) Apply(Filter());
        else _ = Task.Run(Filter).ContinueWith(t => Dispatcher.InvokeAsync(() => { if (t.IsCompletedSuccessfully) Apply(t.Result); else if (questFilterKey == key) fingerprint = null; }), TaskScheduler.Default);
    }
    private void FitShortColumns(IEnumerable<ObjectiveRow> values) => TableLayout.SampleChanged(table);

    private void ResetLayout()
    {
        Controller.Profile.PanelSplits.Remove("quest"); questSplit.ColumnDefinitions[0].Width = new(.7, GridUnitType.Star); questSplit.ColumnDefinitions[2].Width = new(.3, GridUnitType.Star);
        TableLayout.Reset(table);
        LayoutQuestColumns(); Controller.Save();
    }
    internal void ResetTrackerLayout()=>ResetLayout();
    public void SetZoneFilter(string selected) { zone.SelectedItem = selected; fingerprint = null; Refresh(); }
    internal void SetSearch(string query) { search.Text = query; Refresh(); }
    private void ShowDetail()
    {
        detail.Children.Clear(); if (table.SelectedItem is not ObjectiveRow row) return;
        breadcrumb.Text = row.Class + " › " + row.Quest + (row.ParentReward.Length > 0 ? " › " + row.ParentReward : "") + " › " + row.Item;
        detail.Children.Add(UI.Card(UI.Stack(UI.Label(row.Class + " / " + row.Quest), UI.Text(row.Item, 20, "Accent", true), UI.Text(row.Quantity + " allocated · " + row.TotalOwned + " known owned", 13, "Mint"), UI.Text(row.Eligibility, 12, "Violet"))));
        detail.Children.Add(UI.CommitButton("Add to queue", () => Pin(row)));
        detail.Children.Add(UI.Label("WHERE TO GET IT"));
        foreach (var source in row.Sources.OrderByDescending(x => Identity.ZoneKey(x.Zone) == Identity.ZoneKey(Controller.Profile.Zone)).ThenBy(x => x.Era != "Classic"))
            detail.Children.Add(UI.Card(UI.Stack(UI.Text(source.Zone, 14, "Foreground", true), UI.Text(source.Mob, 13), UI.Text(source.Method + " · " + (ExpansionRules.Names.Contains(source.Era) ? source.Era : "Expansion unknown (" + source.Era + ")") + " · " + source.Availability, 11, source.Availability == "Unavailable" ? "Violet" : "Muted"), UI.Text(source.Coordinates, 12, "Mint"))));
        detail.Children.Add(FocusedLayout.Disclosure("Quest context & prerequisites", UI.Text(row.Instructions, 12), "Collection sources are independent of later turn-ins unless a prerequisite explicitly says otherwise."));
        detail.Children.Add(UI.Label("OWNERSHIP EVIDENCE")); detail.Children.Add(UI.Text((Controller.Catalog.Find(row.Item) is { } ownedItem ? Controller.Inventory.Ownership(ownedItem) : row.Location) + "\n" + row.Evidence, 12, "Muted"));
        detail.Children.Add(UI.Button("Open wiki item", () => UI.OpenUrl(row.WikiUrl), true));
        if (row.StepId.Length > 0) detail.Children.Add(FocusedLayout.Menu("Corrections ▾", ("Complete / reopen step", () => { lock (Controller.Sync) { if (!Controller.Profile.CompletedSteps.Add(row.StepId)) Controller.Profile.CompletedSteps.Remove(row.StepId); fingerprint = null; Controller.Save(); Refresh(); } }, "Reversible manual confirmation of the selected quest step. Missing stock never proves a turn-in.")));
        breadcrumb.Margin=new(0,0,0,4);foreach(var child in detail.Children.OfType<FrameworkElement>().Where(c=>c is not Border))child.Margin=new(0,child.Margin.Top,0,child.Margin.Bottom);
        Usability.Tooltips(detail);
    }
    private void ConfigureGoalGroups()
    {
        var expander = new FrameworkElementFactory(typeof(Expander));
        expander.SetBinding(Expander.HeaderProperty, new Binding("Name")); expander.SetValue(Expander.IsExpandedProperty, true);
        expander.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => { var view = (Expander)sender; if (view.DataContext is CollectionViewGroup group) view.IsExpanded = !Controller.Profile.CollapsedGoalGroups.Contains(group.Name?.ToString() ?? ""); }));
        expander.AddHandler(Expander.CollapsedEvent, new RoutedEventHandler((sender, e) => { if (e.OriginalSource == sender && ((Expander)sender).DataContext is CollectionViewGroup group) Controller.Profile.CollapsedGoalGroups.Add(group.Name?.ToString() ?? ""); }));
        expander.AddHandler(Expander.ExpandedEvent, new RoutedEventHandler((sender, e) => { if (e.OriginalSource == sender && ((Expander)sender).DataContext is CollectionViewGroup group) Controller.Profile.CollapsedGoalGroups.Remove(group.Name?.ToString() ?? ""); }));
        expander.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        table.GroupStyle.Add(new GroupStyle { ContainerStyle = new Style(typeof(GroupItem)) { Setters = { new Setter(Control.TemplateProperty, new ControlTemplate(typeof(GroupItem)) { VisualTree = expander }) } } });
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(table, true);
    }
    private void UpdateChips()
    {
        string state = search.Text + zone.SelectedItem + status.SelectedItem;
        if (chips.Tag?.ToString() == state) return; chips.Tag = state; chips.Children.Clear();
        void Chip(string text, Action clear) { var button = UI.Button(text + " ×", clear); button.FontSize = 10; button.Padding = new(4, 1, 4, 1); button.ToolTip = "Clear this filter"; chips.Children.Add(button); }
        if (search.Text.Length > 0) Chip("Search: " + search.Text, () => search.Clear());
        if (zone.SelectedItem?.ToString() != "All zones") Chip(zone.SelectedItem?.ToString() ?? "Zone", () => zone.SelectedItem = "All zones");
        if (status.SelectedItem?.ToString() != "All") Chip(status.SelectedItem?.ToString() ?? "Status", () => status.SelectedItem = "All");
    }
    private async void ScheduleGearSearch()
    {
        gearSearchCancel?.Cancel(); gearSearchCancel = new(); var token = gearSearchCancel.Token;
        string text = gearSearch.Text.Trim(); if (text.Length < 3 || text == lastSearch) return;
        try { await Task.Delay(500, token); lastSearch = text; await Controller.SearchGearAsync(text, token); if (!token.IsCancellationRequested) { gearFingerprint = null; Refresh(); } }
        catch (OperationCanceledException) { }
    }
    private void ShowGearDetail() => ShowSelectedGear();
    private void ConfigureProgressTree()
    {
        var template = new HierarchicalDataTemplate(typeof(QuestProgressNode)) { ItemsSource = new Binding("Children") };
        var row = new FrameworkElementFactory(typeof(StackPanel)); row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Title")); name.SetValue(TextBlock.FontSizeProperty, 12d);
        var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("Foreground")));
        var completed = new DataTrigger { Binding = new Binding("Fulfilled"), Value = true }; completed.Setters.Add(new Setter(TextBlock.TextDecorationsProperty, TextDecorations.Strikethrough)); completed.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("Muted"))); style.Triggers.Add(completed); name.SetValue(StyleProperty, style);
        var state = new FrameworkElementFactory(typeof(TextBlock)); state.SetBinding(TextBlock.TextProperty, new Binding("Status")); state.SetValue(TextBlock.FontSizeProperty, 10d); state.SetValue(MarginProperty, new Thickness(5, 0, 0, 0)); state.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        row.AppendChild(name); row.AppendChild(state); template.VisualTree = row; progressTree.ItemTemplate = template;
        var container = new Style(typeof(TreeViewItem), (Style)Application.Current.FindResource(typeof(TreeViewItem)));
        container.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty, new Binding("IsExpanded") { Mode = BindingMode.TwoWay }));
        container.Setters.Add(new Setter(TreeViewItem.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay }));
        container.Setters.Add(new Setter(ToolTipProperty, new Binding("Node.Detail"))); progressTree.ItemContainerStyle = container;
        VirtualizingPanel.SetVirtualizationMode(progressTree, VirtualizationMode.Recycling); progressTree.ItemsSource = progressNodes;
    }
    private void RefreshSteps()
    {
        FilterProgressQuests();
        var quest = Controller.Catalog.Quests.FirstOrDefault(x => x.Name == questSelection.SelectedItem?.ToString());
        if (quest == null) { selectedStep = null; progressNodes.Clear(); howTo.Children.Clear(); howTo.Children.Add(UI.Text("No quests match. Clear search or turn off Tracked only.", 13, "Accent", true)); stepDetails.Text = "No matching quest"; coverage.Text = ""; stepFingerprint = null; return; }
        coverage.Text = quest.Coverage + $" · wiki revision {quest.Revision}";
        initializing = true; route.SelectedIndex = Controller.Profile.Routes.GetValueOrDefault(quest.Id, "Default") == "Alternative" ? 1 : 0; initializing = false;
        string sig = quest.Id + "|" + Controller.QuestRevision + "|" + Controller.Inventory.Revision + "|" + string.Join(',', Controller.Profile.CompletedSteps) + string.Join(',', Controller.Profile.CompletedQuests) + route.SelectedIndex;
        if (sig == stepFingerprint) return; stepFingerprint = sig;
        var nodes = Controller.Hierarchy(quest);
        var root = new QuestNode { Kind = QuestNodeKind.Quest, Id = quest.Id, QuestId = quest.Id, Name = quest.Class + " / " + quest.Name, Detail = quest.Coverage, Status = Controller.Profile.CompletedQuests.Contains(quest.Id) ? "Completed" : "Incomplete", Children = nodes };
        QuestProgressNode.Sync(progressNodes, [root], Controller);
        var selected = progressNodes.SelectMany(x => x.Descendants()).FirstOrDefault(x => x.IsSelected) ?? progressNodes.FirstOrDefault();
        if (selected != null) { selected.IsSelected = true; selectedStep = selected.Node; }
        ShowProgressDetails();
    }
    private void ShowProgressDetails()
    {
        howTo.Children.Clear();
        if (selectedStep is not { } node) { stepDetails.Text = "Select a quest step"; return; }
        bool selectionChanged = progressDetailNodeId != node.Id; progressDetailNodeId = node.Id;
        stepDetails.Text = node.Status + " · " + node.Name + "\n" + (node.Status switch { "Covered by reward" => "An owned intermediate satisfies these ingredients.", "Owned" => "Ownership is supported by inventory evidence.", "Completed" => "Manual completion is reversible.", _ => "This requirement is incomplete." });
        var quest = Controller.Catalog.Quests.FirstOrDefault(q => q.Id == node.QuestId);
        var detail = QuestAcquisition.Describe(node, quest, Controller.Profile);
        var step = detail.Steps.FirstOrDefault();
        howTo.Children.Add(UI.Text(node.Name, 17, "Accent", true));
        var pin = UI.CommitButton("Add to queue", () => { if (node.ItemKeys.FirstOrDefault() is { } key) PinItem(quest, key); else if (quest != null) PinQuest(quest); });
        pin.ToolTip = node.Kind == QuestNodeKind.Quest ? "Queue this quest and its remaining requirements." : "Queue this item; owned intermediates still cover their ingredients.";
        howTo.Children.Add(pin);
        // Collection locations lead on every sub-item, including owned/covered nodes.
        if (node.Kind != QuestNodeKind.Quest) AddProgressSources(detail.ItemKeys, detail.Steps);
        if (node.Kind == QuestNodeKind.Quest) howTo.Children.Add(UI.Label("QUEST PROGRESSION"));
        else if (detail.Steps.Count > 0) howTo.Children.Add(UI.Label(step?.Outputs.Count > 0 ? "MAKE / OBTAIN THIS ITEM" : "ACQUISITION NOTES"));
        foreach (var instruction in detail.Steps)
        {
            if (node.Kind == QuestNodeKind.Quest) howTo.Children.Add(UI.Text(instruction.Name, 12, "Foreground", true));
            if (instruction.Instructions.Length > 0) howTo.Children.Add(UI.Text(instruction.Instructions, 12));
            if (instruction.Outputs.Count > 0 && instruction.Inputs.Count > 0)
            {
                howTo.Children.Add(UI.Text("Requires: " + string.Join(" · ", instruction.Inputs.Select(i => $"{i.Count} × {Controller.Catalog.FindKey(i.ItemKey)?.Name ?? i.ItemKey}" + (!i.Consumed ? " (retained tool)" : ""))), 11, "Muted"));
            }
            if (instruction.RouteGroup.Length > 0 && quest != null)
            {
                var choices = quest.Steps.Where(s => s.RouteGroup == instruction.RouteGroup).Select(s => s.Route).Distinct().ToArray();
                var selection = UI.Select(choices, Controller.Profile.Routes.GetValueOrDefault(quest.Id + "|" + instruction.RouteGroup, "Default"), 200);
                selection.ToolTip = "Choose one recipe route. Ingredients from the other routes are excluded.";
                selection.SelectionChanged += (_, _) => { Controller.Profile.Routes[quest.Id + "|" + instruction.RouteGroup] = selection.SelectedItem?.ToString() ?? "Default"; Controller.Save(); stepFingerprint = fingerprint = null; Refresh(); };
                howTo.Children.Add(UI.Label("RECIPE ROUTE")); howTo.Children.Add(selection);
            }
            if (instruction.RandomReward) howTo.Children.Add(UI.Text("Random reward · ingredients per attempt; obtaining the desired reward completes this goal.", 12, "Melee"));
            if (instruction.Container.Length > 0) howTo.Children.Add(UI.Text("Container: " + instruction.Container, 12));
            foreach (var condition in instruction.Conditions) howTo.Children.Add(UI.Text(condition.ToString(), 12, "Mint"));
        }
        if (detail.Prerequisites.Count > 0)
        {
            howTo.Children.Add(UI.Label("PREREQUISITES FOR THIS STEP"));
            foreach (var prerequisite in detail.Prerequisites) howTo.Children.Add(UI.Text(prerequisite.Name + "\n" + prerequisite.Instructions, 11));
        }
        if (node.Kind == QuestNodeKind.Quest) AddProgressSources(detail.ItemKeys, detail.Steps);
        AddProgressItemEvidence(detail.ItemKeys);
        if (quest != null) howTo.Children.Add(UI.Button("Open walkthrough", () => UI.OpenUrl(step?.WikiUrl.Length > 0 && node.Kind != QuestNodeKind.Quest ? step.WikiUrl : quest.WikiUrl)));
        Usability.Tooltips(howTo);
        // Each new selection starts at its own sources, rather than an old quest scroll offset.
        if (selectionChanged)
            for (FrameworkElement? parent = howTo; parent != null; parent = parent.Parent as FrameworkElement)
                if (parent is ScrollViewer scroll) { scroll.ScrollToTop(); break; }
    }
    internal bool SelectProgressNode(Func<QuestNode, bool> match)
    {
        var nodes = progressNodes.SelectMany(n => n.Descendants()).ToList(); var selected = nodes.FirstOrDefault(n => match(n.Node));
        if (selected == null) return false;
        foreach (var node in nodes) node.IsSelected = node == selected;
        bool Reveal(QuestProgressNode node)
        {
            if (node == selected) return true;
            if (!node.Children.Any(Reveal)) return false;
            node.IsExpanded = true; return true;
        }
        foreach (var root in progressNodes) Reveal(root);
        selectedStep = selected.Node; ShowProgressDetails(); return true;
    }
    internal StackPanel ProgressAcquisitionPane => howTo;
    private void AddProgressSources(IReadOnlyList<string> keys, IReadOnlyList<QuestStep> instructions)
    {
        foreach (var key in keys)
        {
            var item = Controller.Catalog.FindKey(key); if (item == null) { howTo.Children.Add(UI.Text("Item metadata pending", 12, "Muted")); continue; }
            if (keys.Count > 1) howTo.Children.Add(UI.Label(item.Name));
            if (item.Sources.Count == 0) howTo.Children.Add(UI.Text("Source not documented · see item wiki or walkthrough.", 11, "Muted"));
            foreach (var source in item.Sources)
            {
                string notes = instructions.Any(s => s.Instructions == source.Notes) ? "" : source.Notes;
                var card = UI.Card(UI.Stack(UI.Text(source.Zone.Length > 0 ? source.Zone : source.Method == "Crafting" ? "Crafting recipe" : "Zone unknown", 12, "Foreground", true), UI.Text(source.Mob + (source.Coordinates.Length > 0 ? " · " + source.Coordinates : ""), 12), UI.Text(source.Method + " · " + source.Era + " · " + source.Availability + (notes.Length > 0 ? "\n" + notes : ""), 11, "Muted")));
                card.Tag = source;
                card.ToolTip = source.WikiUrl.Length > 0 ? source.WikiUrl + " · revision " + source.Revision : item.WikiUrl;
                howTo.Children.Add(card);
            }
        }
    }
    private void AddProgressItemEvidence(IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
        {
            var item = Controller.Catalog.FindKey(key); if (item == null) continue;
            howTo.Children.Add(UI.Text(Controller.Inventory.Ownership(item), 11, "Mint"));
            if (item.RequiresSourceIdentity) howTo.Children.Add(UI.Button("Confirm fragment identity…", () => ConfirmFragment(item)));
            foreach (var source in item.Sources.Where(s => s.WikiUrl.Length > 0).DistinctBy(s => s.WikiUrl)) howTo.Children.Add(UI.Button("Source · revision " + source.Revision, () => UI.OpenUrl(source.WikiUrl)));
            if (item.WikiUrl.Length > 0) howTo.Children.Add(UI.Button("Open item wiki", () => UI.OpenUrl(item.WikiUrl)));
        }
    }
    private void ExpandProgress(bool expand)
    { foreach (var node in progressNodes.SelectMany(x => x.Descendants())) node.IsExpanded = expand; }
    private void ConfirmFragment(ItemDefinition item)
    {
        var candidates = Controller.Inventory.Entries.Where(e => e.Count == 1 && item.Aliases.Prepend(item.Name).Any(n => Identity.Key(n) == Identity.Key(e.Name))).ToList();
        if (candidates.Count == 0) { MessageBox.Show(this, "No matching inventory entry is present in the latest snapshot.", "Fragment identity"); return; }
        var panel = new FrostWindow(Controller, "Confirm fragment", "Choose the exact inventory location after inspecting its lore. Clearing is reversible.", 650, 440);
        var list = new ListBox { ItemsSource = candidates, SelectedIndex = 0, MaxHeight = 200 };
        void Set(bool clear)
        {
            if (list.SelectedItem is not InventoryEntry entry) return;
            var key = QuestRecipes.InventoryIdentity(entry);
            if (clear) Controller.Profile.ItemIdentityConfirmations.Remove(key); else Controller.Profile.ItemIdentityConfirmations[key] = item.Key;
            QuestRecipes.ApplyIdentities(Controller.Profile, Controller.Inventory); Controller.Save(); stepFingerprint = fingerprint = null; Refresh(); panel.Close();
        }
        panel.Body.Children.Add(UI.Stack(UI.Text(item.Name, 15, "Foreground", true), list, UI.Row(UI.Button("Confirm this fragment", () => Set(false)), UI.Button("Clear confirmation", () => Set(true))), UI.Text("Confirmation applies to this name/ID at this location. Moving it requires confirmation at its new location. Identical stacked fragments remain ambiguous; separate them before confirming.", 12, "Muted")));
        Native.ShowPanel(panel);
    }
    private void ToggleStep() { if (selectedStep is not { StepId.Length: > 0 } node) return; lock (Controller.Sync) { if (!Controller.Profile.CompletedSteps.Add(node.StepId)) Controller.Profile.CompletedSteps.Remove(node.StepId); Controller.Save(); stepFingerprint = fingerprint = null; Refresh(); } }
    private void ToggleQuest() { lock (Controller.Sync) { var q = Controller.Catalog.Quests.FirstOrDefault(x => x.Name == questSelection.SelectedItem?.ToString()); if (q == null) return; if (!Controller.Profile.CompletedQuests.Add(q.Id)) Controller.Profile.CompletedQuests.Remove(q.Id); Controller.Save(); fingerprint = stepFingerprint = null; Refresh(); } }
    private void OpenQuest() { var q = Controller.Catalog.Quests.FirstOrDefault(x => x.Name == questSelection.SelectedItem?.ToString()); if (q != null) UI.OpenUrl(q.WikiUrl); }
    private void Pin(ObjectiveRow row)
    {
        var goal = new QueuedGoal { QuestId = row.QuestId, ItemKey = row.Key, Name = row.Item };
        if (!Controller.Profile.GoalQueue.Any(g => g.Id == goal.Id)) Controller.Profile.GoalQueue.Add(goal);
        var quest = Controller.Catalog.Quests.FirstOrDefault(q => q.Id == row.QuestId); if (quest != null) QuestTracking.Track(Controller.Profile, quest, true);
        Controller.Save(); queueSignature = ""; RefreshQueue();
    }
    private string queueInputRevision = "";
    private void RefreshQueue()
    {
        string input = $"{Controller.QuestRevision}|{Controller.Inventory.Revision}|{Controller.Profile.Zone}|{queueSearch.Text}|{queueState.SelectedItem}|{queueSignature.Length}";
        if (!Controller.Demo && queueInputRevision == input) return;
        queueInputRevision = input;
        var all = ObjectiveQueue.Build(Controller.Profile, Controller.Catalog, Controller.Objectives, Controller.Inventory);
        var rows = all.Where(e => (e.Display + string.Join(' ', e.Ingredients.Select(i => i.Item + " " + i.Zone))).Contains(queueSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase) && (queueState.SelectedItem?.ToString() switch { "Pending" => e.Status != "Paused" && e.Ingredients.Any(i => i.Remaining > 0), "Satisfied" => e.Status is "Owned" or "Satisfied / covered", "Paused" => e.Status == "Paused", _ => true }))
            .OrderBy(e => e.Status != "Paused" && e.Ingredients.Any(i => i.Remaining > 0 && QuestPlanner.InZone(i, Controller.Profile.Zone, Controller.Profile.CurrentExpansion)) ? 0 : 1).ToList();
        string key = "queue:" + Controller.QuestRevision + "|" + Controller.Inventory.Revision + "|" + Controller.Profile.Zone + "|" + queueSearch.Text + queueState.SelectedItem + string.Join('|', rows.Select(r => r.Goal.Id + r.Display + string.Join(';', r.Ingredients.Select(i => i.Key + i.Quantity)))); if (queueSignature == key) return; queueSignature = key;
        queueNoteState.Text = all.Count == 0 ? "Your queue is empty. Add a quest or item to get started." : rows.Count == 0 ? "No queued goals match these filters." : "";
        queueNoteState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed; queueEmptyActions.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        queueSummary.Text = $"{rows.Count} of {all.Count} goals · local objectives first, then saved order";
        string? selected = (queueList.SelectedItem as QueueEntry)?.Goal.Id; double offset = UI.FindScroll(queueList)?.VerticalOffset ?? 0;
        queueList.ItemsSource = rows; queueList.SelectedItem = rows.FirstOrDefault(r => r.Goal.Id == selected) ?? rows.FirstOrDefault();
        UI.FindScroll(queueList)?.ScrollToVerticalOffset(offset); ShowQueueInspector();
    }
    private void MoveQueue(int change)
    {
        if (queueList.SelectedItem is not QueueEntry row) return;
        var goals = Controller.Profile.GoalQueue; int from = goals.FindIndex(g => g.Id == row.Goal.Id), to = from + change;
        if (from < 0 || to < 0 || to >= goals.Count) return; var item = goals[from]; goals.RemoveAt(from); goals.Insert(to, item); Controller.Save(); queueSignature = ""; RefreshQueue();
    }
    private void RemovePin() { if (queueList.SelectedItem is QueueEntry row) { Controller.Profile.GoalQueue.RemoveAll(g => g.Id == row.Goal.Id); Controller.Save(); queueSignature = ""; RefreshQueue(); } }
    private void BrowsePin() { if (queueList.SelectedItem is QueueEntry row) BrowseQueueEntry(row, row.Objective); }
    private void ConfigureQueueDrag()
    {
        Point start = default; queueList.AllowDrop = true;
        queueList.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(queueList);
        queueList.PreviewMouseMove += (_, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && (e.GetPosition(queueList) - start).Length > 8 && queueList.SelectedItem is QueueEntry row) { DragDrop.DoDragDrop(queueList, row.Goal.Id, DragDropEffects.Move); e.Handled = true; } };
        queueList.Drop += (_, e) =>
        {
            string? id = e.Data.GetData(typeof(string)) as string; var target = ItemsControl.ContainerFromElement(queueList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (id == null || target?.Content is not QueueEntry row) return;
            var goals = Controller.Profile.GoalQueue; var item = goals.FirstOrDefault(g => g.Id == id); if (item == null || item.Id == row.Goal.Id) return;
            goals.Remove(item); goals.Insert(goals.FindIndex(g => g.Id == row.Goal.Id), item); Controller.Save(); queueSignature = ""; RefreshQueue(); e.Handled = true;
        };
    }
    private void AddWishlist()
    {
        var form = new EntryDialog("Add a future gear goal", [("Item name", gearTable.SelectedItem is GearRow g ? Identity.Canonical(g.Name) : ""), ("Quantity", "1"), ("Loadout", gearTarget.SelectedItem?.ToString() ?? "Player"), ("Zone", ""), ("Mob / source", ""), ("Notes", "")]) { Owner = this };
        if (form.ShowDialog() != true) return;
        if (string.IsNullOrWhiteSpace(form.Values[0]) || !int.TryParse(form.Values[1], out var n) || n < 1) { MessageBox.Show("Enter an item name and a positive quantity."); return; }
        lock (Controller.Sync) { Controller.Profile.Wishlist.Add(new() { Name = form.Values[0], Quantity = n, Loadout = form.Values[2], Zone = form.Values[3], Source = form.Values[4], Notes = form.Values[5] }); Controller.QueueMetadata(form.Values[0]); Controller.Save(); fingerprint = gearFingerprint = null; Refresh(); }
    }
    private void ToggleWishlist() { lock (Controller.Sync) { int i = wishlist.SelectedIndex; if (i < 0 || i >= Controller.Profile.Wishlist.Count) return; var goal = Controller.Profile.Wishlist[i]; goal.Completed = !goal.Completed; Controller.Save(); gearFingerprint = fingerprint = null; Refresh(); } }
    private void RemoveWishlist() { lock (Controller.Sync) { int i = wishlist.SelectedIndex; if (i < 0 || i >= Controller.Profile.Wishlist.Count) return; Controller.Profile.Wishlist.RemoveAt(i); Controller.Save(); gearFingerprint = fingerprint = null; Refresh(); } }
    private void EditWeights()
    {
        var key = preset.SelectedItem?.ToString() ?? "Pet damage"; var weights = Controller.Profile.Weights[key];
        var form = new EntryDialog("Edit " + key + " weights", [("Stat=weight, comma separated (use INT or WIS for caster)", string.Join(", ", weights.Select(x => x.Key + "=" + x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))))]) { Owner = this };
        if (form.ShowDialog() != true) return;
        var parsed = new Dictionary<string, double>();
        foreach (var pair in form.Values[0].Split(',', StringSplitOptions.RemoveEmptyEntries)) { var parts = pair.Trim().Split('='); if (parts.Length != 2 || !double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) { MessageBox.Show("Use entries such as AC=3, HP=0.1, Ratio=100."); return; } parsed[parts[0].Trim()] = value; }
        lock (Controller.Sync) { Controller.Profile.Weights[key] = parsed; Controller.Save(); gearFingerprint = null; Refresh(); }
    }
    private void EditEquipment()
    {
        var pet = gearTarget.SelectedItem?.ToString() == "Pet"; if (pet) { ShowView(TrackerTab.Pet); SelectPetDestination(PetInventoryDestination.Confirmed); return; } var loadout = Controller.Profile.ActiveLoadout;
        var entries = pet ? loadout.PetEquipment : loadout.Equipment;
        var form = new EntryDialog("Confirm " + (pet ? "pet" : "Self") + " equipment", [("One Slot=Item name +rank per line (Primary, Secondary, Chest, Ear, Finger…)", string.Join("\n", entries.Select(x => x.Location + "=" + x.Name)))]) { Owner = this };
        if (form.ShowDialog() != true) return;
        var result = new List<InventoryEntry>();
        foreach (var row in form.Values[0].Split('\n', StringSplitOptions.RemoveEmptyEntries))
        { var parts = row.Split('=', 2); if (parts.Length != 2) { MessageBox.Show("Use one Slot=Item name per line."); return; } var metadata = Controller.Catalog.Find(parts[1].Trim()); result.Add(new() { Location = parts[0].Trim(), Name = parts[1].Trim(), GameId = metadata?.GameId ?? 0, Count = 1 }); Controller.QueueMetadata(parts[1]); }
        lock (Controller.Sync) { if (pet) { loadout.PetEquipment = result; loadout.ConfirmedPetInventory = null; loadout.PetEquipmentConfirmedAt = DateTime.Now; } else { loadout.Equipment = result; loadout.EquipmentPositions = EquipmentPositions.FromEntries(result); } Controller.Save(); gearFingerprint = null; Refresh(); }
    }
}
public sealed class EntryDialog : FrostDialog
{
    private readonly List<TextBox> inputs = [];
    public List<string> Values => inputs.Select(x => x.Text.Trim()).ToList();
    public EntryDialog(string title, IEnumerable<(string Label, string Value)> fields, string? description = null, string[]? hints = null) : base(title, 560, 360)
    {
        var fieldList = fields.ToList(); Height = Math.Clamp(150 + fieldList.Sum(f => f.Label.Contains("per line") || f.Label.Contains("comma separated") ? 220 : 54), 220, 540); MinHeight = 200;
        var heading = UI.Text(title, 18, "Accent", true); heading.ToolTip = description ?? title;
        var root = UI.Stack(); root.Margin = new(0); if (description != null) root.Children.Add(UI.Text(description, 12, "Muted")); int fieldIndex = 0;
        foreach (var (label, value) in fieldList)
        { var caption = UI.Label(label); var help = hints?.ElementAtOrDefault(fieldIndex++) ?? "Enter " + label.ToLowerInvariant() + "."; caption.ToolTip = help; root.Children.Add(caption); var input = UI.Input(value); input.ToolTip = help; if (label.Contains("per line") || label.Contains("comma separated")) { input.AcceptsReturn = true; input.Height = 190; input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; } inputs.Add(input); root.Children.Add(input); }
        Editor.Children.Add(root); var save = UI.Button("Save", () => DialogResult = true); save.IsDefault = true; save.ToolTip = "Validate and save these entries. Cancel discards your changes."; Actions.Children.Add(save); Actions.Children.Add(UI.Button("Cancel", Close));
    }
}
