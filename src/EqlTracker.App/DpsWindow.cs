using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed record LoadoutRow(string Loadout, string Pet, string Weapons, int Samples, double Seconds, double PetDps, double PlayerDps, double CombinedDps) { public string Buffs { get; init; } = ""; }
public sealed partial class DpsWindow : FrostWindow
{
    private readonly ComboBox scope = UI.Select(["Fight", "Session", "History"], "Fight", 130);
    private readonly ComboBox owner = UI.Select(["Pet", "Self", "Combined", "Unknown"], "Pet", 130);
    private readonly ComboBox target = UI.Select(["All targets"], "All targets", 200);
    private readonly ComboBox kind = UI.Select(["All damage", "Melee", "Spell", "DoT"], "All damage", 145);
    private readonly TextBox ability = UI.Input("", 180);
    private readonly CheckBox critical = new() { Content = "Critical only", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox from = UI.Input("", 190), until = UI.Input("", 190);
    private readonly AnimatedNumber player = new() { Format = "N1", FontSize = 17 }, pet = new() { Format = "N1", FontSize = 17 }, combined = new() { Format = "N1", FontSize = 17 }, max = new() { FontSize = 17 };
    private string visualContext = "";
    private readonly TextBlock filterError = UI.Text("", 11, "Violet");
    private readonly DataGrid breakdown;
    private readonly HealingPanel healing;
    private readonly DamageRowsView damageRows = new();
    private readonly DataGrid targetTable;
    private readonly TextBlock peakInfo = UI.Text("Select an ability to mark its Peak hit on the timeline.", 11, "Melee");
    private DamageRow? selectedAbility;
    private readonly ComboBox sort = UI.Select(["Damage", "Hits", "Average", "DPS"], "Damage", 120);
    private readonly DataGrid comparisons;
    private readonly DataGrid history = UI.Table(("Started", "Started", 125, "MMM/dd HH:mm"), ("Targets", "Targets", 180, null), ("Zone", "Zone", 140, null), ("Seconds", "Duration", 70, "N0"), ("Damage", "Total", 80, "N0"), ("DPS", "Rate", 70, "N1"), ("Pet", "Pet", 85, null));
    private readonly Timeline timeline = new() { Height = 100, Margin = new(0, 3, 0, 6) };
    private string selectionSignature = "";
    private readonly TextBlock historyInfo = UI.Text("Select a saved fight to inspect its spells and strikes.", 12, "Muted");
    private bool updating; private bool manualDateEdit;
    private readonly DisplayCadence dpsCadence = new();
    private readonly TabControl tabs = new() { MinHeight = 100 };
    private DpsViewState? lastPresented; private bool lastHealingShown;
    private string comparisonKey = "";
    private Button dateRange = null!; private StackPanel dates = null!; private Grid filterSlot = null!, metricSlot = null!, statusSlot = null!;
    private CombatLabMode mode;
    private CombatLabTab SelectedTab => tabs.SelectedItem is TabItem { Tag: CombatLabTab id } ? id : CombatLabTab.Breakdown;
    private readonly Dictionary<CombatLabTab, TabItem> tabItems = [];
    private Button damageMode = null!, healingMode = null!;
    private readonly AbilityInspector inspector = new();
    private readonly CombatGroupPanel group;
    private UIElement damageFilters = null!, damageMetrics = null!;
    private readonly ComboBox peaks = UI.Select(["Off", "Highest", "All"], "Highest", 90);
    private CombatInspectionSnapshot? inspected;
    private string selectedInspection = "";
    private readonly Dictionary<CombatLabMode, string> modeSelections = [];
    public DpsWindow(TrackerController controller) : base(controller, "Combat Lab", "Measured damage, spell by spell and strike by strike.", 1170, 740, surface: WindowSurface.Theme)
    {
        Appearance.RaisedButtons(Body); CatalystHeading.Apply(HeadingTitle, "Combat Lab");
        mode = controller.Profile.CombatLab.Mode;
        healing = new(controller); group = new(controller); group.PeriodSelected += FollowGroup;
        ConfigureScopes();
        scope.ToolTip = "Fight shows the latest or explicitly pinned encounter. Session selects a saved login session, including offline data; rates use summed encounter durations.";
        sort.ToolTip = "Order damage abilities by total damage, landed hits, average landed hit or DPS. Filters and totals stay unchanged.";
        owner.ToolTip = "Personal damage attribution: Self, owned Pet, Combined, or unassigned. Group has a separate member projection.";
        target.ToolTip = "Restrict personal and group damage to this logged outgoing target name; identical NPC names remain grouped.";
        kind.ToolTip = "Filter melee, direct spell and damage-over-time events without changing the encounter duration.";
        ability.ToolTip = "Filter damage abilities by name. Healing keeps an independent spell search.";
        critical.ToolTip = "Include only logged critical hits; this is not a cast success or resist-rate denominator.";
        pet.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); player.SetResourceReference(TextBlock.ForegroundProperty, "Mint"); max.SetResourceReference(TextBlock.ForegroundProperty, "Violet");
        breakdown = UI.Table(("Ability", "Ability", 140, null), ("Damage", "Damage", 75, "N0"), ("DPS", "Dps", 60, "N1"), ("Hits", "Hits", 50, "N0"), ("Max", "MaxHit", 60, "N0"), ("Crit %", "CriticalPercent", 60, "0.0'%'"), ("Average", "Average", 70, "N1"), ("Misses", "Misses", 60, null), ("Share", "Percent", 60, "0.0'%'"));
        TableLayout.SetKey(breakdown, "combat-metrics-v1");
        foreach (var column in breakdown.Columns) { string key = "combat-metrics-v1:" + column.SortMemberPath; var prior = controller.Profile.ColumnWidths.FirstOrDefault(p => p.Key.Contains(":Ability|Kind|Damage|Dps|") && p.Key.EndsWith(":" + column.SortMemberPath)); if (!controller.Profile.ColumnWidths.ContainsKey(key) && prior.Value > 0) controller.Profile.ColumnWidths[key] = prior.Value; }
        TableLayout.SetAutoMaximum(breakdown, breakdown.Columns[0], 160);
        targetTable = UI.Table(("Target", "Target", 0, null), ("Dmg", "Damage", 75, "N0"), ("DPS", "Dps", 55, "N1"), ("Share", "Percent", 55, "0'%'")); targetTable.Columns[0].MinWidth = 90; targetTable.Columns[1].MinWidth = 70;
        var targetStyle = new Style(typeof(DataGridRow)); targetStyle.Setters.Add(new Setter(ToolTipProperty, new System.Windows.Data.Binding("Target"))); targetTable.RowStyle = targetStyle;
        var details = new FrameworkElementFactory(typeof(TextBlock)); details.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Details")); details.SetValue(TextBlock.FontSizeProperty, 11d); details.SetValue(TextBlock.MarginProperty, new Thickness(4)); details.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); details.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        targetTable.RowDetailsTemplate = new DataTemplate { VisualTree = details }; targetTable.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.VisibleWhenSelected;
        damageRows.AbilitySelected += row => { if (row != null && !updating) { selectedAbility = row; SelectInspection(row); RefreshPeak(); } };
        breakdown.SelectionChanged += (_, _) => { if (!updating && breakdown.SelectedItem is DamageRow row) { selectedAbility = row; SelectInspection(row); if (mode == CombatLabMode.Damage) RefreshPeak(); else MarkHealing(row); } };
        targetTable.SizeChanged += (_, _) => { foreach (var column in targetTable.Columns.Skip(2)) column.Visibility = targetTable.ActualWidth < 300 ? Visibility.Collapsed : Visibility.Visible; };
        comparisons = UI.Table(("Loadout", "Loadout", 0, null), ("Pet", "Pet", 110, null), ("Weapons", "Weapons", 190, null), ("Fights", "Samples", 65, null), ("Seconds", "Seconds", 85, "N0"), ("Pet DPS", "PetDps", 100, "N1"), ("Self DPS", "PlayerDps", 100, "N1"), ("Total DPS", "CombinedDps", 100, "N1"));
        var controls = new WrapPanel(); foreach (var c in new UIElement[] { UI.Field("Actor", owner), UI.Field("Target", target), UI.Field("Damage type", kind), UI.Field("Spell / strike search", ability), UI.ActionField(critical) }) controls.Children.Add(c); damageFilters = controls;
        var dateFlow = new WrapPanel(); foreach (var item in new UIElement[] { UI.Field("From · local date/time", from), UI.Field("Until · local date/time", until), UI.ActionField(UI.Button("Clear dates", () => { from.Text = until.Text = ""; })) }) dateFlow.Children.Add(item); dates = UI.Stack(dateFlow); dates.Visibility = Visibility.Collapsed;
        dateRange = UI.Button("Date range", () => { dates.Visibility = dates.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; UpdateDateState(); });
        var tools = new ContextMenu(); var corrections = new MenuItem { Header = "Corrections & sample setup" }; tools.Items.Add(corrections);
        foreach (var (label, help, action) in new (string, string, Action)[] { ("Tag loadout", "Start a new combat sample segment with loadout, weapon and buff notes. Ends the current segment.", TagLoadout), ("Pet identity", "Correct pet ownership over a selected time range; retained damage and healing are reattributed.", CorrectPet), ("Class evidence", "Correct the recorded classes over a selected session interval without changing combat records.", CorrectClassEvidence), ("Group roster", "Correct group membership and timing coverage without adding group damage to personal totals.", CorrectGroup) })
        { var item = new MenuItem { Header = label, ToolTip = help }; item.Click += (_, _) => action(); corrections.Items.Add(item); }
        var exportMenu = new MenuItem { Header = "Export" }; tools.Items.Add(exportMenu);
        foreach (var (label, whole) in new[] { ("Export encounter", false), ("Export scope", true) }) { var entry = new MenuItem { Header = label, ToolTip = whole ? "Export the current scope and filters, preserving existing CSV and JSON totals." : "Export the selected encounter snapshot." }; entry.Click += (_, _) => Export(whole); exportMenu.Items.Add(entry); }
        var contribution = new MenuItem { Header = "Show contribution %", IsCheckable = true, IsChecked = controller.Profile.CombatLab.ShowContribution, StaysOpenOnClick = true, ToolTip = "Show each ability’s share of filtered damage or effective healing beside its total. Wash lengths remain relative to the largest row." }; tools.Items.Add(contribution); contribution.Click += (_, _) => { controller.Profile.CombatLab.ShowContribution = contribution.IsChecked; damageRows.ShowContribution = contribution.IsChecked; healing.SetContribution(contribution.IsChecked); controller.Save(); };
        damageRows.ShowContribution = controller.Profile.CombatLab.ShowContribution; healing.SetContribution(controller.Profile.CombatLab.ShowContribution);
        var toolsButton = OverlayMenus.Button(tools, "Exports, display options and corrections", "Tools ▾", timedDismissal: false); toolsButton.Width = double.NaN; toolsButton.Padding = new(6, 1, 6, 1);
        var primaryActions = new WrapPanel(); foreach (var action in new UIElement[] { UI.Field("Time scope", scope), UI.Field("Session", sessionPicker), UI.ActionField(classButton), UI.Field("Sort by", sort), UI.Field("Peaks", peaks), UI.ActionField(latestFight) }) primaryActions.Children.Add(action);
        var actions = new CombatActionRow(primaryActions, UI.Row(UI.ActionField(dateRange), UI.ActionField(toolsButton)));
        foreach (var button in new[] { dateRange, toolsButton }) { button.FontSize = 11; button.Padding = new(5, 2, 5, 2); }
        peaks.SelectedItem = controller.Profile.CombatLab.Peaks.ToString(); timeline.PeakMode = controller.Profile.CombatLab.Peaks;
        peaks.ToolTip = "Off hides chart peaks; Highest marks each visible actor's maximum; All includes local highs. Flat peaks use their earliest sample. Selected ability Peak hit stays separate.";
        peaks.SelectionChanged += (_, _) => { if (Enum.TryParse<ChartPeakMode>(peaks.SelectedItem?.ToString(), out var value)) { timeline.PeakMode = value; controller.Profile.CombatLab.Peaks = value; controller.Save(); } };
        var metrics = new Grid(); var damageCards=new CombatSummaryCards(ActorMetric("TOTAL", combined, "Combined"), ActorMetric("SELF", player, "Player"), ActorMetric("PET", pet, "Pet"), ActorMetric("MAX HIT", max, "Max")); metrics.Children.Add(damageCards); damageMetrics=metrics;
        var targetPanel = new Grid(); targetPanel.RowDefinitions.Add(new() { Height = GridLength.Auto }); targetPanel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); targetPanel.Children.Add(UI.Label("Targets · select for details")); Grid.SetRow(targetTable, 1); targetPanel.Children.Add(targetTable);
        var combatSplit = UI.Split(damageRows, targetPanel, 330, controller.Profile, "combat", 230, 180);
        var breakdownHost = new Grid(); breakdownHost.Children.Add(combatSplit); breakdownHost.Children.Add(healing);
        void Tab(CombatLabTab id, string title, UIElement content) { var tab = new TabItem { Header = title, Tag = id, Content = content }; tabItems[id] = tab; tabs.Items.Add(tab); }
        Tab(CombatLabTab.Breakdown, "BREAKDOWN", breakdownHost); Tab(CombatLabTab.Metrics, "METRICS", new ResponsiveCombatSplit(breakdown, inspector, controller.Profile, "combat-inspector")); Tab(CombatLabTab.Group, "GROUP/RAID", group);
        TableLayout.SetKey(history, "combat-history-v2");
        var historySplit = new ResponsiveCombatSplit(history, HistoryActions(), controller.Profile, "combat-history-v2");
        comparisons.Columns[1].MinWidth = 80; history.Columns[6].MinWidth = 80;
        comparisons.SelectionChanged += (_, _) => UpdateComparisonDetail();
        Tab(CombatLabTab.History, "ENCOUNTER HISTORY", historySplit);
        Tab(CombatLabTab.Comparison, "LOADOUT COMPARISON", new ResponsiveCombatSplit(ComparisonTable(), UI.Card(UI.Scroll(comparisonDetails)), controller.Profile, "combat-comparison-v2"));
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        filterSlot = new Grid(); filterSlot.Children.Add(controls); filterSlot.Children.Add(healing.FilterControls);
        var filters = new Expander { Header = "Filters", Content = filterSlot, IsExpanded = true, Margin = new(0, 0, 0, 3), ToolTip = "Damage and Healing retain independent filters. Scope and date range are shared." };
        metricSlot = new Grid(); metricSlot.Children.Add(metrics); metricSlot.Children.Add(healing.MetricCards);
        statusSlot = new Grid(); var statsCard=StatsCard(); damageCards.Children.Add(statsCard); healing.SetStatsCard(StatsCardForHealing());
        peakInfo.Height = 14; peakInfo.TextTrimming = TextTrimming.CharacterEllipsis; peakInfo.TextWrapping = TextWrapping.NoWrap;
        damageMode = CombatModeButtons.Create("Damage", "Physical", () => SetMode(CombatLabMode.Damage)); healingMode = CombatModeButtons.Create("Healing", "Healing", () => SetMode(CombatLabMode.Healing));
        damageMode.ToolTip = "Inspect damage with its saved filters and selected subtab. Personal totals do not include group members."; healingMode.ToolTip = "Inspect restored HP with independent filters. Healing never extends encounter timing.";
        var modes = UI.Row(damageMode, healingMode); modes.Margin = new(0, 0, 0, 3);
        var overview = new AdaptiveCombatOverview(UI.Stack(metricSlot, timeline), controller);
        var top = UI.Stack(modes, actions, dates, filterError, filters, overview); root.Children.Add(top); Grid.SetRow(tabs, 1); root.Children.Add(tabs);
        healing.Presented += view => { healingStats.SetHealing(view,healing.StatusLine.Text);if (mode == CombatLabMode.Healing) { timeline.Set(view.Chart); timeline.Highlight = null; peakInfo.Text = "Select a healing ability to mark its largest heal."; } };
        healing.AbilitySelected += row => { if (row != null) { selectedAbility = row; MarkHealing(row); } };
        Body.Children.Add(root);
        bool wasSmall = false, priorExpanded = true;
        SizeChanged += (_, _) => { overview.Adapt(ActualHeight < 500); bool small = ActualHeight < 580; bool narrow = ActualWidth < 900; tabs.MinHeight = small ? 0 : 100; if (small && !wasSmall) { priorExpanded = filters.IsExpanded; filters.IsExpanded = false; } else if (!small && wasSmall) filters.IsExpanded = priorExpanded; wasSmall = small; timeline.Height = small ? 48 : 96; peakInfo.Visibility = small ? Visibility.Collapsed : Visibility.Visible; Subtitle.Visibility = small ? Visibility.Collapsed : Visibility.Visible; };
        bool narrowFilters=false, restoreFilters=true;
        root.SizeChanged+=(_,_)=>{bool next=root.ActualWidth<900;if(next==narrowFilters)return;narrowFilters=next;if(next){restoreFilters=filters.IsExpanded;filters.IsExpanded=false;}else filters.IsExpanded=restoreFilters;};
        foreach (var c in new[] { scope, owner, target, kind, sort }) c.SelectionChanged += (_, _) => { if (!updating) { if (ReferenceEquals(c,scope)&&Controller.Profile.Meter.GroupPeriodId.Length>0) { Controller.ExitGroupPeriod(true);updating=true;from.Text=until.Text="";updating=false; } ChangeFilters(); } };
        foreach (var c in new[] { ability, from, until }) c.TextChanged += (_, _) => { if (!updating) { if (!ReferenceEquals(c,ability)) manualDateEdit=true; ChangeFilters(); } };
        critical.Click += (_, _) => ChangeFilters(); history.SelectionChanged += (_, _) => { if (!updating) UpdateHistoryDetail(); };
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, tabs) && !updating) { if (mode == CombatLabMode.Damage) controller.Profile.CombatLab.DamageTab = SelectedTab; else controller.Profile.CombatLab.HealingTab = SelectedTab; lastPresented = null; inspected = null; controller.Save(); Refresh(); } };
        Body.IsVisibleChanged += (_, _) => { controller.HealingVisible = IsVisible && Body.IsVisible && mode == CombatLabMode.Healing && !IsCollapsed; };
        IsVisibleChanged += (_, _) => { controller.HealingVisible = IsVisible && mode == CombatLabMode.Healing && !IsCollapsed; };
        Deactivated += (_, _) => timeline.ClearCursor();
        foreach (var reading in new[] { pet, player, combined }) reading.ToolTip = DisplayCadence.DpsHelp;
        void ModeVisibility() { combatSplit.Visibility = mode == CombatLabMode.Damage ? Visibility.Visible : Visibility.Collapsed; healing.Visibility = mode == CombatLabMode.Healing ? Visibility.Visible : Visibility.Collapsed; }
        modeChanged = ModeVisibility; SetMode(mode, false); Refresh();
    }
    private Action? modeChanged;
    internal CombatLabMode Mode => mode;
    internal CombatLabTab ActiveTab => SelectedTab;
    internal void ShowView(CombatLabMode value, CombatLabTab tab) { SetMode(value, false); tabs.SelectedItem = tabItems[tab]; lastPresented = null; Refresh(); }
    private void SetMode(CombatLabMode value, bool save = true)
    {
        mode = value; Controller.Profile.CombatLab.Mode = value; bool heals = value == CombatLabMode.Healing;
        damageFilters.Visibility = damageMetrics.Visibility = heals ? Visibility.Collapsed : Visibility.Visible;
        healing.FilterControls.Visibility = healing.MetricCards.Visibility = healing.StatusLine.Visibility = heals ? Visibility.Visible : Visibility.Collapsed;
        // The sort field belongs to Damage. Healing owns its independent sort field.
        if (sort.Parent is UIElement field) field.Visibility = heals ? Visibility.Collapsed : Visibility.Visible;
        UI.SetPressed(damageMode, !heals); UI.SetPressed(healingMode, heals); modeChanged?.Invoke();
        updating = true; tabs.SelectedItem = tabItems.GetValueOrDefault(heals ? Controller.Profile.CombatLab.HealingTab : Controller.Profile.CombatLab.DamageTab) ?? tabItems[CombatLabTab.Breakdown]; updating = false;
        Controller.HealingVisible = heals && IsVisible && !IsCollapsed; lastPresented = null; inspected = null; selectedInspection = modeSelections.GetValueOrDefault(mode, ""); inspector.Set(null); timeline.Highlight = null; timeline.ClearCursor();
        breakdown.Columns[1].Header = heals ? "Healing" : "Damage"; breakdown.Columns[2].Header = heals ? "HPS" : "DPS"; breakdown.Columns[3].Header = heals ? "Heals" : "Hits";
        if (save) Controller.Save(); Refresh();
    }
    private void MarkHealing(DamageRow row) { if (row.PeakTime is { } time) { timeline.Highlight = new("", row.Ability, "Healing", row.MaxHit, time, ""); peakInfo.Text = $"Largest heal · {row.Ability} · {row.MaxHit:N0} HP · {time:HH:mm:ss}"; } }
    private void SelectInspection(DamageRow row)
    {
        selectedInspection = CombatInspection.Key(row); modeSelections[mode] = selectedInspection; if (row.Kind != "Melee") Controller.RequestSpellReference(row.Ability);
        if (inspected?.Abilities.TryGetValue(selectedInspection, out var detail) == true) inspector.Set(detail);
    }
    private void RefreshInspection()
    {
        if (owner.Parent is UIElement actorField) actorField.Visibility = SelectedTab == CombatLabTab.Group ? Visibility.Collapsed : Visibility.Visible;
        healing.SetGroupModeIfChanged(SelectedTab == CombatLabTab.Group);
        if (SelectedTab is not (CombatLabTab.Metrics or CombatLabTab.Group)) return;
        var snapshot = Controller.InspectionView(mode == CombatLabMode.Healing, SelectedTab == CombatLabTab.Group); if (snapshot == null || ReferenceEquals(snapshot, inspected)) return; inspected = snapshot;
        if (SelectedTab == CombatLabTab.Group) { group.Set(snapshot, mode == CombatLabMode.Healing); healing.SetGroupChoices(snapshot); return; }
        UI.SetRows(breakdown, snapshot.Rows.ToList());
        if (selectedInspection.Length > 0 && snapshot.Abilities.TryGetValue(selectedInspection, out var selected)) { inspector.Set(selected); breakdown.SelectedItem = breakdown.Items.Cast<DamageRow>().FirstOrDefault(r => CombatInspection.Key(r) == selectedInspection); }
        else { inspector.Set(null); if (breakdown.SelectedItem == null && breakdown.Items.Count > 0) breakdown.SelectedIndex = 0; if (breakdown.SelectedItem is DamageRow row) SelectInspection(row); }
    }
    private static Border Metric(string label, TextBlock number) { var card = UI.Card(UI.Stack(UI.Label(label), number), new(0, 0, 4, 2)); card.Padding = new(4, 1, 4, 1); return card; }
    private void UpdateDateState() => UI.SetPressed(dateRange, dates.Visibility == Visibility.Visible || from.Text.Length > 0 || until.Text.Length > 0);
    private void ChangeFilters()
    {
        DateTime? begin = null, end = null; filterError.Text = "";
        if (from.Text.Length > 0) { if (DateTime.TryParse(from.Text, out var dt)) begin = dt; else filterError.Text = "From date is invalid."; }
        if (until.Text.Length > 0) { if (DateTime.TryParse(until.Text, out var dt)) end = dt; else filterError.Text = "Until date is invalid."; }
        if (begin > end) filterError.Text = "From must be before Until.";
        UpdateDateState(); if (filterError.Text.Length > 0) return;
        lock (Controller.Sync)
        {
            var p = Controller.Profile.Meter; p.Scope = VisibleScope();
            if (mode != CombatLabMode.Healing) {
            p.Actor = owner.SelectedItem?.ToString() == "Self" ? "Player" : owner.SelectedItem?.ToString() ?? "Pet"; p.Scope = VisibleScope(); p.Sort = sort.SelectedItem?.ToString() ?? "Damage";
            p.Target = target.SelectedItem?.ToString() == "All targets" ? "" : target.SelectedItem?.ToString() ?? "";
            p.Kind = kind.SelectedItem?.ToString() == "All damage" ? "" : kind.SelectedItem?.ToString() ?? "";
            p.Ability = ability.Text; p.CriticalOnly = critical.IsChecked == true; } p.From = begin; p.Until = end; if(manualDateEdit){Controller.ExitGroupPeriod();manualDateEdit=false;}
            
            selectionSignature = System.Text.Json.JsonSerializer.Serialize(p); Controller.Save();
        }
        Refresh();
    }
    public void Refresh()
    {
        if (Monitor.IsEntered(Controller.Sync)) { Dispatcher.BeginInvoke(Refresh); return; }
        if (updating) return;
        RefreshSessionControls(); RefreshScopeContext(); RefreshHistorySelection(); group.RefreshPeriods();
        if (mode == CombatLabMode.Healing) { Controller.HealingVisible = IsVisible && !IsCollapsed; updating = true; SetScopeChoice(); from.Text = Controller.Profile.Meter.From?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""; until.Text = Controller.Profile.Meter.Until?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""; UpdateDateState(); updating = false; healing.Refresh(); RefreshInspection(); if (SelectedTab == CombatLabTab.Comparison) QueueComparisons((Controller.Published?.History ?? Controller.DpsView().Fights).ToList(), Controller.Profile.Meter); return; }
        updating = true;
        try
        {
            using (UpdateDiagnostics.Measure("DpsWindow refresh"))
            {
                var p = Controller.Profile.Meter;
                var view = Controller.DpsView(); var fights = view.Fights; var filter = view.Filter;
                string nextContext = MeterVisualContext.Key(Controller.Profile, view, Controller.SessionStart);
                if (dpsCadence.Due(DisplayCadence.Context(view, nextContext))) { pet.SetNumber(view.PetDps, false, true); player.SetNumber(view.PlayerDps, false, true); combined.SetNumber(view.CombinedDps, false, true); }
                var selectedSignature = System.Text.Json.JsonSerializer.Serialize(p);
                if (selectionSignature != selectedSignature)
                {
                    selectionSignature = selectedSignature; SetScopeChoice(); owner.SelectedItem = p.Actor == "Player" ? "Self" : p.Actor;
                    kind.SelectedItem = p.Kind.Length == 0 ? "All damage" : p.Kind; sort.SelectedItem = p.Sort;
                    ability.Text = p.Ability; critical.IsChecked = p.CriticalOnly;
                    from.Text = p.From?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""; until.Text = p.Until?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
                    filterError.Text = ""; UpdateDateState();
                }
                RefreshInspection();
                if (ReferenceEquals(lastPresented, view) && lastHealingShown == Controller.Profile.ShowLinkedHealing && selectionSignature == selectedSignature && !Controller.Demo) return;
                lastPresented = view; lastHealingShown = Controller.Profile.ShowLinkedHealing;
                var all = (Controller.Published?.History ?? view.Fights).OrderByDescending(x => x.Start).ToList();
                var targets = new[] { "All targets" }.Concat(view.TargetChoices).ToList();
                if (p.Target.Length > 0 && !targets.Contains(p.Target, StringComparer.OrdinalIgnoreCase)) { p.Target = ""; Controller.Save(); }
                if (!targets.SequenceEqual(target.ItemsSource as IEnumerable<string> ?? [])) target.ItemsSource = targets;
                target.SelectedItem = p.Target.Length == 0 ? "All targets" : targets.FirstOrDefault(t => CombatIdentity.Same(t, p.Target));
                string context = MeterVisualContext.Key(Controller.Profile, view, Controller.SessionStart); bool snap = context != visualContext; visualContext = context;
                bool smooth = Controller.Profile.SmoothMeterMotion && !Controller.Demo;
                if (snap) selectedAbility = view.Rows.FirstOrDefault(row => CombatInspection.Key(row) == selectedInspection);
                
                if (SelectedTab == CombatLabTab.Breakdown) damageRows.SetRows(Controller.Profile.ShowLinkedHealing ? view.Rows : view.Rows.Select(r => r with { LinkedHealing = null }).ToList(), context + Controller.Profile.ShowLinkedHealing, smooth);
                if (SelectedTab == CombatLabTab.Breakdown) { string? selectedTarget = (targetTable.SelectedItem as TargetRow)?.Target; UI.SetRows(targetTable, view.Targets); if (selectedTarget != null) targetTable.SelectedItem = view.Targets.FirstOrDefault(x => x.Target == selectedTarget); }

                max.SetNumber(view.MaxHit, smooth, snap); UpdateMaxHit(view); max.Visibility = view.DetailComplete ? Visibility.Visible : Visibility.Hidden;
                damageStats.SetDamage(view,(p.Scope == "Session" ? $"{fights.Count} encounters · " : "") + $"{(p.Actor == "Player" ? "Self" : p.Actor)} · existing filtered totals and combat duration" + (Controller.Profile.GroupRosterIncomplete && Controller.Profile.InGroup ? " · Group log coverage incomplete" : "") + (!view.DetailComplete ? " · Summary only; target/breakdown details incomplete; filtered totals unavailable when detail is required" : ""));
                var identity = Controller.Profile.PetIdentities.LastOrDefault(x => x.Until == null);
                Subtitle.Text = $"{Controller.Profile.EffectivePetElement} pet · {identity?.Name ?? Controller.Profile.ActiveLoadout.PetName} · {Controller.Profile.Zone}" + (Controller.Profile.ActiveLoadout.Name is "" or "Current" ? "" : " · " + Controller.Profile.ActiveLoadout.Name);
                timeline.Set(view.Chart);
                RefreshPeak();
                if (SelectedTab == CombatLabTab.Comparison) QueueComparisons(all, p);
            }
        }
        finally { updating = false; }
    }
    private void QueueComparisons(List<Encounter> all, DpsSelection p)
    {
        var samples = all.Where(x => x.Finished).ToList();
        string key = string.Join('|', samples.Select(x => x.Id + x.End.Ticks + ":" + x.Damage)) + selectionSignature;
        if (comparisonKey == key) return; comparisonKey = key;
        var selected = System.Text.Json.JsonSerializer.Deserialize<DpsSelection>(System.Text.Json.JsonSerializer.Serialize(p))!;
        var frozen = Controller.Published?.History.ToList() ?? samples.Select(CombatSnapshots.Copy).ToList();
        _ = Task.Run(() => frozen.GroupBy(x => (x.Loadout, x.PetType, x.Weapons, x.BuffNotes)).Select(g =>
        {
            var subset = g.Where(x => (!selected.From.HasValue || x.End >= selected.From) && (!selected.Until.HasValue || x.Start <= selected.Until)).ToList();
            selected.Scope = "Session"; var result = DpsViews.Build(subset, null, DateTime.MinValue, selected);
            return new LoadoutRow(g.Key.Loadout + (g.Key.BuffNotes.Length > 0 ? " · " + g.Key.BuffNotes : ""), g.Key.PetType, g.Key.Weapons, subset.Count, result.Seconds, result.PetDps, result.PlayerDps, result.CombinedDps) { Buffs = g.Key.BuffNotes };
        }).Where(x => x.Samples > 0).OrderByDescending(x => x.CombinedDps).ToList()).ContinueWith(task => Dispatcher.InvokeAsync(() =>
        { if (task.IsCompletedSuccessfully && comparisonKey == key) { UI.SetRows(comparisons, task.Result); if (comparisons.SelectedItem == null && comparisons.Items.Count > 0) comparisons.SelectedIndex = 0; UpdateComparisonDetail(); } }), TaskScheduler.Default);
    }
    public void MarkAbility(string abilityName) { selectedAbility = Controller.DpsView().Rows.FirstOrDefault(x => x.Ability == abilityName); RefreshPeak(); }
    internal void ShowView(int index) => ShowView(index == 4 ? CombatLabMode.Healing : CombatLabMode.Damage, index switch { 1 => CombatLabTab.Metrics, 2 => CombatLabTab.History, 3 => CombatLabTab.Comparison, _ => CombatLabTab.Breakdown });
    private void RefreshPeak()
    {
        if (selectedAbility == null) { timeline.Highlight = null; peakInfo.Text = "Select an ability to mark its Peak hit on the timeline."; return; }
        var view = Controller.DpsView(); var peak = view.Peaks.Where(x => x.Ability == selectedAbility.Ability && x.Channel == selectedAbility.Channel).OrderByDescending(x => x.Damage).ThenBy(x => x.Time).FirstOrDefault();
        timeline.Highlight = peak; peakInfo.Text = peak == null ? "Peak hit unknown · detailed events unavailable in this selection." : $"Peak hit · {peak.Ability} · {peak.Damage:N0} · {peak.Time:MMM dd HH:mm:ss} · {(peak.Actor == "Player" ? "Self" : peak.Actor)}";
    }
    private void Export(bool wholeScope)
    {
        EncounterExportDocument document;
        lock (Controller.Sync)
        {
            var view = DpsViews.Build(Controller.Combat.Encounters, Controller.Combat.Current, Controller.SessionStart, Controller.Profile.Meter, Controller.Sessions?.Sessions);
            if (!wholeScope) { var selectedId = (history.SelectedItem as EncounterHistoryRow)?.Fight?.Id ?? view.Fights.FirstOrDefault()?.Id; var selected = Controller.Combat.Encounters.Concat(Controller.Combat.Current == null ? [] : [Controller.Combat.Current]).FirstOrDefault(f => f.Id == selectedId); if (selected == null) { filterError.Text = "No encounter to export."; return; } var selection = System.Text.Json.JsonSerializer.Deserialize<DpsSelection>(System.Text.Json.JsonSerializer.Serialize(Controller.Profile.Meter))!; selection.Scope = "History"; selection.EncounterId = selected.Id; view = DpsViews.Build([selected], null, Controller.SessionStart, selection); }
            document = EncounterExporter.Snapshot(view, Controller.Profile, wholeScope ? Controller.BuildHealing(view) : HealingMetrics.Build(view.Fights, [], Controller.Profile.Healing.Filter(view.Filter.From, view.Filter.Until), Controller.Profile.Healing.Sort)) with { Scope = wholeScope ? Controller.Profile.Meter.Scope : "Encounter" };
        }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Encounter archive (CSV + JSON)|*.zip", FileName = $"{Controller.Profile.Character}-{(wholeScope ? "scope" : "encounter")}-{DateTime.Now:yyyyMMdd-HHmmss}.zip" };
        if (dialog.ShowDialog(this) != true) return;
        try { EncounterExporter.Write(dialog.FileName, document); filterError.Text = "Exported " + Path.GetFileName(dialog.FileName); } catch (Exception error) { filterError.Text = "Export failed: " + error.Message; }
    }
    private void CorrectGroup()
    {
        var period=Controller.GroupPeriods.FirstOrDefault(p=>p.Id==Controller.Profile.Meter.GroupPeriodId);
        var roster=period?.Members??Controller.Profile.GroupMembers;
        string List(bool pets)=>string.Join(", ",roster.Where(m=>m.IsPet==pets&&!m.PointOnly&&(period!=null||m.Until==null)).Select(m=>pets?m.Name+"="+m.Owner:m.Name).Distinct());
        var form=new EntryDialog(period==null?"Confirm current group roster":"Correct selected group period",[("Player names (comma separated; omit yourself)",List(false)),("Verified pets: Pet=Owner (comma separated)",List(true))],period==null?"Confirm current participants and explicit pet owners. Personal totals remain separate.":$"Applies to the selected period {period.Start:MMM/dd HH:mm}–{period.End??period.LastObserved:HH:mm}; recorded personal encounters stay unchanged.",["Known player names. Unknown membership should remain unconfirmed.","Example: Wolf=Ally. A pet name alone cannot establish its owner."]){Owner=this};
        if(form.ShowDialog()!=true)return;
        try { Controller.CorrectGroupProjection(period?.Id,form.Values[0],form.Values[1]);lastPresented=null;Refresh(); }
        catch(InvalidDataException error){filterError.Text=error.Message;}
    }
    private void TagLoadout()
    {
        var p = Controller.Profile.ActiveLoadout; var form = new EntryDialog("Tag future combat samples", [("Loadout name", p.Name), ("Pet override (Auto, Earth, Fire, Air, Water, Undead, Animation, Warder, Spirit)", Controller.Profile.PetElementOverride.Length == 0 ? "Auto" : Controller.Profile.PetElementOverride), ("Weapons", p.Weapons), ("Buff notes / test conditions", p.BuffNotes)], "Tag future samples with a loadout, weapons and optional buff notes. Saving ends the current combat segment; historical samples keep their tags.", ["A name for this equipment / buff test setup.", "Auto follows evidence; manual types override pet element detection.", "Weapon names used to label future samples; this does not change confirmed equipment.", "Optional notes about buffs and test conditions."]) { Owner = this };
        if (form.ShowDialog() != true || form.Values[0].Length == 0) return;
        var petTypes = new[] { "Auto", "Water", "Air", "Earth", "Fire", "Undead", "Animation", "Warder", "Spirit", "Warrior" };
        var selectedPet = petTypes.FirstOrDefault(x => x.Equals(form.Values[1], StringComparison.OrdinalIgnoreCase));
        if (selectedPet == null) { MessageBox.Show("Choose a pet type listed in the form."); return; }
        if (form.Values[0] != Controller.Profile.SelectedLoadout && !((App)Application.Current).CanChangeEquipmentContext()) return;
        lock (Controller.Sync)
        {
            Controller.Combat.Finish(); var next = Controller.Profile.Loadouts.FirstOrDefault(x => x.Name == form.Values[0]);
            if (next == null) { next = System.Text.Json.JsonSerializer.Deserialize<LoadoutProfile>(System.Text.Json.JsonSerializer.Serialize(p))!; next.Name = form.Values[0]; Controller.Profile.Loadouts.Add(next); }
            Controller.Profile.PetElementOverride = selectedPet == "Auto" ? "" : selectedPet; next.PetType = Controller.Profile.EffectivePetElement; next.Weapons = form.Values[2]; next.BuffNotes = form.Values[3]; Controller.Profile.SelectedLoadout = next.Name; Controller.Save();
        }
        Refresh();
    }
    private void CorrectPet()
    {
        var form = new EntryDialog("Correct pet ownership", [("Pet name", Controller.Profile.ActiveLoadout.PetName), ("From (local date/time)", (history.SelectedItem as EncounterHistoryRow)?.Fight?.Start.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")), ("Until (blank = still your pet)", "")], "Correct retained damage and healing attribution over this time range. This does not rename or equip the pet in game.", ["The pet name exactly as recorded in logs.", "Local start time for this ownership correction.", "Optional exclusive end time; leave blank while this pet is yours."]) { Owner = this };
        if (form.ShowDialog() != true) return;
        if (form.Values[0].Length == 0 || !DateTime.TryParse(form.Values[1], out var start)) { MessageBox.Show("Enter a pet name and valid start time."); return; }
        DateTime? end = null; if (form.Values[2].Length > 0) { if (!DateTime.TryParse(form.Values[2], out var d) || d <= start) { MessageBox.Show("Until must be a valid time after From."); return; } end = d; }
        Controller.CorrectPet(form.Values[0], start, end); Refresh();
    }
    private async void EditHistory(string action)
    {
        var fight = (history.SelectedItem as EncounterHistoryRow)?.Fight;
        if (action != "Undo" && fight == null) { historyInfo.Text = "Select a finished encounter first."; return; }
        DateTime? splitAt = null;
        if (action == "Split")
        {
            var form = new EntryDialog("Split encounter", [("Split at (local date and time; events at this time go to the second fight)", fight!.Start.AddSeconds(fight.Duration / 2).ToString("yyyy-MM-dd HH:mm:ss"))]) { Owner = this };
            if (form.ShowDialog() != true) return;
            if (!DateTime.TryParse(form.Values[0], out var time)) { historyInfo.Text = "Enter a valid local date/time."; return; } splitAt = time;
        }
        try { historyInfo.Text = "Updating history…"; string result = await Controller.EditHistoryAsync(fight?.Id ?? "", action, splitAt); shownHistory = ""; lastPresented = null; Refresh(); historyInfo.Text = result; }
        catch (Exception error) { historyInfo.Text = error.Message; }
    }
    private void DeleteFight()
    { if (history.SelectedItem is not EncounterHistoryRow selected) return; if (MessageBox.Show(this, "Delete this saved encounter and its retained events?", "Delete encounter", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; Controller.DeleteEncounter(selected.Fight); shownHistory = ""; Refresh(); }
}
public sealed class UniformGridEx : System.Windows.Controls.Primitives.UniformGrid
{ public UniformGridEx(int columns) { Columns = columns; } }
