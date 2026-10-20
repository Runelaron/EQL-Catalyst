using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class HealingPanel : UserControl
{
    private readonly TrackerController controller;
    public UIElement FilterControls { get; private set; } = null!;
    public UIElement MetricCards { get; private set; } = null!;
    public TextBlock StatusLine => status;
    public event Action<HealingViewState>? Presented;
    public event Action<DamageRow?>? AbilitySelected;

    private readonly ComboBox scope = UI.Select(["Fight", "Session", "History"], "Fight", 100);
    private readonly ComboBox direction = UI.Select(["Done", "Received"], "Done", 100);
    private readonly ComboBox actor = UI.Select(["Self + Pet", "Self", "Pet"], "Self + Pet", 105);
    private readonly ComboBox healer = UI.Select(["All healers"], "All healers", 145), recipient = UI.Select(["All recipients"], "All recipients", 145);
    private readonly ComboBox kind = UI.Select(["All healing", "Direct", "HoT"], "All healing", 105);
    private readonly ComboBox sort = UI.Select(["Healing", "HPS", "Heals", "Average", "Max"], "Healing", 100);
    private readonly TextBox spell = UI.Input("", 160);
    private readonly CheckBox lifetap = new() { Content = "Lifetaps", VerticalAlignment = VerticalAlignment.Center }, critical = new() { Content = "Critical only", VerticalAlignment = VerticalAlignment.Center };
    private readonly AnimatedNumber effective = new() { Format = "N0", FontSize = 16 }, hps = new() { Format = "N1", FontSize = 16 }, largest = new() { Format = "N0", FontSize = 16 }, overheal = new() { Format = "N0", FontSize = 16 };
    private readonly TextBlock status = UI.Text("", 11, "Muted");
    private readonly DamageRowsView spells = new();
    private readonly DataGrid parties;
    private readonly ComboBox partyMode = UI.Select(["Recipients", "Healers"], "Recipients", 115);
    private readonly DisplayCadence cadence = new();
    private HealingViewState? presented;
    private bool updating;
    private bool groupMode;
    private IReadOnlyList<string> groupHealers = [], groupRecipients = [];
    internal void SetGroupModeIfChanged(bool value) { if (groupMode != value) SetGroupMode(value); }
    internal void SetGroupMode(bool value) { groupMode = value; if (actor.Parent is UIElement field) field.Visibility = value ? Visibility.Collapsed : Visibility.Visible; presented = null; }
    internal void SetGroupChoices(CombatInspectionSnapshot snapshot) { groupHealers = snapshot.Healers; groupRecipients = snapshot.Recipients; if (groupMode) { updating = true; Fill(healer, "All healers", groupHealers, controller.Profile.Healing.Healer); Fill(recipient, "All recipients", groupRecipients, controller.Profile.Healing.Recipient); updating = false; } }
    private string context = "", importStatus = "";
    public HealingPanel(TrackerController controller)
    {
        this.controller = controller;
        parties = UI.Table(("Name", "Name", 0, null), ("Healing", "Effective", 80, "N0"), ("HPS", "Hps", 60, "N1"), ("Heals", "Count", 60, "N0"), ("Max", "MaxHeal", 70, "N0"), ("Avg", "Average", 65, "N1"), ("Crit %", "CriticalPercent", 65, "0.#'%'"));
        parties.Columns[0].MinWidth = 90; for (int i = 1; i < parties.Columns.Count; i++) parties.Columns[i].MinWidth = i == 1 ? 65 : i == 3 ? 45 : 55; parties.Columns[1].Header = "HP"; parties.Columns[1].Width = 65;
        parties.Columns[2].Width = 55; parties.Columns[3].Width = 45; parties.Columns[4].Width = 55;
        parties.SizeChanged += (_, _) => { double width = parties.ActualWidth; for (int i = 2; i < parties.Columns.Count; i++) parties.Columns[i].Visibility = width >= new[] { 0, 0, 250, 310, 400, 485, 565 }[i] ? Visibility.Visible : Visibility.Collapsed; };
        var partyStyle = new Style(typeof(DataGridRow)); partyStyle.Setters.Add(new Setter(ToolTipProperty, new System.Windows.Data.Binding("Name"))); parties.RowStyle = partyStyle;
        var quick = new WrapPanel(); var filters = new WrapPanel();
        foreach (var item in new UIElement[] { UI.Field("Healing", direction), UI.Field("Actor", actor), UI.Field("Sort by", sort) }) quick.Children.Add(item);
        foreach (var item in new UIElement[] { UI.Field("Healer", healer), UI.Field("Recipient", recipient), UI.Field("Type", kind), UI.Field("Spell search", spell), UI.ActionField(lifetap), UI.ActionField(critical) }) filters.Children.Add(item);
        FilterControls = UI.Stack(quick, filters);
        var cards = new CombatSummaryCards();
        foreach (var pair in new[] { ("RESTORED HP", effective), ("EFFECTIVE HPS", hps), ("LARGEST HEAL", largest), ("KNOWN OVERHEAL", overheal) })
        { pair.Item2.SetResourceReference(TextBlock.ForegroundProperty, "Mint"); var card = UI.Card(UI.Stack(UI.Label(pair.Item1), pair.Item2)); card.Padding = new(4, 1, 4, 1);string help=pair.Item1 switch {"RESTORED HP"=>"Effective HP actually restored within the selected healing filters; not unlogged raw healing.","EFFECTIVE HPS"=>"Effective HP restored divided by existing combat duration. Session uses summed fight durations; healing does not extend encounters.","LARGEST HEAL"=>"Largest effective healing record in the selected scope; direct healing and HoT ticks follow the active filters.",_=>"Known overheal subtotal, only where the log supplies raw healing. Missing raw amounts remain unknown, not zero."};card.ToolTip=help;pair.Item2.ToolTip=help;cards.Children.Add(card); }
        var table = new Grid(); table.RowDefinitions.Add(new() { Height = GridLength.Auto }); table.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); table.Children.Add(partyMode); Grid.SetRow(parties, 1); table.Children.Add(parties);
        var split = UI.Split(spells, table, 500, controller.Profile, "healing", 220, 180);
        MetricCards = cards; Content = split; spells.AbilitySelected += row => AbilitySelected?.Invoke(row);
        foreach (var combo in new[] { scope, direction, actor, healer, recipient, kind, sort }) combo.SelectionChanged += (_, _) => Change();
        spell.TextChanged += (_, _) => Change(); critical.Click += (_, _) => Change(); lifetap.Click += (_, _) => Change(); partyMode.SelectionChanged += (_, _) => { presented = null; Refresh(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) { presented = null; Refresh(); } };
        
        hps.ToolTip = "HP actually restored / first-to-last combat duration (minimum 1 second). Session HPS uses summed fight durations. Healing does not extend fights. Exact readings update every 2 seconds, immediately on first healing and fight completion.";
        overheal.ToolTip = "Only derived where the log supplies a raw healing amount. Missing raw amounts remain unknown; this is a known subtotal.";
        actor.ToolTip = "Done filters who healed. Received filters who was healed. Self-heals appear in both perspectives; the perspectives are never added together.";
    }
    public void SetStatsCard(UIElement card) { ((CombatSummaryCards)MetricCards).Children.Add(card); }
    private void Change()
    {
        if (updating) return;
        lock (controller.Sync)
        {
            var p = controller.Profile.Healing; 
            p.Direction = direction.SelectedItem?.ToString() ?? "Done"; p.Actor = actor.SelectedItem?.ToString() switch { "Self" => "Player", "Pet" => "Pet", _ => "Combined" };
            p.Healer = healer.SelectedItem?.ToString() is { } h && h != "All healers" ? h : ""; p.Recipient = recipient.SelectedItem?.ToString() is { } r && r != "All recipients" ? r : "";
            p.Kind = kind.SelectedItem?.ToString() is { } k && k != "All healing" ? k : ""; p.Ability = spell.Text; p.LifetapOnly = lifetap.IsChecked == true; p.CriticalOnly = critical.IsChecked == true; p.Sort = sort.SelectedItem?.ToString() ?? "Healing"; controller.Save();
        }
        presented = null; Refresh();
    }
    internal void SetContribution(bool value) => spells.ShowContribution = value;
    internal DataGrid PartyTable => parties;
    public void Refresh()
    {
        if (updating || !IsVisible && !controller.HealingVisible) return;
        updating = true;
        try
        {
            var view = controller.HealingView(); var p = controller.Profile.Healing;
            scope.SelectedItem = controller.Profile.Meter.Scope; direction.SelectedItem = p.Direction; actor.SelectedItem = p.Actor == "Player" ? "Self" : p.Actor == "Pet" ? "Pet" : "Self + Pet";
            kind.SelectedItem = p.Kind.Length == 0 ? "All healing" : p.Kind; sort.SelectedItem = p.Sort; spell.Text = p.Ability; critical.IsChecked = p.CriticalOnly; lifetap.IsChecked = p.LifetapOnly;
            if (ReferenceEquals(view, presented) && importStatus == controller.HealingImportStatus) return; presented = view; importStatus = controller.HealingImportStatus;
            bool reset = context != view.Context; context = view.Context;
            bool smooth = controller.Profile.SmoothMeterMotion && controller.Profile.MeterStyle != "Minimal" && SystemParameters.ClientAreaAnimation;
            foreach (var number in new[] { effective, largest, overheal }) number.Smooth = smooth;
            effective.SetNumber(view.Effective, smooth, reset || effective.Text == "—"); largest.SetNumber(view.MaxHeal, smooth, reset || largest.Text == "—"); overheal.SetNumber(view.KnownOverheal, smooth, reset || overheal.Text == "—");
            if (view.RawKnownCount == 0) { overheal.SetNumber(0, false, true); overheal.Text = "—"; }
            if (cadence.Due(context + "|" + (view.Count > 0) + "|" + view.TotalsAvailable + "|" + view.Lifecycle)) hps.SetNumber(view.Hps, false, true);
            Fill(healer, "All healers", groupMode ? groupHealers : view.AvailableHealers, p.Healer); Fill(recipient, "All recipients", groupMode ? groupRecipients : view.AvailableRecipients, p.Recipient);
            if (!view.TotalsAvailable) { foreach (var number in new[] { effective, hps, largest, overheal }) { number.SetNumber(0, false, true); number.Text = "—"; } }
            var rows = view.Rows.Select(r => new DamageRow(r.Ability + (r.Lifetap ? " · Lifetap" : ""), "Healing", r.Effective, r.Hps, r.Count, r.MaxHeal, r.Average, 0, r.CriticalPercent, 0, r.Percent, "Healing", r.PeakTime)
            { Unit = "hps", CustomTooltip = $"{r.Ability} · {r.Kind}{(r.Lifetap ? " · verified lifetap" : "")}\n{r.Effective:N0} HP restored · {r.Hps:N1} HPS · {r.Percent:N1}%\n{r.Count:N0} heals · avg {r.Average:N1} · max {r.MaxHeal:N0} at {r.PeakTime:HH:mm:ss}\n{r.CriticalPercent:N1}% crit · {r.KnownOverheal:N0} known overheal ({r.RawKnownCount}/{r.Count} raw amounts logged)" }).ToList();
            if (IsVisible) spells.SetRows(rows, context, smooth); string? selectedParty = (parties.SelectedItem as HealingPartyRow)?.Name; var partyRows = partyMode.SelectedItem?.ToString() == "Healers" ? view.Healers : view.Recipients; if (IsVisible) UI.SetRows(parties, partyRows); if (selectedParty != null) parties.SelectedItem = partyRows.FirstOrDefault(r => CombatIdentity.Same(r.Name, selectedParty));
            status.Text = $"{view.Count:N0} heals · {view.Seconds:N0}s combat · {view.RawKnownCount:N0} raw amounts logged" + (controller.Profile.Meter.Scope == "Session" ? $" · {view.OutsideCombat:N0} HP outside combat (excluded from HPS)" : "") + (view.Coverage == "Complete" ? "" : $" · Healing coverage: {view.Coverage.ToLowerInvariant()}") + (view.DetailComplete ? "" : " · Some spell/party details unavailable") + "\n" + controller.HealingImportStatus;
            Presented?.Invoke(view);
        }
        finally { updating = false; }
    }
    private static void Fill(ComboBox combo, string all, IEnumerable<string> values, string selected)
    {
        var items = new[] { all }.Concat(values.Concat(selected.Length > 0 ? [selected] : []).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)).ToList();
        if (!combo.Items.Cast<string>().SequenceEqual(items)) combo.ItemsSource = items;
        combo.SelectedItem = selected.Length == 0 ? all : selected;
    }
}
