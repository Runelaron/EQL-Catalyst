using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Automation.Peers;
namespace EqlTracker.App;

internal static class RewardNameButton
{
    private sealed class RewardToggleButton : ToggleButton
    {
        protected override AutomationPeer OnCreateAutomationPeer()=>new RewardPeer(this);
    }
    private sealed class RewardPeer(ToggleButton owner) : ToggleButtonAutomationPeer(owner)
    {
        // The owning toggle supplies the accessible name; the three bevel layers
        // must not be announced as three separate copies of the reward.
        protected override List<AutomationPeer>? GetChildrenCore()=>null;
    }
    private static ControlTemplate? template;
    internal static ToggleButton Create(string name)
    {
        template??=(ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ToggleButton">
          <Border x:Name="Face" Background="Transparent" Padding="{TemplateBinding Padding}" BorderThickness="1" BorderBrush="Transparent" SnapsToDevicePixels="True">
            <Grid Margin="1">
              <TextBlock x:Name="Shade" Text="{TemplateBinding Content}" FontFamily="{TemplateBinding FontFamily}" FontSize="{TemplateBinding FontSize}" FontWeight="Normal" Foreground="{DynamicResource ButtonShadow}" TextWrapping="Wrap" IsHitTestVisible="False"><TextBlock.RenderTransform><TranslateTransform x:Name="ShadeOffset" Y="0.8"/></TextBlock.RenderTransform></TextBlock>
              <TextBlock x:Name="Light" Text="{TemplateBinding Content}" FontFamily="{TemplateBinding FontFamily}" FontSize="{TemplateBinding FontSize}" FontWeight="Normal" Foreground="#FFF0BD" Opacity="0.65" TextWrapping="Wrap" IsHitTestVisible="False"><TextBlock.RenderTransform><TranslateTransform x:Name="LightOffset" Y="-0.55"/></TextBlock.RenderTransform></TextBlock>
              <TextBlock x:Name="Letters" Text="{TemplateBinding Content}" FontFamily="{TemplateBinding FontFamily}" FontSize="{TemplateBinding FontSize}" FontWeight="Normal" Foreground="{TemplateBinding Foreground}" TextWrapping="Wrap" IsHitTestVisible="False"><TextBlock.RenderTransform><TranslateTransform x:Name="LetterOffset"/></TextBlock.RenderTransform></TextBlock>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Mint}"/></Trigger>
            <Trigger Property="IsChecked" Value="True"><Setter TargetName="Shade" Property="RenderTransform"><Setter.Value><TranslateTransform Y="-0.75"/></Setter.Value></Setter><Setter TargetName="Light" Property="RenderTransform"><Setter.Value><TranslateTransform Y="0.75"/></Setter.Value></Setter><Setter TargetName="Letters" Property="RenderTransform"><Setter.Value><TranslateTransform Y="0.75"/></Setter.Value></Setter><Setter TargetName="Letters" Property="Opacity" Value="0.86"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="Shade" Property="RenderTransform"><Setter.Value><TranslateTransform Y="-0.75"/></Setter.Value></Setter><Setter TargetName="Light" Property="RenderTransform"><Setter.Value><TranslateTransform Y="0.75"/></Setter.Value></Setter><Setter TargetName="Letters" Property="RenderTransform"><Setter.Value><TranslateTransform Y="0.75"/></Setter.Value></Setter><Setter TargetName="Letters" Property="Opacity" Value="0.86"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Face" Property="Opacity" Value="0.5"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
        return new RewardToggleButton{Content=name,FontFamily=CatalystHeading.DisplayFont,FontSize=14,FontWeight=FontWeights.Normal,Foreground=CatalystHeading.GoldLettering,MinHeight=28,Cursor=Cursors.Hand,HorizontalAlignment=HorizontalAlignment.Left,HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,3),Padding=new(3,2,3,2),ToolTip=name+"\nShow or hide reward details",Template=template};
    }
}
