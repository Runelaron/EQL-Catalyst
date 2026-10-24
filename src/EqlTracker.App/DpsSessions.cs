using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class CombatActionRow : Grid
{
    public CombatActionRow(UIElement primary, UIElement secondary)
    {
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        Children.Add(primary); Children.Add(secondary); Grid.SetColumn(secondary, 1);
        SizeChanged += (_, _) =>
        {
            bool narrow = ActualWidth < 980;
            if (primary is WrapPanel flow)
            {
                if (narrow && !flow.Children.Contains(secondary)) { Children.Remove(secondary); flow.Children.Add(secondary); }
                else if (!narrow && flow.Children.Contains(secondary)) { flow.Children.Remove(secondary); Children.Add(secondary); Grid.SetColumn(secondary, 1); }
            }
            Grid.SetColumnSpan(primary, narrow ? 2 : 1);
            if (secondary is FrameworkElement f) f.HorizontalAlignment = HorizontalAlignment.Right;
        };
    }
}
public sealed partial class DpsWindow
{
    private readonly ComboBox sessionPicker = UI.Select([], "", 130);
    private readonly Button latestFight = UI.Button("Back to session", () => { });
    private readonly StackPanel comparisonDetails = new();
    private string shownSessions = "", shownHistory = "";
    private readonly List<ActorBadge> summaryBadges = [];
    private DpsViewState? badgeView; private string cachedBadgeElement="Unknown";
    private void ConfigureScopes()
    {
        Controller.EnableLabBrowsing();
        latestFight.ToolTip="Return to this session without changing its retained combat records.";
        latestFight.Click+=(_,_)=>{Controller.ExitGroupPeriod(true);Controller.Profile.Meter.Scope="Session";Controller.Profile.Meter.EncounterId="";Controller.Save();selectionSignature="";lastPresented=null;Refresh();};
        sessionPicker.ToolTip="Current follows the most recent retained login session. Choose an earlier date to keep that session selected. Counts are fights, not confirmed kills.";
        System.Windows.Automation.AutomationProperties.SetName(sessionPicker,"Session");
        sessionPicker.SelectionChanged+=(_,_)=>
        {
            if(updating||sessionPicker.SelectedItem is not SessionChoice choice)return;
            Controller.SetLabSession(choice.Id);lastPresented=null;shownSessions="";shownHistory="";Refresh();
        };
        var style=new Style(typeof(ComboBoxItem),(Style)FindResource(typeof(ComboBoxItem)));style.Setters.Add(new Setter(ToolTipProperty,new System.Windows.Data.Binding("Label")));sessionPicker.ItemContainerStyle=style;
        zoneFilter.ToolTip="Limit the selected session to a recorded zone. Zone aliases are normalized; encounter timing and damage records stay unchanged.";
        namesOnly.ToolTip="Only encounters containing documented named NPCs in that zone. Mixed fights retain all their targets; unknown classifications are excluded.";
        zoneFilter.SelectionChanged+=(_,_)=>{if(!updating){Controller.SetLabFilters(zoneFilter.SelectedItem?.ToString() is {} z&&z!="All zones"?z:"",namesOnly.IsChecked==true);lastPresented=null;shownHistory="";Refresh();}};
        namesOnly.Click+=(_,_)=>{if(!updating){Controller.SetLabFilters(Controller.LabBrowsing.Zone,namesOnly.IsChecked==true);lastPresented=null;shownHistory="";Refresh();}};
    }
    private string VisibleScope()=>Controller.Profile.Meter.Scope=="History"?"History":"Session";
    private void RefreshSessionControls()
    {
        var choices=Controller.Sessions?.Choices??[];
        var rows=new[]{new SessionChoice("",choices.FirstOrDefault() is {} latest?"Current · "+latest.Label:"Current · 0 fights")}.Concat(choices.Skip(1)).ToArray();
        string key=string.Join('|',rows.Select(c=>c.Id+":"+c.Label))+"|"+Controller.Profile.Meter.SessionId;
        if(key!=shownSessions&&!sessionPicker.IsDropDownOpen)
        {
            shownSessions=key;bool prior=updating;updating=true;sessionPicker.ItemsSource=rows;
            sessionPicker.SelectedItem=rows.FirstOrDefault(c=>c.Id==Controller.Profile.Meter.SessionId)??rows[0];updating=prior;
        }
        sessionPicker.ToolTip=(sessionPicker.SelectedItem as SessionChoice)?.Label+"\nCurrent follows the most recent retained session; counts are fights, not confirmed kills. Last observed activity is not a verified logout.";
        bool was=updating;updating=true;
        var zones=new[]{"All zones"}.Concat(Controller.LabSessionEncounters(Controller.Published?.History).GroupBy(f=>Identity.ZoneKey(f.Zone)).Select(g=>Identity.Zone(g.First().Zone)).Order(StringComparer.OrdinalIgnoreCase)).Distinct().ToList();
        string selected=Controller.LabBrowsing.Zone;
        if(selected.Length>0&&!zones.Any(z=>Identity.ZoneKey(z)==Identity.ZoneKey(selected)))zones.Add(selected);
        if(!zones.SequenceEqual(zoneFilter.Items.Cast<string>()))zoneFilter.ItemsSource=zones;
        zoneFilter.SelectedItem=selected.Length==0?"All zones":zones.First(z=>Identity.ZoneKey(z)==Identity.ZoneKey(selected));namesOnly.IsChecked=Controller.LabBrowsing.NamesOnly;updating=was;
    }
    private void CorrectClassEvidence()
    {
        var s = Controller.SelectedSession; if (s == null) { filterError.Text = "Wait for saved session indexing."; return; }
        var dialog = new EntryDialog("Correct session classes", [("Classes · 1–3 abbreviations", SessionEvidence.ClassesAt(s, s.LastObserved)), ("From · local time", s.Start.ToString("yyyy-MM-dd HH:mm:ss")), ("Until · local time", s.LastObserved.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss"))], "Correct class attribution within this saved session only. Damage, healing and encounter boundaries remain unchanged. This overrides class evidence in the selected interval.", ["Examples: SHD/BRD/MAG. Use confirmed active classes, not equipment tags.", "Inclusive start time, within the selected session.", "Exclusive end time. Outside this interval the recorded evidence remains in use."]) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { if (!DateTime.TryParse(dialog.Values[1], out var from) || !DateTime.TryParse(dialog.Values[2], out var until)) throw new InvalidDataException("Enter valid local times."); Controller.CorrectClasses(s.Id, from, until, dialog.Values[0].ToUpperInvariant().Split(['/', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); lastPresented = null; shownSessions = ""; Refresh(); } catch (InvalidDataException error) { filterError.Text = error.Message; }
    }
    private Border ActorMetric(string label, TextBlock number, string actor)
    {
        FrameworkElement badge;
        if(actor=="Max")badge=new MaxHitGlyph{Width=16,Height=16,Margin=new(0,0,4,0)};
        else { var identity=new ActorBadge{Width=16,Height=16,Margin=new(0,0,4,0),Tag=actor};identity.Set(actor,true,"",Controller.Profile.EffectivePetElement);summaryBadges.Add(identity);badge=identity; }
        UIElement value=number;
        if(actor=="Max") { var row=new Grid();row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.Children.Add(number);maxCaption.Margin=new(6,0,0,0);maxCaption.VerticalAlignment=VerticalAlignment.Center;maxCaption.TextWrapping=TextWrapping.Wrap;maxCaption.MaxHeight=28;maxCaption.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(maxCaption,1);row.Children.Add(maxCaption);value=row; }
        var card=UI.Card(UI.Stack(UI.Row(badge,UI.Label(label)),value),new(0,0,4,2));card.Padding=new(4,1,4,1);
        string help=actor switch {"Combined"=>"Total DPS · Self plus the timestamp-verified owned pet. Group members are excluded.","Player"=>"Self DPS · outgoing damage attributed to this character. Owned-pet and group damage are excluded.","Pet"=>"Pet DPS · outgoing damage attributed to the owned pet at event time. The badge reflects the pet evidence for this selection.",_=>"Max hit · largest logged hit in the filtered selection. Ability, Self/Pet attribution and timestamp are shown when retained details are available."};
        card.ToolTip=help+(actor=="Max"?"":"\n"+DisplayCadence.DpsHelp);number.ToolTip=card.ToolTip;badge.ToolTip=card.ToolTip;return card;
    }
    private void RefreshScopeContext()
    {
        var view = Controller.DpsView(); var p = Controller.Profile.Meter; var fight = p.Scope == "History" ? (Controller.Published?.History ?? view.Fights).FirstOrDefault(f => f.Id == p.EncounterId) : view.Fights.FirstOrDefault();
        string text = p.Scope == "Session" ? view.Session is { } saved ? $"Session {saved.Start:MMM/dd HH:mm} · {view.Fights.Count} fights · {saved.Evidence}" : "Session · saved evidence loading" : fight == null ? "No retained fight in this selection" : $"{(p.Scope == "History" ? "Pinned fight" : "Latest fight")} · {(fight.Targets.Length > 0 ? fight.Targets : "Targets unavailable")} · {fight.Zone} · {fight.Start:MMM/dd HH:mm:ss}";
        string ended=Controller.SelectedSession is {} contextSession?Controller.SelectedSessionLive?"Session Ended: In progress":$"Session Ended: {contextSession.LastObserved:yyyy-MM-dd HH:mm:ss} (last observed; logout unverified)":"Session Ended: Unknown";
        string evidence=text+"\n"+ended+"\n"+Controller.SessionImportStatus+"\n"+Controller.LabNamedCoverage();damageStats.SetContext(evidence);healingStats.SetContext(evidence);
        foreach(var number in new TextBlock[]{combined,player,pet})if(number.Parent is FrameworkElement parent) { string meaning=ReferenceEquals(number,combined)?"Total · Self + verified owned pet":ReferenceEquals(number,player)?"Self · character damage only":"Pet · verified ownership at event time";string help=meaning+"\n"+DisplayCadence.DpsHelp+"\n"+text;number.ToolTip=help;if(parent.Parent is Border card)card.ToolTip=help; }
        latestFight.Visibility = p.Scope == "History" || p.GroupPeriodId.Length>0 ? Visibility.Visible : Visibility.Collapsed;
        string element = ReferenceEquals(badgeView,view)?cachedBadgeElement:PetIdentityResolver.DisplayFamily(Controller.Profile,view.Fights,p.Scope=="History"||!Controller.LabBrowsing.FollowCurrent);
        badgeView=view;cachedBadgeElement=element;
        foreach (var badge in summaryBadges.Where(b => b.Tag?.ToString() == "Pet")) badge.Set("Pet", PetElementDetector.HasFamilyIcon(element), (element=="Unknown"?"Pet type unknown or multiple pet types in this selection":"Pet · "+element)+"\n"+(p.Scope=="History"||!Controller.LabBrowsing.FollowCurrent?"Recorded pet evidence for this selection":PetIdentityResolver.SessionEvidence(Controller.Profile,view.Fights)), element);
    }
    private void UpdateMaxHit(DpsViewState view)
    {
        var peak = view.Peaks.OrderByDescending(p => p.Damage).ThenBy(p => p.Time).FirstOrDefault();
        max.ToolTip = peak == null ? "Largest hit unavailable in this selection." : $"Max hit · {peak.Damage:N0} · {peak.Ability} · {(peak.Actor == "Player" ? "Self" : peak.Actor)} · {peak.Time:MMM/dd HH:mm:ss}";
        maxCaption.Text=peak==null?"":peak.Ability+" · "+(peak.Actor=="Player"?"Self":peak.Actor);maxCaption.ToolTip=max.ToolTip;
        if(max.Parent is FrameworkElement row&&row.Parent is FrameworkElement stack&&stack.Parent is Border card){card.ToolTip=max.ToolTip;foreach(var icon in Usability.Tree(card).OfType<MaxHitGlyph>())icon.ToolTip=max.ToolTip;}
    }
    private UIElement HistoryActions()
    {
        var actions = new Grid { Margin = new(0, 8, 0, 0) }; actions.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); actions.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var items = new (string Label, Action Run, string Help)[] {
            ("Inspect fight", () => { if (history.SelectedItem is EncounterHistoryRow r) { Controller.PinFight(r.Id); lastPresented = null; ChangeFilters(); } }, "Pin this saved fight across Breakdown, Metrics and Group without modifying it."),
            ("Load older", () => Controller.LoadOlder(), "Load the next retained encounter page; selected sessions load their full saved range independently."),
            ("Merge previous", () => EditHistory("Merge"), "Merge with the preceding encounter. Existing history Undo reverses this edit."),
            ("Split fight", () => EditHistory("Split"), "Choose an explicit time to split this encounter. Damage and healing records are preserved."),
            ("Undo edit", () => EditHistory("Undo"), "Reverse the latest saved history split or merge."),
            ("Delete fight…", DeleteFight, "Delete the selected encounter after confirmation. This is separate from reversible split/merge edits.") };
        for (int i = 0; i < items.Length; i++) { if (i % 2 == 0) actions.RowDefinitions.Add(new() { Height = GridLength.Auto }); var item = items[i]; var button = UI.Button(item.Label, item.Run); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.ToolTip = item.Help; button.Margin = new(2, i >= 4 ? 8 : 2, 4, 4); Grid.SetColumn(button, i % 2); Grid.SetRow(button, i / 2); actions.Children.Add(button); }
        return UI.Card(UI.Scroll(UI.Stack(UI.Label("Selected encounter"), historyInfo, actions)));
    }
    private void RefreshHistorySelection()
    {
        if (SelectedTab != CombatLabTab.History) return;
        var source = Controller.LabHistory(Controller.Published?.History ?? Controller.DpsView().Fights); string key = mode + ":" + System.Text.Json.JsonSerializer.Serialize(Controller.Profile.Healing) + ":" + string.Join('|',source.Select(f=>f.Id+":"+f.End.Ticks+":"+f.Damage)) + ":" + Controller.Sessions?.ContentRevision;
        if (key == shownHistory && !Controller.Demo) return; shownHistory = key;
        bool prior = updating; updating = true;
        var old = (history.SelectedItem as EncounterHistoryRow)?.Id;
        var frozen = source.ToArray(); var selectedMode = mode; var filter = Controller.Profile.Healing.Filter(); var order = Controller.Profile.Healing.Sort; var sessions = Controller.Sessions?.Sessions;
        historyWork?.Cancel(); historyWork = new(); var cancellation = historyWork.Token;
        var oldCache = historyRowsCache; string filterKey = selectedMode + System.Text.Json.JsonSerializer.Serialize(Controller.Profile.Healing);
        List<EncounterHistoryRow> Build() => frozen.OrderByDescending(f => f.Start).Select(f =>
        {
            cancellation.ThrowIfCancellationRequested(); string classes = SessionEvidence.ClassesFor(SessionEvidence.For(sessions ?? [], f.Start), f);
            if (historyCacheFilter == filterKey && oldCache.TryGetValue(f.Id, out var cached) && ReferenceEquals(cached.Fight, f) && cached.Classes == classes) return cached;
            var h = selectedMode == CombatLabMode.Healing ? HealingMetrics.Build([f], [], filter, order) : null;
            return new EncounterHistoryRow(f, selectedMode == CombatLabMode.Damage ? f.Damage : h?.TotalsAvailable == true ? h.Effective : null, selectedMode == CombatLabMode.Damage ? f.Dps : h?.TotalsAvailable == true ? h.Hps : null, classes);
        }).ToList();
        void Apply(List<EncounterHistoryRow> rows)
        {
            if (shownHistory != key) return;
            historyRowsCache = rows.ToDictionary(r => r.Id); historyCacheFilter = filterKey;
            bool wasUpdating = updating; updating = true; UI.SetRows(history, rows); history.Columns[4].Header = selectedMode == CombatLabMode.Healing ? "Healing" : "Damage"; history.Columns[5].Header = selectedMode == CombatLabMode.Healing ? "HPS" : "DPS";
            history.SelectedItem = rows.FirstOrDefault(r => r.Id == old) ?? rows.FirstOrDefault(r => r.Id == Controller.Profile.Meter.EncounterId) ?? rows.FirstOrDefault(); updating = wasUpdating; UpdateHistoryDetail();
        }
        updating = prior;
        if (Controller.Demo) Apply(Build());
        else _ = Task.Run(Build, cancellation).ContinueWith(task => { if (task.IsCompletedSuccessfully) Dispatcher.InvokeAsync(() => Apply(task.Result)); }, TaskScheduler.Default);
    }
    private CancellationTokenSource? historyWork;
    private IReadOnlyDictionary<string, EncounterHistoryRow> historyRowsCache = new Dictionary<string, EncounterHistoryRow>();
    private string historyCacheFilter = "";
    private UIElement ComparisonTable()
    {
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); grid.Children.Add(UI.Label("Damage samples · existing denominators")); Grid.SetRow(comparisons, 1); grid.Children.Add(comparisons); return grid;
    }
    private void UpdateHistoryDetail()
    {
        if (history.SelectedItem is not EncounterHistoryRow row) { historyInfo.Text = "Select a saved fight. Headers sort the retained encounter list."; return; }
        var f = row.Fight; historyInfo.Text = $"{f.Start:MMM/dd HH:mm:ss} · {f.Zone}\n{row.Targets}\nClasses: {row.Classes}\nWeapons: {f.Weapons}\nBuff notes: {f.BuffNotes}" + (f.DetailAvailable ? "" : "\nSummary only; event details unavailable.");
    }
    private void UpdateComparisonDetail()
    {
        comparisonDetails.Children.Clear(); comparisonDetails.Children.Add(UI.Label("Selected damage sample"));
        if (comparisons.SelectedItem is not LoadoutRow row) { comparisonDetails.Children.Add(UI.Text("Select a loadout sample to inspect its conditions and denominators.", 12)); return; }
        comparisonDetails.Children.Add(UI.Text(row.Loadout, 15, "Accent", true));
        comparisonDetails.Children.Add(UI.Text($"Pet: {row.Pet}\nWeapons: {(row.Weapons.Length > 0 ? row.Weapons : "Not recorded")}\nBuff notes: {(row.Buffs.Length > 0 ? row.Buffs : "Not recorded")}\n{row.Samples:N0} encounters · {row.Seconds:N0}s summed combat\nTotal DPS {row.CombinedDps:N1}\nSelf DPS {row.PlayerDps:N1} · Pet DPS {row.PetDps:N1}", 12));
        comparisonDetails.Children.Add(UI.Text("Existing damage sample denominators and filters. Equipment tags are not verified class combinations; different targets, conditions and log coverage prevent causal performance claims.", 11, "SecondaryText"));
    }
}
