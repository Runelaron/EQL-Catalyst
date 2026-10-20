using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace EqlTracker.App;

// Three reviewed concepts and the selected segmented Frost treatment.
public static class RankSliderStyles
{
    public static readonly DependencyProperty ReviewStateProperty = DependencyProperty.RegisterAttached("ReviewState", typeof(string), typeof(RankSliderStyles), new PropertyMetadata("Normal"));
    public static string GetReviewState(DependencyObject value) => (string)value.GetValue(ReviewStateProperty);
    public static void SetReviewState(DependencyObject value, string state) => value.SetValue(ReviewStateProperty, state);
    private static readonly Dictionary<int, Style> cache = [];
    internal static Style Style(int version)
    {
        if (cache.TryGetValue(version, out var style)) return style;
        string edge = version == 2 ? "Melee" : "Accent", active = version == 3 ? "Melee" : edge, tick = version is 1 or 3 ? "Visible" : "Collapsed";
        string xaml = $$$"""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:EqlTracker.App;assembly=EQLTracker" TargetType="Slider">
          <Setter Property="Height" Value="32"/><Setter Property="Margin" Value="2,4,2,4"/><Setter Property="Focusable" Value="True"/><Setter Property="Foreground" Value="{DynamicResource {{{edge}}}}"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider"><Border x:Name="Frame" Padding="7,2" Background="{DynamicResource SolidInnerSurface}" BorderBrush="{DynamicResource Edge}" BorderThickness="1" CornerRadius="3"><Grid>
            <TickBar Fill="{DynamicResource Muted}" Placement="Bottom" Height="5" VerticalAlignment="Bottom" ReservedSpace="14" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" TickFrequency="1" Visibility="{{{tick}}}"/>
            <Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" IsDirectionReversed="False">
              <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="4" CornerRadius="2" Background="{DynamicResource Accent}"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
              <Track.Thumb><Thumb Width="14" Height="20" Focusable="False"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{DynamicResource InnerSurface}" BorderBrush="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Slider}}" BorderThickness="2" CornerRadius="3"><Border Margin="4,4" Background="{DynamicResource Foreground}" Width="2"/></Border></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
              <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="4" CornerRadius="2" Background="{DynamicResource Edge}"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
            </Track>
          </Grid></Border><ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource Accent}"/><Setter TargetName="Frame" Property="BorderThickness" Value="2"/></Trigger>
            <Trigger Property="IsMouseCaptureWithin" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource {{{active}}}}"/><Setter Property="Foreground" Value="{DynamicResource {{{active}}}}"/><Setter TargetName="Frame" Property="Background" Value="{DynamicResource Selection}"/></Trigger>
            <Trigger Property="local:RankSliderStyles.ReviewState" Value="Hover"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
            <Trigger Property="local:RankSliderStyles.ReviewState" Value="Focused"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource Accent}"/><Setter TargetName="Frame" Property="BorderThickness" Value="2"/></Trigger>
            <Trigger Property="local:RankSliderStyles.ReviewState" Value="Dragging"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource {{{active}}}}"/><Setter Property="Foreground" Value="{DynamicResource {{{active}}}}"/><Setter TargetName="Frame" Property="Background" Value="{DynamicResource Selection}"/></Trigger>
          </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
        </Style>
        """;
        return cache[version] = (Style)XamlReader.Parse(xaml);
    }
    internal static FrameworkElement Review()
    {
        var root = UI.Stack(UI.Text("Rank slider · reviewed options & selected treatment", 19, "Accent", true), UI.Text("Selected B+C: segmented Frost, with gold only during clicking or dragging.", 12));
        string[] names = ["A · Frost rail", "B · Segmented rank track", "C · Gold-edged Frost", "Selected B+C · Segmented Frost · active gold edge"];
        for (int version = 0; version < 4; version++)
        {
            var samples = new Grid(); for (int i = 0; i < 4; i++) samples.ColumnDefinitions.Add(new());
            string[] states = ["Normal", "Hover", "Focused", "Dragging"];
            for (int i = 0; i < 4; i++)
            {
                var slider = new Slider { Minimum = 0, Maximum = 10, Value = 3 + i, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 1, Style = Style(version), MinWidth = 110 };
                SetReviewState(slider, states[i]); var sample = UI.Stack(UI.Text(states[i], 11, "Muted"), slider, UI.Text("+" + slider.Value, 11, "Mint")); sample.Margin = new(4); Grid.SetColumn(sample, i); samples.Children.Add(sample);
            }
            root.Children.Add(UI.Card(UI.Stack(UI.Text(names[version], 14, "Foreground", true), samples)));
        }
        return UI.Card(root);
    }
}
