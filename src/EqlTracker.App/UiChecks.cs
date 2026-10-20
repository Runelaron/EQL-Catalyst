using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public static class UiChecks
{
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    public static void Run(TrackerController controller, DpsWindow detailed, OverlayWindow compact, string folder, QuestWindow quest)
    {
        var results = new List<string>(); var original = controller.Profile.Meter; var originalOverlay=controller.Profile.OverlayMeter;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("UI regression: " + message); results.Add(message); }
        void Flush() { detailed.Refresh(); compact.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); }
        try
        {
            controller.Profile.Meter = new(); controller.Profile.OverlayMeter=new(); Flush();
            var playerButton = Tree(compact).OfType<Button>().Single(b => b.Content?.ToString() == "Self");
            playerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            Check(controller.Profile.OverlayMeter!.Actor == "Player", "Compact actor button updates its independent selection");
            var actor = Tree(detailed).OfType<ComboBox>().Single(b => b.Items.Cast<object>().Any(x => x.ToString() == "Unknown"));
            Check(actor.SelectedItem?.ToString() == "Pet", "Detailed actor stays independent of compact selection");
            actor.SelectedItem = "Combined"; Flush();
            Check(controller.Profile.Meter.Actor == "Combined" && controller.Profile.OverlayMeter!.Actor=="Player", "Detailed actor does not update compact state");
            Check(compact.HeadingTitle.Text.StartsWith("Self damage", StringComparison.Ordinal), "Compact title follows its own actor selection");
            var session = Tree(compact).OfType<Button>().Single(b => b.Content?.ToString() == "Session");
            session.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            Check(controller.Profile.OverlayMeter!.Scope == "Session", "Compact scope button updates its own selection");
            var scope = Tree(detailed).OfType<ComboBox>().Single(b => b.Items.Contains("Fight") && b.Items.Contains("Session"));
            Check(scope.SelectedItem?.ToString() == "Fight", "Detailed scope stays independent of compact selection");
            var sorting = Tree(detailed).OfType<ComboBox>().Single(b => b.IsVisible && b.Items.Cast<object>().Any(x => x.ToString() == "Average"));
            sorting.SelectedItem = "Average"; Flush();
            Check(controller.Profile.Meter.Sort == "Average", "Detailed sorting persists in shared state");
            var search = Tree(detailed).OfType<TextBox>().Single(b => b.Width == 180);
            search.Text = "Spirit of Vermin"; Flush(); var view = controller.DpsView();
            Check(view.Rows.Count == 1 && view.Rows[0].Ability == "Spirit of Vermin Strike IV", "Spell search filters Combat Lab only");
            Check(Math.Abs(view.Dps - view.Damage / view.Seconds) < .00001, "Filtered UI totals use the shared duration");
            Check(controller.OverlayDpsView().Rows.Count>view.Rows.Count,"Compact view stays unfiltered during Lab search");
            var filters=Tree(compact).OfType<Button>().Single(b=>b.Content?.ToString()?.StartsWith("Filters…")==true);filters.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush();
            var popup=Application.Current.Windows.OfType<OverlayFilterWindow>().Single();Tree(popup).OfType<Button>().Single(b=>b.Content?.ToString()=="Clear filters").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush();
            Check(search.Text=="Spirit of Vermin","Tracker clear filters preserves detailed search");popup.Close();
            var pin = compact.HeaderActions.Children.OfType<Button>().Single(b => b.Content?.ToString() is "◇" or "◆");
            bool wasLocked = controller.Profile.Dps.Locked; pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(compact.IsLocked != wasLocked && pin.Content?.ToString() == (compact.IsLocked ? "◆" : "◇"), "Pin state and label update together");
            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            double originalWidth = compact.Width, originalHeight = compact.Height;
            compact.SetMeterSize(128, 72); Flush(); compact.UpdateLayout();
            Check(compact.ActualWidth == 128 && compact.ActualHeight == 72, "Meter can shrink to 128 by 72 logical pixels");
            var face = (FrameworkElement)compact.Content;
            Check(Tree(compact).OfType<Button>().Where(b => b.IsVisible && b.ActualWidth > 0).All(b => { var bounds = b.TransformToAncestor(face).TransformBounds(new Rect(0, 0, b.ActualWidth, b.ActualHeight)); return bounds.Left >= 0 && bounds.Right <= face.ActualWidth + 1 && bounds.Top >= 0 && bounds.Bottom <= face.ActualHeight + 1; }), "Hotbar-size buttons remain inside the window");
            var smallSession = compact.HeadingTitle.ContextMenu.Items.OfType<MenuItem>().Single(b => b.Header?.ToString() == "Session scope");
            smallSession.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Flush();
            Check(controller.Profile.OverlayMeter!.Scope == "Session", "Hotbar-size scope controls use independent state");
            compact.SetMeterSize(240, 160); Flush(); compact.UpdateLayout();
            var smallActor = Tree(compact).OfType<ComboBox>().Single(b => b.Items.Cast<object>().Any(x => x.ToString() == "All"));
            Check(smallActor.IsVisible, "Compact actor selector is visible without clipping");
            smallActor.SelectedItem = "All"; Flush();
            Check(controller.Profile.OverlayMeter!.Actor == "Combined", "Compact All selector maps to combined damage");
            var flip = Tree(compact).OfType<Button>().Single(b => b.Content?.ToString() == "⇅"); flip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            Check(controller.Profile.OverlayMeter!.Actor == "Pet", "Paired meter swap updates its independent actor");
            double expandedHeight = compact.Height, savedExpandedHeight = controller.Profile.Dps.Height; compact.ToggleCollapse(); Flush(); compact.UpdateLayout();
            Check(compact.IsCollapsed && compact.Height == 28 && compact.Body.Visibility == Visibility.Collapsed, "Collapse keeps only a 28 DIP draggable title bar");
            Check(controller.Profile.Dps.Height == savedExpandedHeight, "Collapsing does not overwrite expanded overlay bounds");
            compact.ToggleCollapse(); Flush(); Check(!compact.IsCollapsed && compact.Height == expandedHeight, "Restore returns the exact expanded height");
            Check(smallActor.Height == 20 && smallActor.Width < 90, "Dense selectors use content-sized widths and 20 DIP height");
            Check(System.Windows.Controls.VirtualizingPanel.GetScrollUnit(Tree(compact).OfType<ListBox>().Single()) == System.Windows.Controls.ScrollUnit.Pixel, "Damage meter uses pixel scrolling while retaining virtualization");
            Check(compact.HeadingTitle.ContextMenu.Template != null, "Frost menu replaces the operating system icon gutter");
            Check(detailed.Topmost && !detailed.ShowActivated && !detailed.ShowInTaskbar, "Expanded panels open topmost without requesting activation or taskbar space");
            Check(Tree(detailed).OfType<GridSplitter>().Any(), "Combat abilities and targets have a draggable splitter");
            detailed.MarkAbility("backstabs"); Flush();
            Check(Tree(detailed).OfType<Timeline>().Single().Highlight?.Damage > 0, "Peak hit selection survives periodic refresh");
            var scrollSample = new ScrollViewer { Width = 200, Height = 60, Content = UI.Stack(Enumerable.Range(0, 20).Select(i => (UIElement)new Border { Height = 23 }).ToArray()) };
            compact.Body.Children.Add(scrollSample); Flush(); compact.UpdateLayout(); WheelRouter.Scroll(scrollSample, -30); Flush(); compact.UpdateLayout();
            Check(scrollSample.VerticalOffset > 0 && scrollSample.VerticalOffset < 23, "Fractional wheel input moves within a row instead of paging"); compact.Body.Children.Remove(scrollSample);
            quest.ShowView(TrackerTab.Gear, "Bloodmoon");
            var gearGrid = Tree(quest).OfType<DataGrid>().Single(g => g.Columns.Any(c => c.Header?.ToString() == "Data quality"));
            Check(gearGrid.Items.OfType<GearRow>().Any(r => r.Name == "Bloodmoon"), "Offline gear search displays Bloodmoon even with an unconfirmed baseline");
            Check(gearGrid.Items.OfType<GearRow>().All(r => r.Comparison?.CanCompare == true || r.Delta == null), "Incomplete comparisons do not display invented score changes");
            Check(gearGrid.Columns.Any(c => c.Header?.ToString() == "Item score") && gearGrid.Items.OfType<GearRow>().First(r => r.Name == "Bloodmoon").ItemScore != null, "Item scores stay visible independently of an unavailable upgrade baseline");
            Check(gearGrid.Items.OfType<GearRow>().First().ScoreTooltip.Contains("not measured DPS"), "Score cells explain preference scoring");
            quest.ShowView(TrackerTab.Progress, "Magician Epic Quest");
            var tree = Tree(quest).OfType<TreeView>().Single();
            var branches = tree.Items.OfType<QuestProgressNode>().SelectMany(n => n.Descendants()).ToList();
            Check(branches.Any(i => i.Node.Name == "Broom of Trilon" && i.Node.Status == "Owned"), "Progress tree exposes bank-owned intermediate rewards");
            Check(branches.Any(i => i.Node.Name.StartsWith("Cyclops Toes") && i.Node.Status == "Covered by reward"), "Progress tree explains ingredient rollups");
            Check(branches.All(i => i.Node.QuestId.Length > 0), "Hierarchy nodes retain quest detail references");
            Check(Tree(quest).OfType<TextBlock>().Any(t => t.Text.Equals("How to obtain", StringComparison.OrdinalIgnoreCase)), "Progress includes an acquisition pane for fulfilled and incomplete nodes");
            Check(Tree(quest).OfType<TextBlock>().Any(t => t.Text == "STEP STATUS"), "Progress status is a titled card");
            quest.ShowView(TrackerTab.Items);
            var goalTable = Tree(quest).OfType<DataGrid>().Single(g => g.Columns.Any(c => c.Header?.ToString() == "H/N"));
            quest.SetZoneFilter("All zones"); quest.SetSearch(""); Flush(); int populated = goalTable.Items.Count; double classWidth = goalTable.Columns[2].ActualWidth;
            quest.SetSearch("zzzz-no-matching-quest-item"); Flush();
            Check(goalTable.Items.Count == 0, "An empty quest filter clears previously populated rows");
            Check(goalTable.HeadersVisibility == DataGridHeadersVisibility.Column && goalTable.Columns[2].ActualWidth >= classWidth - 1, "Empty quest results preserve headers and short-column widths");
            quest.SetSearch(""); Flush(); Check(goalTable.Items.Count == populated && populated > 0, "Quest results recover after clearing an empty search");
            double dw = detailed.Width, dh = detailed.Height; detailed.Width = 640; detailed.Height = 420; Flush(); detailed.UpdateLayout();
            var targetGrid = Tree(detailed).OfType<DataGrid>().Single(x => x.Columns.Any(c => c.SortMemberPath == "Target"));
            Json.Write(Path.Combine(folder, "target-layout-check.json"), new { Width = targetGrid.ActualWidth, Columns = targetGrid.Columns.Select(c => new { c.Header, c.ActualWidth, Width = c.Width.ToString(), c.Visibility }).ToArray(), Scrolls = Tree(targetGrid).OfType<ScrollViewer>().Select(s => new { s.ViewportWidth, s.ExtentWidth, s.HorizontalOffset }).ToArray() });
            Check(targetGrid.Columns[1].ActualWidth >= 70, "Narrow target damage column preserves readable numeric width");
            Check(targetGrid.Columns.Where(c => c.Visibility == Visibility.Visible).Sum(c => c.ActualWidth) <= targetGrid.ActualWidth + 1, "Minimum Combat Lab target columns fit the panel");
            detailed.Width = dw; detailed.Height = dh; Flush();
            Check(Tree(compact).OfType<DockPanel>().Any(x => x.Cursor == System.Windows.Input.Cursors.SizeAll && x.Background == Brushes.Transparent), "Overlay header has a hit-testable move surface");
            compact.SetMeterSize(128, 72); Flush();
            Check(Tree(compact).OfType<Button>().Single(b => b.Content?.ToString() == "↗").Visibility == Visibility.Collapsed, "Narrow headers move expand controls into the menu");
            Check(flip.IsVisible, "Minimum meter retains the paired actor swap");
            foreach (string element in PetElementDetector.Elements) { var icon = new ActorBadge(); icon.Set("Pet", true, element, element); Check(icon.Element == element, "Element vector badge accepts " + element); }
            compact.SetMeterSize(originalWidth, originalHeight); Flush();
            var stableRows = new DamageRowsView(); var sample = new DamageRow("Slash", "Melee", 100, 10, 1, 100, 100, 0, 0, 0, 100);
            stableRows.SetRows([sample], "same fight", false); var stableList = (ListBox)stableRows.Content; var presentation = stableList.Items[0];
            stableRows.SetRows([sample with { Damage = 250 }], "same fight", true);
            Check(ReferenceEquals(presentation, stableList.Items[0]), "Damage updates retain row instances instead of rebuilding the list");
            Check(((DamagePresentation)stableList.Items[0]).Row.Damage == 250, "Animation models retain exact target damage");
            stableRows.SetRows([sample], "different fight", false);
            Check(!ReferenceEquals(presentation, stableList.Items[0]), "Scope changes reset animation context");
            long clock = 0; var cadence = new DisplayCadence(() => clock);
            Check(cadence.Due("first fight"), "First combat reading bypasses the two-second throttle");
            Check(!cadence.Due("first fight"), "Repeated DPS reading waits for the display interval");
            clock = System.Diagnostics.Stopwatch.Frequency;
            Check(cadence.Due("second short fight"), "A fight shorter than two seconds cannot hide the next fight");
            Check(cadence.Due("second short fight · target died"), "Final combat evidence bypasses the display throttle");
            clock += System.Diagnostics.Stopwatch.Frequency * 2;
            Check(cadence.Due("second short fight · target died"), "Subsequent DPS display becomes due after two seconds");
            var number = new AnimatedNumber { Smooth = false, Value = 1234 }; number.Snap();
            Check(number.Value == 1234 && number.Text == 1234d.ToString("N0"), "Reduced-motion numbers show their exact target");
            var motionToggle = compact.HeadingTitle.ContextMenu.Items.OfType<MenuItem>().SelectMany(m => m.Items.OfType<MenuItem>()).Single(x => x.Header?.ToString() == "Smooth motion");
            bool oldMotion = controller.Profile.SmoothMeterMotion; controller.Profile.SmoothMeterMotion = true; motionToggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Flush();
            Check(!controller.Profile.SmoothMeterMotion, "Meter menu can disable smooth motion");
            controller.Profile.SmoothMeterMotion = oldMotion;
            var luminous = compact.HeadingTitle.ContextMenu.Items.OfType<MenuItem>().SelectMany(m => m.Items.OfType<MenuItem>()).Single(x => x.Header?.ToString() == "Luminous");
            string oldStyle = controller.Profile.MeterStyle; luminous.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Flush();
            Check(controller.Profile.MeterStyle == "Luminous" && ((Border)compact.Content).Effect != null, "Meter style menu applies the optional glow");
            controller.Profile.MeterStyle = oldStyle; Flush();
            compact.Body.Children.Add(number); Flush(); number.Smooth = true; number.Value = 2000;
            Check(number.Value == 2000, "Rolling display exposes the exact target while animating");
            var frame = new DispatcherFrame(); var stop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) }; stop.Tick += (_, _) => { stop.Stop(); frame.Continue = false; }; stop.Start(); Dispatcher.PushFrame(frame);
            Check(number.Text == 2000d.ToString("N0"), "Rolling display settles on the exact target"); compact.Body.Children.Remove(number);
            DamageRowChecks.Run(Check, folder);
            HealingUiChecks.Run(controller, detailed, Check);
            AppearanceUiChecks.Run(controller, quest, compact, Check);
            PlanningUiChecks.Run(controller, quest, Check, folder);
            BrandUiChecks.Run(controller, Check);
            UsabilityUiChecks.Run(controller, quest, detailed, compact, Check, folder);
            ProgressAcquisitionUiChecks.Run(controller, quest, Check);
            GearBuildUiChecks.Run(controller, quest, Check, folder);
            FocusedWorkspaceChecks.Run(controller, quest, Check);
            SolidDialogChecks.Run(controller, quest, Check);
            WindowVisualChecks.Run(controller, quest, detailed, compact, Check);
            PetUpgradeUiChecks.Run(controller, quest, Check);
            SessionPolishChecks.Run(controller, detailed, Check, folder);
            CombatPolishChecks.Run(controller, detailed, quest, Check, folder);
            IconStyleUiChecks.Run(controller, quest, Check, folder);
            Json.Write(Path.Combine(folder, "ui-checks.json"), new { Passed = results.Count, AppAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))).ToLowerInvariant(), CoreAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(GearScorer).Assembly.Location))).ToLowerInvariant(), Checks = results });
        }
        finally { controller.Profile.Meter = original; controller.Profile.OverlayMeter=originalOverlay; controller.Save(); Flush(); }
    }
}
