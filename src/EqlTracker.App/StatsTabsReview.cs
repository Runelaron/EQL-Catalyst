using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class DpsWindow
{
    internal void ReviewStatsTabs(string folder,QuestWindow tracker)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Isolated review required");
        Directory.CreateDirectory(Path.Combine(folder,"qa"));var checks=new List<string>();var samples=new List<object>();var c=Controller;var app=(App)Application.Current;
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        void Flush(){Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        IEnumerable<GeometryDrawing> Shapes(Drawing drawing) { if(drawing is GeometryDrawing shape)yield return shape;if(drawing is DrawingGroup group)foreach(var child in group.Children)foreach(var item in Shapes(child))yield return item; }
        void Capture(string name,FrameworkElement content,double width,double height)
        {
            foreach(double scale in new[]{1d,1.5,2,3})
            {
                var bitmap=PreviewRenderer.Capture(content,width,height,scale);
                Check(bitmap.PixelWidth==(int)Math.Ceiling(width*scale)&&bitmap.PixelHeight==(int)Math.Ceiling(height*scale),$"Pixel bounds · {name} · {scale:0.0}x");
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(folder,"qa",$"stats-tabs-{name}-{scale:0.0}x.png"));encoder.Save(file);
            }
        }
        c.Profile.SmoothMeterMotion=false;
        foreach(var window in new Window[]{this,tracker}) { window.Left=window.Top=-15000;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show(); }
        var fights=c.Combat.Encounters.OrderBy(e=>e.Start).ToArray();var fight=fights.Last();
        c.SetPreviewSessions(new(1,[new(){Id="stats-review-session",Start=fights.First().Start,LastObserved=fight.End}], [new("stats-review-session","Saved review session")]){ContentRevision=1});
        var beforeOverlay=JsonSerializer.Serialize(c.Profile.OverlayMeter);
        foreach(string theme in new[]{"Rune4K_Frost_Clear","Rune4K_Vellum_Clear"})
        {
            string suffix=theme.Contains("Vellum")?"vellum":"frost";c.Profile.Theme=theme;app.ApplySettings();
            c.Profile.Meter=new(){Scope="History",EncounterId=fight.Id,Actor="Combined"};ShowView(CombatLabMode.Damage,CombatLabTab.Breakdown);Refresh();Flush();
            var view=c.DpsView();
            Check(damageStats.Fields.Count==6&&damageStats.Fields.All(t=>t.Foreground==UI.Brush("Melee")),"Four gold Damage statistics · "+suffix);
            Check(damageStats.Fields[0].Text.EndsWith(view.Dps.ToString("N1"))&&damageStats.Fields[3].Text.EndsWith(view.Damage.ToString("N0")),"Exact filtered snapshot totals/rate · "+suffix);
            Check(damageStats.ToolTip?.ToString()?.Contains("Pinned fight")==true&&damageStats.ToolTip.ToString()!.Contains(fight.Zone),"Pinned fight and zone retained in tooltip · "+suffix);
            Check(!Usability.Tree(damageStats).OfType<TextBlock>().Any(t=>t.Text.Contains("Pinned fight")),"No visible context row · "+suffix);
            Check(damageStats.Fields[1].ToolTip!.ToString()!.Contains("not individual mobs or kills"),"Target count semantics explained · "+suffix);
            Capture("damage-"+suffix,(FrameworkElement)Content,1170,740);
            ShowView(CombatLabMode.Healing,CombatLabTab.Breakdown);Refresh();Flush();
            var heals=c.HealingView();healingStats.SetHealing(heals,healing.StatusLine.Text);
            Check(healingStats.Fields[0].Text.StartsWith("Average HPS")&&healingStats.Fields[3].Text.StartsWith("Total healing"),"Healing labels follow mode · "+suffix);
            Capture("healing-"+suffix,(FrameworkElement)Content,1170,740);
            var sheet=new StackPanel();
            foreach(var choice in TrackerPresentation.Tabs)
            {
                sheet.Children.Add(UI.Text(choice.Name,13,"SecondaryText",true));
                var row=new TabControl();TrackerTabChrome.Install(row);row.Resources["TrackerTabStyle"]=choice.Id;
                foreach(var name in new[]{"Queue","Items","Quests","Progress","Gear","Exaltations","Pet"})row.Items.Add(new TabItem{Header=name});row.SelectedIndex=4;sheet.Children.Add(row);
            }
            sheet.Measure(new(1100,1050));sheet.Arrange(new(0,0,1100,1050));sheet.UpdateLayout();
            foreach(var chrome in Usability.Tree(sheet).OfType<TrackerTabChrome>())
            {
                var item=(TabItem)chrome.TemplatedParent;chrome.Hovered=Equals(item.Header,"Items");chrome.Focused=Equals(item.Header,"Quests");
                var clip=LayoutInformation.GetLayoutClip(item);var bounds=chrome.OutlineBounds;
                Check(item.Margin==new Thickness(0)&&(clip==null||clip.Bounds.Width>=chrome.ActualWidth-.01),$"Complete tab layout bounds · {chrome.StyleId} · {item.Header} · {suffix}");
                Check(bounds.Left>=.5&&bounds.Right+.5<chrome.ActualWidth&&bounds.Top>=.5&&bounds.Bottom+.5<chrome.ActualHeight,$"Stroke and gaps inside layout · {chrome.StyleId} · {item.Header} · {suffix}");
                Check(chrome.ShowInsideMarks==(chrome.Hovered||chrome.Focused),$"Hover/keyboard marks without selection trigger · {chrome.StyleId} · {item.Header} · {suffix}");
            }
            Capture("all-styles-"+suffix,sheet,1100,1050);
            foreach(var chrome in Usability.Tree(sheet).OfType<TrackerTabChrome>())
            {
                var drawing=VisualTreeHelper.GetDrawing(chrome);var marks=Shapes(drawing).Where(d=>d.Pen?.Thickness==1.4).ToArray();
                Check(marks.Length==(chrome.ShowInsideMarks?6:0),$"Exactly three internal marks per edge · {chrome.StyleId} · {((TabItem)chrome.TemplatedParent).Header} · {suffix}");
                Check(marks.All(m=>new Rect(0,0,chrome.OutlineBounds.Width,chrome.OutlineBounds.Height).Contains(m.Geometry.GetRenderBounds(m.Pen))),$"All interaction marks inside tab · {chrome.StyleId} · {((TabItem)chrome.TemplatedParent).Header} · {suffix}");
            }
            var narrow=new TabControl();TrackerTabChrome.Install(narrow);narrow.Resources["TrackerTabStyle"]=TrackerTabStyle.Dwarf;
            foreach(var name in new[]{"Queue","Items","Quests","Progress","Gear","Exaltations","Pet"})narrow.Items.Add(new TabItem{Header=name});narrow.SelectedIndex=4;
            Capture("wrapped-tabs-"+suffix,narrow,360,130);
            Check(Usability.Tree(narrow).OfType<TabItem>().Select(i=>LayoutInformation.GetLayoutSlot(i).Y).Distinct().Count()>1,"Narrow header wraps complete tabs · "+suffix);
            Check(narrow.SelectedIndex==4,"Wrapping preserves selection · "+suffix);
        }
        c.Profile.Theme="Rune4K_Frost_Clear";app.ApplySettings();c.Profile.Meter=new(){Scope="Session",SessionId="stats-review-session",Actor="Combined"};ShowView(CombatLabMode.Damage,CombatLabTab.Breakdown);Refresh();
        Check(damageStats.ToolTip!.ToString()!.Contains("Session"),"Saved session context remains in tooltip");
        var sessionView=c.DpsView();var filtered=DpsViews.Build(fights,null,DateTime.MinValue,new(){Scope="Session",Actor="Combined",Ability="slash"});damageStats.SetDamage(filtered,"Filtered");
        Check(damageStats.Fields[3].Text.EndsWith(filtered.Damage.ToString("N0")),"Filtered stats use existing damage arithmetic");
        var summary=CombatSnapshots.Copy(fight);summary.DetailAvailable=false;summary.Events.Clear();
        var summaryView=DpsViews.Build([summary],null,DateTime.MinValue,new(){Scope="History",EncounterId=summary.Id,Actor="Combined"});damageStats.SetDamage(summaryView,"Summary only");
        Check(damageStats.Fields[1].Text.EndsWith("Unavailable")&&damageStats.Fields[3].Text.EndsWith(summaryView.Damage.ToString("N0")),"Summary-only retains known totals without inventing target count");
        var unknown=DpsViews.Build([summary],null,DateTime.MinValue,new(){Scope="History",EncounterId=summary.Id,Actor="Combined",Ability="slash"});damageStats.SetDamage(unknown,"Details unavailable");
        Check(new[]{1,3,4,5}.All(i=>damageStats.Fields[i].Text.EndsWith("Unavailable")),"Summary-only content filters do not present missing details as zero");
        var empty=DpsViews.Build([],null,DateTime.MinValue,new());damageStats.SetDamage(empty,"No retained fight");Check(damageStats.Fields[2].Text.EndsWith("0:00")&&damageStats.Fields[1].Text.EndsWith("0"),"Empty retained selection remains explicit zero");
        var healingView=c.HealingView();healingStats.SetHealing(healingView with { TotalsAvailable=false,DetailComplete=false },"Unknown coverage");Check(new[]{0,1,3,4,5}.All(i=>healingStats.Fields[i].Text.EndsWith("Unavailable")),"Unavailable healing stays unavailable");
        c.Profile.Meter=new(){Scope="Fight",Actor="Combined"};ShowView(CombatLabMode.Damage,CombatLabTab.Breakdown);Refresh();Flush();Check(damageStats.ToolTip!.ToString()!.Contains("Latest fight"),"Latest fight remains identifiable in tooltip");
        Capture("damage-narrow",(FrameworkElement)Content,640,540);
        tracker.ShowView(TrackerTab.Items);Capture("tracker",(FrameworkElement)tracker.Content,1220,800);
        Check(beforeOverlay==JsonSerializer.Serialize(c.Profile.OverlayMeter),"Stats and tabs do not change independent overlay selection");
        damageStats.SetDamage(sessionView,"Measured immutable session fixture");
        for(int run=1;run<=3;run++)for(int i=0;i<100;i++){long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();damageStats.SetDamage(sessionView,"Measured immutable session fixture");samples.Add(new{Run=run,Index=i,Milliseconds=watch.Elapsed.TotalMilliseconds,Allocated=GC.GetAllocatedBytesForCurrentThread()-allocated});}
        Json.Write(Path.Combine(folder,"stats-tabs-checks.json"),new{Checks=checks,Count=checks.Count,Samples=samples,Geometry=PreviewRenderer.CaptureGeometry,AppSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),CoreSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CharacterProfile).Assembly.Location)))});
    }
}
