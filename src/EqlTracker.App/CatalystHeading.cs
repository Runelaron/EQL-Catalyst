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
    public static FontFamily DisplayFont { get; } = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#EQL Quest Display, Georgia");
    internal static readonly FontFamily ClassicEmblemFont=new(new Uri("pack://application:,,,/"),"./Assets/Fonts/#Cinzel Decorative, Georgia");
    internal static readonly FontFamily ClassicWordmarkFont=new("Georgia");
    internal const string EmblemFontKey="CatalystEmblemFont",WordmarkFontKey="CatalystWordmarkFont",WeightKey="CatalystWordmarkWeight";
    private static readonly Brush gold = Gold();
    internal static Brush GoldLettering=>gold;
    internal static void Use(string? choice)
    {
        if(Application.Current==null)return;bool quest=HeadingAppearance.Resolve(choice)=="quest";var resources=Application.Current.Resources;
        void Set(string key,object value){if(!Equals(resources[key],value))resources[key]=value;}
        Set(EmblemFontKey,ClassicEmblemFont);Set(WordmarkFontKey,ClassicWordmarkFont);Set(WeightKey,FontWeights.Bold);Set("OptionalDisplayFont",quest?DisplayFont:new FontFamily("Segoe UI"));Set("OptionalDisplayWeight",quest?FontWeights.Normal:FontWeights.SemiBold);
    }
    internal static Run Wordmark(string text,bool emblem=false)
    {
        if(Application.Current!=null&&!Application.Current.Resources.Contains(EmblemFontKey))Use("classic");
        var run=new Run(text){FontFamily=emblem?ClassicEmblemFont:ClassicWordmarkFont,FontWeight=FontWeights.Bold,Foreground=gold};
        if(Application.Current!=null){run.SetResourceReference(TextElement.FontFamilyProperty,emblem?EmblemFontKey:WordmarkFontKey);run.SetResourceReference(TextElement.FontWeightProperty,WeightKey);}return run;
    }
    private static Brush Gold() { var b = new LinearGradientBrush(); b.StartPoint = new(0, 0); b.EndPoint = new(0, 1); b.GradientStops.Add(new(Color.FromRgb(255, 237, 178), 0)); b.GradientStops.Add(new(Color.FromRgb(233, 185, 79), .48)); b.GradientStops.Add(new(Color.FromRgb(165, 105, 33), 1)); b.Freeze(); return b; }
    public static void Apply(TextBlock title, string page)
    {
        title.Text = ""; title.Inlines.Clear(); title.FontSize = 17;
        var emblem = Wordmark("EQL",true);
        emblem.SetBinding(TextElement.FontSizeProperty, new Binding(nameof(TextBlock.FontSize)) { Source = title, Converter = fontScale, ConverterParameter = 20d / 17 }); title.Inlines.Add(emblem);
        title.Inlines.Add(Wordmark(" Catalyst"));
        var caption = new Run(" — " + page) { FontFamily = new("Georgia"), FontWeight = FontWeights.Normal };
        caption.SetBinding(TextElement.FontSizeProperty, new Binding(nameof(TextBlock.FontSize)) { Source = title, Converter = fontScale, ConverterParameter = 16d / 17 }); title.Inlines.Add(caption);
        title.ToolTip = "EQL Catalyst · " + page; title.Visibility = Visibility.Visible;
    }
}
