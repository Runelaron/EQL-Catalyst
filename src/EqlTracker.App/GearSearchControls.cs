using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private FrameworkElement GearSearchControls()
    {
        var wrap = new WrapPanel();
        var heading = new Grid { Height = 19 };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        gearFindLabel.Margin = new(0); gearFindLabel.VerticalAlignment = VerticalAlignment.Center;
        gearTheoryCheck.Margin = new(5, 0, 0, 0); gearTheoryCheck.VerticalAlignment = VerticalAlignment.Center; gearTheoryCheck.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(gearTheoryCheck, 1); heading.Children.Add(gearFindLabel); heading.Children.Add(gearTheoryCheck);
        var search = (FrameworkElement)FocusedLayout.Search(gearSearch, "Find a replacement…");
        gearSearch.Height = gearPosition.Height = 28; gearSearch.Margin = new(0); gearPosition.Margin = new(6, 0, 0, 0);
        var fields = UI.Row(search, gearPosition); var main = UI.Stack(heading, fields); main.Margin = new(0, 0, 6, 4);
        wrap.Children.Add(main); wrap.Children.Add(GearExpansionControl());
        void Fit()
        {
            var font = new Typeface(gearPosition.FontFamily, gearPosition.FontStyle, gearPosition.FontWeight, gearPosition.FontStretch);
            double pixels = VisualTreeHelper.GetDpi(gearPosition).PixelsPerDip;
            double Measure(string text) => new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, font, gearPosition.FontSize, Brushes.White, pixels).WidthIncludingTrailingWhitespace;
            gearPosition.Width = Math.Ceiling(gearPosition.Items.OfType<EqlTracker.Core.EquipmentPosition>().Select(p => Measure(p.Label)).Append(Measure("All slots")).DefaultIfEmpty(70).Max() + 34);
            double preferred = 220 - Measure("          ");
            gearSearch.Width = Math.Max(140, Math.Min(preferred, wrap.ActualWidth - gearPosition.Width - 108));
        }
        wrap.Loaded += (_, _) => Fit(); wrap.SizeChanged += (_, _) => Fit();
        gearPosition.SourceUpdated += (_, _) => Fit();
        gearPosition.SelectionChanged += (_, _) => Fit();
        return wrap;
    }
}
