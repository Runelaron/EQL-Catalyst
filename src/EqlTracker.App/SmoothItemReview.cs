using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private bool previewAsyncForReview;
    internal async Task ReviewSmoothItems(string folder, bool inspectorsOnly = false)
    {
        if (!Controller.Demo) throw new InvalidOperationException("Isolated review data required.");
        Directory.CreateDirectory(Path.Combine(folder, "qa")); var checks = new List<string>();
        var timings = new List<object>();
        object? setupDiagnostics = null, previewDiagnostics = null;
        object? retainedTables = null;
        if (inspectorsOnly)
        {
            string report = Path.Combine(folder, "..", "consolidated-table-checks.json");
            using var previous = JsonDocument.Parse(File.ReadAllText(report));
            var tableChecks = previous.RootElement.GetProperty("Checks").EnumerateArray().Select(c => c.GetString()!).Where(c => c.Contains("sortable header publishes") || c.Contains("active sort arrow") || c.Contains("arrow follows") || c.Contains("column resizing respects")).ToArray();
            checks.AddRange(tableChecks); retainedTables = new { Checks = tableChecks.Length, AppAssemblySha256 = previous.RootElement.GetProperty("AppAssemblySha256").GetString(), Reason = "Retained consolidated table proofs; follow-up changes affect inspector labels and rating heading only." };
        }
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        void Flush() { Refresh(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); }
        void Capture(string name, FrameworkElement view, double width, double height)
        {
            foreach (double scale in new[] { 1d, 1.5, 2d, 3d })
            {
                var image = PreviewRenderer.Capture(view, width, height, scale); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var file = File.Create(Path.Combine(folder, "qa", $"smooth-items-{name}-{scale:0.0}x.png")); encoder.Save(file);
            }
        }
        void PetFind(string name) { petSearch.Text = name; Flush(); petResults.SelectedItem = petResults.Items.OfType<PetSearchRow>().FirstOrDefault(r => r.Item.Name.Contains(name, StringComparison.OrdinalIgnoreCase)); Flush(); }
        try
        {
            GearBuildFixtures.Apply(Controller); Controller.Profile.ColumnWidths.Clear(); Controller.Profile.PanelSplits.Clear();
            Controller.Profile.SmoothMeterMotion = false; var owner = Controller.Profile.ActiveLoadout;
            Controller.Inventory.Entries.AddRange(new[] { ("Red Dragonscale Armor",3,"BankA"), ("Red Dragonscale Armor",6,"BankB"), ("Indicolite Breastplate",2,"BagA"), ("Indicolite Breastplate",8,"BagB") }.Select(x => new InventoryEntry { Name = x.Item1 + " +" + x.Item2, Location = x.Item3, Count = 1, ConfirmedItemKey = Controller.Catalog.Find(x.Item1)!.Key })); Controller.Inventory.Invalidate();
            Width = 1440; Height = 860; ShowView(TrackerTab.Gear); SetGearTarget(false); gearOwnership.SelectedIndex = 0; gearDestination.SelectedIndex = 0; gearSearch.Text = "Red Dragonscale Armor"; SelectGearPosition("CHEST:1"); Flush();
            Check(gearTable.Items.Count == 1 && gearTable.SelectedItem is GearRow { Rank: 6 }, $"Gear All items shows one representative owned copy at its actual rank (rows={gearTable.Items.Count}; ranks={string.Join(',', gearTable.Items.OfType<GearRow>().Select(r => r.Rank))}; position={GearSelectedPosition}; stock={Controller.Inventory.Evidence(Controller.Catalog.Find("Red Dragonscale Armor")!).Count()})");
            var gripperHeaders = Usability.Tree(gearTable).OfType<DataGridColumnHeader>().Where(h => h.Column != null).OrderBy(h => h.Column.DisplayIndex).Take(2).ToArray();
            var rightGrip = (Thumb)gripperHeaders[0].Template.FindName("PART_RightHeaderGripper", gripperHeaders[0]);
            double initialTotal=gearTable.Columns.Where(c=>c.Visibility==Visibility.Visible).Sum(c=>c.ActualWidth);
            TableLayout.ResizeColumn(gearTable,gripperHeaders[0].Column,gripperHeaders[0].Column.ActualWidth+20);Flush();
            Check(Math.Abs(initialTotal-gearTable.Columns.Where(c=>c.Visibility==Visibility.Visible).Sum(c=>c.ActualWidth))<1,"Header resize preserves the occupied table width");
            var leftGrip = (Thumb)gripperHeaders[1].Template.FindName("PART_LeftHeaderGripper", gripperHeaders[1]);
            Check(rightGrip.ActualWidth>0&&leftGrip.ActualWidth>0,"Both header edges retain usable pointer grippers; routed input is covered by the dedicated cascade review");
            Check(Usability.Tree(gearInspector).OfType<ClassicItemCard>().Count() == 1 && Usability.Tree(gearInspector).OfType<Slider>().Count() == 1, "Gear uses the shared classic card and selected-item rank slider");
            Check(gearDisplayedComparison?.Evaluation?.Unbuffed.Delta.HasValue == true && gearDisplayedComparison.Evaluation.Buffed.Delta.HasValue, "Gear displays independent Unbuffed and Usual buffs results");
            var gearRows = gearTable.ItemsSource; var gearFacts = Usability.Tree(gearInspector).OfType<ClassicItemCard>().Single(); var gearView = Usability.Tree(gearInspector).OfType<BuildComparisonView>().Single();
            gearInspector.RankSlider.Value = 10;
            Check(ReferenceEquals(gearFacts, Usability.Tree(gearInspector).OfType<ClassicItemCard>().Single()) && ReferenceEquals(gearView, Usability.Tree(gearInspector).OfType<BuildComparisonView>().Single()) && gearView.Children.Count > 0, "Dragging Gear preview retains facts and completed rating instead of blanking");
            ShowSelectedGear(); Flush();
            Check(GearInspectedRank((GearRow)gearTable.SelectedItem) == 10 && gearDisplayedComparison!.Candidate.Contains("+10") && ReferenceEquals(gearRows, gearTable.ItemsSource), "Gear preview updates comparison rank without replacing candidate rows");
            foreach (var context in new[] { gearDisplayedComparison!.Evaluation!.Unbuffed, gearDisplayedComparison.Evaluation.Buffed }) Check(Math.Abs(context.Changes.Sum(c => c.Points) - context.Delta!.Value) < .001, context.Name + " contributions reconcile with the displayed total");
            var formula = new RatingFormulaDialog("Balanced Tank", new(Controller.Profile.Weights["Balanced Tank"]), gearDisplayedComparison, false) { Owner = this, Left = -15000, Top = -15000, ShowActivated = false }; formula.Show(); Flush();
            Check(Usability.Tree(formula).OfType<TextBlock>().Any(t => t.Text == "Unbuffed") && Usability.Tree(formula).OfType<TextBlock>().Any(t => t.Text == "Usual buffs"), "Gear formula exposes both contexts and their exact arithmetic"); Capture("gear-formula", (FrameworkElement)formula.Content, 620, 480); formula.Close();
            TryGearDraft(); Flush(); Check(owner.EquipmentDraft!.Positions.Single(p => p.Id == "CHEST:1").Item!.Rank == 10 && owner.EquipmentPositions.Single(p => p.Id == "CHEST:1").Item!.Rank == 5, "Explicit Add to build adopts inspected rank without changing confirmed equipment");
            UndoGearDraft(); owner.EquipmentDraft = null; gearDestination.SelectedIndex = 0; Controller.Save(); gearFingerprint = null; Flush();
            Capture("gear", (FrameworkElement)Content, 1440, 860);
            owner.Level = 50; owner.PetPreset = "Pet DPS"; owner.PetType = "Earth";
            owner.ConfirmedPetInventory = new() { Name = "Current build", PetType = "Earth", PetLevel = 50, Preset = "Pet DPS", Items = new[] { ("Sunderfury",4), ("Indicolite Breastplate",5), ("Indicolite Bracer",7), ("Indicolite Greaves",7), ("Cloak of Flames",4), ("Icebelt",3), ("Indicolite Vambraces",7) }.Select((x,i) => new PetInventoryItem { Id = "smooth-"+i, Name = x.Item1, ItemKey = Controller.Catalog.Find(x.Item1)!.Key, Rank = x.Item2, Position = i+1, Slot = "Storage" }).ToList() };
            owner.PetBuilds = [GuidedPetBuilds.Clone(owner.ConfirmedPetInventory, "Earth weapon plan")]; Controller.Save(); ShowView(TrackerTab.Pet); RefreshBuildChoices("current"); ResetPetPanels(); Flush();
            string inventoryBefore = JsonSerializer.Serialize(owner.ConfirmedPetInventory);
            PetFind("Indicolite Breastplate"); Check(petResults.Items.Count == 1 && ((PetSearchRow)petResults.SelectedItem).Rank == 8, "Pet All items deduplicates owned copies and selects highest rank");
            var copyPicker = Usability.Tree(petComparison).OfType<ComboBox>().Single(); copyPicker.SelectedIndex = 1; Flush(); Check(PetInspectedRank((PetSearchRow)petResults.SelectedItem) == 2, "Owned-copy selector switches inspected rank and effects without changing list rows");
            var draggedCopy = CatalogData((PetSearchRow)petResults.SelectedItem).GetData("EQL.PetCatalog") as PetCatalogDrag; Check(draggedCopy?.Instance?.Rank == 2 && PetInspectedInstance((PetSearchRow)petResults.SelectedItem)?.Rank == 2, "Drag payload and Add share the selected physical copy rather than its preview or representative rank");
            petOwnership.SelectedIndex = 1; Flush(); Check(petResults.Items.Count == 2 && petOwnership.SelectedItem?.ToString() == "Inventory Only", "Inventory Only shows separate physical copies with the new label"); petOwnership.SelectedIndex = 0; Flush(); PetFind("Indicolite Breastplate");
            var rowsBefore = petResults.ItemsSource; var petFacts = Usability.Tree(petComparison).OfType<ClassicItemCard>().Single(); var petView = Usability.Tree(petComparison).OfType<BuildComparisonView>().Single();
            petComparison.RankSlider.Value = 10; Check(ReferenceEquals(petFacts, Usability.Tree(petComparison).OfType<ClassicItemCard>().Single()) && ReferenceEquals(petView, Usability.Tree(petComparison).OfType<BuildComparisonView>().Single()) && petView.Children.Count > 0, "Pet slider retains visible controls and completed rating while updating"); ShowPetComparison(); Flush();
            Check(PetInspectedRank((PetSearchRow)petResults.SelectedItem) == 10 && ReferenceEquals(rowsBefore, petResults.ItemsSource) && JsonSerializer.Serialize(owner.ConfirmedPetInventory) == inventoryBefore, "Pet preview stays independent of stored inventory and other result rows");
            Check(Usability.Tree(petComparison).OfType<TextBlock>().Count(t => t.Text == "Upgrade rating · Pet DPS") == 1, "Pet retains one equipment-only rating context");
            var arithmetic = Usability.Tree(petComparison).OfType<Expander>().Single(e => e.Header?.ToString() == "Equipment · stats, caps & arithmetic"); arithmetic.IsExpanded = true; Flush();
            var arithmeticTable = Usability.Tree(arithmetic).OfType<DataGrid>().Single(); var arithmeticColumn = arithmeticTable.Columns[0]; TableLayout.Prefer(arithmeticTable, arithmeticColumn, arithmeticColumn.MinWidth + 30); double savedWidth = arithmeticColumn.ActualWidth;
            ChangePetPreviewRank(7); ShowPetComparison(); Flush();
            Check(arithmetic.IsExpanded && ReferenceEquals(arithmeticTable, Usability.Tree(arithmetic).OfType<DataGrid>().Single()) && Math.Abs(savedWidth-arithmeticColumn.ActualWidth)<1, "Expanded arithmetic retains its controls and manual column width across rank previews");
            Check(arithmeticTable.Items.OfType<BuildContribution>().Select(c => c.Points).SequenceEqual(RatingExplanation.Ordered(petDisplayedComparison!.Evaluation!.Unbuffed.Changes).Select(c => c.Points)), "Lazily opened arithmetic shows the current preview's exact contributions");
            Capture("pet-arithmetic", (FrameworkElement)Content, 1440, 860); arithmetic.IsExpanded = false; Flush();
            var type = petResults.Columns.Single(c => c.SortMemberPath == "Type"); TableLayout.Reset(petResults); Flush(); Check(type.ActualWidth >= type.MinWidth && type.MinWidth > 65, "Pet Type default accommodates measured type names");
            Check(Usability.Tree(this).OfType<Button>().Any(b => b.Content?.ToString() == "Expansion Filter · All ▾"), "Expansion Filter uses a compact summary beside Theorycraft");
            Capture("pet", (FrameworkElement)Content, 1440, 860);
            previewAsyncForReview = true;
            for (int rank = 1; rank <= 10; rank++) { ChangePetPreviewRank(rank); ShowPetComparison(); }
            await Task.Delay(200); Flush(); Check(petDisplayedComparison?.Candidate.Contains("+10") == true, "Rapid asynchronous preview requests discard obsolete rank results");
            ChangePetPreviewRank(5); ShowPetComparison(); PetFind("Sunderfury"); await Task.Delay(150); Flush(); Check(petDisplayedComparison?.Candidate.Contains("Sunderfury") == true, "Selection changes reject stale asynchronous results from the prior item"); previewAsyncForReview = false;
            PetFind("Indicolite Breastplate"); Controller.Profile.SmoothMeterMotion = true;
            petComparison.RankSlider.Value = 9; ShowPetComparison(); await Task.Delay(180); Flush();
            Check(Usability.Tree(petComparison).OfType<TextBlock>().All(t => !t.Text.Contains("NaN")), "Numeric roll transitions finish with finite formatted values"); Controller.Profile.SmoothMeterMotion = false;
            setupDiagnostics = UpdateDiagnostics.Drain();
            for (int run = 0; run < 4; run++)
            foreach (int rank in Enumerable.Range(0,11).Concat(Enumerable.Range(0,11).Reverse()))
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew(); ChangePetPreviewRank(rank); ShowPetComparison(); Flush(); watch.Stop();
                timings.Add(new { Phase = run == 0 ? "Warmup" : "Warm", Run = run, Rank = rank, WallMs = watch.Elapsed.TotalMilliseconds, UiAllocatedBytes = GC.GetAllocatedBytesForCurrentThread()-allocated });
            }
            previewDiagnostics = UpdateDiagnostics.Drain();
            Check(ReferenceEquals(rowsBefore, petResults.ItemsSource), "Repeated warmed previews preserve the stable candidate collection");
            owner.ConfirmedPetInventory.SummonSpell = ""; owner.ConfirmedPetInventory.LevelMode = PetLevelMode.Automatic; owner.ConfirmedPetInventory.PetLevel = null; LoadPetContext(); Flush();
            petSpell.SelectedIndex = 0; Flush(); Check(owner.ConfirmedPetInventory.SummonSpell == "Greater Conjuration: Earth" && petSpell.Text.Contains("Greater Conjuration: Earth"), "Selecting the first spell from a blank field populates it immediately");
            petSummonTier.Text = "10"; Flush(); Check(owner.ConfirmedPetInventory.PetLevel == 50 && owner.ConfirmedPetInventory.LevelMode == PetLevelMode.Automatic && petContextNote.Text.Contains("base 41"), "Automatic Earth level uses base 41 plus rank 10 capped at owner 50");
            petLevel.Text = "47"; Flush(); petSummonTier.Text = "9"; Flush(); Check(owner.ConfirmedPetInventory.PetLevel == 47 && owner.ConfirmedPetInventory.LevelMode == PetLevelMode.Manual, "Manual level overrides survive spell-rank edits"); petLevel.Text = "Auto"; Flush(); Check(owner.ConfirmedPetInventory.PetLevel == 50, "Choosing Auto resumes sourced level calculation");
            var contextExpander = Usability.Tree(this).OfType<Expander>().Single(e => e.Header?.ToString() == "Pet context"); contextExpander.IsExpanded = true; Flush(); Capture("pet-context", (FrameworkElement)Content, 1440, 860); contextExpander.IsExpanded = false;
            var ball = Controller.Catalog.Find("Ball of Everliving Golem")!; Check(PetItemRules.Type(ball) == "Held item" && !ball.Stats.ContainsKey("Ratio"), "Ball of Everliving Golem is a held Range item without weapon ratio");
            Capture("held-item", new ClassicItemCard(ball, 0), 340, 460);
            var sliderReview = RankSliderStyles.Review();
            var selectedSliders = Usability.Tree(sliderReview).OfType<Slider>().TakeLast(4).ToArray(); sliderReview.Measure(new(1060,610)); sliderReview.Arrange(new Rect(0,0,1060,610)); sliderReview.UpdateLayout();
            Check(selectedSliders.Take(3).All(s => s.Foreground.ToString() == UI.Brush("Accent").ToString()) && selectedSliders[3].Foreground.ToString() == UI.Brush("Melee").ToString(), "Selected segmented Frost keeps idle/hover/focus cyan and uses gold only for active dragging");
            Check(ReferenceEquals(petComparison.RankSlider.Style, RankSliderStyles.Style(3)) && ReferenceEquals(gearInspector.RankSlider.Style, RankSliderStyles.Style(3)), "Both production inspectors use the selected B+C slider treatment");
            Capture("slider-options", sliderReview, 1060, 610);
            foreach (var tab in inspectorsOnly ? Array.Empty<TrackerTab>() : new[] { TrackerTab.Queue, TrackerTab.Items, TrackerTab.Quests, TrackerTab.Progress, TrackerTab.Gear, TrackerTab.Exaltations, TrackerTab.Pet })
            {
                Width = 1440; Height = 860; ShowView(tab); Flush();
                var tables = Usability.Tree(this).OfType<DataGrid>().Where(t => t.IsVisible && t.ActualWidth > 50).ToArray();
                foreach (var table in tables)
                {
                    TableLayout.Reset(table); Flush(); var column = table.Columns.FirstOrDefault(c => c.Visibility == Visibility.Visible && c.CanUserSort && c.Header?.ToString()?.Length > 0);
                    if (column == null) continue;
                    var header = Usability.Tree(table).OfType<DataGridColumnHeader>().FirstOrDefault(h => h.Column == column);
                    if (header == null) continue;
                    void Sort() { typeof(DataGrid).GetMethod("PerformSort", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(table, [column]); Flush(); }
                    Sort(); Check(column.SortDirection.HasValue, tab + $" sortable header publishes active direction (path={column.SortMemberPath}; sortable={table.CanUserSortColumns}/{column.CanUserSort}; count={table.Items.Count}; source={table.ItemsSource?.GetType().Name})");
                    var arrow = header.Template.FindName("SortArrow", header) as TextBlock; Check(arrow?.Visibility == Visibility.Visible && arrow.Text is "▲" or "▼", tab + " active sort arrow is visible");
                    Sort(); Check(arrow!.Text == (column.SortDirection == ListSortDirection.Ascending ? "▲" : "▼"), tab + " arrow follows ascending/descending sorting");
                    TableLayout.ResizeColumn(table, column, Math.Max(column.MinWidth, column.ActualWidth - 12)); table.UpdateLayout(); Check(table.Columns.Where(c => c.Visibility == Visibility.Visible).All(c => c.ActualWidth + .5 >= c.MinWidth), tab + " column resizing respects measured minimums");
                }
                Capture("tab-"+tab.ToString().ToLowerInvariant(), (FrameworkElement)Content, 1440, 860);
            }
            ShowView(TrackerTab.Pet); Flush(); Capture("pet-narrow", (FrameworkElement)Content, 640, 420); Width = 1440; Height = 860; ShowView(TrackerTab.Gear); Flush(); Capture("gear-narrow", (FrameworkElement)Content, 640, 420);
            Json.Write(Path.Combine(folder, "smooth-items-checks.json"), new { Passed = checks.Count, Checks = checks, PreviewSamples = timings, SetupDiagnostics = setupDiagnostics, PreviewDiagnostics = previewDiagnostics, RetainedTableProof = retainedTables, AppAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))).ToLowerInvariant(), CoreAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Catalog).Assembly.Location))).ToLowerInvariant(), Scope = "One consolidated affected Tracker review, with focused inspector repairs. Preview timings include synchronous demo evaluation and dispatcher/layout; no outliers removed. Not a retained-combat performance benchmark." });
        }
        finally { previewAsyncForReview = false; petPreviewDelay.Stop(); gearPreviewDelay.Stop(); petPreviewCancel?.Cancel(); gearPreviewCancel?.Cancel(); petPersistTimer.Stop(); }
    }
}
