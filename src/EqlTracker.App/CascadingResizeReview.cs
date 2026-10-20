using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class CascadingResizeReview
{
    private sealed record Row(string Name, string Type, string Zone, double Rating);
    private sealed record Fixture(FrostWindow Window, DataGrid Table, Grid Outer, Grid Inner, TabControl Tabs);
    private static void Flush(Window window) { window.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
    private static void Save(string path, FrameworkElement content, double width, double height, double scale)
    {
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(content,width,height,scale)));
        using var file=File.Create(path);encoder.Save(file);
    }
    private static Fixture Create(TrackerController c)
    {
        var window=new FrostWindow(c,"Cascading resize review","Synthetic data only · drag table boundaries and both panel handles",1120,780);
        var root=new DockPanel(); var tabs=new TabControl { Height=108 }; TrackerTabChrome.Install(tabs);
        foreach(string name in new[]{"Items","Gear","Pet"}) tabs.Items.Add(new TabItem { Header=name, Content=UI.Field("Role",UI.Select(["DPS","Tank","Balanced"],"DPS",150)) });
        tabs.SelectedIndex=1; DockPanel.SetDock(tabs,Dock.Top);root.Children.Add(tabs);
        var table=UI.Table(("Item","Name",300,null),("Type","Type",120,null),("Zone","Zone",240,null),("Rating","Rating",90,"+0.0;-0.0;0"));
        table.Name="CascadeTable";table.Height=220;TableLayout.SetKey(table,"review.cascade.table");
        UI.SetRows(table,Enumerable.Range(0,60).Select(i=>new Row($"Synthetic equipment with a long readable name {i:00}", i%2==0?"2H Slashing":"1H Blunt","A synthetic acquisition zone",i-20)).ToList());
        table.SelectedIndex=4; DockPanel.SetDock(table,Dock.Top);root.Children.Add(table);
        Border Panel(string title,string text) => UI.Card(UI.Stack(UI.Label(title),UI.Text(text,13)),new(3,8,3,4));
        var inventory=Panel("Equipment","Nearest neighbor shrinks first. Other panels remain unchanged until its minimum is reached.");
        var candidates=Panel("Candidates","This middle panel has a 180-DIP minimum. Expanding Item Details uses this space first.");
        var details=Panel("Item Details","Visible handles retain the existing 5-DIP splitter hit area. Hover, focus, and drag use the active theme.");
        var inner=UI.Split(inventory,candidates,350,c.Profile,"review.cascade.inner",160,180);inner.Name="CascadeInner";
        var outer=UI.Split(inner,details,300,c.Profile,"review.cascade.outer",345,190);outer.Name="CascadePanels";
        var footer=UI.Text("Selected tab: no dashes. Other tab headers: three internal marks on hover or keyboard focus.",12,"SecondaryText");
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);root.Children.Add(outer);window.Body.Children.Add(root);
        return new(window,table,outer,inner,tabs);
    }
    internal static void OpenPointer(string folder,TrackerController c)
    {
        if(!c.Demo)throw new InvalidOperationException("Isolated review required");Directory.CreateDirectory(folder);
        var fixture=Create(c);var events=new List<CascadingDrag.Observation>();
        CascadingDrag.Observe=value=>{events.Add(value);Json.Write(Path.Combine(folder,"pointer-drags.json"),new{Events=events,Scope="Actual captured pointer input; property-application timings exclude deferred WPF layout."});};
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(200) };
        timer.Tick+=(_,_)=>
        {
            var chromes=Usability.Tree(fixture.Tabs).OfType<TrackerTabChrome>().ToArray();
            Json.Write(Path.Combine(folder,"pointer-state.json"),new{
                Table=fixture.Table.Columns.OrderBy(x=>x.DisplayIndex).Select(x=>new{Header=x.Header?.ToString(),Width=x.ActualWidth,Minimum=x.MinWidth,DisplayIndex=x.DisplayIndex,Visible=x.Visibility==Visibility.Visible}),
                Panels=new[]{fixture.Inner.ColumnDefinitions[0].ActualWidth,fixture.Inner.ColumnDefinitions[2].ActualWidth,fixture.Outer.ColumnDefinitions[2].ActualWidth},
                Tabs=chromes.Select(x=>new{Header=((TabItem)x.TemplatedParent).Header?.ToString(),x.Selected,x.Hovered,x.Focused,x.ShowInsideMarks}),
                Rows=fixture.Table.Items.Count,Selected=fixture.Table.SelectedIndex,Events=events.Count});
        };
        fixture.Window.Closed+=(_,_)=>{timer.Stop();CascadingDrag.Observe=null;((App)Application.Current).ExitTracker();};
        fixture.Window.HideRequested=()=>{timer.Stop();CascadingDrag.Observe=null;((App)Application.Current).ExitTracker();};
        fixture.Window.ShowInTaskbar=true;fixture.Window.ShowActivated=true;fixture.Window.WindowStartupLocation=WindowStartupLocation.CenterScreen;
        fixture.Window.Show();fixture.Window.Hide();fixture.Window.Show();timer.Start();
    }
    internal static void Run(string folder,TrackerController c,QuestWindow tracker,DpsWindow lab,bool gearCaptureOnly=false,bool labPanelsOnly=false)
    {
        if(!c.Demo)throw new InvalidOperationException("Isolated review required");Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(folder,"qa"));
        if(labPanelsOnly){ReviewLabPanels(folder,c,lab);return;}
        if(gearCaptureOnly)
        {
            GearBuildFixtures.Apply(c);tracker.Left=tracker.Top=-15000;tracker.ShowActivated=false;tracker.ShowInTaskbar=false;tracker.Show();tracker.ShowView(TrackerTab.Gear);Flush(tracker);
            foreach(double scale in new[]{1d,1.5,2d,3d})Save(Path.Combine(folder,"qa",$"tracker-Gear-{scale:0.0}x.png"),(FrameworkElement)tracker.Content,1220,800,scale);
            Save(Path.Combine(folder,"qa","tracker-Gear-narrow.png"),(FrameworkElement)tracker.Content,640,420,1);return;
        }
        var checks=new List<string>();var timings=new List<object>();
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        var fixture=Create(c);fixture.Window.Left=fixture.Window.Top=-15000;fixture.Window.ShowActivated=false;fixture.Window.ShowInTaskbar=false;fixture.Window.Show();Flush(fixture.Window);
        var table=fixture.Table;TableLayout.Reset(table);Flush(fixture.Window);
        var rows=table.ItemsSource;var selected=table.SelectedItem;var columns=table.Columns.OrderBy(x=>x.DisplayIndex).ToArray();
        double total=columns.Sum(x=>x.ActualWidth),far=columns[0].ActualWidth,nearest=columns[1].ActualWidth;
        TableLayout.ResizeColumn(table,columns[2],columns[2].ActualWidth+10);Flush(fixture.Window);
        Check(Math.Abs(total-columns.Sum(x=>x.ActualWidth))<.5,"Table resize keeps total width bounded");
        Check(ReferenceEquals(rows,table.ItemsSource)&&ReferenceEquals(selected,table.SelectedItem),"Table resize preserves row collection and selection");
        TableLayout.Reset(table);Flush(fixture.Window);
        columns[2].Visibility=Visibility.Collapsed;Flush(fixture.Window);columns[1].DisplayIndex=0;Flush(fixture.Window);
        TableLayout.ResizeColumn(table,columns[0],columns[0].ActualWidth-10);Flush(fixture.Window);
        Check(columns.Where(x=>x.Visibility==Visibility.Visible).All(x=>x.ActualWidth+.5>=x.MinWidth),"Hidden and reordered columns respect measured minimums");
        columns[2].Visibility=Visibility.Visible;columns[0].DisplayIndex=0;TableLayout.Reset(table);Flush(fixture.Window);
        var scroll=Usability.Tree(table).OfType<ScrollViewer>().First();scroll.ScrollToVerticalOffset(100);Flush(fixture.Window);double offset=scroll.VerticalOffset;
        columns[0].SortDirection=ListSortDirection.Descending;CollectionViewSource.GetDefaultView(rows).SortDescriptions.Add(new("Name",ListSortDirection.Descending));Flush(fixture.Window);
        TableLayout.ResizeColumn(table,columns[0],columns[0].ActualWidth+15);Flush(fixture.Window);
        Check(scroll.VerticalOffset==offset&&columns[0].SortDirection==ListSortDirection.Descending,"Resizing preserves scroll and active sort arrow");
        var custom=new Style(typeof(TextBlock));custom.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding("Zone")));custom.Setters.Add(new Setter(TextBlock.FontWeightProperty,FontWeights.Bold));
        var styled=UI.Table(("Item","Name",0,null));((DataGridTextColumn)styled.Columns[0]).ElementStyle=custom;TableLayout.SetKey(styled,"review.styled");
        fixture.Window.Body.Children.Add(styled);UI.SetRows(styled,new List<Row>{new("Example","Held item","Tooltip evidence",0)});Flush(fixture.Window);
        var applied=((DataGridTextColumn)styled.Columns[0]).ElementStyle;
        Check(ReferenceEquals(applied.BasedOn,custom)&&applied.Setters.OfType<Setter>().Any(x=>x.Property==TextBlock.TextTrimmingProperty),"Cell trimming preserves custom styles and tooltip bindings");fixture.Window.Body.Children.Remove(styled);
        void ResizePane(Grid outer,GridSplitter grip,double delta,string name)
        {
            Flush(fixture.Window);var session=PaneLayout.Prepare(outer,grip)!;var before=session.Tracks.Select(t=>t.Width).ToArray();
            var clock=Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();
            CascadeWidths.Resize(session.Tracks,session.Boundary,delta,session.Widths);session.Apply(session.Widths);Flush(fixture.Window);
            timings.Add(new{Name=name,Milliseconds=clock.Elapsed.TotalMilliseconds,AllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated});
            var actual=new[]{fixture.Inner.ColumnDefinitions[0].ActualWidth,fixture.Inner.ColumnDefinitions[2].ActualWidth,fixture.Outer.ColumnDefinitions[2].ActualWidth};
            Check(actual.Zip(session.Widths).All(pair=>Math.Abs(pair.First-pair.Second)<1),name+" actual nested grids match shared width solver");
            Check(Math.Abs(before.Sum()-actual.Sum())<1,name+" conserves pane space");session.Finish(false);
        }
        var detailGrip=fixture.Outer.Children.OfType<GridSplitter>().Single();
        ResizePane(fixture.Outer,detailGrip,-30,"Nearest pane");
        var firstPane=fixture.Inner.ColumnDefinitions[0].ActualWidth;
        ResizePane(fixture.Outer,detailGrip,-500,"Cascade past middle minimum");
        Check(fixture.Inner.ColumnDefinitions[0].ActualWidth<firstPane&&Math.Abs(fixture.Inner.ColumnDefinitions[2].ActualWidth-180)<1,"Details expansion reaches Equipment only after Candidates minimum");
        Check(c.Profile.PanelSplits.ContainsKey("review.cascade.inner")&&c.Profile.PanelSplits.ContainsKey("review.cascade.outer"),"Nested pane preferences save both existing split identifiers");
        fixture.Window.Width=930;Flush(fixture.Window);Check(fixture.Outer.ActualWidth<=fixture.Window.ActualWidth,"Window resize retains bounded pane group");
        var minSession=PaneLayout.Prepare(fixture.Outer,detailGrip)!;
        CascadeWidths.Resize(minSession.Tracks,minSession.Boundary,10000,minSession.Widths);minSession.Apply(minSession.Widths);Flush(fixture.Window);
        Check(minSession.Widths.Zip(minSession.Tracks).All(pair=>pair.First>=pair.Second.Minimum-.01),"Pane drag clamps at all minimums");minSession.Finish(false);
        foreach(var chrome in Usability.Tree(fixture.Tabs).OfType<TrackerTabChrome>())
        {
            bool selection=chrome.Selected;chrome.SetCurrentValue(TrackerTabChrome.HoveredProperty,true);chrome.SetCurrentValue(TrackerTabChrome.FocusedProperty,true);
            Check(chrome.ShowInsideMarks==!selection,"Only unselected tab headers can show hover/focus dashes");
            var binding=BindingOperations.GetBinding(chrome,TrackerTabChrome.HoveredProperty);Check(binding?.RelativeSource?.Mode==RelativeSourceMode.Self,"Tab hover is bound to chrome itself, not selected page content");
        }
        foreach(double scale in new[]{1d,1.5,2d,3d})Save(Path.Combine(folder,"qa",$"cascade-fixture-{scale:0.0}x.png"),(FrameworkElement)fixture.Window.Content,1120,780,scale);
        fixture.Window.Hide();
        GearBuildFixtures.Apply(c);c.Profile.SmoothMeterMotion=false;
        foreach(var window in new Window[]{tracker,lab}){window.Left=window.Top=-15000;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();}
        foreach(var tab in Enum.GetValues<TrackerTab>())
        {
            tracker.Width=1220;tracker.Height=800;tracker.ShowView(tab);tracker.Refresh();Flush(tracker);
            var character=Usability.Tree(tracker).OfType<ComboBox>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Character and server");
            Check(character.IsVisible&&character.ActualHeight==28&&character.TranslatePoint(new(),tracker).X>tracker.ActualWidth/2,tab+" character selector is right-aligned beside the tabs");
            if(tab==TrackerTab.Gear)
            {
                var expansion=Usability.Tree(tracker).OfType<Button>().Single(b=>b.Tag?.ToString()=="gear-expansion-filter");
                Check(expansion.Parent is Grid field&&field.Children.OfType<TextBlock>().Any(t=>t.Text=="EXPANSION"),"Gear Expansion label sits above its button");
                void Choose(params string[] names){foreach(var entry in expansion.ContextMenu!.Items.OfType<MenuItem>()){entry.IsChecked=names.Contains(entry.Header?.ToString());entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));}Flush(tracker);}
                Choose("Kunark");Check(expansion.Content?.ToString()=="Kunark ▾","Single Gear expansion uses its full name");
                Choose("Classic","Velious");Check(expansion.Content?.ToString()=="C / V ▾","Multiple Gear expansions use compact abbreviations");
                Choose();Check(expansion.Content?.ToString()=="None ▾","Empty Gear expansion selection stays explicit");
                Choose("Classic","Kunark","Velious","Unknown");Check(expansion.Content?.ToString()=="All ▾","All Gear expansions use compact All label");
            }
            foreach(var grid in Usability.Tree(tracker).OfType<DataGrid>().Where(g=>g.IsVisible))
            {
                TableLayout.Reset(grid);Flush(tracker);
                Check(grid.Columns.Where(x=>x.Visibility==Visibility.Visible).All(x=>x.ActualWidth+.75>=x.MinWidth),tab+" table honors measured minimums");
                if(grid.Columns.FirstOrDefault(x=>x.Visibility==Visibility.Visible&&x.CanUserResize) is {} col){var source=grid.ItemsSource;TableLayout.ResizeColumn(grid,col,col.ActualWidth+15);Flush(tracker);Check(ReferenceEquals(source,grid.ItemsSource),tab+" resizing does not rebuild rows");}
            }
            foreach(double scale in new[]{1d,1.5,2d,3d})Save(Path.Combine(folder,"qa",$"tracker-{tab}-{scale:0.0}x.png"),(FrameworkElement)tracker.Content,1220,800,scale);
            Save(Path.Combine(folder,"qa",$"tracker-{tab}-narrow.png"),(FrameworkElement)tracker.Content,640,420,1);
            if(tab is TrackerTab.Gear or TrackerTab.Pet)
                Check(Usability.Tree(tracker).OfType<DataGrid>().Where(g=>g.IsVisible).Any(g=>g.ActualHeight>=g.ColumnHeaderHeight+20),tab+" keeps visible list rows at the narrow minimum");
        }
        foreach(int tab in new[]{0,2,3})
        {
            lab.Width=1170;lab.Height=740;lab.ShowView(tab);lab.Refresh();Flush(lab);
            foreach(double scale in new[]{1d,1.5,2d,3d})Save(Path.Combine(folder,"qa",$"combat-{tab}-{scale:0.0}x.png"),(FrameworkElement)lab.Content,1170,740,scale);
            Save(Path.Combine(folder,"qa",$"combat-{tab}-narrow.png"),(FrameworkElement)lab.Content,640,420,1);
        }
        var sheet=new StackPanel();
        foreach(var choice in TrackerPresentation.Tabs)
        {
            var tabs=new TabControl();TrackerTabChrome.Install(tabs);tabs.Resources["TrackerTabStyle"]=choice.Id;
            foreach(string title in new[]{"Items","Gear","Pet"})tabs.Items.Add(new TabItem{Header=title});tabs.SelectedIndex=1;sheet.Children.Add(tabs);
        }
        sheet.Measure(new(550,800));sheet.Arrange(new(0,0,550,800));sheet.UpdateLayout();
        foreach(var chrome in Usability.Tree(sheet).OfType<TrackerTabChrome>()){chrome.SetCurrentValue(TrackerTabChrome.HoveredProperty,true);Check(!chrome.Selected||!chrome.ShowInsideMarks,"Selected hover remains dash-free · "+chrome.StyleId);}
        foreach(double scale in new[]{1d,1.5,2d,3d})Save(Path.Combine(folder,"qa",$"tabs-all-styles-{scale:0.0}x.png"),sheet,550,800,scale);
        Json.Write(Path.Combine(folder,"cascade-checks.json"),new{Passed=checks.Count,Checks=checks,LayoutSamples=timings,Scope="One affected-component WPF render review at 100/150/200/300%; physical monitor DPI and pointer input are recorded separately."});
    }
    private static void ReviewLabPanels(string folder,TrackerController c,DpsWindow lab)
    {
        var checks=new List<string>();void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        lab.Left=lab.Top=-15000;lab.ShowActivated=false;lab.ShowInTaskbar=false;lab.Show();
        var content=UI.Text("Observed roster evidence and member details",12);
        var fixture=new FrostWindow(c,"Group pane review","Synthetic layout",1120,700){Left=-15000,Top=-15000,ShowActivated=false,ShowInTaskbar=false};
        var pane=new GroupPaneLayout(UI.Card(UI.Text("Recent groups")),UI.Card(UI.Text("Members")),UI.Card(content),c.Profile,()=>{});fixture.Body.Children.Add(pane);fixture.Show();Flush(fixture);
        var grip=pane.Children.OfType<GridSplitter>().Last();var first=PaneLayout.Prepare(pane,grip)!;
        CascadeWidths.Resize(first.Tracks,first.Boundary,-20,first.Widths);first.Apply(first.Widths);Flush(fixture);
        Check(Math.Abs(pane.ColumnDefinitions[0].ActualWidth-first.Tracks[0].Width)<1,"Recent groups stays fixed until Members reaches its minimum");
        Check(Math.Abs(pane.ColumnDefinitions[2].ActualWidth-first.Widths[1])<1,"Group/Raid Members consumes the nearest requested space");first.Finish(false);
        var cascade=PaneLayout.Prepare(pane,grip)!;double requested=cascade.Tracks[1].Width-cascade.Tracks[1].Minimum+30;
        CascadeWidths.Resize(cascade.Tracks,cascade.Boundary,-requested,cascade.Widths);cascade.Apply(cascade.Widths);Flush(fixture);
        Check(Math.Abs(pane.ColumnDefinitions[2].ActualWidth-200)<1&&pane.ColumnDefinitions[0].ActualWidth<cascade.Tracks[0].Width,"Group/Raid cascades to Recent groups only after Members minimum");cascade.Finish(false);
        Check(c.Profile.PanelSplits.ContainsKey("group-recent-width")&&c.Profile.PanelSplits.ContainsKey("group-members-width"),"Group/Raid retains both saved proportions");fixture.Hide();
        foreach(var choice in new[]{(CombatLabMode.Damage,CombatLabTab.Group,"damage-group"),(CombatLabMode.Healing,CombatLabTab.Group,"healing-group"),(CombatLabMode.Damage,CombatLabTab.Metrics,"metrics")})
        {
            lab.Width=1170;lab.Height=740;lab.ShowView(choice.Item1,choice.Item2);lab.Refresh();Flush(lab);
            Check(Usability.Tree(lab).OfType<GridSplitter>().Where(g=>g.IsVisible).All(g=>g.Template.FindName("Handle",g) is Border),choice.Item3+" has visible themed panel handles");
            foreach(double scale in new[]{1d,1.5,2d,3d})Save(Path.Combine(folder,"qa",$"combat-{choice.Item3}-{scale:0.0}x.png"),(FrameworkElement)lab.Content,1170,740,scale);
            Save(Path.Combine(folder,"qa",$"combat-{choice.Item3}-narrow.png"),(FrameworkElement)lab.Content,640,420,1);
            Check(Usability.Tree(lab).OfType<DataGrid>().Where(g=>g.IsVisible).All(g=>g.Columns.Where(col=>col.Visibility==Visibility.Visible).All(col=>col.ActualWidth+.75>=col.MinWidth)),choice.Item3+" tables retain their minimums after stacking");
        }
        Json.Write(Path.Combine(folder,"shared-panel-checks.json"),new{Passed=checks.Count,Checks=checks,Scope="Focused Group/Raid and Metrics supplement; no unrelated-window sweep."});
    }
}
