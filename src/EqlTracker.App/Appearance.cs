using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

public static class Appearance
{
    private sealed class ThemeColor : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(Color), typeof(ThemeColor));
        public Color Value { get => (Color)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    }
    private static readonly Dictionary<string, ThemeColor> colors = [];
    public static readonly string[] SecondaryTextChoices = ["Bright Frost", "Parchment Ivory", "Warm Gold", "Candlelight Cream", "Ash Silver"];
    public static string SecondaryText { get; private set; } = "Bright Frost";
    public static void ApplySecondaryText(string choice)
    {
        SecondaryText = SecondaryTextChoices.Contains(choice) ? choice : "Bright Frost";
        var value = (Color)ColorConverter.ConvertFromString(SecondaryText switch { "Parchment Ivory" => "#E6D8B5", "Warm Gold" => "#D9BF79", "Candlelight Cream" => "#FFF1C9", "Ash Silver" => "#D0D0D0", _ => "#C7E4E9" });
        if (colors.TryGetValue("SecondaryText", out var token)) token.Value = value;
        else { token = new() { Value = value }; colors["SecondaryText"] = token; var brush = new SolidColorBrush(); BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(ThemeColor.Value)) { Source = token }); Application.Current.Resources["SecondaryText"] = brush; }
    }
    public static readonly string[] Themes = ThemeCatalog.Choices.Select(c => c.Name).ToArray();
    public static void Apply(string theme)
    {
        ApplySecondaryText(SecondaryText);
        theme = ThemeCatalog.Resolve(theme).RenderKey;
        bool classic = theme == "Classic EQ", warm = theme == "Warm Amber" || theme.Contains("Vellum"), black = theme == "Midnight", chrome = theme.Contains("Krome"), clear = theme.Contains("Clear");
        var palette = new Dictionary<string, string>
        {
            ["Surface"] = classic ? "#EF242326" : warm ? clear ? "#BE211911" : "#EF211911" : black ? "#F0090B0E" : chrome ? "#F2161B21" : clear ? "#BE02080D" : "#EF081621",
            ["InnerSurface"] = classic ? "#EF343135" : warm ? "#E02C2118" : black ? "#E015171C" : chrome ? "#E0242A32" : "#D20C1C29",
            ["DetailSurface"] = classic ? "#F22A292D" : warm ? "#F22D241C" : black ? "#F21D1F26" : "#F2112230",
            ["Foreground"] = classic || warm ? "#F5ECD6" : "#E8F8FC",
            ["Muted"] = classic ? "#C4BBAF" : warm ? "#C9B79A" : black ? "#B0BAC5" : "#8BB4BE",
            ["ReadableNote"] = classic || warm ? "#FFF3DA" : "#EAF4F7",
            ["Accent"] = classic ? "#EDD28A" : warm ? "#E6B778" : black ? "#BACFDC" : chrome ? "#CDD6DF" : "#69E1F2",
            ["Mint"] = "#7BE8C5", ["Violet"] = classic ? "#B6A2CB" : warm ? "#D6AFB5" : "#B5A1F4",
            ["Selection"] = classic ? "#806A567D" : warm ? "#80614B3B" : black ? "#80514D63" : "#604A3C79",
            ["Edge"] = classic ? "#A08F859B" : warm ? "#907C674A" : black ? "#805A626D" : "#805A7180",
            ["Melee"] = "#E9B94F", ["Spell"] = "#64BDEE", ["Healing"] = "#79D45C",
            ["PetHP"]="#FF8C88",["PetMana"]="#64BDEE",["PetEndurance"]="#F1D16A",
            ["ButtonFace"] = classic ? "#D0C5A7" : warm ? "#796044" : black ? "#363B45" : "#345563",
            ["ButtonText"] = classic ? "#201E20" : "#F5ECD6", ["ButtonLight"] = classic ? "#F4EAD0" : warm ? "#BE9E72" : "#78959D",
            ["ButtonShadow"] = "#FF12151C", ["PetEarth"] = "#CFA779", ["PetAir"] = "#DCECEF", ["PetFire"] = "#EC8C89"
        };
        palette["PressedSurface"] = "#27444D"; palette["AlternateRow"] = "#102230"; palette["CellSelection"] = "#2B354C";
        if (theme == "Shadowman")
        {
            foreach (var pair in new Dictionary<string, string>
            {
                ["Surface"]="#BE090909", ["InnerSurface"]="#BF121212", ["DetailSurface"]="#BF171717",
                ["Foreground"]="#F4F4F4", ["Muted"]="#BDBDBD", ["ReadableNote"]="#EBEBEB",
                ["Accent"]="#D5D5D5", ["Selection"]="#80424242", ["Edge"]="#90545454",
                ["ButtonFace"]="#252525", ["ButtonText"]="#F4F4F4", ["ButtonLight"]="#626262", ["ButtonShadow"]="#FF080808",
                ["PressedSurface"]="#343434", ["AlternateRow"]="#1C1C1C", ["CellSelection"]="#333333"
            }) palette[pair.Key] = pair.Value;
        }
        foreach (string name in new[] { "InnerSurface", "DetailSurface" }) palette[name] = "#BF" + palette[name][^6..];
        foreach (string name in new[] { "Surface", "InnerSurface", "DetailSurface", "Selection" }) palette["Solid" + name] = "#FF" + palette[name][^6..];
        foreach (var (name, hex) in palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            if (colors.TryGetValue(name, out var token)) token.Value = color;
            else
            {
                token = new() { Value = color }; colors[name] = token;
                var brush = new SolidColorBrush(); BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(ThemeColor.Value)) { Source = token });
                Application.Current.Resources[name] = brush;
            }
        }
    }
    public static void SolidResources(FrameworkElement window)
    { foreach (string name in new[] { "Surface", "InnerSurface", "DetailSurface", "Selection" }) window.Resources[name] = UI.Brush("Solid" + name); }
    public static void RaisedButtons(FrameworkElement body)
    {
        var style = (Style)XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:EqlTracker.App;assembly=EQLTracker" TargetType="Button">
            <Style.Resources><Style TargetType="TextBlock"><Setter Property="Foreground" Value="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/></Style></Style.Resources>
            <Setter Property="ContentTemplate"><Setter.Value><DataTemplate><TextBlock Text="{Binding}" Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" TextTrimming="CharacterEllipsis" HorizontalAlignment="Center"/></DataTemplate></Setter.Value></Setter>
            <Setter Property="Foreground" Value="{DynamicResource ButtonText}"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Padding" Value="8,2"/><Setter Property="Margin" Value="0,0,4,4"/><Setter Property="MinHeight" Value="24"/>
            <Setter Property="HorizontalAlignment" Value="Left"/><Setter Property="VerticalAlignment" Value="Center"/><Setter Property="ToolTipService.ShowOnDisabled" Value="True"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="Rim" BorderBrush="{DynamicResource Edge}" BorderThickness="1" CornerRadius="2" Background="{DynamicResource ButtonShadow}">
                <Border x:Name="Face" BorderBrush="{DynamicResource ButtonLight}" BorderThickness="1,1,0,0" Margin="1,1,2,2" Background="{DynamicResource ButtonFace}">
                  <Grid><Border IsHitTestVisible="False"><Border.Background><LinearGradientBrush StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#30FFFFFF" Offset="0"/><GradientStop Color="#00FFFFFF" Offset="0.45"/><GradientStop Color="#30000000" Offset="1"/></LinearGradientBrush></Border.Background></Border><ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center" VerticalAlignment="Center"/></Grid>
                </Border>
              </Border>
              <ControlTemplate.Triggers><Trigger Property="local:FocusedLayout.IsPrimary" Value="True"><Setter Property="Foreground" Value="#10202A"/><Setter TargetName="Face" Property="Background" Value="{DynamicResource Melee}"/><Setter TargetName="Rim" Property="BorderBrush" Value="{DynamicResource Melee}"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Rim" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="Face" Property="Margin" Value="2,2,1,1"/><Setter TargetName="Face" Property="BorderThickness" Value="0,0,1,1"/></Trigger><Trigger Property="local:Usability.IsPressed" Value="True"><Setter Property="Foreground" Value="{DynamicResource Foreground}"/><Setter TargetName="Face" Property="Margin" Value="2,2,1,1"/><Setter TargetName="Face" Property="BorderThickness" Value="0,0,1,1"/><Setter TargetName="Rim" Property="BorderBrush" Value="{DynamicResource Accent}"/><Setter TargetName="Face" Property="Background" Value="{DynamicResource Selection}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Rim" Property="BorderBrush" Value="{DynamicResource Mint}"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        body.Resources[typeof(Button)] = style;
        body.Loaded += (_, _) =>
        {
            void Visit(DependencyObject element)
            {
                if (element is Button { Content: string } button) button.ClearValue(Control.ForegroundProperty);
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Visit(VisualTreeHelper.GetChild(element, i));
            }
            Visit(body);
        };
    }
}

public sealed class MeterPreview : Border
{
    private readonly DamageRowsView rows = new() { Height = 78, Compact = true };
    private readonly TextBlock label = UI.Text("Pet · 142.8 dps", 12, "Accent", true);
    public MeterPreview()
    {
        Width = 190; Padding = new(5); Margin = new(8, 0, 0, 4); CornerRadius = new(3); BorderThickness = new(1); IsHitTestVisible = false;
        SetResourceReference(BackgroundProperty, "Surface"); SetResourceReference(BorderBrushProperty, "Accent");
        Child = UI.Stack(label, rows, UI.Text("12,850 Dmg · 450 Max", 10, "Muted"));
        ToolTip="Illustrative meter example. Follows Meter style, opacity, Smooth motion and the selected icon style.";
    }
    public void Apply(CharacterProfile profile)
    {
        Opacity = profile.Dps.Opacity; label.Text = profile.MeterStyle + " · 142.8 dps";
        Padding = new(profile.MeterStyle == "Minimal" ? 2 : 5);
        Effect = profile.MeterStyle == "Luminous" ? new System.Windows.Media.Effects.DropShadowEffect { Color = UI.Brush("Accent").Color, BlurRadius = 10, ShadowDepth = 0, Opacity = .4 } : null;
        rows.SetRows([new("Slash", "Melee", 8100, 90, 36, 450, 225, 3, 8.3, 0, 63, "Physical"), new("Spirit Tap", "Spell", 4750, 52.8, 25, 240, 190, 2, 8, 0, 37, "Magic") { LinkedHealing = profile.ShowLinkedHealing ? 3100 : null }], "settings-preview", profile.SmoothMeterMotion);
    }
}
