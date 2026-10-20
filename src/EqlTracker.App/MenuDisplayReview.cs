using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class DpsWindow
{
    internal async Task ReviewMenuDisplay(string folder,QuestWindow tracker,OverlayWindow overlay,OverlayWindow objective)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Isolated review required");
        Directory.CreateDirectory(Path.Combine(folder,"qa"));var checks=new List<string>();var audit=new List<object>();var samples=new List<object>();var c=Controller;var app=(App)Application.Current;
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        void Flush(){Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        void Show(Window window){window.Left=window.Top=-15000;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();Flush();}
        string Tip(object? value)=>value is ToolTip t?Tip(t.Content):value is TextBlock text?text.Text:value?.ToString()??"";
        void Audit(string name,DependencyObject root)
        {
            Usability.Tooltips(root);var controls=Usability.Tree(root).OfType<FrameworkElement>().Where(Usability.TooltipControl).Where(e=>e.TemplatedParent is not Control||e is TabItem or DataGridColumnHeader).ToArray();
            var missing=controls.Where(e=>!Usability.HasTooltip(e)).Select(e=>e.GetType().Name+":"+(e is ContentControl cc?cc.Content:e.Name)).ToArray();
            audit.Add(new{Name=name,Controls=controls.Length,Disabled=controls.Count(e=>!e.IsEnabled),Missing=missing});Check(missing.Length==0,"Tooltip coverage · "+name+" · "+string.Join(',',missing));
            foreach(var menu in Usability.Tree(root).OfType<FrameworkElement>().Select(e=>e.ContextMenu).Where(m=>m!=null).Distinct())
            {Usability.Tooltips(menu!);var entries=MenuEntries(menu!).ToArray();Check(entries.All(m=>Usability.HasTooltip(m)),"Menu tooltip coverage · "+name);}
        }
        IEnumerable<MenuItem> MenuEntries(ItemsControl parent){foreach(var item in parent.Items.OfType<MenuItem>()){yield return item;foreach(var child in MenuEntries(item))yield return child;}}
        void Capture(string name,FrameworkElement content,double width,double height)
        {foreach(double scale in new[]{1d,1.5,2,3}){var bitmap=PreviewRenderer.Capture(content,width,height,scale);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(folder,"qa",$"menu-display-{name}-{scale:0.0}x.png"));encoder.Save(file);Check(bitmap.PixelWidth==(int)Math.Ceiling(width*scale),$"Component pixels · {name} · {scale:0.0}x");}}
        c.Profile.SmoothMeterMotion=false;GearBuildFixtures.Apply(c);foreach(var window in new Window[]{this,tracker,overlay,objective})Show(window);
        var fights=c.Combat.Encounters.OrderBy(f=>f.Start).ToArray();var fight=fights.Last();c.Profile.Meter=new(){Scope="History",EncounterId=fight.Id,Actor="Combined"};ShowView(CombatLabMode.Damage,CombatLabTab.Breakdown);Refresh();Flush();
        var view=c.DpsView();Check(damageStats.Fields.Count==6&&damageStats.Fields[0].Text.StartsWith("Average DPS")&&damageStats.Fields[2].Text.StartsWith("Fight duration"),"Six-field Stats has rate above duration");
        Check(damageStats.Fields[4].Text.EndsWith(view.Rows.Sum(r=>r.Hits).ToString("N0")),"Landed hit count matches filtered records");
        var weighted=view with {Rows=[new("A","Melee",100,1,100,1,1,20,20,0,50),new("B","Melee",1,1,1,1,1,1,100,0,50)]};damageStats.SetDamage(weighted,"Weighted fixture");Check(damageStats.Fields[5].Text.EndsWith("20.8%"),"Critical rate weights counts instead of averaging ability percentages");
        damageStats.SetDamage(view with {Rows=[]},"Empty");Check(damageStats.Fields[4].Text.EndsWith("0")&&damageStats.Fields[5].Text.EndsWith("Unavailable"),"Zero records do not invent a critical-rate denominator");
        damageStats.SetDamage(view with {DetailComplete=false},"Summary only");Check(damageStats.Fields[4].Text.EndsWith("Unavailable")&&damageStats.Fields[5].Text.EndsWith("Unavailable"),"Summary-only hit and critical counts remain unavailable");
        var criticalView=DpsViews.Build(fights,null,DateTime.MinValue,new(){Scope="History",EncounterId=fight.Id,Actor="Combined",CriticalOnly=true});damageStats.SetDamage(criticalView,"Critical only");Check(criticalView.Rows.Sum(r=>r.Hits)==0||damageStats.Fields[5].Text.EndsWith("100.0%"),"Critical-only filter uses its own denominator");Refresh();
        Check(Usability.Tree(damageMetrics).OfType<TextBlock>().Any(t=>t.Text=="TOTAL")&&!Usability.Tree(damageMetrics).OfType<TextBlock>().Any(t=>t.Text=="TOTAL DPS"),"Summary caption is Total");
        foreach(var card in Usability.Tree(damageMetrics).OfType<CombatSummaryCards>().Single().Children.OfType<FrameworkElement>())Check(Usability.HasTooltip(card),"Whole summary card tooltip · "+card.GetType().Name);
        var tooltip=new ToolTip{Content=damageStats.ToolTip,PlacementTarget=damageStats};tooltip.IsOpen=true;Flush();Check(Usability.Tree(tooltip).OfType<TextBlock>().Any(t=>t.TextWrapping==TextWrapping.Wrap),"Dynamic string tooltips wrap rather than clip");Capture("stats-tooltip",tooltip,420,245);tooltip.IsOpen=false;
        var tools=Usability.Tree((DependencyObject)Content).OfType<Button>().Single(b=>Equals(b.Content,"Tools ▾"));var menu=tools.ContextMenu!;
        void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Pointer(Button b,bool down,bool pipeline)
        {var args=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=down?Mouse.PreviewMouseDownEvent:Mouse.PreviewMouseUpEvent,Source=b};if(pipeline)InputManager.Current.ProcessInput(args);else b.RaiseEvent(args);}
        foreach(bool pipeline in new[]{false,true})for(int i=0;i<3;i++)
        {Click(tools);Flush();Check(menu.IsOpen&&Usability.GetIsPressed(tools),$"Menu opens and marks anchor · {pipeline} · {i}");Pointer(tools,true,pipeline);Flush();Check(!menu.IsOpen&&!Usability.GetIsPressed(tools),$"Second routed press closes anchor menu · {pipeline} · {i} (open {menu.IsOpen}; pressed {Usability.GetIsPressed(tools)})");Click(tools);Check(!menu.IsOpen,$"Closing gesture cannot reopen menu · {pipeline} · {i}");Pointer(tools,false,pipeline);}
        Click(tools);menu.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});Check(!menu.IsOpen&&!Usability.GetIsPressed(tools),"Escape closes the menu and clears the anchor immediately");
        Click(tools);Click(tools);Check(!menu.IsOpen,"Keyboard-style Click toggles an already-open menu");
        Click(tools);var multi=menu.Items.OfType<MenuItem>().Single(m=>m.StaysOpenOnClick);typeof(MenuItem).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(multi,null);Check(menu.IsOpen,"Explicit multi-select command stays open");menu.IsOpen=false;
        var isolatedMenu=new ContextMenu();var ordinary=new MenuItem{Header="Review action"};bool actionRan=false;ordinary.Click+=(_,_)=>actionRan=true;isolatedMenu.Items.Add(ordinary);var submenu=new MenuItem{Header="Review submenu"};submenu.Items.Add(new MenuItem{Header="Child action"});isolatedMenu.Items.Add(submenu);var isolatedAnchor=OverlayMenus.Button(isolatedMenu,"Review menu");Body.Children.Add(isolatedAnchor);Click(isolatedAnchor);Flush();submenu.IsSubmenuOpen=true;Flush();Check(submenu.IsSubmenuOpen,"Keyboard-style submenu opening is preserved");submenu.IsSubmenuOpen=false;
        typeof(MenuItem).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(ordinary,null);Flush();Check(actionRan&&!isolatedMenu.IsOpen,"Ordinary actions close their menu");Body.Children.Remove(isolatedAnchor);
        Check(OverlayMenus.DismissForForegroundChange(false,true,true)&&OverlayMenus.DismissForForegroundChange(true,true,false)&&!OverlayMenus.DismissForForegroundChange(true,true,true)&&!OverlayMenus.DismissForForegroundChange(false,false,false),"Application-change rules preserve compact overlay behavior");
        var hover=new MenuInteractionState();var now=DateTime.UtcNow;Check(!hover.ShouldDismiss(now,false,false)&&hover.ShouldDismiss(now.AddSeconds(1),false,false)&&!hover.ShouldDismiss(now.AddSeconds(2),true,false),"Compact hover dismissal and re-entry remain unchanged");
        var bound=new TextBlock();bound.SetBinding(ToolTipProperty,new Binding("Tip"));bound.DataContext=new{Tip="First evidence"};Usability.Tooltips(bound);bound.DataContext=new{Tip="Updated evidence"};bound.GetBindingExpression(ToolTipProperty)!.UpdateTarget();Flush();Check(BindingOperations.IsDataBound(bound,ToolTipProperty)&&Tip(bound.ToolTip)=="Updated evidence",$"Tooltip audit preserves live bindings (bound {BindingOperations.IsDataBound(bound,ToolTipProperty)}; value {Tip(bound.ToolTip)}; status {bound.GetBindingExpression(ToolTipProperty)?.Status})");
        var conditional=UI.Button("Save",()=>{});conditional.IsEnabled=false;Usability.Tooltips(conditional);Check(Tip(conditional.ToolTip).Contains("Unavailable"),"Disabled generated tooltips explain unavailable actions");conditional.IsEnabled=true;Check(!Tip(conditional.ToolTip).Contains("Unavailable"),"Generated tooltip availability refreshes when enabled");
        foreach(var tab in TrackerNavigation.Order){tracker.ShowView(tab);Flush();Audit("Tracker · "+tab,(DependencyObject)tracker.Content);}
        foreach(var mode in Enum.GetValues<CombatLabMode>())foreach(var tab in Enum.GetValues<CombatLabTab>()){ShowView(mode,tab);Refresh();Flush();Audit("Combat Lab · "+mode+" · "+tab,(DependencyObject)Content);}
        using(var windows=new ReviewWindows())
        {
            var settings=windows.Add(new SettingsWindow(c));Show(settings);foreach(var section in Enum.GetValues<SettingsSection>()){settings.SelectSection(section);Flush();Audit("Settings · "+section,(DependencyObject)settings.Content);}
            foreach(var window in new Window[]{windows.Add(new NativeControlsWindow(c)),windows.Add(new IconGallery(c)),windows.Add(new EntryDialog("Rename build",[("Build name","Review build")])),windows.Add(new CurrentBuildBackupDialog("Review plan")),windows.Add(new GearSlotPickerDialog("Candidate",[])),windows.Add(new StatProfileDialog(c,c.Profile.ActiveLoadout,false,[],()=>{})),windows.Add(new RatingFormulaDialog(c.Profile.Weights.Keys.First(),new(c.Profile.Weights.First().Value),null,false)),windows.Add(new PetItemDialog(c,c.Catalog.Items.First(i=>i.MetadataComplete),new(){Name="Review plan"},PetInventoryDestination.Planned)),windows.Add(new OverlayFilterWindow(c,overlay))}){Show(window);Audit(window.GetType().Name,(DependencyObject)window.Content);}
            foreach(var window in new Window[]{windows.Add(new GearRolesDialog(c.Profile,c.Profile.Weights.Keys.First())),windows.Add(new PetBuildReviewDialog("Review plan",["Context and inventory evidence retained"])),windows.Add(new TheorycraftExitDialog())}){Show(window);Audit(window.GetType().Name,(DependencyObject)window.Content);}
        }
        Audit("DPS overlay",(DependencyObject)overlay.Content);Audit("Objective overlay",(DependencyObject)objective.Content);
        var destinations=Enumerable.Range(1,8).Select(i=>new InspectionLine("Target "+i,$"{i*100:N0} damage")).ToArray();
        var member=new GroupMemberRow("review-member","Review member",3600,36,100,8,"Verified roster and owner evidence",view.Rows,destinations){Seconds=100,CharacterTotal=2400,PetTotal=1200};
        var snapshot=new CombatInspectionSnapshot("destinations-8",view.Rows,new Dictionary<string,AbilityInspection>(),[member],"Retained example coverage");ShowView(CombatLabMode.Damage,CombatLabTab.Group);group.Set(snapshot,false);Flush();
        var more=Usability.Tree(group).OfType<Expander>().Single(e=>e.Header?.ToString()=="View More >");
        Check(!more.IsExpanded&&more.Visibility==Visibility.Visible,"More than five destinations starts collapsed");
        Check(!Usability.Tree(group).OfType<ScrollViewer>().Any(s=>s.Content is TextBlock t&&t.Text.Contains("Target 1")),"Targets have no nested scrollbar");
        more.IsExpanded=true;group.Set(snapshot with {Key="refreshed"},false);Check(more.IsExpanded,"Destination expansion survives snapshot refresh");
        group.Set(snapshot with {Key="healing"},true);Check(!more.IsExpanded,"Changing combat mode resets destination expansion");
        foreach(int count in new[]{0,5,6,20}){var rows=Enumerable.Range(1,count).Select(i=>new InspectionLine("Target "+i,i+" damage")).ToArray();group.Set(snapshot with {Key="count:"+count,Members=[member with {Details=rows}]},false);Check(more.Visibility==(count>5?Visibility.Visible:Visibility.Collapsed),"Destination expander threshold · "+count);}
        foreach(string theme in new[]{"Rune4K_Frost_Clear","Rune4K_Vellum_Clear"})
        {
            string suffix=theme.Contains("Vellum")?"vellum":"frost";c.Profile.Theme=theme;app.ApplySettings();c.Profile.Meter=new(){Scope="History",EncounterId=fight.Id,Actor="Combined"};ShowView(CombatLabMode.Damage,CombatLabTab.Breakdown);Refresh();Capture("lab-damage-"+suffix,(FrameworkElement)Content,1170,740);
            ShowView(CombatLabMode.Healing,CombatLabTab.Breakdown);Refresh();Check(healingStats.Fields[0].Text.StartsWith("Average HPS")&&healingStats.Fields[4].Text.StartsWith("Logged heals"),"Healing has matching six-field labels · "+suffix);Capture("lab-healing-"+suffix,(FrameworkElement)Content,1170,740);
            ShowView(CombatLabMode.Damage,CombatLabTab.Group);group.Set(snapshot,false);Capture("member-collapsed-"+suffix,(FrameworkElement)Content,1170,740);more.IsExpanded=true;Capture("member-expanded-"+suffix,(FrameworkElement)Content,1170,740);
        }
        ShowView(CombatLabMode.Damage,CombatLabTab.Group);group.Set(snapshot with {Key="narrow"},false);more.IsExpanded=true;Capture("member-narrow",(FrameworkElement)Content,640,540);
        var paneScroll=Usability.Tree(group).OfType<ScrollViewer>().Single(s=>s.Content is GroupPaneLayout);paneScroll.ScrollToBottom();Flush();Check(paneScroll.ScrollableHeight>0&&paneScroll.VerticalOffset>0,"Short narrow Group/Raid panes remain reachable through scrolling");Capture("member-narrow-details",(FrameworkElement)Content,640,540);
        overlay.ReviewDisplayModes(Check,Capture,Audit);Audit("DPS overlay after switching",(DependencyObject)overlay.Content);
        var measure=c.DpsView();damageStats.SetDamage(measure,"Warm revision");for(int run=1;run<=3;run++)for(int i=0;i<100;i++){long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();damageStats.SetDamage(measure,"Warm revision");samples.Add(new{Run=run,Index=i,Milliseconds=watch.Elapsed.TotalMilliseconds,Allocated=GC.GetAllocatedBytesForCurrentThread()-allocated});}
        await Task.Delay(50);
        Json.Write(Path.Combine(folder,"menu-display-checks.json"),new{Checks=checks,Count=checks.Count,TooltipAudit=audit,Samples=samples,Geometry=PreviewRenderer.CaptureGeometry,AppSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),CoreSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CharacterProfile).Assembly.Location)))});
    }
    private sealed class ReviewWindows:IDisposable
    {private readonly List<Window> all=[];internal T Add<T>(T window)where T:Window{all.Add(window);return window;}public void Dispose(){foreach(var window in all)window.Close();}}
}

public sealed partial class OverlayWindow
{
    internal void ReviewDisplayModes(Action<bool,string> check,Action<string,FrameworkElement,double,double> capture,Action<string,DependencyObject> audit)
    {
        var button=paired.Children.OfType<Button>().Single();var menu=button.ContextMenu!;string labBefore=JsonSerializer.Serialize(Controller.Profile.Meter);
        foreach(var style in Enum.GetValues<IconArtworkStyle>())
        {
            Controller.Profile.IconStyle=style;IconArtwork.Apply(Controller.Profile);
            foreach(string actor in new[]{"Pet","Player","Combined","Group"})
            {
                SelectDisplay(actor);Refresh();UpdateLayout();check(paired.Visibility==Visibility.Visible&&button.Width==20&&button.Height==17,"Footer selector stays available and same size · "+actor+" · "+style);
                check(secondaryDps.Suffix.Length==0&&paired.Children.Count==2,"Footer contains only selected numeric DPS and icon · "+actor+" · "+style);
                if(actor!="Group")check(secondaryDps.Text==Controller.OverlayDpsView().Dps.ToString("N1"),"Footer follows selected personal DPS · "+actor+" · "+style);
                check(menu.Items.OfType<MenuItem>().Count()==4&&menu.Items.OfType<MenuItem>().All(i=>i.Icon is ActorBadge),"Four display choices have matching role icons · "+actor+" · "+style);
                capture("overlay-"+actor.ToLowerInvariant()+"-"+style.ToString().ToLowerInvariant(),(FrameworkElement)Content,300,260);
            }
        }
        check(labBefore==JsonSerializer.Serialize(Controller.Profile.Meter),"Footer selection leaves Combat Lab independent");
        capture("overlay-minimum",(FrameworkElement)Content,128,72);check(button.ActualWidth==20&&button.ActualHeight==17&&button.IsVisible,"Minimum overlay keeps the same display button");
        var help=GetHelpWindow();help.Left=help.Top=-15000;help.ShowActivated=false;help.ShowInTaskbar=false;help.Show();audit("Objective help",(DependencyObject)help.Content);help.Close();
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));var items=menu.Items.OfType<Control>().ToArray();menu.Items.Clear();var host=new StackPanel{Width=170,Background=UI.Brush("SolidInnerSurface"),Resources=menu.Resources};foreach(var item in items)host.Children.Add(item);capture("display-menu",host,170,145);check(Usability.Tree(host).OfType<ActorBadge>().Count(b=>b.Visibility==Visibility.Visible&&b.ActualWidth>0&&VisualTreeHelper.GetDrawing(b)!=null)==4,"Menu template renders every assigned display icon");host.Children.Clear();foreach(var item in items)menu.Items.Add(item);
    }
}
