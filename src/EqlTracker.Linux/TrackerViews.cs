using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using EqlTracker.Core;

namespace EqlTracker.Linux;

internal sealed partial class CatalystWindow
{
    private Control BuildTracker()
    {
        var tabs = new TabControl();
        foreach (var page in new[] { "Objectives", "Progress", "Gear", "Exaltations", "Pet", "Journal", "Notebook", "Holdings" }) tabs.Items.Add(new TabItem { Header = page });
        async void Select()
        {
            if (!await ResolveDraft()) return;
            if (tabs.SelectedItem is not TabItem tab) return;
            live.Clear(); tab.Content = tab.Header?.ToString() switch { "Progress" => Progress(), "Gear" => Gear(), "Exaltations" => Exaltations(), "Pet" => Pet(), "Journal" => Journal(), "Notebook" => Notebook(), "Holdings" => Holdings(), _ => Objectives() };
        }
        tabs.SelectionChanged += (_, e) => { if (e.Source == tabs) Select(); }; tabs.SelectedIndex = 0; return tabs;
    }
    private Control Objectives()
    {
        var content = Column(); var list = new ListBox(); string key = "";
        void Update() { var rows = controller!.Objectives; string next = PetComparisonEngine.Key(rows); if (next == key) return; key = next; list.ItemsSource = rows.Select(r => r.Class + " · " + (r.Mob.Length > 0 ? r.Mob + " — " : "") + r.Item + "  " + r.Quantity).ToArray(); }
        live.Add(Update); Update(); content.Children.Add(list); return Scroll(content);
    }
    private Control Progress()
    {
        var content = Column(); var search = Entry("Find quest"); search.Width = 170; var tracked = new CheckBox { Content = "Tracked" };
        var choices = new ListBox { MaxHeight = 170 }; var details = Column(); var row = Flow(); row.Children.Add(search); row.Children.Add(tracked); content.Children.Add(row); content.Children.Add(choices); content.Children.Add(details);
        void Filter() => choices.ItemsSource = controller!.Catalog.Quests.Where(q => q.Name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase) && (tracked.IsChecked != true || QuestTracking.Selected(q, controller.Profile, controller.Inventory, controller.Catalog))).OrderBy(q => q.Name).Select(q => new ChoiceItem<QuestDefinition>(q.Name, q)).ToArray();
        search.TextChanged += (_, _) => Filter(); tracked.IsCheckedChanged += (_, _) => Filter();
        choices.SelectionChanged += (_, _) =>
        {
            details.Children.Clear(); if (choices.SelectedItem is not ChoiceItem<QuestDefinition> chosen) return; var quest = chosen.Value;
            details.Children.Add(Heading(quest.Name)); var track = new CheckBox { Content = "Track", IsChecked = QuestTracking.Selected(quest, controller!.Profile, controller.Inventory, controller.Catalog) };
            track.IsCheckedChanged += (_, _) => { QuestTracking.Track(controller!.Profile, quest, track.IsChecked == true); controller.Save(); }; details.Children.Add(track);
            foreach (var step in quest.Steps)
            {
                var done = new CheckBox { Content = step.Name, IsChecked = controller!.Profile.CompletedSteps.Contains(step.Id) }; ToolTip.SetTip(done, step.Instructions);
                done.IsCheckedChanged += (_, _) => { if (done.IsChecked == true) controller!.Profile.CompletedSteps.Add(step.Id); else controller!.Profile.CompletedSteps.Remove(step.Id); controller.Save(); }; details.Children.Add(done);
            }
            foreach (var reward in quest.RewardAlternatives.SelectMany(a => a).Prepend(quest.RewardKey).Distinct().Where(k => k.Length > 0)) if (controller!.Catalog.FindKey(reward) is { } item) details.Children.Add(Action(item.Name, () => ShowItem(item, 0)));
            if (quest.WikiUrl.Length > 0) details.Children.Add(Action("Source", () => OpenUrl(quest.WikiUrl)));
        };
        Filter(); return Scroll(content);
    }
    private Control Gear()
    {
        var content = Column(); var search = Entry("Find equipment"); search.Width = 170; var items = new ListBox { MaxHeight = 190 }; var details = Column();
        content.Children.Add(search); content.Children.Add(items); content.Children.Add(details);
        void Filter() => items.ItemsSource = controller!.Catalog.Items.Where(i => i.Slots.Count > 0 && i.Name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Name).Take(250).Select(i => new ChoiceItem<ItemDefinition>(i.Name, i)).ToArray();
        search.TextChanged += (_, _) => Filter(); items.SelectionChanged += (_, _) => { details.Children.Clear(); if (items.SelectedItem is not ChoiceItem<ItemDefinition> item) return; details.Children.Add(ItemStats(item.Value, 0)); details.Children.Add(Text(GearScorer.Eligibility(item.Value, controller!.Profile.ActiveLoadout, false, null, controller.Catalog))); }; Filter(); return Scroll(content);
    }
    private Control Exaltations()
    {
        var content = Column(); var search = Entry("Find effect"); var charged = new CheckBox { Content = "Include charged clicks", IsChecked = false }; var list = new ListBox();
        content.Children.Add(search); content.Children.Add(charged); content.Children.Add(list);
        void Filter()
        {
            list.ItemsSource = ExaltationPlanner.Build(controller!.Catalog,controller.Inventory).Where(e=>ExaltationChargePolicy.Visible(e,charged.IsChecked==true)&&e.Name.Contains(search.Text??"",StringComparison.OrdinalIgnoreCase)).Take(250).Select(e=>e.Name+" · "+e.Effect).ToArray();
        }
        search.TextChanged += (_, _) => Filter(); charged.IsCheckedChanged += (_, _) => Filter(); Filter(); return Scroll(content);
    }
    private Control Pet()
    {
        var content = Column(); var owner = controller!.Profile.ActiveLoadout; var saved = PetInventoryEditing.Confirmed(owner, controller.Catalog);
        petDraft ??= new(saved, followsDetected: true); var build = petDraft.Working;
        var actions = Flow(); var undo = Action("Undo", () => { petDraft.Undo(); body.Content = BuildTracker(); }); var save = Action("Save", () => { if (SavePet()) { undo.IsEnabled = saveButton!.IsEnabled = false; } });
        saveButton = save; undo.IsEnabled = save.IsEnabled = petDraft.Dirty; actions.Children.Add(undo); actions.Children.Add(save); actions.Children.Add(Action("Pet Details", () => ShowPetDetails())); content.Children.Add(actions);
        var dual = new CheckBox { Content = "Dual wield", IsChecked = build.DualWield }; dual.IsCheckedChanged += (_, _) => { PetSlotPlanning.Toggle(build, dual.IsChecked == true, controller.Catalog, 10); petDraft.Changed(); undo.IsEnabled = save.IsEnabled = petDraft.Dirty; }; content.Children.Add(dual);
        var positions = new ComboBox { ItemsSource = Enumerable.Range(1, 10).Select(n => n.ToString()).ToArray(), SelectedIndex = 0, Height = 28, Width = 70 };
        var search = Entry("Find pet equipment"); search.Width = 170; var candidates = new ListBox { MaxHeight = 140 }; var rank = new NumericUpDown { Value = 0, Minimum = 0, Maximum = int.MaxValue, Width = 70, Height = 28 };
        var fields = Flow(); fields.Children.Add(Field("Position", positions)); fields.Children.Add(Field("Search", search)); fields.Children.Add(Field("+Rank", rank)); content.Children.Add(fields); content.Children.Add(candidates);
        var rows = new ListBox { MaxHeight = 180 };
        void Fill() => rows.ItemsSource = build.Items.OrderBy(i => i.Position).Select(i => new ChoiceItem<PetInventoryItem>($"{i.Position}  {i.Name} +{i.Rank}", i)).ToArray();
        void Dirty() { petDraft.Changed(); undo.IsEnabled = save.IsEnabled = petDraft.Dirty; Fill(); }
        search.TextChanged += (_, _) => candidates.ItemsSource = controller.Catalog.Items.Where(i => i.Slots.Count > 0 && i.Name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Name).Take(100).Select(i => new ChoiceItem<ItemDefinition>(i.Name, i)).ToArray();
        var editing = Flow(); editing.Children.Add(Action("Place", () => { if (candidates.SelectedItem is not ChoiceItem<ItemDefinition> chosen) return; if (PetPositionEditing.Replace(build, chosen.Value, null, positions.SelectedIndex + 1, 10, out string error, owner, controller.Catalog)) { build.Items.Single(i => i.Position == positions.SelectedIndex + 1).Rank = (int)(rank.Value ?? 0); Dirty(); } else status.Text = error; }));
        editing.Children.Add(Action("Remove", () => { if (rows.SelectedItem is not ChoiceItem<PetInventoryItem> chosen) return; PetPositionEditing.Remove(build, chosen.Value.Id); Dirty(); }));
        content.Children.Add(editing); content.Children.Add(rows); Fill();
        content.Children.Add(Text("This Linux frontend is under parity validation. User placement changes the build only; it does not assert client equipment observation.")); return Scroll(content);
    }
    private Button? saveButton;
    private bool SavePet()
    {
        if (petDraft?.Dirty != true || controller == null) return true;
        var owner = controller.Profile.ActiveLoadout; var saved = PetInventoryEditing.Confirmed(owner, controller.Catalog);
        if (!petDraft.CanCommit(saved)) { status.Text = "The saved Pet build changed. Review it before saving this draft."; return false; }
        var result = petDraft.Commit(); PetInventoryEditing.Confirm(owner, result, DateTime.Now); controller.Save(); return true;
    }
    private void ShowPetDetails()
    {
        if (controller == null) return; var build = petDraft?.Working ?? PetInventoryEditing.Confirmed(controller.Profile.ActiveLoadout, controller.Catalog); var content = Column(); content.Children.Add(Heading(build.Name + " · " + build.PetType));
        content.Children.Add(Text("Spell: " + build.SummonSpell + " · rank " + (build.SummonRankKnown ? build.SummonRank.ToString() : "Unknown") + " · level " + (build.PetLevel?.ToString() ?? "Unknown")));
        foreach (var item in build.Items.OrderBy(i => i.Position)) if (controller.Catalog.FindKey(item.ItemKey) is { } definition) { content.Children.Add(Heading(item.Position + " · " + definition.Name)); content.Children.Add(ItemStats(definition, item.Rank)); }
        var dialog = new Window { Title = "Pet Details", Width = 680, Height = 580, MinWidth = 460, MinHeight = 360, Content = Scroll(content), Background = Background, Foreground = Foreground }; dialog.Show(this);
    }
    private Control Journal() { var list = new ListBox(); list.ItemsSource = controller!.JournalSnapshot().Take(500).Select(a => $"{a.At:g} · {a.Kind} · {a.Name}").ToArray(); return list; }
    private Control Notebook()
    {
        var content = Column(); var setup = controller!.IntegratedSnapshot(); content.Children.Add(Heading("Camp notes"));
        foreach (var property in setup.Camps.GetType().GetProperties().Where(p => p.PropertyType == typeof(string))) content.Children.Add(Text(property.Name + ": " + property.GetValue(setup.Camps)));
        content.Children.Add(Text("Existing camp evidence is retained. Camp configuration is not yet release-qualified on Linux.")); return Scroll(content);
    }
    private Control Holdings() { var content = Column(); foreach (var item in controller!.Catalog.Items.Where(i => controller.OtherHoldings(i).Count > 0).Take(100)) { content.Children.Add(Heading(item.Name)); foreach (var entry in controller.OtherHoldings(item)) content.Children.Add(Text(entry.ToString())); } return Scroll(content); }
    private static Control ItemStats(ItemDefinition item, int rank)
    {
        var content = Column(); var projection = RankedItemStats.Resolve(item, rank); content.Children.Add(Heading(item.Name + (rank > 0 ? " +" + rank : "")));
        foreach (var pair in projection.Stats) content.Children.Add(Text(pair.Key + "  " + pair.Value.ToString("0.###")));
        content.Children.Add(Text(projection.Evidence)); if (projection.Weight.HasValue) content.Children.Add(Text((projection.WeightIsBase ? "Base weight " : "Weight ") + projection.Weight.Value.ToString("0.###"))); return content;
    }
    private void ShowItem(ItemDefinition item, int rank) { var popup = new Window { Title = item.Name, Width = 420, Height = 500, MinWidth = 300, MinHeight = 260, Background = Background, Foreground = Foreground, Content = Scroll(ItemStats(item, rank)) }; popup.Show(this); }
    private static void OpenUrl(string url) { if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme is "https" or "http") System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true }); }
    private sealed record ChoiceItem<T>(string Label, T Value) { public override string ToString() => Label; }
}
