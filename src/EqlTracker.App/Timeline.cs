using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class Timeline : FrameworkElement
{
    private PeakHit? highlight;
    public PeakHit? Highlight { get => highlight; set { if (highlight == value) return; highlight = value; drawing = null; InvalidateVisual(); } }
    private CombatChart chart = new(default, 1, 1, [], [], []);
    private DrawingGroup? drawing;
    private Size cachedSize;
    private readonly List<(Point Position, string Text)> markers = [];
    private ChartPeakMode peakMode = ChartPeakMode.Highest;
    public ChartPeakMode PeakMode { get => peakMode; set { if (peakMode == value) return; peakMode = value; drawing = null; InvalidateVisual(); } }
    private double? cursorX;
    public bool CursorVisible => cursorX.HasValue;
    internal void PreviewCursor(double fraction) { cursorX = 42 + Math.Max(0, ActualWidth - 54) * Math.Clamp(fraction, 0, 1); InvalidateVisual(); }
    public void ClearCursor() { cursorX = null; if (IsMouseCaptured) ReleaseMouseCapture(); InvalidateVisual(); }
    public Timeline()
    {
        MouseMove += (_, e) => { var p = e.GetPosition(this); cursorX = Math.Clamp(p.X, 42, Math.Max(42, ActualWidth - 12)); var hit = markers.FirstOrDefault(m => (m.Position - p).Length <= 12); string text = hit.Text ?? $"Chart {chart.Unit} · {chart.BinSeconds:0.##}s sampling interval\nElapsed wall time; session gaps remain visible. Pet blue / Self mint."; if (!Equals(ToolTip, text)) ToolTip = text; InvalidateVisual(); };
        MouseLeftButtonDown += (_, e) => { CaptureMouse(); e.Handled = true; };
        MouseLeftButtonUp += (_, e) => { if (IsMouseCaptured) { ReleaseMouseCapture(); e.Handled = true; } };
        MouseLeave += (_, _) => ClearCursor(); LostMouseCapture += (_, _) => { cursorX = null; InvalidateVisual(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { ClearCursor(); e.Handled = true; } };
        IsVisibleChanged += (_, _) => { if (!IsVisible) ClearCursor(); };
        Unloaded += (_, _) => ClearCursor();
        ToolTipService.SetInitialShowDelay(this, 650); ToolTipService.SetShowDuration(this, 5000);
        // Mutable shared brushes update existing drawings; invalidate geometry only for size/data changes.
        SizeChanged += (_, _) => drawing = null;
    }
    public void Set(List<Encounter> values, CombatFilter selected) => Set(CombatChart.Build(values, selected));
    public void Set(CombatChart value) { if (ReferenceEquals(value, chart)) return; chart = value; drawing = null; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 60 || ActualHeight <= 30) return;
        if (drawing == null || cachedSize != RenderSize) { cachedSize = RenderSize; drawing = new(); using var context = drawing.Open(); DrawChart(context); }
        dc.DrawDrawing(drawing);
        DrawCursor(dc);
    }
    private void DrawChart(DrawingContext dc)
    {
        markers.Clear(); double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(UI.Brush("InnerSurface"), new Pen(UI.Brush("Edge"), 1), new(0, 0, w, h), 3, 3);
        double left = 42, top = 13, right = w - 12, bottom = h - 17, height = Math.Max(8, bottom - top);
        double actualMax = Math.Max(1, chart.Pet.Concat(chart.Self).DefaultIfEmpty(0).Max());
        int desiredTicks = h < 80 ? 2 : 3;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(actualMax / desiredTicks))), raw = actualMax / desiredTicks / magnitude;
        double step = (raw <= 1 ? 1 : raw <= 2 ? 2 : raw <= 5 ? 5 : 10) * magnitude, max = Math.Ceiling(actualMax / step) * step;
        FormattedText Label(string text, string color = "SecondaryText") => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, UI.Brush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        string Value(double value) => value >= 1e6 ? $"{value / 1e6:0.#}m" : value >= 1e3 ? $"{value / 1e3:0.#}k" : $"{value:0.#}";
        dc.DrawText(Label(chart.Unit + (chart.DetailComplete ? "" : " · partial details")), new(left, 1));
        if (!chart.DetailComplete && !chart.HasEvents) { dc.DrawText(Label("Timeline unavailable · event details missing"), new(left, h / 2)); return; }
        int ticks = (int)Math.Round(max / step);
        for (int i = 0; i <= ticks; i++) { double value = max * i / ticks, y = bottom - height * i / ticks; dc.DrawLine(new Pen(UI.Brush("Edge"), .5), new(left, y), new(right, y)); dc.DrawText(Label(Value(value)), new(3, y - 5)); }
        for (int i = 0; i <= 4; i++) { var label = Label(CombatChart.ElapsedLabel(chart.Duration * i / 4)); dc.DrawText(label, new(Math.Clamp(left + (right - left) * i / 4 - label.Width / 2, left - 8, w - label.Width - 2), h - 13)); }
        Point At(int i, double value) => new(left + Math.Min(chart.Duration, i * chart.BinSeconds) / chart.Duration * (right - left), bottom - value / max * height);
        foreach (var (actor, series, color) in new[] { ("Pet", chart.Pet, "Accent"), ("Self", chart.Self, "Mint") })
        {
            if (series.Length == 0) continue;
            var geometry = new StreamGeometry(); using (var line = geometry.Open()) { line.BeginFigure(At(0, series[0]), false, false); for (int i = 1; i < series.Length; i++) line.LineTo(At(i, series[i]), true, false); } geometry.Freeze(); dc.DrawGeometry(null, new Pen(UI.Brush(color), 1.5), geometry);
            var peaks = PeakMode == ChartPeakMode.Off ? [] : PeakMode == ChartPeakMode.All ? chart.LocalPeaks : chart.Peaks;
            foreach (var peak in peaks.Where(p => p.Actor == actor)) {
                var point = At(peak.Index, peak.Dps); dc.DrawEllipse(UI.Brush(color), new Pen(UI.Brush("Foreground"), .7), point, 3, 3);
                markers.Add((point, $"{actor} peak chart {chart.Unit}: {peak.Dps:N1}\n{peak.Time:MMM dd HH:mm:ss} · {chart.BinSeconds:0.##}s sampling interval"));
            }
        }
        if (Highlight is { } hit && hit.Time >= chart.Start && hit.Time <= chart.Start.AddSeconds(chart.Duration))
        {
            double x = left + (hit.Time - chart.Start).TotalSeconds / chart.Duration * (right - left);
            dc.DrawLine(new Pen(UI.Brush("Melee"), 1) { DashStyle = DashStyles.Dash }, new(x, top), new(x, bottom)); dc.DrawEllipse(UI.Brush("Melee"), null, new(x, 7), 3, 3);
            markers.Add((new(x, 7), $"{(chart.Unit == "HPS" ? "Largest heal" : "Peak hit")} · {hit.Ability} · {hit.Damage:N0} {(chart.Unit == "HPS" ? "HP restored" : "damage")}\n{hit.Time:MMM dd HH:mm:ss}"));
        }
    }
    private void DrawCursor(DrawingContext dc)
    {
        if (cursorX is not { } x || chart.Start == default || chart.Pet.Length + chart.Self.Length == 0) return;
        double elapsed = Math.Clamp((x - 42) / Math.Max(1, ActualWidth - 54), 0, 1) * chart.Duration;
        if (chart.Sample(elapsed) is not { } sample) return;
        string text = $"{CombatChart.ElapsedLabel(sample.Elapsed)} · {sample.Time:HH:mm:ss}  Self {sample.Self:N1} · Pet {sample.Pet:N1} {chart.Unit}  ({sample.Interval:0.##}s sample)";
        var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, UI.Brush("Foreground"), VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(1, ActualWidth - 16), Trimming = TextTrimming.CharacterEllipsis };
        double width = Math.Min(ActualWidth - 8, label.Width + 10), left = Math.Clamp(x - width / 2, 4, Math.Max(4, ActualWidth - width - 4));
        dc.DrawLine(new Pen(UI.Brush("Foreground"), 1) { DashStyle = DashStyles.Dot }, new(x, 13), new(x, ActualHeight - 17));
        dc.DrawRoundedRectangle(UI.Brush("SolidInnerSurface"), new Pen(UI.Brush("Accent"), .7), new(left, 1, width, label.Height + 5), 2, 2); dc.DrawText(label, new(left + 5, 3));
    }
}
