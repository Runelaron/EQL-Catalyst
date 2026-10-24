using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class Beta16Review
{
    internal static async Task Run(TrackerController c,QuestWindow tracker,DpsWindow lab,OverlayWindow overlay,string folder)
    {
        if(!c.Demo)throw new InvalidOperationException("Synthetic review only");Directory.CreateDirectory(folder);
        var checks=new List<string>();var timer=Stopwatch.StartNew();
        void Check(bool ok,string text){if(!ok){Json.Write(Path.Combine(folder,"partial-beta16-checks.json"),new{Checks=checks,Failed=text});throw new InvalidOperationException(text);}checks.Add(text);}
        void Flush(Window w){w.UpdateLayout();w.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);w.UpdateLayout();}
        void Capture(string name,Window w){Flush(w);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture((FrameworkElement)w.Content,w.ActualWidth,w.ActualHeight,1)));using var stream=File.Create(Path.Combine(folder,name+".png"));png.Save(stream);}
        if(!Environment.GetCommandLineArgs().Contains("--beta16-session-only"))await WorkspaceToolbarReview.Run(c,tracker,lab,overlay,Path.Combine(folder,"toolbar"));
        lab=new DpsWindow(c){Opacity=0,ShowActivated=false};
        c.Profile.SmoothMeterMotion=false;
        var panel=new FrostWindow(c,"Attachment review","",700,460){Opacity=0,ShowActivated=false};
        var bar=new WorkspaceToolbarWindow(c,_=>panel,()=>{},new()){Opacity=0,ShowActivated=false};bar.Show();bar.Width=460;bar.Open("Tracker");Flush(bar);Flush(panel);
        var header=Usability.Tree(bar).OfType<DockPanel>().First(p=>p.ContextMenu!=null);
        var close=Usability.Tree(bar).OfType<Button>().Single(b=>Equals(b.Tag,"toolbar-close"));var grip=bar.OuterResize!.Grip;
        Check(bar.Buttons.Values.All(b=>b.Cursor==Cursors.Hand)&&bar.SettingsButton.Cursor==Cursors.Hand&&close.Cursor==Cursors.Hand,"Workspace and action buttons have explicit pointer cursors");
        Check(grip.Cursor==Cursors.SizeWE&&grip.Width==14,"Toolbar grip retains horizontal resize cursor and a dedicated 14-DIP slot");
        var gr=grip.TransformToAncestor(bar).TransformBounds(new Rect(grip.RenderSize));var cr=close.TransformToAncestor(bar).TransformBounds(new Rect(close.RenderSize));
        Check(gr.Right+3.9<=cr.Left,"Resize hit region is separated from Close by four DIP");
        Check(close.Content is FrameworkElement x&&x.Width==16&&bar.SettingsButton.Content is ToolbarActionArtwork,"Close uses 16-DIP artwork and Settings uses original tinker-sprocket vector");
        Point pointer=Native.CursorPosition();bar.InteractionPointer=()=>pointer;
        header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});
        Check(header.IsMouseCaptured,"Routed toolbar mouse-down captures the header drag");
        foreach(var delta in new[]{new Vector(35,45),new Vector(-20,-25),new Vector(50,30),new Vector(-10,-15)})
        {
            pointer+=delta;header.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.PreviewMouseMoveEvent});Flush(bar);Flush(panel);
            var b=Native.WindowTopLeft(bar);var p=Native.WindowTopLeft(panel);var bd=VisualTreeHelper.GetDpi(bar);var pd=VisualTreeHelper.GetDpi(panel);
            Check(Math.Abs(p.Y-(b.Y+bar.ActualHeight*bd.DpiScaleY))<=1||Math.Abs(b.Y-(p.Y+panel.ActualHeight*pd.DpiScaleY))<=1,"Each routed movement/reversal keeps native panel and toolbar edges joined");
        }
        header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent});Flush(bar);
        Check(!header.IsMouseCaptured&&bar.State.Placement.ScreenLeft.HasValue,"Header release commits native toolbar placement");Capture("toolbar-controls",bar);bar.TryHide();

        string overlayBefore=PetResearch.Hash(c.Profile.OverlayMeter!),footerBefore=PetResearch.Hash(c.Profile.OverlayFooterDisplay!);
        var at=new DateTime(2026,10,23,10,0,0);
        Encounter Fight(string id,DateTime time,string zone,string target,long damage,int seconds)=>new(){Id=id,Start=time,End=time.AddSeconds(seconds),Finished=true,Zone=zone,PetDamage=damage,PetType="Air",HealingCoverage="Complete",Events=[new(){Id=id+":1",Time=time.AddSeconds(1),Owner="Pet",Target=target,Damage=damage/2,Ability="hits",Kind="Melee",Zone=zone},new(){Id=id+":2",Time=time.AddSeconds(seconds-1),Owner="Pet",Target=target,Damage=damage-damage/2,Ability="hits",Kind="Melee",Zone=zone}],Heals=[new(){Id=id+":heal",Time=time.AddSeconds(2),Effective=100,Healer="ExampleHero",HealerOwner="Player",Recipient="Windling",RecipientOwner="Pet",Ability="Example Heal",Zone=zone,EncounterId=id}]};
        var old=Fight("old",at.AddDays(-1),"Castle","Named Ghost",20,10);var a=Fight("ordinary",at,"Castle","a ghost",100,10);var b2=Fight("named",at.AddMinutes(1),"Castle","Named Ghost",200,20);var other=Fight("elsewhere",at.AddMinutes(2),"Lake","Named Ghost",300,30);
        c.Combat.Encounters.Clear();c.Combat.Restore([old,a,b2,other],null);c.Profile.Meter=new(){Actor="Combined",Scope="Session"};c.Profile.Healing=new(){Actor="Combined"};
        c.SetPreviewSessions(new(1,[new(){Id="older",Start=old.Start,NextLogin=at,LastObserved=old.End},new(){Id="recent",Start=at,LastObserved=other.End}],[new("recent","10/23 10:00 · 3 fights"),new("older","10/22 10:00 · 1 fights")]){ContentRevision=1});
        c.SetReviewNamedNpcs(new(){Records=[new(){Name="Named Ghost",Zone="Castle",Url="https://eqlwiki.com/Named_Ghost",Revision=1,ClassificationEvidence="Reviewed named-NPC fixture"}]});
        c.SetLabSession("");c.SetLabFilters("",false);lab.Width=1000;lab.Height=740;lab.Show();lab.ShowView(CombatLabMode.Damage,CombatLabTab.Breakdown);Flush(lab);
        string retained=PetResearch.Hash(c.Combat.Encounters);var view=c.DpsView();Check(view.Damage==600&&view.Seconds==60&&view.Fights.Count==3,"Current selects the latest session and retains existing summed-fight duration");
        Check(!Usability.Tree(lab).OfType<ComboBox>().Any(box=>box.IsVisible&&box.Items.Contains("Fight")),"Time scope and Fight scope are absent from Combat Lab");
        Check(!Usability.Tree(lab).OfType<Button>().Any(button=>button.IsVisible&&button.Content?.ToString()?.StartsWith("Classes:")==true),"Retired class filter is absent");
        var session=Usability.Tree(lab).OfType<ComboBox>().Single(box=>System.Windows.Automation.AutomationProperties.GetName(box)=="Session");Check(session.IsVisible&&session.Width==130&&session.Items.Cast<SessionChoice>().First().Label.StartsWith("Current"),"Session selector is always visible, compact, and led by Current");
        Check(session.Items.Cast<SessionChoice>().All(s=>!s.Label.Contains("last observed")&&!s.Label.Contains("live")),"Session entries contain no ending-status text");
        session.IsDropDownOpen=true;Flush(lab);var popup=(Popup)session.Template.FindName("PART_Popup",session);Check(popup.IsOpen&&((FrameworkElement)popup.Child).ActualWidth>=session.ActualWidth,"Session dropdown can expand beyond its compact field");Check(session.ToolTip?.ToString()?.Contains("3 fights")==true,"Closed session field retains its complete count/date tooltip");session.IsDropDownOpen=false;
        c.SetLabFilters("Castle",false);Check(c.DpsView().Damage==300&&c.DpsView().Seconds==30,"Zone filters complete encounters before existing aggregation");
        c.SetLabFilters("Castle",true);view=c.DpsView();Check(view.Damage==200&&view.Seconds==20&&view.Fights.Single().Id=="named","Names only uses explicit zone-qualified NPC evidence");
        Check(c.BuildHealing(view).Effective==100&&c.BuildHealing(view).Seconds==20,"Healing uses the same named/zone encounter selection");Check(c.LabExportView().Damage==view.Damage,"Export projection honors the same session and encounter filters");
        c.Profile.Meter.From=b2.Start.AddSeconds(10);c.Profile.Meter.Until=b2.End;view=c.DpsView();Check(view.Damage==100&&view.Seconds==10,"Date range clips events and duration within the selected session");
        c.Profile.Meter.Scope="History";c.Profile.Meter.EncounterId=b2.Id;c.SetLabSession("older");c.SetLabFilters("",false);
        Check(c.Profile.Meter.Scope=="Session"&&c.Profile.Meter.EncounterId==""&&c.Profile.Meter.From==null&&c.Profile.Meter.Until==null&&c.DpsView().Damage==20,"Changing session clears pins/dates and leaves historical selection fixed");
        c.SetLabSession("");var newest=Fight("newest",at.AddDays(1),"Castle","Named Ghost",40,10);c.Combat.Encounters.Add(newest);
        c.SetPreviewSessions(new(2,[new(){Id="older",Start=old.Start,NextLogin=at,LastObserved=old.End},new(){Id="recent",Start=at,NextLogin=newest.Start,LastObserved=other.End},new(){Id="new",Start=newest.Start,LastObserved=newest.End}],[new("new","10/24 10:00 · 1 fights"),new("recent","10/23 10:00 · 3 fights"),new("older","10/22 10:00 · 1 fights")]){ContentRevision=2});
        Check(c.DpsView().Damage==40,"Current follows a newly retained session without choosing it manually");c.SetLabSession("older");Check(c.DpsView().Damage==20,"Explicit historical choice remains fixed after a newer session arrives");
        c.SetLabSession("");lab.Refresh();Flush(lab);Check(overlayBefore==PetResearch.Hash(c.Profile.OverlayMeter!)&&footerBefore==PetResearch.Hash(c.Profile.OverlayFooterDisplay!),"Combat Lab browsing leaves both DPS overlay selectors independent");
        Check(PetResearch.Hash(new[]{old,a,b2,other})==retained,"Browsing leaves damage, healing and encounter records unchanged");
        foreach(double width in new[]{1000d,640d})
        {
            lab.Width=width;lab.Height=600;Flush(lab);var toggle=Usability.Tree(lab).OfType<ToggleButton>().Single(t=>t.Content?.ToString()=="Filters");var before=toggle.TransformToAncestor(lab).TransformBounds(new Rect(toggle.RenderSize));
            var peer=new ToggleButtonAutomationPeer(toggle);((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!).Toggle();Keyboard.Focus(toggle);Flush(lab);var after=toggle.TransformToAncestor(lab).TransformBounds(new Rect(toggle.RenderSize));
            Check(before==after&&toggle.ActualHeight==28,"Filters retain bounds through first activation and keyboard focus at width "+width);
            foreach(var mode in new[]{CombatLabMode.Damage,CombatLabMode.Healing}){lab.ShowView(mode,CombatLabTab.Breakdown);Capture("session-"+mode.ToString().ToLowerInvariant()+"-"+width,lab);Check(session.IsVisible,"Session stays visible in "+mode+" at width "+width);}
        }
        var settings=new SettingsWindow(c){ShowActivated=false,Opacity=0};settings.Show();Flush(settings);settings.UndeadChoice.SelectedValue="wraith-a";string undead=c.UndeadArtwork;
        settings.FireChoice.SelectedValue="wizard-crystal";Flush(settings);Check(c.FireArtwork=="wizard-crystal"&&c.UndeadArtwork==undead&&IconArtwork.ActorRole("Pet","Fire")=="pet-fire-crystal","Fire crystal applies immediately without resetting Undead appearance");
        settings.UndeadChoice.SelectedValue="wraith-b";Flush(settings);Check(c.FireArtwork=="wizard-crystal"&&IconArtwork.ActorRole("Pet","Undead")=="pet-undead-wraith-b","Changing Undead preserves the selected Fire artwork");
        settings.FireChoice.SelectedValue="wizard-staff";Flush(settings);Check(IconArtwork.ActorRole("Pet","Fire")=="pet-fire","Fire staff is the selectable default");
        Capture("fire-settings",settings);settings.Width=640;settings.Height=420;Capture("fire-settings-minimum",settings);settings.Hide();
        using(var store=new TrackerStore(Path.Combine(folder,"appearance-persistence.db"))){new PetArtworkPreferences{Undead="wraith-b",Fire="wizard-crystal"}.Write(store);var read=PetArtworkPreferences.Read(store);Check(read.Undead=="wraith-b"&&read.Fire=="wizard-crystal","Both independent appearance choices survive storage/restart reads");}
        Check(PetArtworkPreferences.ResolveFire(null)=="wizard-staff"&&PetArtworkPreferences.ResolveFire("unsupported")=="wizard-staff","Missing and unsupported Fire values fall back to staff");
        Check(ApprovedPetArtwork.Detailed(32,IconArtworkStyle.Vibrant)&&!ApprovedPetArtwork.Detailed(24,IconArtworkStyle.Vibrant)&&!ApprovedPetArtwork.Detailed(64,IconArtworkStyle.Clean),"Clean/Vibrant detail remains selected by DIP size");
        Check(IconArtwork.Painted("pet-fire",192)?.PixelWidth>=192&&IconArtwork.Painted("pet-fire-crystal",192)?.PixelWidth>=192,"Both approved Fire paintings decode for high-DPI large controls");
        c.SetLabSession("");c.SetLabFilters("Castle",true);c.HealingVisible=true;c.StartProjectionValidation();
        async Task Until(Func<bool> ready){var watch=Stopwatch.StartNew();while(!ready()&&watch.ElapsedMilliseconds<8000)await Task.Delay(25);Check(ready(),"Revisioned damage/healing snapshots reach the latest browsing intent");}
        await Until(()=>c.Published?.Combat.Damage==40&&c.Published.Healing?.Effective==100);
        string combatBefore=PetResearch.Hash(c.Combat.Encounters);
        for(int i=0;i<40;i++)c.SetLabFilters(i%2==0?"Lake":"Castle",i%3==0);
        c.SetLabFilters("Castle",true);await Until(()=>c.Published?.Combat.Damage==40&&c.Published.Healing?.Effective==100);
        var completed=c.Published!.Combat;c.SetLabFilters("Castle",true,!c.LabBrowsing.FiltersExpanded);await Task.Delay(400);
        Check(ReferenceEquals(completed,c.Published?.Combat),"Disclosure-only changes do not rebuild completed combat results");
        Check(combatBefore==PetResearch.Hash(c.Combat.Encounters),"Rapid background projections preserve all retained combat records");
        Json.Write(Path.Combine(folder,"beta16-checks.json"),new{Passed=checks.Count,Checks=checks,Milliseconds=timer.Elapsed.TotalMilliseconds,PrivateEvidenceUsed=false,NativeToolbarMovement=true,SessionFilterSemantics="Documented named-NPC encounters; complete mixed fights retained"});
    }
}
