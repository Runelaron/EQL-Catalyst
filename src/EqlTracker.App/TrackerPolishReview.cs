using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;
public sealed partial class QuestWindow
{
    internal async Task ReviewTrackerPolish(string folder,DpsWindow lab,OverlayWindow overlay,bool retainRenders=false)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Isolated review required");
        Directory.CreateDirectory(Path.Combine(folder,"qa"));var checks=new List<string>();var samples=new List<object>();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        void Flush(){Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        void Capture(string name,FrameworkElement content,double width,double height)
        {foreach(double scale in new[]{1d,1.5,2,3}){string path=Path.Combine(folder,"qa",$"tracker-polish-{name}-{scale:0.0}x.png");if(retainRenders&&File.Exists(path)&&!name.StartsWith("effect-")&&!name.StartsWith("tabs-")&&name!="filters")continue;var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(content,width,height,scale)));using var output=File.Create(path);encoder.Save(output);}}
        var c=Controller;var app=(App)Application.Current;c.Profile.SmoothMeterMotion=false;
        GearBuildFixtures.Apply(c);Width=1220;Height=800;Left=Top=-15000;ShowActivated=false;Show();
        lab.Left=lab.Top=overlay.Left=overlay.Top=-15000;lab.ShowActivated=overlay.ShowActivated=false;lab.Show();overlay.Show();
        var profileBefore=JsonSerializer.Serialize(c.Profile.Meter);var overlayBefore=JsonSerializer.Serialize(c.Profile.OverlayMeter);
        foreach(var theme in new[]{"Rune4K_Frost_Clear","Rune4K_Vellum_Clear"})
        {
            c.Profile.Theme=theme;app.ApplySettings();ShowView(TrackerTab.Items);Flush();
            Check(UI.Brush("SecondaryText").Color==(Color)ColorConverter.ConvertFromString(theme.Contains("Vellum")?"#D9BF79":"#FFF1C9"),"Selected helper text follows "+theme);
            var sheet=new StackPanel();
            foreach(var choice in TrackerPresentation.Tabs)
            {
                var title=UI.Text(choice.Name,16,"SecondaryText",true);sheet.Children.Add(title);
                var row=new TabControl();TrackerTabChrome.Install(row);row.SetResourceReference(TrackerTabChrome.StyleIdProperty,"TrackerTabStyle");
                foreach(var name in new[]{"Queue","Items","Quests","Progress","Gear","Exaltations","Pet"})row.Items.Add(new TabItem{Header=name});row.SelectedIndex=5;
                // Local resource keeps each gallery row in its own stable race style.
                row.Resources["TrackerTabStyle"]=choice.Id;sheet.Children.Add(row);
                var geo=TrackerTabChrome.Shape(choice.Id,100,28);Check(!geo.Bounds.IsEmpty&&geo.Bounds.Width<=100.1,"Valid vector tab outline · "+choice.Name);
            }
            sheet.Measure(new(1100,1030));sheet.Arrange(new(0,0,1100,1030));sheet.UpdateLayout();
            foreach(var chrome in Usability.Tree(sheet).OfType<TrackerTabChrome>())
            {
                if(chrome.TemplatedParent is TabItem item&&item.Header?.ToString()=="Items"){Check(BindingOperations.IsDataBound(chrome,TrackerTabChrome.FocusedProperty),"Keyboard focus is bound to the tab state");chrome.Focused=true;}
                if(chrome.TemplatedParent is TabItem petItem&&petItem.Header?.ToString()=="Pet")chrome.Hovered=true;
            }
            Capture("tabs-"+(theme.Contains("Vellum")?"vellum":"frost"),sheet,1100,1030);
        }
        Check(profileBefore==JsonSerializer.Serialize(c.Profile.Meter)&&overlayBefore==JsonSerializer.Serialize(c.Profile.OverlayMeter),"Tab rendering leaves both combat selections unchanged");
        c.Profile.Theme="Rune4K_Frost_Clear";c.Profile.TrackerTabs=TrackerTabStyle.Dwarf;app.ApplySettings();
        foreach(var tab in TrackerNavigation.Order)
        {
            ShowView(tab);Flush();var buttons=Usability.Tree(tabs).OfType<Button>().Where(b=>b.IsVisible&&Equals(b.Tag,"tracker-settings-footer")).ToList();
            Check(buttons.Count==1,"Exactly one direct Settings footer · "+tab);
            Check(buttons[0].ContextMenu==null&&buttons[0].Content?.ToString()=="Settings","Settings opens directly without a menu · "+tab);
            Check(buttons[0].ActualHeight>=27&&buttons[0].TranslatePoint(new(),tabs).Y+buttons[0].ActualHeight>=tabs.ActualHeight-15,"Left footer uses bottom of tab workspace · "+tab);
            Capture(tab.ToString().ToLowerInvariant(),(FrameworkElement)Content,1220,800);
        }
        ShowView(TrackerTab.Pet);Flush();
        var undo=Usability.Tree(tabs).OfType<Button>().Single(b=>b.IsVisible&&b.Content?.ToString()=="Undo");var build=Usability.Tree(tabs).OfType<Button>().Single(b=>b.IsVisible&&b.Content?.ToString()=="Build ▾");
        Check(Math.Abs(undo.ActualHeight-build.ActualHeight)<.1&&Math.Abs(undo.ActualWidth-build.ActualWidth)<.1&&Math.Abs(undo.TranslatePoint(new(),tabs).Y-build.TranslatePoint(new(),tabs).Y)<.5,"Pet Build/Undo share dimensions and baseline");
        c.Profile.PetExpansions=["Kunark"]; // Recreate only the filter control under test; persisted selection is unchanged by presentation.
        var expansion=BuildPetExpansionFilter();expansion.Measure(new(250,100));expansion.Arrange(new(0,0,250,60));Check(Usability.Tree(expansion).OfType<Button>().Single().Content?.ToString()=="Kunark ▾","Single expansion is shown in full beneath its label");
        c.Profile.PetExpansions=["Classic","Kunark","Velious","Unknown"];
        var details=Usability.Tree(tabs).OfType<Expander>().First(e=>e.Header?.ToString()=="Pet Details");details.IsExpanded=true;Flush();var hit=Usability.Tree(details).OfType<System.Windows.Controls.Primitives.ToggleButton>().First();Check(hit.ActualWidth<details.ActualWidth*.5,"Pet Details header hit area remains content sized");details.IsExpanded=false;
        ShowView(TrackerTab.Exaltations);Flush();Check(Math.Abs(exaltSearch.ActualHeight-exaltType.ActualHeight)<.5,"Exaltation search matches neighboring input height");
        exaltContent.SelectedItem="All";exaltSearch.Text="Blade of the Swarm";RefreshExaltations();Flush();
        if(exaltTable.Items.OfType<ExaltationOption>().FirstOrDefault(e=>e.Effect.Contains("Swarming Pain")) is {} swarm)
        {exaltTable.SelectedItem=swarm;ShowExaltation();Check(Usability.Tree(exaltDetail).OfType<TextBlock>().Any(t=>t.Text.Contains("50 per tick")),"Effect card shows sourced Swarming Pain amount");Capture("effect-swarming",(FrameworkElement)Content,1220,800);}
        else throw new InvalidOperationException("Bundled Blade of the Swarm effect is missing from review catalog");
        exaltSearch.Clear();
        foreach(var tab in new[]{TrackerTab.Items,TrackerTab.Pet,TrackerTab.Gear,TrackerTab.Exaltations}){ShowView(tab);Capture(tab.ToString().ToLowerInvariant()+"-narrow",(FrameworkElement)Content,760,620);}
        ShowView(TrackerTab.Items);Flush();Check(breadcrumb.Margin.Left==0&&detail.Margin.Left==0,"Item routing line and card share the same left origin");
        var settings=new SettingsWindow(c){Left=-15000,Top=-15000,ShowActivated=false};settings.Show();Flush();
        var stylePicker=Usability.Tree(settings).OfType<ComboBox>().Single(b=>Equals(b.Tag,"tab-style-selector"));Check(stylePicker.SelectedItem is TabAppearanceChoice{Name:"Cut Stone - Dwarf"}&&stylePicker.Items.Count==14,"Settings has fourteen styles and exact default label");
        var themePicker=Usability.Tree(settings).OfType<ComboBox>().Single(b=>b.Items.Contains("Rune4K_Vellum_Clear"));var tonePicker=Usability.Tree(settings).OfType<ComboBox>().Single(b=>b.Items.Contains("Candlelight Cream"));
        tonePicker.SelectedItem="Parchment Ivory";themePicker.SelectedItem="Rune4K_Vellum_Clear";Flush();Check(tonePicker.SelectedItem?.ToString()=="Warm Gold","Theme switch restores its own helper-text choice");themePicker.SelectedItem="Rune4K_Frost_Clear";Flush();Check(tonePicker.SelectedItem?.ToString()=="Parchment Ivory","Frost custom helper tone survives theme round trip");tonePicker.SelectedItem="Candlelight Cream";
        ShowView(TrackerTab.Pet);stylePicker.SelectedValue=TrackerTabStyle.WoodElf;Flush();Check(Usability.Tree(tabs).OfType<TrackerTabChrome>().Any(t=>t.StyleId==TrackerTabStyle.WoodElf)&&SelectedTab==TrackerTab.Pet,"Tab style updates open Tracker without changing selection");stylePicker.SelectedValue=TrackerTabStyle.Dwarf;
        Capture("settings",(FrameworkElement)settings.Content,940,700);settings.Close();
        var menu=overlay.HeadingTitle.ContextMenu!;var entries=menu.Items.OfType<MenuItem>().ToList();int scopeIndex=menu.Items.IndexOf(entries.Single(e=>e.Header?.ToString()=="Session scope"));Check(menu.Items[scopeIndex+1] is Separator,"Divider follows Session scope");
        Check(entries.Any(e=>e.Header?.ToString()=="Filters…")&&!entries.Any(e=>e.Header?.ToString()=="Clear filters"),"Overlay menu routes to its independent Filters window");
        var appearance=entries.Single(e=>e.Header?.ToString()=="Appearance");Check(appearance.Items.OfType<MenuItem>().Any(e=>e.Header?.ToString()=="Window size")&&!entries.Any(e=>e.Header?.ToString()=="Window size"),"Window sizes are nested under Appearance");
        Check(appearance.Items[appearance.Items.Count-2] is Separator,"Window sizes have their own Appearance divider");
        var objective=app.ObjectivesOverlay;var objectiveMenu=objective.HeadingTitle.ContextMenu!;Check(objectiveMenu.Items.OfType<MenuItem>().Last().Header?.ToString()=="Help","Objective Help is the last menu action");
        foreach(var expected in new[]{"Combat Lab","Quest & Gear Tracker","Settings"})Check(entries.Any(e=>e.Header?.ToString()==expected)&&objectiveMenu.Items.OfType<MenuItem>().Any(e=>e.Header?.ToString()==expected),"Both compact menus reach "+expected);
        objective.Left=objective.Top=-15000;objective.ShowActivated=false;objective.Show();objective.OpenHelp();var help=Application.Current.Windows.OfType<ThemedUtilityWindow>().Single(w=>w.Title.Contains("Objectives help"));help.Left=help.Top=-15000;help.ShowActivated=false;
        Check(help.AllowsTransparency&&help.Opacity==1&&help.Width<=330&&!help.Resources.Contains("Surface"),"Objective Help is compact, theme transparent and keeps opaque text");Capture("objective-help",(FrameworkElement)help.Content,320,190);help.Close();
        c.Profile.OverlayMeter=new(){Actor="Combined",Scope="Session"};var filters=new OverlayFilterWindow(c,overlay){Left=-15000,Top=-15000,ShowActivated=false};filters.Show();Flush();
        Check(filters.AllowsTransparency&&filters.Opacity==1&&Usability.Tree(filters).OfType<ComboBox>().All(b=>b.Width>=160&&b.Width<=170),"Filter utility is transparent with compact 160–170 DIP dropdowns");
        var textboxes=Usability.Tree(filters).OfType<TextBox>().Where(t=>t.IsVisible).ToArray();TextBox Field(string name)=>textboxes.Single(t=>t.Parent is Grid g&&g.Children.OfType<TextBlock>().Any(l=>l.Text==name));
        string labBefore=JsonSerializer.Serialize(c.Profile.Meter);Field("SPELL / STRIKE SEARCH").Text="Slash";await Task.Delay(250);Flush();Check(c.Profile.OverlayMeter.Ability=="Slash"&&JsonSerializer.Serialize(c.Profile.Meter)==labBefore,"Live utility search affects only compact tracker");
        Field("FROM · LOCAL DATE/TIME").Text="not a date";await Task.Delay(250);Flush();Check(c.Profile.OverlayMeter.From==null&&Usability.Tree(filters).OfType<TextBlock>().Any(t=>t.Text.Contains("Previous valid dates")),"Invalid dates keep the last valid tracker date limits");
        Usability.Tree(filters).OfType<Button>().Single(b=>b.Content?.ToString()=="Clear filters").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush();Check(c.Profile.OverlayMeter.FilterCount==0&&c.Profile.OverlayMeter.Actor=="Combined"&&c.Profile.OverlayMeter.Scope=="Session"&&labBefore==JsonSerializer.Serialize(c.Profile.Meter),"Clear filters is local and retains actor/scope/sorting");
        Capture("filters",(FrameworkElement)filters.Content,402,350);filters.Close();Check(c.Profile.OverlayMeter.FilterCount==0,"Closing utility preserves applied settings");
        c.Profile.OverlayMeter=new(){Actor="Combined"};c.Profile.Meter=new(){Actor="Player",Ability="nothing-matches"};Flush();Check(c.DpsView().Damage==0&&c.OverlayDpsView().Damage>0,"Combat Lab filters do not affect compact totals");
        c.Profile.Meter.ClearFilters();var labTotals=c.DpsView().Damage;c.Profile.OverlayMeter.Ability="nothing-matches";Flush();Check(c.OverlayDpsView().Damage==0&&c.DpsView().Damage==labTotals,"Tracker filters do not affect Combat Lab totals");
        // Exercise the actual background publisher, including independent revision reuse.
        c.Profile.Meter=new(){Actor="Player"};c.Profile.OverlayMeter=new(){Actor="Combined"};c.StartProjectionValidation();
        async Task WaitFor(Func<bool> ready){var timeout=Stopwatch.StartNew();while(!ready()){if(timeout.ElapsedMilliseconds>12000)throw new InvalidOperationException("Projection publication timed out");await Task.Delay(30);}}
        await WaitFor(()=>c.Published?.OverlayCombat!=null);var baseLab=c.Published!.Combat;var baseOverlay=c.Published.OverlayCombat!;
        c.Profile.Meter.Ability="nothing-matches";await WaitFor(()=>c.Published!.Combat.Damage==0);Check(ReferenceEquals(baseOverlay,c.Published!.OverlayCombat),"Lab edit reuses the unchanged published tracker aggregate");
        var changedLab=c.Published.Combat;c.Profile.OverlayMeter.Ability="nothing-matches";await WaitFor(()=>c.Published!.OverlayCombat!.Damage==0);Check(ReferenceEquals(changedLab,c.Published!.Combat),"Tracker edit reuses the unchanged published Lab aggregate");
        c.Profile.Meter.ClearFilters();c.Profile.OverlayMeter.ClearFilters();await WaitFor(()=>c.Published!.Combat.Damage==baseLab.Damage&&c.Published.OverlayCombat!.Damage==baseOverlay.Damage);
        for(int i=0;i<60;i++){var watch=Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();overlay.Refresh();samples.Add(new{Index=i,Milliseconds=watch.Elapsed.TotalMilliseconds,Allocated=GC.GetAllocatedBytesForCurrentThread()-allocated});}
        Json.Write(Path.Combine(folder,$"refresh-samples-{DateTime.UtcNow:yyyyMMdd-HHmmss-ffff}.json"),samples);
        Check(c.Published.OverlayCombat!.Seconds==baseOverlay.Seconds,"Independent filtering leaves encounter denominator unchanged");
        var at=DateTime.Now.Date.AddDays(-2);
        Encounter Fight(string id,DateTime start,long amount)=>new(){Id=id,Start=start,End=start.AddSeconds(10),Finished=true,PlayerDamage=amount,Events=[new(){Time=start,Owner="Player",Target=id,Ability="Test strike",Kind="Melee",Outcome="Hit",Damage=amount}]};
        lock(c.Sync){c.Combat.Encounters.Clear();c.Combat.Restore([Fight("old-fight",at,111),Fight("new-fight",at.AddDays(1),222)],null);c.SetPreviewSessions(new SessionView(2,[new(){Id="old",Start=at,NextLogin=at.AddDays(1),LastObserved=at.AddMinutes(1)},new(){Id="new",Start=at.AddDays(1),LastObserved=at.AddDays(1).AddMinutes(1)}],[new("old","Old session"),new("new","New session")]){ContentRevision=2});c.Profile.Meter=new(){Actor="Combined",Scope="Session",SessionId="old"};c.Profile.OverlayMeter=new(){Actor="Combined",Scope="Session",SessionId="new"};}
        await WaitFor(()=>c.Published!.Combat.Session?.Id=="old"&&c.Published.OverlayCombat!.Session?.Id=="new");
        Check(c.Published!.Combat.Damage==111&&c.Published.OverlayCombat!.Damage==222,"Two simultaneous saved-session projections retain separate encounter sets");
        Json.Write(Path.Combine(folder,"tracker-polish-checks.json"),new{Passed=checks.Count,Checks=checks,Samples=samples,AppSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),CoreSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CharacterProfile).Assembly.Location))),CaptureGeometry=PreviewRenderer.CaptureGeometry,Scope="Consolidated affected components only; all refresh samples retained. Raster/layout scale proofs are not physical monitor-DPI or live-game acceptance."});
    }
}
