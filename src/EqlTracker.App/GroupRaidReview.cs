using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class DpsWindow
{
    internal async Task ReviewGroupRaid(string folder,OverlayWindow overlay,OverlayWindow objective)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Isolated preview required");
        Directory.CreateDirectory(Path.Combine(folder,"qa"));var checks=new List<string>();var performance=new List<object>();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        void Flush(){Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        void Capture(string name,FrameworkElement content,double width,double height)
        {foreach(double scale in new[]{1d,1.5,2,3}){var encoder=new PngBitmapEncoder();var bitmap=PreviewRenderer.Capture(content,width,height,scale);Check(bitmap.PixelWidth==(int)Math.Ceiling(width*scale)&&bitmap.PixelHeight==(int)Math.Ceiling(height*scale),$"Actual component pixel bounds {name} {scale:0.0}x");encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(folder,"qa",$"group-raid-{name}-{scale:0.0}x.png"));encoder.Save(file);}}
        var c=Controller;var app=(App)Application.Current;c.Profile.SmoothMeterMotion=false;
        var fights=c.Combat.Encounters.OrderBy(e=>e.Start).ToArray();var first=fights.First();var last=fights.Last();var end=last.End;
        var period=new GroupPeriod{Id="review-party",Start=first.Start,LastObserved=end,End=end,SessionId="review-session",Members=[new(){Name="Aeloria",From=first.Start,Until=end,Source="Logged join"},new(){Name="Thurgan",From=first.Start,Until=end,Source="Logged join"},new(){Name="Forestwolf",Owner="Aeloria",IsPet=true,From=first.Start,Until=end,Source="Manual"},new(){Name="Quiet member",From=first.Start,Until=end,Source="Logged join"}],RaidEvidence=[new("raid-proof",first.Start,"Verified raid-only system message")]};
        foreach(var fight in fights)
        {
            for(int i=0;i<12;i++)foreach(var actor in new[]{"Aeloria","Thurgan","Forestwolf"})fight.Events.Add(new(){Id=$"group-review:{fight.Id}:{actor}:{i}",Time=fight.Start.AddSeconds(Math.Min(fight.Duration,i)),Actor=actor,Owner="Group",ActivityOnly=true,Target=fight.Targets.Length>0?fight.Targets:"a frost giant",Kind=i%3==0?"Spell":"Melee",Ability=i%3==0?"Forest flame "+i:"Slash "+i,Damage=(actor=="Forestwolf"?10:60)*(12-i),Outcome="Hit"});
            fight.Heals.Add(new(){Id="group-heal:"+fight.Id,Time=fight.Start,Healer="Aeloria",HealerOwner="Group",Recipient=c.Profile.Character,Effective=300,Ability="Healing Light"});
        }
        c.SetPreviewSessions(new(1,[new(){Id="review-session",Start=first.Start,LastObserved=end,Members=period.Members}], [new("review-session","Review saved session")]){ContentRevision=1});
        c.SetGroupPeriodsForValidation([new(){Id="older-party",Start=first.Start.AddDays(-1),LastObserved=first.Start.AddDays(-1).AddMinutes(20),End=first.Start.AddDays(-1).AddMinutes(20),Members=[new(){Name="Older member",From=first.Start.AddDays(-1),Source="Logged join"}]},period]);
        c.Profile.Meter=new(){Actor="Combined",Scope="Session",SessionId="review-session"};c.Profile.OverlayMeter=new(){Actor="Pet",Scope="Session",SessionId="review-session",DisplayMode=OverlayCombatMode.Group};
        c.StartProjectionValidation();
        async Task Wait(Func<bool> ready){var watch=Stopwatch.StartNew();while(!ready()){if(watch.ElapsedMilliseconds>15000)throw new InvalidOperationException("Group snapshot timeout");await Task.Delay(30);}}
        await Wait(()=>c.Published?.OverlayGroup!=null);
        foreach(var window in new Window[]{this,overlay,objective}){window.Left=window.Top=-15000;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();}
        ShowView(CombatLabMode.Damage,CombatLabTab.Group);Refresh();Flush();
        Check(tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,CombatLabTab.Group)).Header?.ToString()=="GROUP/RAID","Group caption changes without changing tab identity");
        Check(c.Published!.OverlayGroup!.Members.Any(m=>m.Name=="Aeloria"&&m.PetTotal>0)&&!c.Published.OverlayGroup.Members.Any(m=>m.Name=="Forestwolf"),"Owner and verified pet share one character row");
        Check(c.Published.OverlayGroup.Members.Single(m=>m.Name=="Quiet member").Total==null,"Roster-only member remains unavailable rather than zero");
        Check(c.Published.OverlayGroup.Members.Where(m=>m.Total.HasValue).Sum(m=>m.Share??0)>99.99,"Folded member percentages reconcile");
        var selectionBefore=JsonSerializer.Serialize(c.Profile.OverlayMeter);FollowGroup(period.Id);await Wait(()=>c.Published!.Combat.Filter.From==period.Start);Refresh();Flush();
        Check(c.Profile.Meter.GroupPeriodId==period.Id&&c.Profile.Meter.Scope=="Session"&&c.Profile.Meter.From==period.Start&&c.Profile.Meter.Until==period.End,"Recent group follows its saved session and complete period");
        Check(selectionBefore==JsonSerializer.Serialize(c.Profile.OverlayMeter),"Selecting a Lab group leaves overlay selection unchanged");
        Check(latestFight.IsVisible&&latestFight.Parent is FrameworkElement parent&&parent.Parent is WrapPanel,"Return to latest lives in the top action row");
        Check(!Usability.Tree((DependencyObject)Content).OfType<TextBlock>().Any(t=>t.Text.StartsWith("Personal totals stay separate")),"Redundant personal-totals footer removed");
        Check(!Usability.Tree((DependencyObject)Content).OfType<TextBlock>().Any(t=>t.IsVisible&&t.Text.StartsWith("Observed group results")),"Verbose observed-group description removed");
        Check(summaryBadges.Where(b=>Equals(b.Tag,"Pet")).All(b=>PetElementDetector.Elements.Contains(b.Element)),"Pet badge receives an element rather than tooltip text");
        Check(Usability.Tree((DependencyObject)Content).OfType<MaxHitGlyph>().Count()==1,"Distinct Max hit glyph is present");
        foreach(string theme in new[]{"Rune4K_Frost_Clear","Vellum"})
        {
            c.Profile.Theme=theme;app.ApplySettings();Refresh();Flush();string themeId=theme=="Vellum"?"vellum":"frost";
            Capture("lab-group-"+themeId,(FrameworkElement)Content,1170,740);
            var cards=Usability.Tree(damageMetrics).OfType<CombatSummaryCards>().Single();Check(cards.Children.Count==5&&cards.Children.OfType<FrameworkElement>().Sum(child=>child.Width+child.Margin.Left+child.Margin.Right)<=cards.ActualWidth+1,"Five summary cards share one wide row "+themeId);
            Capture("lab-group-narrow-"+themeId,(FrameworkElement)Content,730,650);
            ShowView(CombatLabMode.Healing,CombatLabTab.Group);Refresh();Flush();Capture("lab-healing-"+themeId,(FrameworkElement)Content,1170,740);
            ShowView(CombatLabMode.Damage,CombatLabTab.Group);overlay.Refresh();Capture("overlay-group-"+themeId,(FrameworkElement)overlay.Content,440,410);
        }
        c.Profile.Theme="Rune4K_Frost_Clear";app.ApplySettings();
        foreach(var density in Enum.GetValues<OverlayMenuSize>())
        {
            c.Profile.OverlayMenus=density;OverlayMenuDensity.Update(c.Profile);
            foreach(var (window,name) in new[]{(overlay,"damage"),(objective,"objective")})
            {
                var menu=Usability.Tree((DependencyObject)window.Content).OfType<Button>().Select(b=>b.ContextMenu).First(m=>m!=null)!;
                Check(Math.Abs(menu.FontSize-(density==OverlayMenuSize.Compact?11:13))<.1,$"{name} menu uses {density} text size");
                var bars=(Style)menu.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)];Check(bars.Setters.OfType<Setter>().Any(s=>s.Property==WidthProperty&&Equals(s.Value,density==OverlayMenuSize.Compact?5d:10d)),name+" menu scrollbar scope "+density);
                // Render the actual MenuItem controls without opening a native popup.
                menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));var items=menu.Items.OfType<Control>().ToArray();menu.Items.Clear();var host=new StackPanel{Background=UI.Brush("SolidInnerSurface"),Width=255};host.Resources=menu.Resources;
                foreach(var item in items)host.Children.Add(item);Capture("menu-"+name+"-"+density.ToString().ToLowerInvariant(),host,255,520);host.Children.Clear();foreach(var item in items)menu.Items.Add(item);
            }
        }
        c.Profile.OverlayMenus=OverlayMenuSize.Compact;OverlayMenuDensity.Update(c.Profile);
        var settingsWindow=new SettingsWindow(c){Left=-15000,Top=-15000,ShowActivated=false,ShowInTaskbar=false};settingsWindow.Show();
        Check(Usability.Tree((DependencyObject)settingsWindow.Content).OfType<TextBlock>().Any(t=>t.Text.Equals("Overlay menu size",StringComparison.OrdinalIgnoreCase)),"Appearance exposes overlay menu size");Capture("settings-appearance",(FrameworkElement)settingsWindow.Content,800,720);settingsWindow.Close();
        var styleGrid=new StackPanel();foreach(var style in Enum.GetValues<IconArtworkStyle>()){c.Profile.IconStyle=style;IconArtwork.Apply(c.Profile);var row=UI.Row(UI.Label(style.ToString()));foreach(int size in new[]{16,24,32,48,64})row.Children.Add(new MaxHitGlyph{PreviewStyle=style,Width=size,Height=size,Margin=new(7)});styleGrid.Children.Add(row);}Capture("max-hit-both-styles",styleGrid,600,190);c.Profile.IconStyle=IconArtworkStyle.Vibrant;IconArtwork.Apply(c.Profile);
        var original=c.Profile.Meter.Copy();latestFight.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(c.Profile.Meter.GroupPeriodId==""&&c.Profile.Meter.Scope=="Fight"&&c.Profile.Meter.From==null,"Return to latest clears period bounds and follows latest fight");
        c.Profile.Meter=original;Refresh();
        // Three warmed runs use the same immutable retained fixture and preserve every sample.
        for(int run=1;run<=3;run++)
        {
            overlay.Refresh();Flush();for(int i=0;i<100;i++){long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();overlay.Refresh();performance.Add(new{Run=run,Index=i,Milliseconds=watch.Elapsed.TotalMilliseconds,Allocated=GC.GetAllocatedBytesForCurrentThread()-allocated});}await Task.Delay(220);
        }
        Json.Write(Path.Combine(folder,"group-raid-checks.json"),new{Checks=checks,Count=checks.Count,Performance=performance,Diagnostics=UpdateDiagnostics.Drain(),Geometry=PreviewRenderer.CaptureGeometry,AppSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),CoreSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CharacterProfile).Assembly.Location))),Coverage="Actual WPF components at 100/150/200/300%; synthetic retained group fixture. Native browser popup automation, physical monitor DPI and live-game input remain pending."});
    }
}

