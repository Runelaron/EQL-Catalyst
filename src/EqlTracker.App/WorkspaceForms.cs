using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
namespace EqlTracker.App;

internal static class WorkspaceForms
{
    internal static T Control<T>(T control,string name="") where T:Control
    {control.Height=28;control.VerticalContentAlignment=VerticalAlignment.Center;control.Margin=new(0,0,4,0);if(name.Length>0)AutomationProperties.SetName(control,name);return control;}
    internal static WrapPanel Flow(params UIElement[] children)
    {var panel=new WrapPanel{Margin=new(0,0,0,4)};foreach(var child in children)panel.Children.Add(child);return panel;}
}
