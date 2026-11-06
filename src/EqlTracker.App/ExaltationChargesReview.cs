using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class QuestWindow
{
    internal void ReviewExaltationCharges(string folder)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Synthetic evidence only");Directory.CreateDirectory(folder);var checks=new List<string>();
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        void Flush(){UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        var charged=new ItemDefinition{Key="reviewed charged charm",Name="Reviewed Charged Charm",Effects=["Tap (Charge Clicky, Casting Time: Instant)"],Slots=["NECK"],Classes=["ALL"],MetadataComplete=true};
        var unlimited=new ItemDefinition{Key="reviewed unlimited charm",Name="Reviewed Unlimited Charm",Effects=["Click: Heal (Casting Time: Instant)"],Slots=["NECK"],Classes=["ALL"],MetadataComplete=true};
        Controller.Catalog.Items.AddRange([charged,unlimited]);Controller.IncludeChargedExaltations=false;
        Placement!.ReviewEnabled=true;Opacity=0;ShowActivated=false;ShowInTaskbar=false;Left=Top=-15000;Show();ShowView(TrackerTab.Exaltations);exaltSearch.Text="Reviewed";exaltContent.SelectedIndex=2;Flush();
        var disclosure=Usability.Tree(this).OfType<Expander>().Single(e=>Equals(e.Header,"Expansion & compatibility"));disclosure.IsExpanded=true;Flush();
        Check(exaltChargedClicks.IsChecked!=true,"Include charged clicks starts unchecked");
        Check(exaltTable.Items.OfType<ExaltationOption>().All(o=>o.Source.Key!=charged.Key)&&exaltTable.Items.OfType<ExaltationOption>().Any(o=>o.Source.Key==unlimited.Key),"Default list excludes charged clicks and keeps unlimited clicks");
        var toggle=(IToggleProvider)new CheckBoxAutomationPeer(exaltChargedClicks).GetPattern(PatternInterface.Toggle);
        toggle.Toggle();Flush();Check(exaltTable.Items.OfType<ExaltationOption>().Any(o=>o.Source.Key==charged.Key),"One activation includes charged effects");exaltTable.SelectedItem=exaltTable.Items.OfType<ExaltationOption>().Single(o=>o.Source.Key==charged.Key);Flush();
        Check(Usability.Tree(exaltDetail).OfType<TextBlock>().Any(t=>t.Text.Contains("conversion to an Exaltation is unverified")),"Included charged effect has a specific conversion limitation");
        toggle.Toggle();Flush();Check(exaltTable.Items.OfType<ExaltationOption>().All(o=>o.Source.Key!=charged.Key),"Unchecking removes candidates without deleting source metadata");
        foreach(var theme in ThemeCatalog.Choices.Skip(1))foreach(var size in new[]{(Name:"normal",W:915d,H:650d),(Name:"minimum",W:640d,H:420d),(Name:"short",W:915d,H:420d)})
        {
            Controller.Profile.Theme=theme.Id;Appearance.Apply(theme.Id);Width=size.W;Height=size.H;Flush();var bounds=exaltChargedClicks.TransformToAncestor((FrameworkElement)Content).TransformBounds(new Rect(exaltChargedClicks.RenderSize));exaltChargedClicks.Focus();Flush();
            Check(exaltChargedClicks.ActualHeight==28&&bounds==exaltChargedClicks.TransformToAncestor((FrameworkElement)Content).TransformBounds(new Rect(exaltChargedClicks.RenderSize)),theme.Id+"/"+size.Name+" stable 28-DIP focus geometry");
            Check(bounds.Left>=0&&bounds.Right<=((FrameworkElement)Content).ActualWidth&&exaltTable.ActualHeight>=30,theme.Id+"/"+size.Name+" checkbox and candidate viewport fit");
            foreach(double scale in new[]{1d,1.5,2d,3d})
            {
                var bitmap=PreviewRenderer.Capture((FrameworkElement)Content,size.W,size.H,scale);Check(bitmap.PixelWidth==(int)Math.Ceiling(size.W*scale)&&bitmap.PixelHeight==(int)Math.Ceiling(size.H*scale),theme.Id+"/"+size.Name+"/"+scale+" synthetic rendering");
                if(scale==1){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(folder,theme.Id+"-"+size.Name+".png"));png.Save(output);}
            }
        }
        Json.Write(Path.Combine(folder,"exaltation-charges-checks.json"),new{Passed=true,Checks=checks,Synthetic=true,PhysicalInputOrMultiMonitorClaimed=false});Hide();
    }
}
