using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace EqlTracker.App;
internal static class PetFilterToggle
{
    internal static ToggleButton Create()
    {
        var toggle=new ToggleButton{Content="Filter",Height=28,MinHeight=28,Padding=new(6,0,10,0),Margin=new(0),Focusable=true};
        toggle.Template=(ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ToggleButton">
          <Border x:Name="Face" BorderBrush="Transparent" BorderThickness="1" Background="{DynamicResource InnerSurface}" Padding="{TemplateBinding Padding}" CornerRadius="2"><StackPanel Orientation="Horizontal" VerticalAlignment="Center"><TextBlock x:Name="Arrow" Text="▸" Width="20" Foreground="{DynamicResource SecondaryText}"/><ContentPresenter VerticalAlignment="Center" TextElement.Foreground="{DynamicResource SecondaryText}" TextElement.FontWeight="SemiBold"/></StackPanel></Border>
          <ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Arrow" Property="Text" Value="▾"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Face" Property="Background" Value="{DynamicResource Selection}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger></ControlTemplate.Triggers>
        </ControlTemplate>
        """);return toggle;
    }
}
