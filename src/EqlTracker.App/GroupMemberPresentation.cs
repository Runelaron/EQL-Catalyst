using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

public static class GroupMemberPresentation
{
    private static readonly string[] colors = ["#78D6E8", "#9FDBC5", "#D5B3F1", "#E5C074", "#8EADE5", "#D5A4B4", "#BDCF91", "#DFAD7F"];
    public static Color Color(string id)
    { uint value = 2166136261; foreach (char c in id.ToLowerInvariant()) value = (value ^ c) * 16777619; return (Color)ColorConverter.ConvertFromString(colors[value % colors.Length]); }
    public static string Tooltip(GroupMemberRow row) => $"{row.Name}\n{(row.Total.HasValue ? $"{row.Total:N0} total · {row.Rate:N1} /s · {row.Share:N1}% · {row.Seconds:N0}s combat\nCharacter {row.CharacterTotal:N0} · verified pets {row.PetTotal:N0}" : "Totals unavailable; no qualifying member events")}\n{row.Evidence}";
    public static void Configure(DataGrid table, bool compact = false)
    {
        foreach(var c in table.Columns.OfType<DataGridTextColumn>())if(c.Binding is Binding b)b.TargetNullValue="—";
        TableLayout.SetKey(table, compact ? "overlay-group-members-v1" : "combat-group-members-v2");
        var column = table.Columns[0]; int index = table.Columns.IndexOf(column); table.Columns.Remove(column);
        var grid = new FrameworkElementFactory(typeof(Grid)); grid.SetValue(FrameworkElement.MinHeightProperty, compact ? 22d : 25d);
        var wash = new FrameworkElementFactory(typeof(Border)); wash.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left); wash.SetValue(FrameworkElement.MarginProperty, new Thickness(0,2,0,2));
        wash.SetBinding(Border.BackgroundProperty, new Binding { Converter = new MemberBrush() });
        var width = new MultiBinding { Converter = new MemberWidth() }; width.Bindings.Add(new Binding("Share")); width.Bindings.Add(new Binding("ActualWidth") { RelativeSource = new(RelativeSourceMode.FindAncestor, typeof(DataGridCell), 1) }); wash.SetBinding(FrameworkElement.WidthProperty,width); grid.AppendChild(wash);
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty,new Binding("Name")); name.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center); name.SetValue(FrameworkElement.MarginProperty,new Thickness(4,0,4,0));name.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);grid.AppendChild(name);
        var replacement = new DataGridTemplateColumn { Header = "Member", SortMemberPath = "Name", Width = column.Width, MinWidth = 80, CellTemplate = new() { VisualTree = grid } }; table.Columns.Insert(index,replacement);
        var style = new Style(typeof(DataGridRow)); style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding { Converter = new MemberTip() }));table.RowStyle=style;
    }
    private sealed class MemberBrush : IValueConverter
    { public object Convert(object value, Type type, object parameter, CultureInfo culture) { var color=Color((value as GroupMemberRow)?.Id??"");color.A=68;var brush=new SolidColorBrush(color);brush.Freeze();return brush; } public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>Binding.DoNothing; }
    private sealed class MemberWidth : IMultiValueConverter
    { public object Convert(object[] values,Type type,object parameter,CultureInfo culture)=>values[0] is double share&&values[1] is double width?Math.Max(0,width*Math.Clamp(share/100,0,1)):0d;public object[] ConvertBack(object value,Type[] types,object parameter,CultureInfo culture)=>throw new NotSupportedException(); }
    private sealed class MemberTip : IValueConverter
    { public object Convert(object value,Type type,object parameter,CultureInfo culture)=>value is GroupMemberRow member?Tooltip(member):"";public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>Binding.DoNothing; }
}
