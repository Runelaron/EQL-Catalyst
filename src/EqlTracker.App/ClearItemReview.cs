using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;
namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewClearItems(string folder)
    {
        if (!Controller.Demo) throw new InvalidOperationException("An isolated preview profile is required.");
        Directory.CreateDirectory(Path.Combine(folder, "qa")); var checks = new List<string>(); var samples = new List<object>();
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks.Add(name); }
        void Flush() { Refresh(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); }
        void Capture(string name, FrameworkElement element, double width = 1220, double height = 700)
        {
            if (ReferenceEquals(element, Content)) { Width = width; Height = height; Flush(); }
            foreach (double scale in new[] { 1d, 1.5, 2, 3 }) { var image = PreviewRenderer.Capture(element, width, height, scale); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(Path.Combine(folder, "qa", $"clear-items-{name}-{scale:0.0}x.png")); encoder.Save(file); }
        }
        void GearFind(string query) { gearSearch.Text = query; Flush(); gearTable.SelectedItem = gearTable.Items.OfType<GearRow>().FirstOrDefault(r => r.Item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)); Flush(); }
        void ExitAnswer(string label) => Dispatcher.BeginInvoke(new Action(() => { var dialog = Application.Current.Windows.OfType<TheorycraftExitDialog>().Single(); Usability.Tree(dialog).OfType<Button>().Single(b => b.Content?.ToString() == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }), DispatcherPriority.ApplicationIdle);
        try
        {
            var started = Stopwatch.GetTimestamp(); GearBuildFixtures.Apply(Controller); var owner = Controller.Profile.ActiveLoadout;
            owner.GearBuilds = []; owner.SelectedGearBuildId = "current"; owner.CurrentGearUndo = []; owner.GearBuildVersion = 1;
            Controller.Profile.PanelSplits.Clear(); Controller.Profile.ColumnWidths.Clear(); Controller.Profile.SmoothMeterMotion = false;
            Controller.Inventory.Entries.AddRange(new[] { ("Red Dragonscale Armor", 3, "BankA"), ("Red Dragonscale Armor", 6, "BankB"), ("Indicolite Breastplate", 5, "BagA") }.Select(x => new InventoryEntry { Name = x.Item1 + " +" + x.Item2, Location = x.Item3, Count = 1, ConfirmedItemKey = Controller.Catalog.Find(x.Item1)!.Key })); Controller.Inventory.Invalidate();
            owner.ConfirmedPetInventory = new() { Name = "Current build", Preset = "Pet DPS", PetType = "Earth", PetLevel = 50, LevelMode = PetLevelMode.Manual, Items = new[] { ("Sunderfury",4), ("Indicolite Breastplate",5), ("Indicolite Bracer",7), ("Indicolite Greaves",7), ("Cloak of Flames",4), ("Icebelt",3), ("Indicolite Vambraces",7) }.Select((x,i) => new PetInventoryItem { Id = "clear-"+i, ItemKey = Controller.Catalog.Find(x.Item1)!.Key, Name = x.Item1, Position = i+1, Rank = x.Item2, Slot = "Storage", ConfirmedAt = DateTime.Now }).ToList() };
            owner.PetBuilds = []; owner.SelectedPetBuildId = "current"; Controller.Save(); RefreshGearBuildChoices("current");
            Width = 1220; Height = 700; ShowView(TrackerTab.Gear); SelectGearPosition("CHEST:1"); GearFind("Red Dragonscale Armor");
            Check(!GearPet && gearTarget.Parent == null, "Gear is Self-only with no visible target selector");
            Check(gearOwnership.Items.Cast<string>().SequenceEqual(new[] { "All", "Inventory Only", "Have Drops", "Have Ingredients", "Quest rewards" }), "Items filter exposes separate quest and crafting component choices");
            Check(gearRankChoice.Items.Count == 12 && gearRankChoice.Items[0]?.ToString() == "Default", "List rank defaults to recorded/base with +0–10 previews");
            Check(gearEquipment.Columns.Select(c => c.Header?.ToString()).SequenceEqual(new[] { "Slot", "Equipment", "Type" }) && gearTable.Columns.Any(c => c.Header?.ToString() == "Type"), "Gear equipment and candidate tables include precise Type columns");
            Check(gearPosition.Items.OfType<EquipmentPosition>().First().ToString() == "All slots", "All slots sentinel has no unknown-confirmation suffix");
            Check(gearBuildChoice.SelectedValue?.ToString() == "current" && gearUpdateCurrent.IsChecked == true, "Gear defaults to Current and automatic export updates");
            Check(gearTable.SelectedItem is GearRow { Rank: 6 } && gearTable.Items.Count == 1, "All groups owned copies and selects the highest recorded rank");
            Controller.Inventory.Entries.Add(new() { Name = "Sheet Metal", Location = "BankCraft", Count = 1 }); Controller.Inventory.Entries.Add(new() { Name = "Glowing Mask", Location = "BankQuest", Count = 1 }); Controller.Inventory.Invalidate();
            gearOwnership.SelectedIndex = 3; GearFind("Banded Mail"); Check(gearTable.Items.OfType<GearRow>().Any(r => r.Item.Key == "banded mail" && r.Ownership.Contains(" / 5")), "Have Ingredients shows actual sourced craft output and known ingredient quantities (" + string.Join("; ", gearTable.Items.OfType<GearRow>().Select(r => r.Item.Key + ": " + r.Ownership)) + ")");
            gearOwnership.SelectedIndex = 2; SelectGearPosition("FACE:1"); GearFind("Acumen Mask"); Check(gearTable.Items.OfType<GearRow>().Any(r => r.Item.Key == "acumen mask"), "Have Drops shows a quest reward when a documented component is owned");
            gearOwnership.SelectedIndex = 0; SelectGearPosition("CHEST:1"); GearFind("Red Dragonscale Armor");
            Check(Usability.Tree(gearInspector).OfType<ItemRoleGlyph>().Count(i => i.IsVisible) == 2, "Owned selected Gear item shows type and player-ownership icons");
            var card = Usability.Tree(gearInspector).OfType<ClassicItemCard>().Single(); var rating = Usability.Tree(gearInspector).OfType<BuildComparisonView>().Single(); var rowsBefore = gearTable.ItemsSource;
            gearInspector.RankSlider.Value = 10; Check(ReferenceEquals(card, Usability.Tree(gearInspector).OfType<ClassicItemCard>().Single()) && ReferenceEquals(rating, Usability.Tree(gearInspector).OfType<BuildComparisonView>().Single()), "Rank changes retain existing classic card and comparison controls"); ShowSelectedGear(); Flush();
            Check(ReferenceEquals(rowsBefore, gearTable.ItemsSource) && ((GearRow)gearTable.SelectedItem).Rank == 6 && GearInspectedRank((GearRow)gearTable.SelectedItem) == 10, "Inspected rank does not rewrite list or inventory rank");
            string inventoryBefore = JsonSerializer.Serialize(Controller.Inventory); string currentBefore = EquipmentPositions.Fingerprint(owner.EquipmentPositions); int windows = Application.Current.Windows.Count;
            TryGearDraft(); Flush(); Check(owner.EquipmentPositions.Single(p => p.Id == "CHEST:1").Item!.Rank == 10 && Application.Current.Windows.Count == windows, "Add to Current applies directly without equip-confirmation popup");
            Check(inventoryBefore == JsonSerializer.Serialize(Controller.Inventory), "Current edits do not create inventory ownership"); UndoGearDraft(); Flush(); Check(EquipmentPositions.Fingerprint(owner.EquipmentPositions) == currentBefore, "Current Undo restores the exact previous equipment");
            var plan = GearBuildEditing.Create(owner, "Raid alternative"); RefreshGearBuildChoices("current"); gearBuildChoice.SelectedValue = plan.Id; Flush(); Check(gearWorkspaceId == plan.Id && owner.SelectedGearBuildId == plan.Id, "Named build selector uses stable saved IDs");
            GearFind("Red Dragonscale Armor"); gearInspector.RankSlider.Value = 9; ShowSelectedGear(); TryGearDraft(); Flush();
            Check(plan.Positions.Single(p => p.Id == "CHEST:1").Item!.Rank == 9 && EquipmentPositions.Fingerprint(owner.EquipmentPositions) == currentBefore && gearChanges.Visibility == Visibility.Visible, "Named build edit and Build Changes preserve Current");
            Check(plan.Undo.Count > 0 && gearUseCurrent!.IsEnabled, "Ordinary build has Undo and a direct Use as current action"); Capture("gear-build", (FrameworkElement)Content);
            UndoGearDraft(); Flush(); gearBuildChoice.SelectedValue = "current"; Flush();
            gearTheoryCheck.IsChecked = true; gearTheoryCheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Check(gearTheory != null && gearTheory.Dirty && !gearUseCurrent!.IsEnabled, "Theorycraft creates an unsaved isolated copy and disables activation");
            gearTheory!.Working.Positions.Single(p => p.Id == "CHEST:1").Item!.Name = "Hypothetical +10"; GearEdited(); Check(!JsonSerializer.Serialize(owner).Contains("Hypothetical +10"), "Unrelated profile saves cannot include unsaved theory edits");
            ExitAnswer("Cancel"); Check(!ConfirmGearWorkspaceExit() && gearTheory != null, "Cancel keeps the unsaved Gear theory workspace active");
            var saved = gearTheory!.Save("Future equipment"); owner.GearBuilds.Add(saved); Controller.Save(); RefreshGearBuildChoices(saved.Id); GearEdited(); Check(!gearTheory.Dirty && saved.IsTheorycraft, "Explicit theory save retains stable designation and ID"); Capture("gear-theorycraft", (FrameworkElement)Content);
            gearTheory.Working.Positions.Single(p => p.Id == "CHEST:1").Item!.Name = "Unsaved revision"; GearEdited(); ExitAnswer("Discard"); Check(ConfirmGearWorkspaceExit() && gearTheory == null && gearWorkspaceId == "current", "Discard exits Theorycraft and returns to Current"); Check(!JsonSerializer.Serialize(owner).Contains("Unsaved revision"), "Discard preserves the explicitly saved theory snapshot");
            gearBuildChoice.SelectedValue = saved.Id; Flush(); Check(gearTheory?.SavedId == saved.Id && !gearTheory.Dirty, "Saved Theorycraft reopens as a detached clean working copy"); Check(ConfirmGearWorkspaceExit(), "Clean theory exit needs no prompt"); Flush();
            GearFind("Red Dragonscale Armor"); gearOwnership.SelectedIndex = 1; Flush(); Check(gearTable.Items.Count == 2, "Inventory Only retains separate physical copies"); gearOwnership.SelectedIndex = 0; Flush();
            GearFind("Red Dragonscale Armor"); gearInspector.RankSlider.Value = 6; ShowSelectedGear();
            previewAsyncForReview = true; for (int n = 1; n <= 10; n++) { ChangeGearPreviewRank(n); ShowSelectedGear(); } await Task.Delay(200); Flush(); Check(gearDisplayedComparison?.Candidate.Contains("+10") == true, "Rapid async Gear previews reject obsolete results"); previewAsyncForReview = false;
            samples.Add(new { Phase = "Setup", Run = 0, Rank = 0, WallMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds, UiAllocatedBytes = 0L });
            var setupDiagnostics = UpdateDiagnostics.Drain();
            for (int run = 0; run <= 3; run++) foreach (int rank in Enumerable.Range(0,11).Concat(Enumerable.Range(0,11).Reverse())) {
                long at = Stopwatch.GetTimestamp(), bytes = GC.GetAllocatedBytesForCurrentThread(); gearInspector.RankSlider.Value = rank; ShowSelectedGear(); Flush(); samples.Add(new { Phase = run == 0 ? "Warmup" : "Warm", Run = run, Rank = rank, WallMs = Stopwatch.GetElapsedTime(at).TotalMilliseconds, UiAllocatedBytes = GC.GetAllocatedBytesForCurrentThread()-bytes });
            }
            var previewDiagnostics = UpdateDiagnostics.Drain(); Capture("gear", (FrameworkElement)Content);
            var selectedRow = (GearRow)gearTable.SelectedItem; var active = visibleGearPositions.Single(p => p.Id == selectedRow.PositionId).Item!;
            var raw = ItemPairComparison.Create(Controller.Catalog.FindKey(active.ConfirmedItemKey), active, selectedRow.Item, GearInspectedRank(selectedRow), GearInspectedInstance(selectedRow), owner, Controller.Catalog, false);
            var pairWindow = new FrostWindow(Controller, "Item comparison", "Current item / candidate / differences", 750, 550) { Owner = this, Left = -15000, Top = -15000, ShowActivated = false }; var pairView = new PetPairComparisonView(raw); pairWindow.Body.Children.Add(UI.Scroll(pairView)); pairWindow.Show(); pairWindow.UpdateLayout(); Check(Usability.Tree(pairView).OfType<ItemRoleGlyph>().Count() >= 2, "Raw comparison headers share item-type and known-ownership glyphs"); Capture("comparison", (FrameworkElement)pairWindow.Content, 750, 550); pairWindow.Close();
            var roles = new GearRolesDialog(Controller.Profile, "Balanced Tank") { Owner = this, Left = -15000, Top = -15000, ShowActivated = false }; roles.Show(); roles.UpdateLayout(); Check(Usability.Tree(roles).OfType<Expander>().Count() >= 4, "Role explanation includes available Self presets and custom weights"); Capture("roles", (FrameworkElement)roles.Content, 640, 480); roles.Close();
            var formula = new RatingFormulaDialog("Balanced Tank", new(Controller.Profile.Weights["Balanced Tank"]), gearDisplayedComparison, false) { Owner = this, Left = -15000, Top = -15000, ShowActivated = false }; formula.Show(); formula.UpdateLayout(); Check(Usability.Tree(formula).OfType<TextBlock>().Any(t => t.Text == "Unbuffed") && Usability.Tree(formula).OfType<TextBlock>().Any(t => t.Text == "Usual buffs"), "Formula continues to expose both Self contexts"); Capture("formula", (FrameworkElement)formula.Content, 620, 480); formula.Close();
            var stats = new StatProfileDialog(Controller, owner, false, owner.EquipmentPositions, () => { }, aaCompare: v => gearWithoutAa.IsChecked = v) { Left = -15000, Top = -15000, ShowActivated = false }; stats.Show(); var advanced = Usability.Tree(stats).OfType<Expander>().Single(e => e.Header?.ToString() == "Advanced comparisons"); advanced.IsExpanded = true; stats.UpdateLayout(); Check(Usability.Tree(advanced).OfType<CheckBox>().Single().Content?.ToString() == "Compare without AAs", "AA-off control resides in advanced stat-profile comparisons"); Capture("stat-profiles", (FrameworkElement)stats.Content, 900, 650); stats.Close();
            ShowView(TrackerTab.Pet); RefreshBuildChoices("current"); petSearch.Text = "Indicolite Breastplate"; Flush(); petResults.SelectedItem = petResults.Items.OfType<PetSearchRow>().FirstOrDefault(); Flush();
            var details = Usability.Tree(this).OfType<Expander>().Single(e => e.Header?.ToString() == "Pet Details"); details.IsExpanded = true; Flush(); var toggle = Usability.Tree(details).OfType<ToggleButton>().First(); Check(toggle.ActualWidth < details.ActualWidth * .5 && toggle.HorizontalAlignment == HorizontalAlignment.Left, "Pet Details header hit area is content-sized while fields remain wide"); Capture("pet-details", (FrameworkElement)Content); details.IsExpanded = false; Flush();
            Check(petItems.Items.OfType<PetItemRow>().Count(r => r.Item.ItemKey.Length > 0) == 7, "Seven-item pet inventory is preserved");
            Check(Usability.Tree(petComparison).OfType<ItemRoleGlyph>().Count(i => i.IsVisible) == 2, "Pet candidate inspector shows sourced type and independent player ownership"); Capture("pet", (FrameworkElement)Content);
            ShowView(TrackerTab.Exaltations); exaltSearch.Text = "Siphon Strength"; exaltCompatibility.SelectedIndex = 0; Flush();
            var siphon = exaltTable.Items.OfType<ExaltationOption>().Single(e => e.Source.Key == "disciple symbol of innoruuk"); exaltTable.SelectedItem = siphon; Flush(); Check(siphon.Type == "Click" && siphon.UnlockRank == 2, "Siphon Strength is classified as Click at +2 in the actual Exaltations table");
            Check(Usability.Tree(this).OfType<TextBlock>().Any(t => t.Text == "EFFECT") && !Usability.Tree(this).OfType<TextBlock>().Any(t => t.Text == "SELECTED EFFECT"), "Effect label floats above the existing effect card");
            exaltCompatibility.SelectedIndex = 1; Flush(); Check(!exaltTable.Items.OfType<ExaltationOption>().Any(e => e.Source.Key == siphon.Source.Key), "Current Loadout filter excludes known incompatible source classes"); exaltCompatibility.SelectedIndex = 2; Flush(); Check(exaltTable.Items.OfType<ExaltationOption>().Any(e => e.Source.Key == siphon.Source.Key), "Incompatible filter includes known class restrictions"); exaltCompatibility.SelectedIndex = 0;
            Controller.Inventory.Entries.Add(new() { Name = "Disciple Symbol of Innoruuk +1", Location = "BankC", Count = 1 }); Controller.Inventory.Invalidate(); exaltOwnership.SelectedIndex = 2; Flush(); Check(exaltTable.Items.OfType<ExaltationOption>().Any(e => e.SourceOwned && !e.Owned), "Have source item is distinct from extracted-effect ownership");
            Controller.Inventory.Entries.Add(new() { Name = "Disciple Symbol of Innoruuk (Exaltation)", Location = "Augmentation", Count = null }); Controller.Inventory.Invalidate(); exaltOwnership.SelectedIndex = 1; Flush(); Check(exaltTable.Items.OfType<ExaltationOption>().Any(e => e.Owned && e.OwnershipEvidence.Keyring), "Owned effects recognizes augmentation storage presence without inventing quantities"); Capture("exaltations", (FrameworkElement)Content);
            exaltOwnership.SelectedIndex = 0; exaltSearch.Text = "Black Silken Bridle"; exaltContent.SelectedIndex = 0; Flush(); Check(exaltTable.Items.Count == 0, "Future buyable mount excluded from Current content"); exaltContent.SelectedIndex = 1; Flush(); Check(exaltTable.Items.OfType<ExaltationOption>().Any(e => e.Type == "Click" && e.Purpose == EffectPurpose.Utility && e.Content == "Future"), "Future mount retains separate Utility, Click and availability fields"); Capture("future-utility", (FrameworkElement)Content);
            foreach (var tab in new[] { TrackerTab.Gear, TrackerTab.Pet, TrackerTab.Exaltations }) {
                Width = 1440; Height = 860; ShowView(tab); Flush(); foreach (var grid in Usability.Tree(this).OfType<DataGrid>().Where(g => g.IsVisible && g.ActualWidth > 60)) {
                    var col = grid.Columns.FirstOrDefault(c => c.Visibility == Visibility.Visible && c.CanUserSort && c.SortMemberPath.Length > 0); if (col == null) continue;
                    typeof(DataGrid).GetMethod("PerformSort", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(grid, [col]); Flush(); Check(col.SortDirection.HasValue, tab + " active sort direction remains available");
                    TableLayout.ResizeColumn(grid, col, Math.Max(col.MinWidth, col.ActualWidth-10)); Flush(); Check(grid.Columns.Where(c => c.Visibility == Visibility.Visible).All(c => c.ActualWidth + .5 >= c.MinWidth), tab + " table resizing respects measured minimums");
                }
            }
            var settings = new SettingsWindow(Controller) { Left = -15000, Top = -15000, ShowActivated = false }; settings.Show(); settings.UpdateLayout();
            var badge = Usability.Tree(settings).OfType<ComboBox>().Single(c => c.Items.Cast<object>().Any(i => i.ToString() == "Frieren in a Mimic Chest")); badge.SelectedIndex = 1; settings.UpdateLayout(); Check(Controller.Profile.InventoryBadge == InventoryBadgeStyle.FrierenMimic && IconArtwork.Badge == InventoryBadgeStyle.FrierenMimic, "Mimic preference saves and redraws immediately without changing app identity"); Capture("settings", (FrameworkElement)settings.Content, 940, 680); badge.SelectedIndex = 0; settings.Close();
            foreach (var style in Enum.GetValues<IconArtworkStyle>()) { Controller.Profile.IconStyle = style; IconArtwork.Apply(Controller.Profile); var sheet = UI.Stack(UI.Text(style+" · item types and ownership",18,"Melee",true)); sheet.Background = UI.Brush("SolidSurface"); var row = new WrapPanel(); sheet.Children.Add(row);
                foreach (string role in new[] { "item-greatsword","glyph-sword","glyph-mace","glyph-bow","glyph-shield","item-armor","item-ring","item-earring","item-necklace","item-cloak","item-belt","item-held","item-arrow","item-scroll","item-bag","item-potion","inventory-chest","inventory-mimic-frieren" }) row.Children.Add(UI.Stack(new ItemRoleGlyph { Role = role, Width = 48, Height = 48 }, UI.Text(role.Replace("item-", ""),10)));
                Capture("icons-"+style.ToString().ToLowerInvariant(), sheet, 940, 260);
            }
            Controller.Profile.IconStyle = IconArtworkStyle.Vibrant; IconArtwork.Apply(Controller.Profile);
            foreach (var tab in new[] { TrackerTab.Gear, TrackerTab.Pet, TrackerTab.Exaltations }) { ShowView(tab); Flush(); Capture(tab.ToString().ToLowerInvariant()+"-narrow", (FrameworkElement)Content, 640, 420); if (tab == TrackerTab.Gear) Check(gearTable.ActualHeight >= 55, "Gear minimum window retains visible candidate rows below its header"); Width = 1440; Height = 860; Flush(); }
            var catalogue = Controller.Catalog; Check(catalogue.Items.Sum(i => i.CraftRecipes.Count) > 10, "Bundled real-world crafting fixtures retain nested icon templates and input quantities"); var knownExpansion = catalogue.Items.Count(i => ItemPresentation.Expansion(i) != "Unknown");
            Json.Write(Path.Combine(folder, "clear-items-checks.json"), new { Passed = checks.Count, Checks = checks, Samples = samples, SetupDiagnostics = setupDiagnostics, PreviewDiagnostics = previewDiagnostics, Data = new { Items = catalogue.Items.Count, InferredExpansions = catalogue.Items.Count(i => i.ExpansionProvenance.Inferred && i.IntroducedExpansion == "Unknown"), KnownOrAssumedExpansions = knownExpansion, CraftRecipes = catalogue.Items.Sum(i => i.CraftRecipes.Count), SourceZones = Json.Read<List<ZoneExpansionEvidence>>(Path.Combine(AppContext.BaseDirectory,"data/zone-expansion-evidence.json")).Count }, AppAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))).ToLowerInvariant(), CoreAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Catalog).Assembly.Location))).ToLowerInvariant(), CaptureGeometry = PreviewRenderer.CaptureGeometry, Scope = "One affected Gear/Pet/Exaltations and Appearance/stat-profile component review; 100/150/200/300% raster/layout proofs. All warmed and setup outliers retained. No live-game frame-rate claim." });
        }
        finally { previewAsyncForReview = false; gearSaveDelay.Stop(); gearPreviewDelay.Stop(); petPersistTimer.Stop(); petPreviewDelay.Stop(); gearPreviewCancel?.Cancel(); petPreviewCancel?.Cancel(); }
    }
}
