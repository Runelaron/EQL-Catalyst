using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Data;
using System.Globalization;

namespace EqlTracker.App;

public static class CatalystHeading
{
    private sealed class FontScale : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (double)value * (double)parameter;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    private static readonly FontScale fontScale = new();
    private static readonly FontFamily display = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Cinzel Decorative, Georgia");
    private static readonly Brush gold = Gold();
    private static Brush Gold() { var b = new LinearGradientBrush(); b.StartPoint = new(0, 0); b.EndPoint = new(0, 1); b.GradientStops.Add(new(Color.FromRgb(255, 237, 178), 0)); b.GradientStops.Add(new(Color.FromRgb(233, 185, 79), .48)); b.GradientStops.Add(new(Color.FromRgb(165, 105, 33), 1)); b.Freeze(); return b; }
    public static void Apply(TextBlock title, string page)
    {
        title.Text = ""; title.Inlines.Clear(); title.FontSize = 17;
        var emblem = new Run("EQL") { FontFamily = display, FontWeight = FontWeights.Bold, Foreground = gold };
        emblem.SetBinding(TextElement.FontSizeProperty, new Binding(nameof(TextBlock.FontSize)) { Source = title, Converter = fontScale, ConverterParameter = 20d / 17 }); title.Inlines.Add(emblem);
        title.Inlines.Add(new Run(" Catalyst") { FontFamily = new("Georgia"), Foreground = gold });
        var caption = new Run(" — " + page) { FontFamily = new("Georgia"), FontWeight = FontWeights.Normal };
        caption.SetBinding(TextElement.FontSizeProperty, new Binding(nameof(TextBlock.FontSize)) { Source = title, Converter = fontScale, ConverterParameter = 16d / 17 }); title.Inlines.Add(caption);
        title.ToolTip = "EQL Catalyst · " + page; title.Visibility = Visibility.Visible;
    }
}
