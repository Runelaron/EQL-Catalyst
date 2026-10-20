using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class DamagePresentation(DamageRow row, double percent, bool inline, bool compact, bool smooth) : INotifyPropertyChanged
{
    public DamageRow Row { get; private set; } = row;
    public double BarPercent { get; private set; } = percent;
    public bool Inline { get; private set; } = inline;
    public bool Compact { get; private set; } = compact;
    public bool Smooth { get; private set; } = smooth;
    public string Key => Row.Kind + "\0" + Row.Ability + "\0" + Row.Channel;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(DamageRow value, double percent, bool inline, bool compact, bool smooth)
    {
        if (Row == value && BarPercent == percent && Inline == inline && Compact == compact && Smooth == smooth) return;
        Smooth = smooth; PropertyChanged?.Invoke(this, new(nameof(Smooth)));
        Row = value; BarPercent = percent; Inline = inline; Compact = compact;
        foreach (string name in new[] { nameof(Row), nameof(BarPercent), nameof(Inline), nameof(Compact), nameof(Label), nameof(Details), nameof(Tooltip), nameof(HealingText) }) PropertyChanged?.Invoke(this, new(name));
    }
    public string Label => Row.Kind == "Melee" ? Row.Ability switch
    {
        "slashes" => "Slash", "pierces" => "Pierce", "crushes" => "Crush", "hits" => "Hit",
        "backstabs" => "Backstab", "kicks" => "Kick", "bashes" => "Bash", "cleaves" => "Cleave",
        "punches" => "Punch", "strikes" => "Strike", "flying kicks" => "Flying kick", _ => Row.Ability
    } : Row.Ability;
    public string Details => AbilityDetailText.Full(Row);
    public string HealingText => Row.LinkedHealing is { } hp ? $"{hp:N0} HP" : "";
    public string Tooltip => Row.CustomTooltip ?? $"{Label} · {Row.Channel}\n{Row.Damage:N0} damage · {Row.Percent:N1}% of detailed damage\n{Details}\nPeak hit {Row.MaxHit:N0} at {Row.PeakTime:MMM dd HH:mm:ss}\n{Row.Criticals:N0} critical hits · {Row.Misses:N0} misses / avoidance\n" + (Row.LinkedHealing.HasValue ? $"{Row.LinkedHealing:N0} HP restored · {Row.LinkedHealCount} verified linked healing records\n" : "") + "Bar: damage relative to the largest row · select to mark peak hit";
}
public sealed class DamageBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => UI.Brush(value?.ToString() == "Healing" ? "Mint" : value?.ToString() == "Melee" ? "Melee" : "Spell");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public partial class DamageRowsView : UserControl
{
    public static readonly DependencyProperty HideHealingProperty = DependencyProperty.Register(nameof(HideHealing), typeof(bool), typeof(DamageRowsView), new PropertyMetadata(false));
    public bool HideHealing { get => (bool)GetValue(HideHealingProperty); set => SetValue(HideHealingProperty, value); }
    public static readonly DependencyProperty ShowContributionProperty = DependencyProperty.Register(nameof(ShowContribution), typeof(bool), typeof(DamageRowsView), new PropertyMetadata(false, (d, _) => ((DamageRowsView)d).Present()));
    public bool ShowContribution { get => (bool)GetValue(ShowContributionProperty); set => SetValue(ShowContributionProperty, value); }
    public static readonly DependencyProperty MonkKickProperty = DependencyProperty.Register(nameof(MonkKick), typeof(bool), typeof(DamageRowsView), new PropertyMetadata(false));
    public bool MonkKick { get => (bool)GetValue(MonkKickProperty); set => SetValue(MonkKickProperty, value); }
    public event Action<DamageRow?>? AbilitySelected;
    private List<DamageRow> rows = [];
    private readonly ObservableCollection<DamagePresentation> items = [];
    private string signature = "";
    private string context = "", presentedContext = "";
    private bool smooth;
    private static readonly DependencyProperty MotionKeyProperty = DependencyProperty.RegisterAttached("MotionKey", typeof(string), typeof(DamageRowsView), new PropertyMetadata(""));
    private int motionRevision;
    internal Dictionary<string, double> MotionOrigins { get; } = [];
    private bool presentedInline, presentedCompact;
    private Dictionary<string, double>? pendingPositions;
    private DispatcherOperation? pendingMotion;
    private bool compact;
    public bool Compact { get => compact; set { compact = value; Present(); } }
    private void LeaveAbility(object sender, System.Windows.Input.MouseEventArgs e) { if (sender is FrameworkElement { ToolTip: ToolTip tip }) tip.IsOpen = false; }
    public DamageRowsView() { InitializeComponent();
        var scrollbar = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar), (Style)Application.Current.FindResource(typeof(System.Windows.Controls.Primitives.ScrollBar)));
        scrollbar.Setters.Add(new Setter(WidthProperty, 4d)); Rows.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = scrollbar;
        Rows.Resources[SystemParameters.VerticalScrollBarWidthKey] = 4d;
        Rows.ItemsSource = items; Rows.SelectionChanged += (_, _) => AbilitySelected?.Invoke((Rows.SelectedItem as DamagePresentation)?.Row);
        SizeChanged += (_, _) => { HideHealing = ActualWidth < 170; ResetMotion(); Present(); };
        Unloaded += (_, _) => ResetMotion(); IsVisibleChanged += (_, _) => { if (!IsVisible) ResetMotion(); };
        Rows.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => { if (e.VerticalChange != 0 || e.HorizontalChange != 0) ResetMotion(); }));
        VirtualizingStackPanel.AddCleanUpVirtualizedItemHandler(Rows, (_, e) => { if (e.UIElement is ListBoxItem container) ResetContainer(container); });
        Rows.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (Rows.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated) return;
            foreach (var item in items) if (Rows.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container && (string)container.GetValue(MotionKeyProperty) is { Length: > 0 } key && key != item.Key) ResetContainer(container);
        };
    }
    public void SetRows(List<DamageRow> values, string contextKey = "", bool animate = false) { if (context == contextKey && smooth == animate && rows.SequenceEqual(values)) return; rows = values; context = contextKey; smooth = animate; Present(); }
    private void Present()
    {
        double Measure(string text, double size) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
        double nameWidth = Math.Min(220, rows.Select(r => Measure(r.Ability, compact ? 11 : 13) + 22).DefaultIfEmpty(100).Max());
        double metricsWidth = rows.Select(r => Measure(AbilityDetailText.Full(r), compact ? 9 : 10)).DefaultIfEmpty(260).Max();
        double totalWidth = rows.Select(r => Measure(r.Damage.ToString("N0"), compact ? 11 : 13) + (r.LinkedHealing.HasValue && !HideHealing ? Measure(r.LinkedHealing.Value.ToString("N0") + " HP", 11) + 6 : 0)).DefaultIfEmpty(60).Max();
        totalWidth += ShowContribution ? 46 : 0;
        bool inline = !compact && ActualWidth >= Math.Max(480, nameWidth + metricsWidth + totalWidth + 34);
        var next = inline + "|" + compact + "|" + smooth + "|" + context + "|" + string.Join('|', rows.Select(x => x.ToString()));
        if (next == signature) return;
        signature = next;
        bool reset = context != presentedContext; presentedContext = context;
        bool layoutChanged = inline != presentedInline || compact != presentedCompact;
        presentedInline = inline; presentedCompact = compact;
        var orderedKeys = rows.Select(x => x.Kind + "\0" + x.Ability + "\0" + x.Channel).ToList();
        bool reordered = !items.Select(x => x.Key).SequenceEqual(orderedKeys);
        bool motion = smooth && !reset && !layoutChanged && reordered && IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation;
        // Read the on-screen position before removing an unfinished translation. A second
        // update in the same dispatcher turn keeps the original, not-yet-rendered positions.
        var positions = motion ? pendingPositions ?? CapturePositions() : null;
        if (reset || layoutChanged || reordered || !smooth) ResetMotion();
        if (reset) items.Clear();
        long largest = Math.Max(1, rows.Select(x => x.Damage).DefaultIfEmpty(0).Max());
        var keys = orderedKeys.ToHashSet();
        for (int i = items.Count - 1; i >= 0; i--) if (!keys.Contains(items[i].Key)) items.RemoveAt(i);
        var existing = items.ToDictionary(x => x.Key);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; string key = orderedKeys[i];
            existing.TryGetValue(key, out var item);
            if (item == null) { item = new(row, row.Damage * 100d / largest, inline, compact, smooth); items.Insert(i, item); }
            else { item.Update(row, row.Damage * 100d / largest, inline, compact, smooth); int previous = items.IndexOf(item); if (previous != i) items.Move(previous, i); }
        }
        if (positions == null || positions.Count == 0) return;
        int current = motionRevision; pendingPositions = positions;
        pendingMotion = Dispatcher.BeginInvoke(() =>
        {
            if (current != motionRevision) return;
            pendingMotion = null; pendingPositions = null;
            Rows.UpdateLayout();
            if (current != motionRevision) return;
            foreach (var item in items)
            {
                if (!positions.TryGetValue(item.Key, out double old) || Rows.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container) continue;
                double top = container.TransformToAncestor(Rows).Transform(new Point()).Y;
                if (Math.Abs(old - top) < .5 || top + container.ActualHeight < 0 || top > Rows.ActualHeight) continue;
                MotionOrigins[item.Key] = old;
                var translate = new TranslateTransform(); container.RenderTransform = translate;
                container.SetValue(MotionKeyProperty, item.Key);
                var animation = new DoubleAnimation(old - top, 0, TimeSpan.FromMilliseconds(250)) { FillBehavior = FillBehavior.Stop, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                animation.Completed += (_, _) => { if (ReferenceEquals(container.RenderTransform, translate)) translate.BeginAnimation(TranslateTransform.YProperty, null); };
                translate.BeginAnimation(TranslateTransform.YProperty, animation);
            }
        }, DispatcherPriority.Loaded);
    }
    private Dictionary<string, double> CapturePositions()
    {
        var result = new Dictionary<string, double>();
        foreach (var item in items) if (Rows.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
        {
            double top = container.TransformToAncestor(Rows).Transform(new Point()).Y;
            if (top + container.ActualHeight >= 0 && top <= Rows.ActualHeight) result[item.Key] = top;
        }
        return result;
    }
    private void ResetMotion()
    {
        ++motionRevision; pendingMotion?.Abort(); pendingMotion = null; pendingPositions = null; MotionOrigins.Clear();
        foreach (var item in items) if (Rows.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container) ResetContainer(container);
    }
    private static void ResetContainer(ListBoxItem container)
    {
        if (container.RenderTransform is TranslateTransform translate) translate.BeginAnimation(TranslateTransform.YProperty, null);
        container.RenderTransform = Transform.Identity; container.ClearValue(MotionKeyProperty);
    }
}
