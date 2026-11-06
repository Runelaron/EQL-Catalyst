using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;
namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewHeadingRewards(string folder,DpsWindow lab)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Synthetic review only");Directory.CreateDirectory(folder);var checks=new List<string>();var samples=new List<object>();
        void Check(bool v,string name){if(!v){Json.Write(Path.Combine(folder,"heading-rewards-checks.json"),new{Passed=false,Checks=checks,Failed=name});throw new InvalidOperationException(name);}checks.Add(name);}
        void Flush(FrameworkElement e){e.UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);e.UpdateLayout();}
        void Toggle(ToggleButton button){((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Toggle)).Toggle();Flush(this);}
        void Key(ToggleButton button,Key key){button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,Environment.TickCount,key){RoutedEvent=Keyboard.PreviewKeyDownEvent});Flush(this);}
        TranslateTransform Offset(ToggleButton b,string name)=>(TranslateTransform)((TextBlock)b.Template.FindName(name switch{"ShadeOffset"=>"Shade","LightOffset"=>"Light",_=>"Letters"},b)).RenderTransform;
        var pressedKey=(DependencyPropertyKey)typeof(ButtonBase).GetField("IsPressedPropertyKey",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        var fonts=Path.Combine(folder,"preferences");Directory.CreateDirectory(fonts);
        using(var a=new TrackerStore(Path.Combine(fonts,"a.db")))using(var b=new TrackerStore(Path.Combine(fonts,"b.db")))
        {
            Check(HeadingAppearance.Read(a)=="classic"&&HeadingAppearance.Read(b)=="classic","Missing per-character choices default to Classic");a.Set(HeadingAppearance.StateKey,"quest");Check(HeadingAppearance.Read(a)=="quest"&&HeadingAppearance.Read(b)=="classic","Heading preferences are isolated by character store");b.Set(HeadingAppearance.StateKey,"unknown");Check(HeadingAppearance.Read(b)=="classic","Unsupported preferences fall back to Classic");b.Set(HeadingAppearance.StateKey,new{Unexpected=true});Check(HeadingAppearance.Read(b)=="classic","Unexpected stored types fall back safely");
        }
        using(var reopened=new TrackerStore(Path.Combine(fonts,"a.db")))Check(HeadingAppearance.Read(reopened)=="quest","Saved heading choice survives reopening the store");
        bool resolved=new Typeface(CatalystHeading.DisplayFont,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal).TryGetGlyphTypeface(out var glyph);Check(resolved&&glyph.FamilyNames.Values.Contains("EQL Quest Display"),"Reward font resolves from its bundled resource");
        foreach(int code in Enumerable.Range(32,95).Concat(new[]{0x2013,0x2014,0x2019,0x2026}))Check(glyph.CharacterToGlyphMap.TryGetValue(code,out ushort id)&&id!=0&&glyph.AdvanceWidths[id]>0,"Custom glyph maps U+"+code.ToString("X4"));
        var fallback=new FormattedText("Éclat’s reward — +14",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(CatalystHeading.DisplayFont,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),14,Brushes.Gold,1);Check(CatalystHeading.DisplayFont.Source.Contains("Georgia")&&!fallback.BuildGeometry(new()).Bounds.IsEmpty,"Unsupported letters use the configured font fallback");
        Controller.SetHeadingStyle("classic");CatalystHeading.Use("classic");
        var settings=new SettingsWindow(Controller){Opacity=0,ShowActivated=false,ShowInTaskbar=false,Left=-15000,Top=-15000};var overview=new OverviewWindow(Controller){Opacity=0,ShowActivated=false,ShowInTaskbar=false,Left=-15000,Top=-15000};
        foreach(var w in new FrostWindow[]{this,lab,settings,overview}){if(w.Placement is {} placement)placement.ReviewEnabled=true;w.Opacity=0;w.ShowActivated=false;w.ShowInTaskbar=false;w.Left=w.Top=-15000;w.Show();Flush(w);}
        Check(settings.HeadingStyleSelector.SelectedIndex==0,"Appearance initially selects Classic");var oldRuns=new[]{HeadingTitle,lab.HeadingTitle,settings.HeadingTitle,overview.HeadingTitle}.Select(t=>t.Inlines.OfType<Run>().ToArray()).ToArray();
        settings.HeadingStyleSelector.SelectedIndex=1;Flush(settings);Check(Controller.HeadingStyle=="quest","Selecting the font updates the internal appearance choice");
        var titles=new[]{HeadingTitle,lab.HeadingTitle,settings.HeadingTitle,overview.HeadingTitle};
        for(int i=0;i<titles.Length;i++)Check(oldRuns[i].SequenceEqual(titles[i].Inlines.OfType<Run>())&&oldRuns[i][0].FontFamily==(i==2?CatalystHeading.ClassicWordmarkFont:CatalystHeading.ClassicEmblemFont)&&oldRuns[i][0].FontWeight==FontWeights.Bold,"Branding stays Classic without reconstructing content: "+i);
        var optional=UI.Text("Decorative heading",17,"Melee",true);Check(optional.FontFamily==CatalystHeading.DisplayFont&&optional.FontWeight==FontWeights.Normal,"Optional decorative headings use Regular display lettering without changing the wordmark");
        var later=new OverviewWindow(Controller);Check(later.HeadingTitle.Inlines.OfType<Run>().First().FontFamily==CatalystHeading.ClassicEmblemFont,"Newly created windows use the selected heading style");later.Close();
        settings.HeadingStyleSelector.SelectedIndex=0;Flush(settings);Check(HeadingTitle.Inlines.OfType<Run>().First().FontFamily==CatalystHeading.ClassicEmblemFont&&HeadingTitle.Inlines.OfType<Run>().Skip(1).First().FontFamily==CatalystHeading.ClassicWordmarkFont&&settings.HeadingTitle.Inlines.OfType<Run>().First().FontFamily==CatalystHeading.ClassicWordmarkFont,"Classic restores Cinzel/Georgia branding and the Settings wordmark");
        Check(FontFamily.Source=="Segoe UI"&&settings.HeadingStyleSelector.FontFamily.Source=="Segoe UI","Window/body/control typography remains Segoe UI");
        // The established reward behavior checks run once; their broad screenshots
        // are disabled while the focused typography captures below cover this edit.
        await ReviewProgressRewards(Path.Combine(folder,"behavior"),rewardOnly:true);
        ShowView(TrackerTab.Progress,"Bard Epic Quest");RefreshSteps();Flush(this);Controller.Profile.SmoothMeterMotion=false;var reward=rewardActions.Values.First();
        Check(reward.FontFamily==CatalystHeading.DisplayFont&&reward.FontSize==14&&reward.FontWeight==FontWeights.Normal&&reward.MinHeight==28,"Reward names always use the supplied Regular font and a 28-DIP hit area");Check(reward.ToolTip!.ToString()!.Contains(reward.Content.ToString()!)&&AutomationProperties.GetName(reward).StartsWith("Reward details:"),"Full reward name and accessible action name remain available");
        Check(UIElementAutomationPeer.CreatePeerForElement(reward)!.GetChildren()==null,"Decorative bevel layers do not duplicate the accessible reward name");
        var before=reward.RenderSize;reward.SetValue(pressedKey,true);Flush(this);Check(Offset(reward,"LetterOffset").Y>0&&Offset(reward,"ShadeOffset").Y<0&&reward.RenderSize==before,"Synthetic pointer-pressed state insets the lettering without moving its bounds");reward.SetValue(pressedKey,false);Flush(this);Check(Offset(reward,"LetterOffset").Y==0,"Pointer release restores raised lettering when unselected");
        Toggle(reward);Check(progressRewardSlide!.IsOpen&&reward.IsChecked==true&&Offset(reward,"LetterOffset").Y>0,"Accessibility activation opens reward details and keeps lettering sunken");Toggle(reward);Check(!progressRewardSlide.IsOpen&&Offset(reward,"LetterOffset").Y==0,"A second activation closes details and raises the lettering");Key(reward,System.Windows.Input.Key.Enter);Check(progressRewardSlide.IsOpen,"Enter opens reward details");Key(reward,System.Windows.Input.Key.Enter);Check(!progressRewardSlide.IsOpen,"Enter toggles reward details closed");
        foreach(var theme in ThemeCatalog.Choices.Skip(1))foreach(bool custom in new[]{false,true})
        {
            Controller.Profile.Theme=theme.Id;Appearance.Apply(theme.Id);settings.HeadingStyleSelector.SelectedIndex=custom?1:0;
            foreach(var size in new[]{(Name:"normal",W:915d,H:650d),(Name:"minimum",W:640d,H:420d),(Name:"short",W:915d,H:420d)})
            {
                foreach(var w in new FrostWindow[]{this,lab,settings,overview}){w.Width=size.W;w.Height=size.H;Flush(w);var first=w.HeadingTitle.Inlines.OfType<Run>().First();Check(first.FontFamily==(ReferenceEquals(w,settings)?CatalystHeading.ClassicWordmarkFont:CatalystHeading.ClassicEmblemFont),theme.Id+"/"+custom+"/"+size.Name+" heading route "+w.GetType().Name);}
                var selection=selectedStep;var underlay=progressRewardSlide!.Underlay;var scroll=UI.FindScroll(underlay)!;double position=scroll.VerticalOffset;reward=rewardActions.Values.First();Check(reward.FontFamily==CatalystHeading.DisplayFont,"Reward typeface is independent of heading choice");Toggle(reward);var selectedBounds=reward.RenderSize;
                Check(progressFooter!.ActualHeight<=96&&progressFooter.Children.OfType<Border>().Select(b=>b.TranslatePoint(new(),progressFooter).Y).Distinct().Count()==1,"Reward footer retains its capped shared row");
                Check(((Border)reward.Template.FindName("Face",reward)).Background is SolidColorBrush brush&&brush.Color.A==0,"Reward face stays transparent in the selected state");
                foreach(double scale in new[]{1d,1.5,2d,3d})foreach(var surface in new[]{(Name:"reward",Element:(FrameworkElement)Content),(Name:"settings",Element:(FrameworkElement)settings.Content)})
                {
                    var bitmap=PreviewRenderer.Capture(surface.Element,size.W,size.H,scale);Check(bitmap.PixelWidth==(int)Math.Ceiling(size.W*scale),theme.Id+"/"+custom+"/"+size.Name+"/"+surface.Name+"/"+scale+" renders");
                    if(scale==1){string file=$"{theme.Id}-{(custom?"quest":"classic")}-{surface.Name}-{size.Name}.png";var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(folder,file));png.Save(output);samples.Add(new{File=file,Theme=theme.Id,Custom=custom,Surface=surface.Name,Width=size.W,Height=size.H});}
                }
                Check(reward.IsChecked==true&&reward.RenderSize==selectedBounds,"Rendering retains pressed selection and bounds");progressRewardSlide.Dismiss(true,restoreFocus:true);Flush(this);Check(ReferenceEquals(selection,selectedStep)&&Math.Abs(scroll.VerticalOffset-position)<1,"Closing preserves walkthrough selection and scrolling");
            }
        }
        var longName=RewardNameButton.Create("Éclat’s extraordinarily long reward — Staff of Elemental Mastery +14");longName.Width=180;longName.Measure(new(180,1000));longName.Arrange(new(0,0,180,longName.DesiredSize.Height));longName.ApplyTemplate();Check(longName.DesiredSize.Height>28&&((TextBlock)longName.Template.FindName("Letters",longName)).TextWrapping==TextWrapping.Wrap,"Long reward names wrap with fallback punctuation instead of clipping");
        Controller.SetHeadingStyle("classic");CatalystHeading.Use("classic");settings.Close();overview.Close();Hide();lab.Hide();Json.Write(Path.Combine(folder,"heading-rewards-checks.json"),new{Passed=true,Checks=checks,Samples=samples,SyntheticPressedProperty=true,PhysicalPointerOrMultiMonitorAcceptance=false});
    }
}
