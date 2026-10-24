using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class StatProfileDialog : FrostWindow
{
    private sealed class StatRow
    {
        public string Stat { get; set; } = "";
        public string Total { get; set; } = "";
        public bool Raw { get; set; }
        public string Overflow { get; set; } = "";
        public string Worn { get; set; } = "";
    }
    public StatProfileDialog(TrackerController controller, LoadoutProfile loadout, bool pet, List<EquipmentPosition> positions, Action saved, PetBuild? build = null, Action<bool>? aaCompare = null, bool withoutAAs = false)
        : base(controller, pet ? "Pet stat profiles" : "Self stat profiles", "Game evidence fills known fields. Confirm missing totals, usual buffs and limits.", 900, 650)
    {
        MinWidth = 640; MinHeight = 420;
        var profiles = build?.StatProfiles ?? (pet ? loadout.PetStatProfiles : loadout.StatProfiles);
        var context = UI.Select(pet ? ["Unbuffed"] : ["Unbuffed", "Usual buffs"], "Unbuffed", 155);
        var level = UI.Input((pet ? build?.PetLevel ?? loadout.PetLevel : loadout.Level)?.ToString() ?? "", 55);
        var classes = UI.Input(string.Join('/', loadout.Classes), 150);
        if (pet) classes.IsReadOnly = true;
        var aaIncluded = new CheckBox { Content = "AA bonuses already in totals", IsChecked = true };
        var aaComplete = new CheckBox { Content = "AA list complete", IsChecked = false };
        aaComplete.ToolTip = "Confirm that all AAs relevant to this comparison are listed. Unknown ranks remain unknown; use explicit 0 for untrained AAs. Required for an AA-off what-if.";
        var buffsIncluded = new CheckBox { Content = "Buff bonuses already in totals", IsChecked = true };
        var buffs = UI.Input("", 220); var source = UI.Input("Confirmed in-game totals", 220);
        var spell = UI.Input(build?.SummonSpell ?? loadout.PetSummonSpell, 190); var summonRank = UI.Input((build?.SummonRank ?? loadout.PetSummonRank).ToString(), 50); var focus = UI.Input(build?.Focus ?? loadout.PetFocus, 160);
        var status = UI.Text("", 11, "Violet");
        var gameStatus=UI.Text("",11,"Mint");var evidenceNote=UI.Text("Logs do not contain a complete stat or active-buff snapshot. Worn values are partial known-item sums, excluding base stats, buffs, AAs and effects.",11,"Muted");
        gameStatus.TextWrapping=TextWrapping.NoWrap;gameStatus.TextTrimming=TextTrimming.CharacterEllipsis;
        long evidenceRevision=-1;bool loading=false,editedContext=false;var editedAAs=new HashSet<string>();
        level.TextChanged+=(_,_)=>{if(!loading)editedContext=true;};classes.TextChanged+=(_,_)=>{if(!loading)editedContext=true;};
        var effects=UI.Table(("Effect / candidates","Name",185,null),("For","Actor",110,null),("Last evidence","State",225,null),("When","At",115,"MMM/dd HH:mm"));
        var receivedHistory=UI.Text("",11,"SecondaryText");
        var rows = new ObservableCollection<StatRow>(); var limits = new ObservableCollection<StatLimit>(); var aas = new ObservableCollection<GearAaModifier>();
        var totals = UI.Table(("Stat", "Stat", 85, null), ("Total", "Total", 78, null), ("Overflow", "Overflow", 78, null)); totals.IsReadOnly = false;
        totals.Columns[0].IsReadOnly = true; totals.Columns.Add(new DataGridCheckBoxColumn { Header = "Raw known", Binding = new Binding("Raw") { Mode = BindingMode.TwoWay }, Width = 80 }); totals.ItemsSource = rows;
        totals.Columns.Insert(2,new DataGridTextColumn { Header="Gear est.",IsReadOnly=true,Binding=new Binding("Worn"),Width=78 });
        var caps = UI.Table(("Stat", "Stat", 85, null), ("Limit", "Value", 75, null), ("Above soft cap ×", "AboveCapWeight", 110, null), ("Evidence / override", "Source", 230, null)); caps.IsReadOnly = false; caps.CanUserAddRows = true; caps.CanUserDeleteRows = true;
        caps.Columns.Insert(1, new DataGridComboBoxColumn { Header = "Kind", ItemsSource = Enum.GetValues<StatLimitKind>(), SelectedItemBinding = new Binding("Kind") { Mode = BindingMode.TwoWay }, Width = 150 });
        caps.Columns.Add(new DataGridCheckBoxColumn { Header = "Verified", Binding = new Binding("Confirmed") { Mode = BindingMode.TwoWay }, Width = 85 });
        caps.Columns.Add(new DataGridCheckBoxColumn { Header = "Includes AAs", Binding = new Binding("IncludesAAs") { Mode = BindingMode.TwoWay }, Width = 95 }); caps.ItemsSource = limits;
        var aaTable = UI.Table(("AA", "Name", 180, null), ("Stat", "Stat", 70, null), ("Total bonus at rank", "Value", 120, null), ("Evidence", "Source", 230, null)); aaTable.IsReadOnly = false; aaTable.CanUserAddRows = true; aaTable.CanUserDeleteRows = true;
        aaTable.Columns[0].MinWidth=120;
        var rankFactory = new FrameworkElementFactory(typeof(AaRankEditor));
        rankFactory.AddHandler(LoadedEvent, new RoutedEventHandler((sender, _) => { var editor = (AaRankEditor)sender; if (editor.Tag is true) return; editor.Tag = true; editor.Changed += aa => { editedAAs.Add(Identity.Key(aa.Name)); status.Text = aa.Confirmed || aa.Kind == AaModifierKind.Information ? "AA rank changed · confirm profile to save." : "AA rank changed · numeric bonus needs confirmation."; }; }));
        aaTable.Columns.Insert(1, new DataGridTemplateColumn { Header = "Rank", CellTemplate = new DataTemplate { VisualTree = rankFactory }, Width = 95, MinWidth = 90, IsReadOnly = true });
        aaTable.Columns.Add(new DataGridComboBoxColumn { Header = "Model", ItemsSource = Enum.GetValues<AaModifierKind>(), SelectedItemBinding = new Binding("Kind") { Mode = BindingMode.TwoWay }, Width = 140 });
        aaTable.Columns.Add(new DataGridCheckBoxColumn { Header = "Verified", Binding = new Binding("Confirmed") { Mode = BindingMode.TwoWay }, Width = 85 }); aaTable.ItemsSource = aas;
        aaTable.Columns.Insert(2,new DataGridTextColumn{Header="Rank evidence",Binding=new Binding("RankEvidence"),IsReadOnly=true,Width=220});
        aaTable.CellEditEnding+=(_,e)=>{if(loading || e.Row.Item is not GearAaModifier aa)return;editedAAs.Add(Identity.Key(aa.Name));if(e.Column.SortMemberPath=="Value") aa.ValueManuallyOverridden = true; if(e.Column.SortMemberPath=="Rank"){aa.RankObservedAt=null;aa.RankEvidence="Manual override";}};
        foreach (var column in aaTable.Columns.Where(c => c.SortMemberPath is "Source" or "RankEvidence" or "Value" or "Kind")) column.Visibility = Visibility.Collapsed;
        var aaDetail = new StackPanel();
        void AaField(string label, string path, string tooltip)
        {
            var input = UI.Input("", 200); input.SetBinding(TextBox.TextProperty, new Binding("SelectedItem." + path) { Source = aaTable, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); input.ToolTip = tooltip; aaDetail.Children.Add(UI.Stack(UI.Label(label), input));
            if (path == "Value") input.TextChanged += (_, _) => { if (input.IsKeyboardFocusWithin && aaTable.SelectedItem is GearAaModifier aa) { aa.ValueManuallyOverridden = true; editedAAs.Add(Identity.Key(aa.Name)); } };
        }
        AaField("Numeric bonus at rank", "Value", "Total confirmed bonus at this rank, not per rank. Editing creates a manual override; no unknown rank scaling is inferred.");
        AaField("Stat", "Stat", "Affected stat or cap; proc influence without a verified formula stays informational.");
        AaField("Source", "Source", "Evidence for the numerical model or your explicit manual override.");
        var model = new ComboBox { ItemsSource = Enum.GetValues<AaModifierKind>(), Width = 180 }; model.SetBinding(ComboBox.SelectedItemProperty, new Binding("SelectedItem.Kind") { Source = aaTable, Mode = BindingMode.TwoWay }); model.ToolTip = "Information does not change scores. Numeric models require confirmed current EQL rules."; aaDetail.Children.Add(UI.Field("Model", model));
        var rankEvidence = UI.Text("", 10, "Muted"); rankEvidence.SetBinding(TextBlock.TextProperty, new Binding("SelectedItem.RankEvidence") { Source = aaTable }); aaDetail.Children.Add(rankEvidence);
        foreach (var table in new[] { totals, caps, aaTable })
        {
            table.MinHeight = 160;
            foreach (var col in table.Columns.OfType<DataGridBoundColumn>()) if (col.Binding is Binding b) { b.Mode = BindingMode.TwoWay; b.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged; }
            foreach (var col in table.Columns.OfType<DataGridCheckBoxColumn>())
            { var style = new Style(typeof(CheckBox),(Style)Application.Current.FindResource(typeof(CheckBox))); style.Setters.Add(new Setter(HorizontalAlignmentProperty,HorizontalAlignment.Center)); col.ElementStyle = style; col.EditingElementStyle = style; }
        }
        void Load()
        {
            loading=true;editedContext=false;editedAAs.Clear();
            var profile = profiles.FirstOrDefault(p => p.Name == context.SelectedItem?.ToString()); rows.Clear(); limits.Clear(); aas.Clear();
            var worn=StatProfilePrefill.WornContributions(controller.Catalog,positions);
            foreach (var name in new[] { "STR", "STA", "AGI", "DEX", "INT", "WIS", "CHA", "HP", "Mana", "AC", "ATK", "Haste", "HPRegen", "ManaRegen", "ResistFire", "ResistCold", "ResistMagic", "ResistPoison", "ResistDisease" }.Concat(profile?.Stats.Keys.AsEnumerable() ?? []).Distinct())
            { var obs = profile?.Stats.GetValueOrDefault(name); rows.Add(new() { Stat = name, Total = obs?.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", Overflow = obs?.Overflow?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", Raw = obs?.UncappedTotalKnown ?? false,Worn=worn.TryGetValue(name,out double amount)?amount.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture):"—" }); }
            foreach (var limit in profile?.Limits ?? []) limits.Add(EquipmentPositions.Copy(limit));
            foreach (var aa in profile?.AAs ?? GearMechanicsEvidence.RelevantAAs(pet)) aas.Add(EquipmentPositions.Copy(aa));
            aaIncluded.IsChecked = profile?.AAsIncludedInTotals ?? true; buffsIncluded.IsChecked = profile?.BuffsIncludedInTotals ?? true; buffs.Text = profile?.Buffs ?? ""; source.Text = profile?.Source ?? "Confirmed in-game totals";
            aaComplete.IsChecked = profile?.AAListComplete ?? false;
            if(profile!=null){level.Text=profile.Level.ToString();classes.Text=string.Join('/',profile.Classes);}else if(!pet){var known=controller.StatEvidenceSnapshot();level.Text=(known.Level ?? loadout.Level).ToString();classes.Text=string.Join('/',known.Classes.Count>0?known.Classes:loadout.Classes);source.Text="Game log evidence; totals manually confirmed";}
            status.Text = profile?.ConfirmedAt is { } date ? "Last confirmed " + date.ToString("g") + ". Saving anchors these totals to the current confirmed equipment." : "Not confirmed. Fill known totals; leave the rest blank.";
            loading=false;evidenceRevision=-1;FillEvidence();
        }
        void FillEvidence()
        {
            long connectedRevision=controller.StatEvidenceRevision+controller.ConnectedSpellRevision;if(evidenceRevision==connectedRevision)return;
            var evidence=controller.StatEvidenceSnapshot();evidenceRevision=connectedRevision;
            receivedHistory.Text=controller.ReceivedSpellEvidence(pet);
            loading=true;int added=StatProfilePrefill.MergeAAs(aas,evidence,pet,editedAAs);loading=false;if(added>0)aaTable.Items.Refresh();
            var relevant=evidence.Effects.Where(e=>e.Owner==(pet?"Pet":"Self")).OrderByDescending(e=>e.At).ToList();string? selectedEffect=(effects.SelectedItem as EffectEvidence)?.Key;UI.SetRows(effects,relevant);effects.SelectedItem=relevant.FirstOrDefault(e=>e.Key==selectedEffect);
            gameStatus.Text=$"Game evidence · {evidence.AAs.Values.Count(a=>a.Rank!=null)} exact AA ranks · {evidence.AAs.Values.Count(a=>a.Rank==null)} learned (rank unknown) · {evidence.Status}";
            gameStatus.ToolTip=gameStatus.Text+$"\nLatest own /who: {evidence.ClassesAt:g}"+"\n"+evidence.SpellSource+"\nHistorical ranks may predate an unlogged respec. Unknown/absent AAs are never zero. Spell landing/fade messages do not establish a complete active buff list.";
            if(profiles.All(p=>p.Name!=context.SelectedItem?.ToString()) && !pet && !editedContext && evidence.Level.HasValue)
            {loading=true;level.Text=evidence.Level.Value.ToString();if(evidence.Classes.Count>0)classes.Text=string.Join('/',evidence.Classes);loading=false;}
        }
        context.SelectionChanged += (_, _) => Load();
        var tab = new TabControl();
        tab.Items.Add(new TabItem { Header = "TOTALS", Content = totals });
        UIElement Page(UIElement heading,UIElement table){var panel=new DockPanel();DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);panel.Children.Add(table);return panel;}
        tab.Items.Add(new TabItem { Header = "CAPS / LIMITS", Content = Page(UI.Text("No caps are prefilled. Confirm a cap only from current EQL evidence or an explicit override. AC soft caps use worn item AC, not displayed composite AC. Proc limits never cap DEX itself.", 11, "Muted"), caps) });
        tab.Items.Add(new TabItem { Header = "AAs", Content = Page(UI.Text("Logged exact ranks prefill automatically; learned abilities without a rank stay unknown. Purchase evidence never supplies a numeric AA model or proves an absent AA is untrained.", 11, "Muted"), UI.Stack(aaTable, new Expander { Header = "Selected AA · bonus & evidence", Content = aaDetail })) });
        var useEffect=UI.Button("Use selected in Usual buffs",()=>
        {
            if(context.SelectedItem?.ToString()!="Usual buffs"){status.Text="Select Usual buffs first; effect history never defines an Unbuffed profile.";return;}
            if(effects.SelectedItem is not EffectEvidence selected || selected.Spells.Count!=1){status.Text="Choose an unambiguous effect, then confirm it belongs in your usual buffs.";return;}
            if(pet && build!=null && build.PetType!="Unknown" && build.PetType!=controller.Profile.EffectivePetElement){status.Text="This plan differs from the live pet. Confirm its buffs manually.";return;}
            string name=selected.Spells[0];if(!buffs.Text.Split(';',StringSplitOptions.TrimEntries).Contains(name,StringComparer.OrdinalIgnoreCase))buffs.Text=string.Join("; ",new[]{buffs.Text,name}.Where(s=>s.Length>0));
        });
        useEffect.ToolTip="Copy this observed effect's name into your usual-buff notes after reviewing it. Does not assert it is currently active or calculate bonuses.";
        effects.ToolTip="Client spell messages can be shared by several spells. Ambiguous candidates remain unresolved. Effect history may include detrimental spells; no active duration or numeric bonus is guessed.";
        tab.Items.Add(new TabItem{Header="GAME EVIDENCE",Content=Page(UI.Stack(evidenceNote,UI.Row(UI.Button("Refresh game files",()=>{controller.RefreshStatEvidence();evidenceRevision=-1;FillEvidence();}),useEffect),FocusedLayout.Disclosure("Received buff history",receivedHistory,"Historical landing/fade transitions and unresolved coverage; never a complete current buff roster.")),effects)});
        var body = new DockPanel();
        var top = new WrapPanel(); foreach (var child in new UIElement[] { pet ? UI.Stack(UI.Label("Context"), UI.Text("Pet stats", 12)) : UI.Field("Context", context), UI.Field("Level", level), UI.Field("Classes", classes), UI.Field("Buff notes", buffs), UI.Field("Observation source", source) }) { if(child is FrameworkElement e)e.Margin=new(0,0,6,0); top.Children.Add(child); }
        if (pet) { buffsIncluded.Visibility = Visibility.Collapsed; buffs.Visibility = Visibility.Collapsed; }
        if (pet) foreach (var child in new UIElement[] { UI.Field("Summon spell", spell), UI.Field("Spell +rank", summonRank), UI.Field("Focus", focus) }) top.Children.Add(child);
        var flags=new WrapPanel();foreach(var control in new[]{aaIncluded,buffsIncluded,aaComplete})flags.Children.Add(control);
        var compare = new CheckBox { Content = "Compare without AAs", IsChecked = withoutAAs, ToolTip = "Temporarily remove only verified reversible AA bonuses from comparisons. Unknown or informational AA effects make the AA-off comparison unavailable." };
        compare.Click += (_, _) => aaCompare?.Invoke(compare.IsChecked == true);
        var advanced = new Expander { Header = "Advanced comparisons", Content = compare, Visibility = aaCompare == null ? Visibility.Collapsed : Visibility.Visible };
        var header = UI.Stack(top, flags, gameStatus, advanced); DockPanel.SetDock(header, Dock.Top); body.Children.Add(header);
        var save = UI.Button("Confirm this profile", () =>
        {
            foreach (var grid in new[] { totals, caps, aaTable }) { if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) { status.Text = "Correct the highlighted cell before saving."; return; } }
            if (!int.TryParse(level.Text, out int lev) || lev is < 1 or > 100 || !int.TryParse(summonRank.Text, out int rank) || rank is < 0 or > 20) { status.Text = "Level must be 1–100; spell +rank 0–20."; return; }
            var classNames = classes.Text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToUpperInvariant()).Distinct().ToList();
            if (classNames.Count is < 1 or > 3 || classNames.Any(x => !"WAR CLR PAL RNG SHD DRU MNK BRD ROG SHM NEC WIZ MAG ENC BST BER".Split(' ').Contains(x))) { status.Text = "Use 1–3 class abbreviations separated by /."; return; }
            var stats = new Dictionary<string, StatObservation>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows.Where(r => r.Total.Length > 0))
            {
                if (!Number(row.Total, out double value) || row.Overflow.Length > 0 && (!Number(row.Overflow, out _) || double.Parse(row.Overflow, System.Globalization.CultureInfo.InvariantCulture) < 0)) { status.Text = "Use finite numbers for totals and nonnegative overflow."; return; }
                stats[row.Stat] = new() { Value = value, UncappedTotalKnown = row.Raw, Overflow = row.Overflow.Length > 0 ? double.Parse(row.Overflow, System.Globalization.CultureInfo.InvariantCulture) : null };
            }
            if (stats.Count == 0) { status.Text = "Enter at least one observed stat total."; return; }
            if (limits.Any(l => string.IsNullOrWhiteSpace(l.Stat) || !double.IsFinite(l.Value) || l.Value <= 0 || !double.IsFinite(l.AboveCapWeight) || l.AboveCapWeight is < 0 or > 1 || l.Confirmed && string.IsNullOrWhiteSpace(l.Source))) { status.Text = "Limits need a stat, positive value, above-cap factor 0–1 and an evidence/override source."; return; }
            if (aas.Any(a => a.Rank < 0 || a.VerifiedMaxRank.HasValue && a.Rank > a.VerifiedMaxRank || !double.IsFinite(a.Value) || a.Confirmed && string.IsNullOrWhiteSpace(a.Source))) { status.Text = "AA ranks must be nonnegative and within any verified maximum, or blank; confirmed models require a source."; return; }
            loadout.Classes = classNames; if (!pet) loadout.Level = lev; else { loadout.PetLevel = lev; loadout.PetSummonSpell = spell.Text.Trim(); loadout.PetSummonRank = rank; loadout.PetFocus = focus.Text.Trim(); }
            if (build != null) { build.PetLevel = lev; build.SummonSpell = spell.Text.Trim(); build.SummonRank = rank; build.Focus = focus.Text.Trim(); }
            var profile = new GearStatProfile { GameEvidenceReviewedAt=DateTime.Now,Name = context.SelectedItem?.ToString() ?? "Unbuffed", Level = lev, Classes = classNames, ConfirmedAt = DateTime.Now, EquipmentFingerprint = EquipmentPositions.Fingerprint(positions), PetSignature = pet ? LoadoutEvaluationSession.PetSignature(loadout) : "", Stats = stats, Limits = limits.ToList(), AAs = aas.Where(a => a.Name.Length > 0).Select(a => { a.Target = pet ? "Pet" : "Self"; return a; }).ToList(), Buffs = buffs.Text.Trim(), Source = source.Text.Trim(), AAListComplete = aaComplete.IsChecked == true, AAsIncludedInTotals = aaIncluded.IsChecked == true, BuffsIncludedInTotals = buffsIncluded.IsChecked == true };
            profiles.RemoveAll(p => p.Name == profile.Name); profiles.Add(profile); controller.Save(); saved(); Load();
        }, true);
        var footer = UI.Stack(status, UI.Row(save, UI.Button("Close", Close))); DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer); body.Children.Add(tab); Body.Children.Add(body); Appearance.RaisedButtons(Body);
        if (pet && profiles.Any(p => p.Name == "Usual buffs")) { var legacy = new Expander { Header = "Legacy buff context · retained", Content = UI.Text("Previous Usual buffs profiles remain saved for compatibility. Pet comparisons now use one equipment context.", 11, "Muted") }; DockPanel.SetDock(legacy, Dock.Bottom); body.Children.Insert(0, legacy); }
        totals.ToolTip = "Copy current in-game totals. Known overflow is extra stat hidden above a cap; leave it blank if unknown. Do not enter item-only values here.";
        caps.ToolTip = "Explicit context-specific limits. A hard attribute cap, AC soft cap and proc limit are separate rules."; aaTable.ToolTip = "Unknown rank differs from zero. Totals already including AAs are never increased twice.";
        aaIncluded.ToolTip = "Normally on for stats copied from the game. Turn off only for a known baseline excluding these confirmed bonuses."; buffsIncluded.ToolTip = "Buffed totals must already include the saved buffs; notes alone do not add stat bonuses.";
        Load(); Loaded += (_, _) => Usability.Tooltips(Body);
        var refreshTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        refreshTimer.Tick+=(_,_)=>{if(!IsVisible || aaTable.IsKeyboardFocusWithin)return;FillEvidence();};Loaded+=(_,_)=>refreshTimer.Start();Closed+=(_,_)=>refreshTimer.Stop();
    }
    private static bool Number(string value, out double number) => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
}
