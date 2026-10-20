using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal void ReviewPetDetails(string folder)
    {
        if (!Controller.Demo) throw new InvalidOperationException("Use isolated preview data.");
        Directory.CreateDirectory(Path.Combine(folder, "qa")); var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        void Flush() { Refresh(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); }
        void Capture(string name, FrameworkElement element, double width, double height)
        {
            foreach (double scale in new[] { 1d, 1.5, 2d, 3d }) { var image = PreviewRenderer.Capture(element, width, height, scale); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(Path.Combine(folder, "qa", $"pet-details-{name}-{scale:0.0}x.png")); encoder.Save(file); }
        }
        void QueueDialog<T>(string button, Action<T>? prepare = null) where T : Window
        {
            Dispatcher.BeginInvoke(() => { var dialog = Application.Current.Windows.OfType<T>().Single(w => w.IsVisible); prepare?.Invoke(dialog); Usability.Tree(dialog).OfType<Button>().Single(b => b.Content?.ToString() == button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }, DispatcherPriority.ApplicationIdle);
        }
        void ExitChoice(string caption) { QueueDialog<TheorycraftExitDialog>(caption); }
        void Toggle(bool value) { petTheoryCheck.IsChecked = value; petTheoryCheck.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Flush(); }
        void Find(string name) { petSearch.Text = name; Flush(); petResults.SelectedItem = petResults.Items.OfType<PetSearchRow>().FirstOrDefault(r => r.Item.Name.Contains(name, StringComparison.OrdinalIgnoreCase)); Flush(); }
        try
        {
            GearBuildFixtures.Apply(Controller); PetItemRules.ApplyDisplayEvidence(Controller.Catalog, Path.Combine(AppContext.BaseDirectory, "data/pet-item-fixtures.json"));
            var owner = Controller.Profile.ActiveLoadout; owner.Classes = ["SHD", "BRD", "MAG"]; owner.PetType = "Earth"; owner.PetLevel = 50; owner.PetPreset = "Pet DPS";
            owner.ConfirmedPetInventory = new() { Name = "Current build", Preset = "Pet DPS", PetType = "Earth", PetLevel = 50, Items = new[] { ("Sunderfury",4), ("Indicolite Breastplate",5), ("Indicolite Bracer",7), ("Indicolite Greaves",7), ("Cloak of Flames",4), ("Icebelt",3), ("Indicolite Vambraces",7) }.Select((x,i) => new PetInventoryItem { Id = "details-" + i, ItemKey = Controller.Catalog.Find(x.Item1)!.Key, Name = x.Item1, Position = i+1, Rank = x.Item2, Slot = "Storage", ConfirmedAt = DateTime.Now }).ToList() };
            owner.PetBuilds = [GuidedPetBuilds.Clone(owner.ConfirmedPetInventory, "Earth weapon plan")];
            Controller.Profile.PetExpansions = ItemExpansionFilter.Choices.ToList(); Controller.Save();
            Width = 1440; Height = 860; ShowView(TrackerTab.Pet); RefreshBuildChoices("current"); ResetPetPanels(); Flush();
            string current = JsonSerializer.Serialize(owner.ConfirmedPetInventory), gearFilter = ItemExpansionFilter.Key(Controller.Profile.GearExpansions);
            Check(petTheory == null && petTheoryCheck.IsChecked == false && petBuild.SelectedValue?.ToString() == "current", "Normal mode starts unchecked on Current build");
            Find("Oakwynd"); Check(petResults.Items.Count == 0, "Oakwynd is hidden in ordinary Pet search offline");
            Find("Bow"); Check(petResults.Items.OfType<PetSearchRow>().All(r => PetItemRules.Unsupported(r.Item).Length == 0), "Other bows and legacy ranged records do not enter normal candidates");
            Find("Indicolite Breastplate"); Check(petResults.SelectedItem is PetSearchRow, "Supported armor remains searchable and rated");
            Check(petResults.Columns.Any(c => c.SortMemberPath == "Type" && c.CanUserSort) && petItems.Columns.Any(c => c.SortMemberPath == "Type" && c.CanUserSort), "Both pet tables expose sortable Type columns");
            Check(petItems.Items.OfType<PetItemRow>().Where(r => r.Item.ItemKey.Length > 0).All(r => r.Type != "Unknown"), "Actual seven-item inventory has sourced equipment types");
            Check(!Usability.Tree(petComparison).OfType<TextBlock>().Any(t => t.Text == "Selected replacement" || t.Text.Contains("preference comparison")), "Inspector begins with the item rather than redundant replacement/preference headings");
            var row = (PetSearchRow)petResults.SelectedItem;
            var originalRows = petResults.ItemsSource;
            petComparison.RankSlider.Value = 10; petPreviewDelay.Stop(); ShowPetComparison(); Flush();
            Check(PetInspectedRank(row) == 10 && row.Rank == 0 && ReferenceEquals(originalRows, petResults.ItemsSource) && JsonSerializer.Serialize(owner.ConfirmedPetInventory) == current, "Slider changes only inspected rank, preserving rows and physical inventory");
            Check(Usability.Tree(petComparison).OfType<TextBlock>().Any(t => t.Text == "Indicolite Breastplate +10") && petDisplayedComparison?.Candidate.Contains("+10") == true, "Classic facts and rating share the preview rank");
            Check(petDisplayedComparison!.Evaluation!.Unbuffed.Changes.Sum(c => c.Points) is var points && Math.Abs(points - petDisplayedComparison.Evaluation.Unbuffed.Delta!.Value) < .001, "Displayed weighted contributions reconcile to the net rating");
            Check(Usability.Tree(petComparison).OfType<TextBlock>().Any(t => t.Text == "Upgrade rating · Pet DPS"), "Rating heading names the active preset inline");
            Check(petComparison.RankSlider.Minimum == 0 && petComparison.RankSlider.Maximum == 10 && petComparison.RankSlider.IsSnapToTickEnabled, "Preview slider provides integer +0–10 keyboard steps");
            var info = Usability.Tree(petComparison).OfType<Button>().Single(b => b.Content?.ToString() == "ⓘ");
            Check(ToolTipService.GetInitialShowDelay(info) == 650 && ToolTipService.GetShowDuration(info) == 5000, "Information action retains the established tooltip timing");
            QueueDialog<RatingFormulaDialog>("Close"); info.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            Check(!Application.Current.Windows.OfType<RatingFormulaDialog>().Any(w => w.IsVisible), "Information button opens the owned formula dialog and keyboard-accessible Close dismisses it");
            Capture("normal", (FrameworkElement)Content, 1440, 860);
            var formula = new RatingFormulaDialog(SelectedPetPreset, new(Controller.Profile.Weights[SelectedPetPreset]), petDisplayedComparison) { Owner = this, Left = -15000, Top = -15000, ShowActivated = false }; formula.Show(); Flush();
            Check(Usability.Tree(formula).OfType<TextBlock>().Any(t => t.Text.Contains("not DPS")) && Usability.Tree(formula).OfType<TextBlock>().Any(t => t.Text.Contains("Ratio")), "Formula dialog exposes active weights and separates preference points from DPS");
            Capture("formula", (FrameworkElement)formula.Content, 620, 480); formula.Close();
            ChangePetPreviewRank(null); petPreviewDelay.Stop(); ShowPetComparison(); Flush(); Check(PetInspectedRank(row) == row.Rank, "Reset preview restores the original candidate rank");
            var high = new PetSearchRow(row.Item, "Character stock") { Rank = 13, Instance = new() { Name = row.Item.Name + " +13", ConfirmedItemKey = row.Item.Key, Count = 1, Location = "General1" } };
            UI.SetRows(petResults, new List<PetSearchRow> { high }); petResults.SelectedItem = high; ShowPetComparison(); Flush();
            Check(PetInspectedRank(high) == 13 && Usability.Tree(petComparison).OfType<TextBlock>().Any(t => t.Text.StartsWith("Recorded +13")), "Owned ranks above +10 are displayed without clamping");
            petComparison.RankSlider.Value = 9; petPreviewDelay.Stop(); ShowPetComparison(); ChangePetPreviewRank(null); petPreviewDelay.Stop(); ShowPetComparison(); Check(PetInspectedRank(high) == 13 && high.Instance.Rank == 13, "Previewing then resetting preserves a +13 physical instance");
            Toggle(true); Find("Oakwynd"); Check(petTheory != null && petResults.SelectedItem is PetSearchRow, "Theorycraft exposes Oakwynd in an isolated workspace");
            Check(petDisplayedComparison?.Evaluation is { IsHypothetical: true } evaluation && evaluation.Assumptions.Any(a => a.Contains("bows")), "Unsupported bow comparison is explicitly hypothetical with a recorded assumption");
            string originalPlan = JsonSerializer.Serialize(owner.PetBuilds[0]);
            var gripItem = SelectedPetInventory!.Items[0]; SelectPetInventoryPosition(gripItem.Position); RemovePetItem(); Flush();
            ChangePetPreviewRank(10); petPreviewDelay.Stop(); ShowPetComparison(); AddPetItem(); Flush();
            Check(SelectedPetInventory!.Items.Single(i => i.ItemKey == "oakwynd").Rank == 0, "A catalog preview at +10 still adds a bare manual item at +0");
            Check(petTheory!.Dirty && JsonSerializer.Serialize(owner.ConfirmedPetInventory) == current && JsonSerializer.Serialize(owner.PetBuilds[0]) == originalPlan, "Theorycraft inventory edits leave Current and ordinary plans unchanged");
            Controller.Save(); Check(owner.PetBuilds.Count == 1 && !JsonSerializer.Serialize(owner).Contains("Theorycraft · Current build"), "An unrelated profile save cannot persist the working copy");
            ExitChoice("Cancel"); Toggle(false); Check(petTheory != null && petTheoryCheck.IsChecked == true, "Cancel departure retains the Theorycraft workspace");
            QueueDialog<EntryDialog>("Save", d => Usability.Tree(d).OfType<TextBox>().First().Text = ""); Check(!SavePetTheorycraft() && petTheory!.Dirty && petFeedback.Text.Contains("Enter a name"), "Empty Theorycraft names preserve the unsaved copy and explain recovery");
            QueueDialog<EntryDialog>("Save", d => Usability.Tree(d).OfType<TextBox>().First().Text = "Future ranged pet"); Check(SavePetTheorycraft(), "Explicit save accepts a named Theorycraft build"); Flush();
            var saved = owner.PetBuilds.Single(b => b.IsTheorycraft); Check(saved.Name == "Future ranged pet" && !petTheory!.Dirty && saved.TheorycraftAssumptions.Any(a => a.Contains("bows")), "Saved Theorycraft build keeps its designation and assumptions");
            Check(!Usability.Tree(this).OfType<Button>().Single(b => b.Content?.ToString() == "Use as current build…").IsEnabled, "Activation action is disabled in Theorycraft");
            PetBuildEditing.Remember(petTheory!.Working); petTheory.Working.Items.Single(i => i.ItemKey == "oakwynd").Rank = 5; PetEdited(); Check(saved.Items.Single(i => i.ItemKey == "oakwynd").Rank != 5 && petTheory.Dirty, "Edits after saving are detached and require another explicit save");
            Find("Oakwynd"); Capture("theorycraft", (FrameworkElement)Content, 1440, 860);
            ExitChoice("Discard"); Toggle(false); Check(petTheory == null && petBuild.SelectedValue?.ToString() == "current" && JsonSerializer.Serialize(owner.ConfirmedPetInventory) == current, "Discard returns to Current without applying hypothetical equipment");
            RefreshBuildChoices(saved.Id); Flush(); Check(petTheory?.SavedId == saved.Id && !petTheory.Dirty && petTheoryCheck.IsChecked == true, "Selecting a saved Theorycraft build reopens a detached working copy");
            PetBuildEditing.Remember(petTheory!.Working); petTheory.Working.Focus = "Unsaved focus correction"; PetEdited();
            ExitChoice("Cancel"); RefreshBuildChoices(owner.PetBuilds.First(b => !b.IsTheorycraft).Id); Flush(); Check(petTheory != null && petBuild.SelectedValue?.ToString() == saved.Id, "Cancelling a build switch restores the Theorycraft selection");
            ExitChoice("Cancel"); Check(!CanHide!() && petTheory != null, "Cancelling window departure keeps the unsaved workspace open");
            PetBuildEditing.Remember(petTheory!.Working); petTheory.Working.Items.Single(i => i.ItemKey == "oakwynd").Rank = 6; PetEdited(); ExitChoice("Save theorycraft…"); Toggle(false); Check(saved.Id == owner.PetBuilds.Single(b => b.IsTheorycraft).Id && petTheory == null && owner.PetBuilds.Single(b => b.IsTheorycraft).Items.Single(i => i.ItemKey == "oakwynd").Rank == 6, "Save on departure updates the stable Theorycraft build then returns to Current");
            Find("Indicolite"); Capture("narrow", (FrameworkElement)Content, 640, 420); Width = 1440; Height = 860; Flush();
            foreach (var name in new[] { "Oakwynd", "Engineer's Ring", "Barbarian Spiritist's Hammer", "Slime Blood of Cazic-Thule" })
            {
                var item = Controller.Catalog.Find(name)!; var card = new ClassicItemCard(item, 0); Capture("card-" + Identity.Key(name).Replace(' ', '-').Replace("'", ""), card, 340, 520);
                Check(Usability.Tree(card).OfType<TextBlock>().Any(t => t.Text.StartsWith("Type:")), name + " classic card includes its sourced type");
            }
            var candidate = Controller.Catalog.Find("Indicolite Breastplate")!; var currentItem = owner.ConfirmedPetInventory.Items[1]; var pair = ItemPairComparison.Create(candidate, currentItem.Entry(), Controller.Catalog.Find("Sunderfury")!, 4, null, owner, Controller.Catalog);
            var compare = new FrostWindow(Controller, "Pet item comparison", "Raw ranked item differences", 720, 530) { Owner = this, Left = -15000, Top = -15000, ShowActivated = false }; compare.Body.Children.Add(UI.Scroll(new PetPairComparisonView(pair))); compare.Show(); Flush(); Capture("comparison", (FrameworkElement)compare.Content, 720, 530); compare.Close();
            Check(gearFilter == ItemExpansionFilter.Key(Controller.Profile.GearExpansions) && JsonSerializer.Serialize(owner.ConfirmedPetInventory) == current, "All review interactions preserve Gear filters and Current inventory");
            var beforeLayout = petItems.ItemsSource; var watch = new System.Diagnostics.Stopwatch(); var samples = new List<double>();
            for (int i = 0; i < 30; i++) { watch.Restart(); RefreshPet(); watch.Stop(); samples.Add(watch.Elapsed.TotalMilliseconds); }
            Check(ReferenceEquals(beforeLayout, petItems.ItemsSource), "Unchanged pet refresh preserves stable inventory rows");
            Json.Write(Path.Combine(folder, "pet-details-checks.json"), new { Passed = checks.Count, Checks = checks, CachedRefreshMs = samples, AppAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))).ToLowerInvariant(), CoreAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Catalog).Assembly.Location))).ToLowerInvariant(), Scope = "One affected-component pass; no unrelated window or retained-data performance reruns" });
        }
        finally { petTheory = null; SetTheoryCheck(false); petPreviewDelay.Stop(); petPreviewCancel?.Cancel(); }
    }
}
