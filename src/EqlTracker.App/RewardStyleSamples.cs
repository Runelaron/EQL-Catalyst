using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
namespace EqlTracker.App;

internal static class RewardStyleSamples
{
    internal static FrameworkElement Create(string name)
    {
        var panel=new StackPanel{Margin=new(8),Width=245};panel.Children.Add(UI.Text(name,15,"SecondaryText",true));
        foreach(string state in new[]{"Normal","Hover","Keyboard focus","Pressed"})
        {
            panel.Children.Add(UI.Text(state,11,"SecondaryText"));
            string face=name=="Inset Plaque"?"{DynamicResource InnerSurface}":"Transparent";
            string edge=name=="Inset Plaque"?"{DynamicResource ButtonLight}":"Transparent";
            string foreground=name=="Engraved Text"?"Melee":"Accent";
            var text=new ToggleButton{Content="Jagged Blade of War",Tag=state,FontSize=14,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,12),IsChecked=state=="Pressed",ToolTip="Review-only reward treatment; click to toggle"};
            string template="""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ToggleButton">
              <Border x:Name="Face" Background="FACE" BorderBrush="EDGE" BorderThickness="THICKNESS" CornerRadius="2" Padding="5,3">
                <Grid><TextBlock Text="{TemplateBinding Content}" Foreground="{DynamicResource ButtonShadow}" Margin="1,1,0,0"/><TextBlock x:Name="Letters" Text="{TemplateBinding Content}" Foreground="{DynamicResource FOREGROUND}" Margin="0,0,1,1"/></Grid>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="Tag" Value="Hover"><Setter TargetName="Letters" Property="Foreground" Value="{DynamicResource Mint}"/><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Letters" Property="Foreground" Value="{DynamicResource Mint}"/><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
                <Trigger Property="Tag" Value="Keyboard focus"><Setter TargetName="Face" Property="BorderThickness" Value="FOCUSTHICKNESS"/><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Mint}"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Face" Property="BorderThickness" Value="FOCUSTHICKNESS"/><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Mint}"/></Trigger>
                <Trigger Property="IsChecked" Value="True"><Setter TargetName="Letters" Property="Foreground" Value="{DynamicResource Melee}"/><Setter TargetName="Letters" Property="FontWeight" Value="Bold"/><Setter TargetName="Face" Property="Background" Value="SELECTEDFACE"/><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Accent}"/><Setter TargetName="Letters" Property="Margin" Value="1,1,0,0"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """;
            text.Template=(ControlTemplate)XamlReader.Parse(template.Replace("SELECTEDFACE",name=="Raised Lettering"?"Transparent":"{DynamicResource Selection}").Replace("FOCUSTHICKNESS",name=="Inset Plaque"?"1":"0,0,0,1").Replace("THICKNESS",name=="Raised Lettering"?"0":name=="Engraved Text"?"0,0,0,1":"1").Replace("FACE",face).Replace("EDGE",edge).Replace("FOREGROUND",foreground));
            if(name=="Engraved Text")text.BorderThickness=new(0,0,0,1);
            panel.Children.Add(text);
        }
        return panel;
    }
}
