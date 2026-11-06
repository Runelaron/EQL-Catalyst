using System.Windows;
using System.Windows.Controls;
namespace EqlTracker.App;
public sealed partial class SettingsWindow
{
    internal ComboBox HeadingStyleSelector {get;private set;}=null!;
    private UIElement BuildHeadingStyleControls()
    {
        var choice=HeadingStyleSelector=WorkspaceForms.Control(UI.Select(["Classic (Default)","EQL Quest Display"],Controller.HeadingStyle=="quest"?"EQL Quest Display":"Classic (Default)",180),"Heading style");
        var sample=UI.Text("Quest rewards · Adventure awaits");sample.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty,"OptionalDisplayFont");sample.Height=28;sample.VerticalAlignment=VerticalAlignment.Center;
        choice.SelectionChanged+=(_,_)=>{Controller.SetHeadingStyle(choice.SelectedIndex==1?"quest":"classic");CatalystHeading.Use(Controller.HeadingStyle);};
        return Flow(Field("Heading style",choice,"Saved per character and applied immediately to decorative headings; Catalyst branding stays Classic. Reward names always use EQL Quest Display; body text stays unchanged."),UI.Field("Preview",sample));
    }
}
