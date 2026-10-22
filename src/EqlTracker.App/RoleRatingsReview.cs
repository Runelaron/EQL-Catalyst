using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewRoleRatings(string folder, bool render)
    {
        if (!Controller.Demo) throw new InvalidOperationException("Synthetic review only.");
        Directory.CreateDirectory(Path.Combine(folder, "qa")); var checks = new List<string>(); var samples = new List<object>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        void Flush() { Refresh(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); }
        void Show(Window window) { window.Left = window.Top = -15000; window.ShowActivated = false; window.Show(); window.UpdateLayout(); }
        void Capture(string name, FrameworkElement view, double width, double height)
        {
            if (!render) return;
            foreach (double scale in new[] { 1d, 1.5, 2d, 3d })
            {
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(view, width, height, scale)));
                using var file = File.Create(Path.Combine(folder, "qa", name + "-" + scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "x.png")); encoder.Save(file);
            }
        }
        GearBuildFixtures.Apply(Controller); Width = 1440; Height = 900; Controller.Profile.SmoothMeterMotion = false;
        var owner = Controller.Profile.ActiveLoadout; owner.Classes = ["SHD", "SHM", "BRD"];
        var old = Controller.Catalog.Find("Gauntlets of Fiery Might")!; var position = owner.EquipmentPositions.Single(p => p.Slot == "Hands");
        position.Knowledge = EquipmentKnowledge.Occupied; position.Item = new() { Name = old.Name + " +4", ConfirmedItemKey = old.Key, Location = "Hands", Count = 1 };
        owner.Equipment = owner.EquipmentPositions.Where(p => p.Item != null).Select(p => p.Item!).ToList();
        foreach (var context in owner.StatProfiles) { context.Classes = owner.Classes.ToList(); context.EquipmentFingerprint = EquipmentPositions.Fingerprint(owner.EquipmentPositions); }
        Controller.Save(); ShowView(TrackerTab.Gear); SetGearTarget(false); SelectGearPosition("HANDS:1"); gearSearch.Text = "Slime Blood"; preset.SelectedItem = "Balanced"; Flush();
        Check(gearTable.SelectedItem is GearRow, "Named candidate selected from real Gear list");
        var legacy = new LoadoutEvaluationSession(Controller.Catalog, owner, false, new(Controller.Profile.Weights["Balanced Tank"]), owner.EquipmentPositions, owner.EquipmentPositions).Present(Controller.Catalog.Find("Slime Blood of Cazic-Thule")!, 5, "HANDS:1");
        Check(legacy.Evaluation?.Unbuffed.Delta == 32.5, "Internal legacy regression example still reconciles to exactly +32.5");
        Check(legacy.Replaces.Contains("Gauntlets of Fiery Might +4"), "Replacement includes its recorded rank");
        preset.SelectedItem = "Balanced"; Flush(); ChangeGearPreviewRank(5); ShowSelectedGear(); Flush();
        Check(gearDisplayedComparison?.Evaluation?.Role is { Name: "Balanced", Complete: true, Automatic: true }, "Gear consumes resolved class-aware Balanced weights");
        Check(preset.Items.Cast<string>().Take(3).SequenceEqual(SelfRoleRatings.Goals), "Main Role choices are Damage Balanced Survival");
        Check(!preset.Items.Cast<string>().Any(SelfRoleRatings.IsRetired), "Gear Role selector excludes all retired presets");
        var roleHelp = new GearRolesDialog(Controller.Profile, "Balanced") { Owner = this }; Show(roleHelp); Check(!Usability.Tree(roleHelp).OfType<Expander>().Any(e => SelfRoleRatings.IsRetired(e.Header?.ToString() ?? "")), "Role information offers only current goals and custom roles"); roleHelp.Close();
        var expansion = Usability.Tree(this).OfType<Button>().Single(b => Equals(b.Tag, "gear-expansion-filter"));
        Check(expansion.Width == 90 && gearSearch.Width < 220 && gearPosition.Width < 140, "Search and dropdowns use compact measured widths");
        Check(Math.Abs(gearSearch.TranslatePoint(new(), this).Y - gearPosition.TranslatePoint(new(), this).Y) < 2 && Math.Abs(expansion.TranslatePoint(new(), this).Y - gearPosition.TranslatePoint(new(), this).Y) < 2, "Search Slots Expansion share one control baseline when wide");
        var selectedSlotText = Usability.Tree(gearPosition).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToArray();
        Check(selectedSlotText.Contains("Hands") && !selectedSlotText.Any(t => t.Contains("Gauntlets")), "Shared dropdown template honors DisplayMemberPath instead of full object descriptions");
        var roleInfo = Usability.Tree(this).OfType<Button>().Single(b => b.ToolTip is ToolTip { Content: TextBlock t } && t.Text == "Select role for rating, click for more" || b.ToolTip is string tip && tip == "Select role for rating, click for more");
        double right(FrameworkElement element) => element.TranslatePoint(new(element.ActualWidth, 0), this).X;
        Check(Math.Abs(right(roleInfo) - right(preset)) < 1, "Role information button aligns exactly with dropdown right edge");
        var view = Usability.Tree(gearInspector).OfType<BuildComparisonView>().Single(); var ratingInfo = Usability.Tree(view).OfType<Button>().Single(b => b.Content?.ToString() == "ⓘ");
        Check(ratingInfo.Parent is Grid header && Math.Abs(right(ratingInfo) - right(header)) < 1, "Rating information button is right-justified on the heading row");
        var labels = Usability.Tree(view).OfType<TextBlock>().Select(t => t.Text).ToList(); Check(labels.Contains("Gains") && labels.Contains("Losses") && !labels.Any(t => t.Contains("Effects gained: none")), "Inspector groups signed changes and omits empty effects");
        var slider = gearInspector.RankSlider; var grip = UI.Text("⠿"); var information = UI.Button("ⓘ", () => { }); information.ToolTip = "Meaningful information"; var tooltips = UI.Stack(slider.Parent == null ? slider : new Slider(), grip, information); Usability.Tooltips(tooltips);
        var testSlider = Usability.Tree(tooltips).OfType<Slider>().Single();
        foreach (TooltipLevel level in Enum.GetValues<TooltipLevel>())
        {
            Controller.Profile.Tooltips = level; Controller.Profile.TooltipDelayMilliseconds = 1000; Usability.UpdateTooltipPreferences(Controller.Profile);
            Check(ToolTipService.GetIsEnabled(information) == (level != TooltipLevel.None) && ToolTipService.GetIsEnabled(testSlider) == (level == TooltipLevel.All) && ToolTipService.GetIsEnabled(grip) == (level == TooltipLevel.All), "Tooltip live coverage · " + level);
            Check(ToolTipService.GetInitialShowDelay(information) == 1000 && ToolTipService.GetBetweenShowDelay(information) == 1000, "Tooltip configured initial and subsequent delay · " + level);
        }
        var bound = new TextBlock(); bound.SetBinding(ToolTipProperty, new Binding("Tip")); bound.DataContext = new { Tip = "First" }; Usability.Tooltips(bound); Controller.Profile.Tooltips = TooltipLevel.Essential; Controller.Profile.TooltipDelayMilliseconds = 750; Usability.UpdateTooltipPreferences(Controller.Profile); bound.DataContext = new { Tip = "Second" }; bound.GetBindingExpression(ToolTipProperty)!.UpdateTarget(); Flush(); Check(BindingOperations.IsDataBound(bound, ToolTipProperty) && bound.ToolTip?.ToString() == "Second", "Tooltip policy preserves dynamic evidence bindings: " + BindingOperations.IsDataBound(bound, ToolTipProperty) + " / " + bound.ToolTip + " / " + bound.GetBindingExpression(ToolTipProperty)?.Status);
        string petBefore = JsonSerializer.Serialize(Controller.Profile.Weights); string equipmentBefore = JsonSerializer.Serialize(owner.EquipmentPositions); var listBefore = gearTable.ItemsSource;
        using (UpdateDiagnostics.Measure("Rapid role preview batch"))
        {
            previewAsyncForReview = true;
            for (int rank = 0; rank <= 10; rank++) { long start = Stopwatch.GetTimestamp(); ChangeGearPreviewRank(rank); ShowSelectedGear(); samples.Add(new { Rank = rank, UiMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds }); }
            await Task.Delay(250); Flush(); previewAsyncForReview = false;
        }
        Check(gearDisplayedComparison?.Candidate.EndsWith("+10") == true && ReferenceEquals(listBefore, gearTable.ItemsSource), "Rapid preview applies final rank and keeps candidate rows stable");
        Check(JsonSerializer.Serialize(owner.EquipmentPositions) == equipmentBefore && JsonSerializer.Serialize(Controller.Profile.Weights) == petBefore, "Preview and new Self roles preserve equipment and Pet weights");
        ChangeGearPreviewRank(5); ShowSelectedGear(); Flush();
        var weightsEditor = new RoleWeightsDialog(Controller.Profile, "Balanced") { Owner = this }; Show(weightsEditor);
        Check(weightsEditor.Inputs.ContainsKey("Endurance") && weightsEditor.Inputs.Count >= 20, "Three-column editor includes sourced raw Endurance and all stat fields");
        Check(weightsEditor.Inputs["AC"].TranslatePoint(new(), weightsEditor).X < weightsEditor.Inputs["STR"].TranslatePoint(new(), weightsEditor).X && weightsEditor.Inputs["STR"].TranslatePoint(new(), weightsEditor).X < weightsEditor.Inputs["ResistFire"].TranslatePoint(new(), weightsEditor).X, "Weight editor lays resources attributes and resists in three columns");
        Check(Usability.Tree(weightsEditor).OfType<Button>().Any(b => b.Content?.ToString() == "Reset weights to default"), "Default role exposes Reset weights to default inside its editor"); weightsEditor.Close();
        var add = new RoleWeightsDialog(Controller.Profile, "Balanced", true) { Owner = this, Left = -15000, Top = -15000, ShowActivated = false };
        add.Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() => { add.RoleName.Text = "Review hybrid"; add.Inputs["Mana"].Text = "0.7"; add.Save(); }));
        add.ShowDialog(); Check(Controller.Profile.SelfRoleOverrides["Review hybrid"]["Mana"] == .7 && SelfRoleRatings.Names(Controller.Profile).Contains("Review hybrid"), "Add role saves isolated Self weights with a unique name");
        var reset = new RoleWeightsDialog(Controller.Profile, "Balanced") { Owner = this, Left = -15000, Top = -15000, ShowActivated = false };
        reset.Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() => { reset.Inputs["AC"].Text = "NaN"; reset.Save(); Check(reset.IsVisible && !Controller.Profile.SelfRoleOverrides.ContainsKey("Balanced"), "Invalid role weight does not commit edits"); reset.ResetDefaults(); reset.Save(); }));
        reset.ShowDialog(); Check(!Controller.Profile.SelfRoleOverrides.ContainsKey("Balanced"), "Reset restores automatic defaults when saved");
        var unchanged = new RoleWeightsDialog(Controller.Profile, "Balanced") { Owner = this, Left = -15000, Top = -15000, ShowActivated = false };
        unchanged.Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() => unchanged.Save())); unchanged.ShowDialog();
        Check(!Controller.Profile.SelfRoleOverrides.ContainsKey("Balanced"), "Saving unchanged automatic weights preserves future class adaptation");
        var customHelp = new GearRolesDialog(Controller.Profile, "Review hybrid") { Owner = this }; Show(customHelp);
        Check(!Usability.Tree(customHelp).OfType<Button>().Any(b => b.Content?.ToString()?.StartsWith("Restore automatic") == true), "Role information is read-only; resets belong only in default-role weight editors"); customHelp.Close();
        foreach (string theme in new[] { "coldain", "sunstrider", "shadowman" })
        {
            Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile, theme)); Appearance.Apply(theme); Flush();
            Capture(theme + "-gear", (FrameworkElement)Content, 1440, 900);
            Width = 850; Flush(); Capture(theme + "-gear-narrow", (FrameworkElement)Content, 850, 760); Width = 1440; Flush();
            var formula = new RatingFormulaDialog("Balanced", new(), gearDisplayedComparison, false) { Owner = this }; Show(formula);
            Check(Usability.Tree(formula).OfType<TextBlock>().Any(t => t.Text == "Slime Blood of Cazic-Thule +5") && Usability.Tree(formula).OfType<TextBlock>().Any(t => t.Text.Contains("Gauntlets of Fiery Might +4")), theme + " formula names candidate and replacement");
            Capture(theme + "-formula", (FrameworkElement)formula.Content, 680, 560);
            Usability.Tree(formula).OfType<ScrollViewer>().First().ScrollToVerticalOffset(500); formula.UpdateLayout(); Capture(theme + "-formula-arithmetic", (FrameworkElement)formula.Content, 680, 560); formula.Close();
            var roles = new GearRolesDialog(Controller.Profile, "Balanced") { Owner = this }; Show(roles); Capture(theme + "-roles", (FrameworkElement)roles.Content, 680, 560); roles.Close();
            var editor = new RoleWeightsDialog(Controller.Profile, "Balanced") { Owner = this }; Show(editor); Capture(theme + "-weight-editor", (FrameworkElement)editor.Content, 740, 600); editor.Close();
            var settings = new SettingsWindow(Controller) { Owner = this }; Show(settings); settings.SelectSection(SettingsSection.Appearance); Capture(theme + "-tooltip-settings", (FrameworkElement)settings.Content, 900, 760); settings.Close();
        }
        var legacyFormula = new RatingFormulaDialog("Balanced Tank", new(Controller.Profile.Weights["Balanced Tank"]), legacy, false) { Owner = this }; Show(legacyFormula);
        Check(Usability.Tree(legacyFormula).OfType<TextBlock>().Any(t => t.Text.EndsWith("= +32.5 points")) && Usability.Tree(legacyFormula).OfType<TextBlock>().Any(t => t.Text.StartsWith("Lose 4 STR")), "Formula renders reconciled legacy equation and real STR loss"); legacyFormula.Close();
        Json.Write(Path.Combine(folder, "role-ratings-checks.json"), new { Passed = checks.Count, Checks = checks, Samples = samples, Performance = UpdateDiagnostics.Drain(), SyntheticOnly = true, PhysicalDpiAndLiveGame = "Separate acceptance" });
    }
}
