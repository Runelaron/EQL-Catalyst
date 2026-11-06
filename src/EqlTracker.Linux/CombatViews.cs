using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EqlTracker.App;
using EqlTracker.Core;

namespace EqlTracker.Linux;

internal sealed partial class CatalystWindow
{
    private Control BuildLab()
    {
        controller!.EnableLabBrowsing(); var content = Column(); var filters = new Expander { Header = "Filters", MinHeight = 28 };
        var sessions = SessionSelector(false); var row = Flow(); row.Children.Add(filters); row.Children.Add(Field("Session", sessions));
        row.Children.Add(Action("Damage overlay", () => ShowOverlay(false))); row.Children.Add(Action("Healing overlay", () => ShowOverlay(true))); content.Children.Add(row);
        var zone = new ComboBox { ItemsSource = new[] { "All zones" }.Concat(controller.LabSessionEncounters().Select(f => f.Zone).Distinct().Order()).ToArray(), SelectedIndex = 0, Width = 160, Height = 28 };
        var named = new CheckBox { Content = "Names only", IsChecked = controller.LabBrowsing.NamesOnly }; var filterRow = Flow(); filterRow.Children.Add(zone); filterRow.Children.Add(named); filters.Content = filterRow;
        void Filter() => controller.SetLabFilters(zone.SelectedIndex <= 0 ? "" : zone.SelectedItem?.ToString() ?? "", named.IsChecked == true);
        zone.SelectionChanged += (_, _) => Filter(); named.IsCheckedChanged += (_, _) => Filter();
        var tabs = new TabControl(); var damage = new TabItem { Header = "Damage" }; var healing = new TabItem { Header = "Healing" }; var history = new TabItem { Header = "Encounter History" }; var songs = new TabItem { Header = "Songs" };
        tabs.Items.Add(damage); tabs.Items.Add(healing); tabs.Items.Add(history); tabs.Items.Add(songs); content.Children.Add(tabs);
        var damageBody = Column(); var stats = Text(""); var rows = new ListBox(); damageBody.Children.Add(stats); damageBody.Children.Add(rows); damage.Content = damageBody;
        var healBody = Column(); var healStats = Text(""); var healRows = new ListBox(); healBody.Children.Add(healStats); healBody.Children.Add(healRows); healing.Content = healBody;
        var fightList = new ListBox(); history.Content = fightList; string key = "";
        void Update()
        {
            var view = controller.DpsView(); string next = PetComparisonEngine.Key(new { controller.CombatRevision, controller.Profile.Meter, controller.LabBrowsing, view.Seconds, view.Damage }); if (next == key) return; key = next;
            stats.Text = $"{view.Fights.Count} fights · {view.Seconds:0.#} combat seconds · {view.Damage:N0} damage · {view.Dps:0.#} DPS · Max hit {view.MaxHit:N0}"; stats.Foreground = Red;
            rows.ItemsSource = view.Rows.Select(r => $"{r.Ability}    {r.Damage:N0}    {r.Dps:0.#} DPS    {r.Hits} hits").ToArray();
            var heals = controller.HealingView(); healStats.Text = heals.TotalsAvailable ? $"{heals.Effective:N0} healing · {heals.Hps:0.#} HPS · {heals.Coverage}" : "Healing unavailable · " + heals.Coverage; healStats.Foreground = Green; healRows.ItemsSource = heals.Rows.Select(h => $"{h.Ability}    {h.Effective:N0}    {h.Hps:0.#} HPS").ToArray();
            fightList.ItemsSource = controller.LabHistory(controller.Combat.Encounters).OrderByDescending(f => f.Start).Select(f => new ChoiceItem<Encounter>($"{f.Start:g} · {f.Zone} · {f.Damage:N0}", f)).ToArray();
        }
        fightList.SelectionChanged += (_, _) => { if (fightList.SelectedItem is ChoiceItem<Encounter> chosen) { controller.Profile.Meter.Scope = "History"; controller.Profile.Meter.EncounterId = chosen.Value.Id; controller.Save(); } };
        row.Children.Add(Action("Back to session", () => { controller.Profile.Meter.Scope = "Session"; controller.Profile.Meter.EncounterId = ""; controller.Save(); }));
        songs.Content = Songs(); live.Add(Update); Update(); return Scroll(content);
    }
    private ComboBox SessionSelector(bool overview)
    {
        var combo = new ComboBox { Width = 130, Height = 28 }; var selected = overview ? controller!.OverviewSession : controller!.Profile.Meter.SessionId;
        var entries = new[] { new ChoiceItem<string>("Current", "") }.Concat((controller.Sessions?.Sessions ?? []).OrderByDescending(s => s.Start).Select(s => new ChoiceItem<string>($"{s.Start:MMM dd HH:mm} · {controller.LabSessionEncounters().Count(f => f.Start >= s.Start && (!s.NextLogin.HasValue || f.Start < s.NextLogin))} fights", s.Id))).ToArray();
        combo.ItemsSource = entries; combo.SelectedItem = entries.FirstOrDefault(e => e.Value == selected) ?? entries[0];
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is not ChoiceItem<string> choice) return; if (overview) controller.SelectOverviewSession(choice.Value); else controller.SetLabSession(choice.Value); }; return combo;
    }
    private Control Songs()
    {
        var content = Column(); var request = controller!.BardPresentation(); var set = new ComboBox { ItemsSource = request.Choices, SelectedItem = request.Choices.FirstOrDefault(c => c.Id == request.Selected), Height = 28, MinWidth = 170 }; content.Children.Add(Field("Song set", set));
        set.SelectionChanged += (_, _) => { if (set.SelectedItem is BardSetChoice chosen) { controller.SelectBardSet(chosen.Id); } };
        var effective = new CheckBox { Content = "Effective strength", IsChecked = request.Input.EffectiveStrength }; ToolTip.SetTip(effective, BardSongComparison.EffectiveTooltip); effective.IsCheckedChanged += (_, _) => controller.SetBardStrength(effective.IsChecked == true); content.Children.Add(effective);
        var result = BardSongComparison.Evaluate(request.Input);
        foreach (var row in result.Rows) { var text = Text(row.Song.Identity.Name + "  +" + (row.Song.Identity.UpgradeRank?.ToString() ?? "Unknown") + "   →   " + row.Status + " · " + row.Reason); ToolTip.SetTip(text, row.Evidence); content.Children.Add(text); }
        content.Children.Add(new Expander { Header = "Setup", Content = Text(request.SetupSummary) }); content.Children.Add(new Expander { Header = "Why?", Content = Text(request.Evidence) }); return content;
    }
    private Control BuildOverview()
    {
        var content = Column(); content.Children.Add(Field("Session", SessionSelector(true))); var summary = Text(""); content.Children.Add(summary);
        var damage = Text(""); damage.Foreground = Red; var pet = Text(""); pet.Foreground = Rose; var healing = Text(""); healing.Foreground = Green; content.Children.Add(damage); content.Children.Add(pet); content.Children.Add(healing);
        var loot = Column(); var progress = Column(); var columns = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 8 }; columns.Children.Add(loot); columns.Children.Add(progress); Grid.SetColumn(progress, 1); content.Children.Add(columns);
        var ended = Text(""); ended.HorizontalAlignment = HorizontalAlignment.Right; content.Children.Add(ended); bool pending = false; string key = "";
        async void Update()
        {
            if (controller == null || pending) return; string next = controller.OverviewFingerprint(); if (next == key) return; pending = true;
            try
            {
                var expected = controller; var view = await expected.ReadOverview(CancellationToken.None); if (controller != expected || workspace != "Overview" || expected.OverviewFingerprint() != next) return; key = next;
                if (view == null) { summary.Text = "No retained session is available."; return; }
                summary.Text = $"{view.Recap.Encounters} fights · {view.CombatSeconds:0.#} combat seconds · Elapsed session {TimeSpan.FromSeconds(view.ElapsedSeconds):g}\nCoin earned {view.Recap.Earned?.ToString("N0") ?? "Unknown"} · Coin spent {view.Recap.Spent?.ToString("N0") ?? "Unknown"}";
                damage.Text = $"Self {view.Recap.PlayerDamage:N0} damage · {view.SelfDps:0.#} DPS"; pet.Text = $"Pet {view.Recap.PetDamage:N0} damage · {view.PetDps:0.#} DPS"; healing.Text = "Healing " + (view.Healing?.ToString("N0") ?? "Unknown"); ended.Text = view.Qualification;
                loot.Children.Clear(); progress.Children.Clear(); loot.Children.Add(Heading("Loot")); progress.Children.Add(Heading("Quest progress"));
                foreach (var item in view.Recap.Loot) { var button = Action(item.Name + " ×" + item.Quantity, () => { if ((expected.Catalog.FindKey(item.ItemKey) ?? expected.Catalog.Find(item.Name)) is { } definition) ShowItem(definition, item.Receipts.FirstOrDefault()?.Rank ?? 0); }); loot.Children.Add(button); }
                foreach (var action in view.Recap.Progress) progress.Children.Add(Text(action.Name));
                loot.Children.Add(new Expander { Header = "Evidence", Content = Text(view.Recap.HistoryIncomplete ? "Partial retained history; earlier receipts may be absent." : "Recorded session receipts."), MinHeight = 14 });
                progress.Children.Add(new Expander { Header = "Evidence", Content = Text("Logged activity and manual checklist actions retain their distinct provenance."), MinHeight = 14 });
                if (Bounds.Width < 760) { columns.ColumnDefinitions = new("*"); columns.RowDefinitions = new("Auto,Auto"); Grid.SetColumn(progress, 0); Grid.SetRow(progress, 1); }
            }
            catch (OperationCanceledException) { }
            finally { pending = false; }
        }
        live.Add(Update); Update(); return Scroll(content);
    }
    private void ShowOverlay(bool healing)
    {
        Window? existing = healing ? healingOverlay : damageOverlay; if (existing != null) { existing.Activate(); return; }
        var text = Text(""); text.Margin = new(8); var overlay = new Window { Title = healing ? "Catalyst Healing" : "Catalyst DPS", Width = 340, Height = 160, MinWidth = 220, MinHeight = 80, Topmost = true, Background = Brush.Parse("#CB102531"), Foreground = Foreground, Content = text };
        if (healing) healingOverlay = overlay; else damageOverlay = overlay;
        var update = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; update.Tick += (_, _) => { if (controller == null) return; if (healing) { var h = controller.HealingView(); text.Text = h.TotalsAvailable ? $"Healing {h.Effective:N0}\n{h.Hps:0.#} HPS" : "Healing unavailable"; } else { var d = controller.OverlayDpsView(); text.Text = $"{d.Damage:N0} damage\n{d.Dps:0.#} DPS · Pet {d.PetDps:0.#}"; } };
        overlay.Closed += (_, _) => { update.Stop(); if (healing) healingOverlay = null; else damageOverlay = null; }; update.Start(); overlay.Show();
    }
}
