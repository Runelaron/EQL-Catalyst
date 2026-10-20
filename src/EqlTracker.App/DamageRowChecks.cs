using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public static class DamageRowChecks
{
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Pause(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    public static void Run(Action<bool, string> check, string folder)
    {
        var shape = DamageDetailWash.CreateShape(new Size(100, 16));
        check(!shape.FillContains(new Point(20, .5)) && !shape.FillContains(new Point(20, 15.5)) && shape.FillContains(new Point(10, .5)) && shape.FillContains(new Point(20, 8)), "EQ wash has paired fifth-interval divots while retaining a connected center");
        var view = new DamageRowsView(); var list = (ListBox)view.Content;
        var host = new Window { Content = view, Width = 340, Height = 180, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Left = -15000, Top = -15000 };
        var a = new DamageRow("Slash", "Melee", 100, 10, 10, 30, 10, 1, 10, 0, 50, "Physical");
        var b = a with { Ability = "Frost", Kind = "Spell", Damage = 80, Channel = "Cold" };
        var c = a with { Ability = "Kick", Damage = 60 };
        try
        {
            view.SetRows([a, b, c], "motion", false); host.Show(); Drain(); host.UpdateLayout();
            var item = (DamagePresentation)list.Items[0]; list.SelectedItem = item;
            ListBoxItem Container(DamagePresentation row) => (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(row);
            double Top(DamagePresentation row) => Container(row).TransformToAncestor(list).Transform(new Point()).Y;
            double Offset(DamagePresentation row) => Container(row).RenderTransform is TranslateTransform t ? t.Y : 0;
            double height = Container(item).ActualHeight;
            check(height <= 30, "Normal single-line strike rows fit within 30 DIP including the wash");
            check(Tree(view).OfType<DamageDetailWash>().Count() == 3 && !Tree(view).OfType<ProgressBar>().Any(), "Every ability embeds its bar behind details instead of adding a separate bar line");
            check(Tree(view).OfType<DamageDetailWash>().All(w => Math.Abs(w.Displayed - w.Target) < .001), "Reduced-motion washes display exact proportional values");
            view.SetRows([c with { Damage = 200 }, a, b], "motion", true); Drain();
            bool animated = SystemParameters.ClientAreaAnimation;
            check(ReferenceEquals(item, list.Items[1]) && ReferenceEquals(item, list.SelectedItem), "Strike rank changes preserve row identity and selection");
            check(!animated || Math.Abs(Offset(item)) > 1, "Rising and falling strikes slide from their previous positions");
            Pause(90); double before = Top(item);
            view.SetRows([b with { Damage = 300 }, c with { Damage = 200 }, a], "motion", true); Drain();
            double after = Top(item);
            // Rendering may advance the new animation while Drain runs on a busy desktop.
            // The captured origin is the invariant: the new slide starts at the exact
            // displayed position rather than at a stale row boundary.
            check(!animated || view.MotionOrigins.TryGetValue(item.Key, out double retargetOrigin) && Math.Abs(before - retargetOrigin) < .5, "A second rank change retargets from the current visual position without jumping");
            var transform = Container(item).RenderTransform;
            view.SetRows([b with { Damage = 301 }, c with { Damage = 201 }, a with { Damage = 101 }], "motion", true); Drain();
            check(ReferenceEquals(transform, Container(item).RenderTransform), "Damage-only refreshes preserve a running row slide");
            Pause(320);
            check(list.Items.OfType<DamagePresentation>().All(row => Math.Abs(Offset(row)) < .01), "Row translations settle at their exact sorted positions");
            view.SetRows([a, b, c], "motion", false); Drain();
            check(list.Items.OfType<DamagePresentation>().All(row => Math.Abs(Offset(row)) < .01), "Disabling smooth motion snaps sorted rows immediately");
            double start = Top(item);
            view.SetRows([c with { Damage = 200 }, a, b], "motion", true);
            view.SetRows([b with { Damage = 300 }, c with { Damage = 200 }, a], "motion", true); Drain();
            check(!animated || view.MotionOrigins.TryGetValue(item.Key, out double origin) && Math.Abs(origin - start) < .1, "Two rank changes before layout preserve the last displayed row positions");
            Pause(300);
            view.SetRows([b, a, c], "new fight", true); Drain();
            check(!ReferenceEquals(item, list.Items[1]) && list.Items.OfType<DamagePresentation>().All(row => Math.Abs(Offset(row)) < .01), "Encounter and filter changes reset movement immediately");
            host.Width = 820; Drain(); host.UpdateLayout();
            var wideWashes = Tree(view).OfType<DamageDetailWash>().ToList();
            check(wideWashes.Count == 3 && wideWashes.Max(w => w.ActualWidth) - wideWashes.Min(w => w.ActualWidth) < .1, "Inline Combat Lab washes share a consistent width for proportional comparison");
            check(Tree(view).OfType<AbilityMetricsText>().All(metrics => metrics.VisibleMetricCount == 5), "Wide Combat Lab rows reveal every prioritized metric without wrapping");
            host.Width = 140; view.Compact = true;
            var example = a with { Damage = 33368, Dps = 397.2, MaxHit = 902, Hits = 43, CriticalPercent = 100, Average = 776 };
            view.SetRows([example], "fitting", false); Drain(); host.UpdateLayout();
            var narrowMetrics = Tree(view).OfType<AbilityMetricsText>().Single();
            check(narrowMetrics.VisibleMetricCount == 3 && narrowMetrics.Text == "397.2 dps · 902 max · ×43", "The preferred 140-DIP meter shows DPS, Max and Hits while omitting Crit and Avg");
            check(narrowMetrics.ActualHeight <= 11.1 && Container((DamagePresentation)list.Items[0]).ActualHeight <= 25.1, "Compact strike details stay on one line instead of increasing row height");
            var fullTooltip = ((DamagePresentation)list.Items[0]).Tooltip;
            check(fullTooltip.Contains("100% crit") && fullTooltip.Contains("776.0 avg") && fullTooltip.Contains("Peak hit"), "Hidden strike metrics and peak-hit evidence remain in the tooltip");
            host.Width = 100; Drain(); host.UpdateLayout();
            check(narrowMetrics.VisibleMetricCount == 2 && narrowMetrics.Text == "397.2 dps · 902 max", "Further narrowing removes Hits before the higher-priority DPS and Max fields");
            host.Width = 140;
            view.SetRows([example with { Dps = 1234567.8, MaxHit = 1234567, Hits = 1234567 }], "fitting", false); Drain(); host.UpdateLayout();
            check(narrowMetrics.VisibleMetricCount <= 2 && narrowMetrics.ActualHeight <= 11.1, "Growing combat values hide complete lower-priority fields without wrapping");
            view.SetRows([example], "fitting", false); Drain(); host.UpdateLayout();
            check(ReferenceEquals(narrowMetrics, Tree(view).OfType<AbilityMetricsText>().Single()) && narrowMetrics.VisibleMetricCount == 3, "Shorter values restore metrics without replacing the strike control");
            host.Width = 220; Drain(); host.UpdateLayout();
            check(narrowMetrics.VisibleMetricCount >= 4, "Widening a compact row automatically restores lower-priority metrics");
            host.Width = 820; Drain(); host.UpdateLayout();
            check(narrowMetrics.VisibleMetricCount == 5, "Returning to the wide layout restores Crit and Avg after compact fitting");
            var widthsSeen = new List<int>();
            foreach (int width in new[] { 100, 140, 180, 220, 300 })
            {
                host.Width = width; Drain(); host.UpdateLayout();
                widthsSeen.Add(narrowMetrics.VisibleMetricCount);
                check(narrowMetrics.ActualHeight <= 11.1 && ((TextBlock)narrowMetrics.Child).ActualWidth <= narrowMetrics.ActualWidth + .1, $"Metrics remain one line within their available width at {width} DIP");
            }
            host.Width = 240; view.Compact = true;
            var many = Enumerable.Range(0, 40).Select(index => a with { Ability = "Strike " + index, Damage = 1000 - index }).ToList();
            view.SetRows(many, "scroll", true); Drain(); host.UpdateLayout();
            var scroll = Tree(list).OfType<ScrollViewer>().First(); scroll.ScrollToVerticalOffset(90); Drain();
            var scrollbar = Tree(list).OfType<System.Windows.Controls.Primitives.ScrollBar>().First(bar => bar.Orientation == Orientation.Vertical);
            check(scrollbar.ActualWidth <= 4.1, "Damage-row scrollbar remains four DIP wide at the native template level");
            (many[4], many[5]) = (many[5], many[4]); view.SetRows(many, "scroll", true); Drain();
            scroll.ScrollToVerticalOffset(145); Drain();
            var realized = list.Items.OfType<DamagePresentation>().Where(row => list.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem).ToList();
            check(scroll.VerticalOffset > 100 && realized.All(row => Math.Abs(Offset(row)) < .01), "Scrolling clears pending movement and recycled-container translations");
            var timings = new List<double>();
            for (int update = 0; update < 64; update++)
            {
                var changed = many.Select((row, index) => row with { Dps = 10 + update * .1 + index, Hits = 10 + update }).ToList();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                view.SetRows(changed, "scroll", false); Drain(); host.UpdateLayout();
                if (update > 7) timings.Add(watch.Elapsed.TotalMilliseconds);
            }
            check(Tree(view).OfType<AbilityMetricsText>().All(metrics => metrics.ActualHeight <= 11.1), "Repeated live metric updates retain the compact single-line layout");
            var ordered = timings.Order().ToArray();
            Json.Write(Path.Combine(folder, "damage-row-checks.json"), new { NormalSingleLineHeight = height, MetricCountsAt100_140_180_220_300 = widthsSeen, LiveMetricUpdateP99Ms = ordered[(int)((ordered.Length - 1) * .99)], LiveMetricUpdateMaxMs = ordered.Last(), RetargetBefore = before, RetargetAfter = after, SystemAnimationEnabled = animated, ScrollOffset = scroll.VerticalOffset, RealizedRows = realized.Count });
        }
        finally { host.Close(); }
    }
}
