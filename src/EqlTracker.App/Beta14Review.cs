using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;
namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewBeta14(string folder)
    {
        var checks=new List<string>();var renders=new List<object>();Directory.CreateDirectory(Path.Combine(folder,"qa"));
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        void Flush(){Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        void Click(Button button){button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent});Flush();}
        void Capture(string name,FrameworkElement element,double width,double height)
        {
            if(Environment.GetCommandLineArgs().Contains("--checks-only"))return;
            foreach(double scale in new[]{1d,1.5,2d,3d})
            {var clock=Stopwatch.StartNew();long bytes=GC.GetAllocatedBytesForCurrentThread();var bitmap=PreviewRenderer.Capture(element,width,height,scale);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(folder,"qa",name+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x.png")))png.Save(output);renders.Add(new{Name=name,Scale=scale,Milliseconds=clock.Elapsed.TotalMilliseconds,AllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-bytes});}
        }
        Controller.Profile.CurrentClasses=["BRD","MAG","SHD"];Controller.Profile.QuestTracking["brd-epic"]=true;Controller.Profile.SmoothMeterMotion=false;
        ShowView(TrackerTab.Progress,"Bard Epic Quest");Width=1220;Height=800;Flush();
        Check(progressTracked.Parent==progressAll.Parent&&progressAll.Parent==progressByItem.Parent,"Visible checks share a title-row group");
        var checkGroup=(FrameworkElement)progressTracked.Parent;var heading=(Grid)checkGroup.Parent;
        Check(progressSelectorLabel.Parent==heading&&checkGroup.HorizontalAlignment==HorizontalAlignment.Right,"Check options right-align opposite Quest/Item title");
        Check(progressTracked.TranslatePoint(new(),questSelection).Y<0&&progressByItem.TranslatePoint(new(),questSelection).Y<0,"Checks occupy the label row above the dropdown");
        Check(questSelection.ActualHeight==28&&progressSearch.ActualHeight==28&&progressMore.ActualHeight==28&&progressCollapse.ActualHeight==28,"Progress controls retain matching 28 DIP heights");
        Check(progressCollapse.Parent==progressMore.Parent&&progressCollapse.TranslatePoint(new(),(UIElement)progressCollapse.Parent).X<progressMore.TranslatePoint(new(),(UIElement)progressMore.Parent).X,"Expand/collapse action sits immediately before More");
        ExpandProgress(true);Click(progressCollapse);Check(!ProgressHasExpandedBranches()&&Equals(progressCollapse.Content,"Expand all"),"Routed collapse changes its next action to Expand all");Click(progressCollapse);Check(ProgressHasExpandedBranches()&&Equals(progressCollapse.Content,"Collapse all"),"Routed expand changes its next action to Collapse all");
        var root=progressNodes.First();root.IsExpanded=false;Flush();Check(Equals(progressCollapse.Content,"Collapse all"),"Mixed expansion offers Collapse all");ExpandProgress(false);RefreshSteps();Check(!ProgressHasExpandedBranches()&&Equals(progressCollapse.Content,"Expand all"),"Refresh preserves collapsed browsing state");ExpandProgress(true);
        Check(!progressMore.ContextMenu.Items.OfType<MenuItem>().Any(i=>i.Header?.ToString() is "Expand all" or "Collapse all"),"More contains no duplicate expansion actions");
        Check(progressOptional.IsCheckable,"Optional steps remain a checked command in More");
        var app=(App)Application.Current;Check(!app.Updates.Preferences.Automatic,"Fresh profiles do not opt in to update requests");app.Updates.ReviewState(ApplicationUpdateState.Ready);Flush();
        var icon=Usability.Tree(Body).OfType<Button>().First(b=>Equals(b.Tag,"tracker-update-ready")&&b.IsVisible);
        var settings=Usability.Tree(Body).OfType<Button>().First(b=>Equals(b.Tag,"tracker-settings-footer")&&b.IsVisible);
        Check(icon.Parent==settings.Parent&&icon.TranslatePoint(new(),this).X>=settings.TranslatePoint(new(settings.ActualWidth,0),this).X,"Ready icon appears immediately to the right of Settings");
        app.Updates.ReviewState(ApplicationUpdateState.Idle);Flush();Check(icon.Visibility==Visibility.Collapsed,"Update icon reserves no space before a verified update is ready");
        app.Updates.ReviewState(ApplicationUpdateState.Ready);Flush();
        foreach(string theme in new[]{"coldain","sunstrider","shadowman"})
        {
            Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme));Appearance.Apply(theme);Width=1220;Height=800;Flush();Capture(theme+"-progress",(FrameworkElement)Content,1220,800);
            Width=720;Height=650;Flush();Check(checkGroup.TranslatePoint(new(),questSelection).Y<0,"Narrow "+theme+" keeps checks above the selector");Capture(theme+"-progress-narrow",(FrameworkElement)Content,720,650);
            var window=new SettingsWindow(Controller){Left=-15000,Top=-15000,ShowActivated=false,Owner=this};window.Show();window.SelectSection(SettingsSection.Sync);window.UpdateLayout();
            var buttons=Usability.Tree(window.Body).OfType<Button>().ToList();Check(buttons.Any(b=>Equals(b.Content,"Check for updates"))&&buttons.Any(b=>Equals(b.Content,"Update and restart")&&b.IsVisible),theme+" settings expose manual check and user-triggered apply");
            Capture(theme+"-updates",(FrameworkElement)window.Content,940,610);window.Close();
        }
        app.Updates.ReviewState(ApplicationUpdateState.Idle);
        var arguments=Environment.GetCommandLineArgs();int fixture=Array.IndexOf(arguments,"--update-fixture");
        if(fixture>=0&&fixture+1<arguments.Length)await UpdateDownloadReview.Run(folder,arguments[fixture+1],checks);
        await ReviewProgressActions(folder,checks,renders,(name,width,height)=>{Width=width;Height=height;Flush();Capture(name,(FrameworkElement)Content,width,height);});
        Json.Write(Path.Combine(folder,"beta14-checks.json"),new{Passed=checks.Count,Checks=checks,Renders=renders,NetworkRequests=0,Scope="Synthetic WPF routed interaction and scaled rendering; physical DPI and live-game acceptance remain separate."});
    }
}
