using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class AbilityDetailText
{
    // Prefixes express display priority; the complete version also feeds the tooltip.
    internal static string[] Fields(DamageRow row) => [$"{row.Dps:N1} {row.Unit}", $"{row.MaxHit:N0} max", $"×{row.Hits:N0}", $"{row.CriticalPercent:0.#}% crit", $"{row.Average:N1} avg"];
    internal static string Full(DamageRow row) => string.Join(" · ", Fields(row));
}

/// <summary>Fits complete metrics on one line, dropping the least important first.</summary>
public sealed class AbilityMetricsText : Decorator
{
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(AbilityMetricsText), new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(AbilityMetricsText));
    public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register(nameof(LineHeight), typeof(double), typeof(AbilityMetricsText), new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(nameof(Row), typeof(DamageRow), typeof(AbilityMetricsText), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((AbilityMetricsText)d).UpdateFields()));
    private readonly TextBlock text = new() { TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.None, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, ClipToBounds = true };
    private string[] prefixes = [];
    private double[] widths = [];
    private bool widthsDirty = true;
    private readonly Dictionary<char, double> glyphWidths = [];
    private IDictionary<int, ushort>? glyphMap;
    private IDictionary<ushort, double>? advanceWidths;
    private (Typeface Font, double Size, double Dpi, FlowDirection Direction, string Culture)? measuredTypography;
    public DamageRow? Row { get => (DamageRow?)GetValue(RowProperty); set => SetValue(RowProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double LineHeight { get => (double)GetValue(LineHeightProperty); set => SetValue(LineHeightProperty, value); }
    internal int VisibleMetricCount { get; private set; }
    internal string Text => text.Text;
    public AbilityMetricsText()
    {
        ClipToBounds = true; Child = text;
        foreach (var property in new[] { TextBlock.FontSizeProperty, TextBlock.ForegroundProperty, TextBlock.LineHeightProperty })
            text.SetBinding(property, new Binding(property.Name) { Source = this });
    }
    private void UpdateFields()
    {
        var fields = Row is { } row ? AbilityDetailText.Fields(row) : [];
        var next = fields.Select((_, index) => string.Join(" · ", fields.Take(index + 1))).ToArray();
        if (prefixes.SequenceEqual(next)) return;
        prefixes = next; widthsDirty = true;
        AutomationProperties.SetName(this, prefixes.LastOrDefault() ?? "");
    }
    protected override Size MeasureOverride(Size constraint)
    {
        var typography = (new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), FontSize, VisualTreeHelper.GetDpi(this).PixelsPerDip, FlowDirection, CultureInfo.CurrentCulture.Name);
        if (measuredTypography != typography)
        {
            measuredTypography = typography;
            glyphWidths.Clear(); glyphMap = null; advanceWidths = null;
            if (typography.Item1.TryGetGlyphTypeface(out var glyphFont)) { glyphMap = glyphFont.CharacterToGlyphMap; advanceWidths = glyphFont.AdvanceWidths; }
            widthsDirty = true;
        }
        // Fast advances choose a candidate; the TextBlock's own measurement below
        // verifies it. This avoids formatting five complete strings on every hit.
        if (widthsDirty) { widths = prefixes.Select(EstimateWidth).ToArray(); widthsDirty = false; }
        int count = widths.Length;
        // Reserve a small rounding allowance so a whole field never clips at fractional DPI.
        double available = double.IsFinite(constraint.Width) ? Math.Max(0, constraint.Width - 1) : double.PositiveInfinity;
        while (count > 0 && widths[count - 1] > available) count--;
        Size desired;
        do
        {
            string next = count == 0 ? "" : prefixes[count - 1];
            if (text.Text != next) text.Text = next;
            text.Measure(new Size(double.PositiveInfinity, constraint.Height)); desired = text.DesiredSize;
            if (desired.Width <= available || count == 0) break;
            count--;
        } while (true);
        VisibleMetricCount = count;
        // Include the allowance in the desired width as well: an Auto column must
        // not repeatedly drop a field merely because it allocated the exact text width.
        return new Size(Math.Min(constraint.Width, count == 0 ? 0 : Math.Max(widths[count - 1], desired.Width) + 1), desired.Height);
    }
    private double EstimateWidth(string value)
    {
        if (glyphMap != null && advanceWidths != null)
        {
            double result = 0;
            foreach (char character in value)
            {
                if (!glyphWidths.TryGetValue(character, out double width))
                {
                    if (!glyphMap.TryGetValue(character, out ushort glyph) || !advanceWidths.TryGetValue(glyph, out double advance)) return FormatWidth(value);
                    width = advance * FontSize; glyphWidths[character] = width;
                }
                result += width;
            }
            return result;
        }
        return FormatWidth(value);
    }
    private double FormatWidth(string value) => new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection, measuredTypography!.Value.Font, FontSize, Brushes.Black, measuredTypography.Value.Dpi).WidthIncludingTrailingWhitespace;
}
