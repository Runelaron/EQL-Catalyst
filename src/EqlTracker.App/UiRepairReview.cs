using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class UiRepairReview
{
    internal static async Task Run(string folder,TrackerController controller,QuestWindow tracker,DpsWindow lab)
    {
        if(!controller.Demo)throw new InvalidOperationException("Synthetic review only");Directory.CreateDirectory(folder);
        var checks=new List<string>();var geometry=new List<object>();var timings=new List<object>();
        void Check(bool value,string name){if(!value){Json.Write(Path.Combine(folder,"partial-checks.json"),new{Checks=checks,Failed=name});throw new InvalidOperationException(name);}checks.Add(name);}
        static void Flush(FrameworkElement element){element.UpdateLayout();element.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);element.UpdateLayout();}
        controller.SeedIntegratedReview();controller.Profile.SmoothMeterMotion=false;
        var overview=new OverviewWindow(controller){Opacity=0,ShowActivated=false,Left=-15000,Top=-15000};overview.Placement!.ReviewEnabled=true;
        var bar=new WorkspaceToolbarWindow(controller,id=>id switch{"Tracker"=>tracker,"CombatLab"=>lab,_=>overview},()=>{},new(){Placement=new(){Width=620,Height=28}}){Opacity=0,ShowActivated=false};
        bar.Show();Flush(bar);Check(bar.State.Order.SequenceEqual(["Tracker","CombatLab","Overview"]),"Three workspace default order");
        Check(overview.Width==915&&overview.Height==650&&overview.MinWidth==640&&overview.MinHeight==420,"Overview independent initial and minimum dimensions");
        bar.Open("Overview");Flush(overview);overview.Refresh();await overview.Pending;Flush(overview);
        Json.Write(Path.Combine(folder,"link-geometry.json"),Usability.Tree(overview).OfType<TextBlock>().Where(t=>t.Inlines.OfType<System.Windows.Documents.Hyperlink>().Any()).Select(t=>new{Text=t.Text,t.ActualWidth,t.ActualHeight,Color=t.Foreground?.ToString(),Inlines=t.Inlines.OfType<System.Windows.Documents.Hyperlink>().Select(l=>new{Text=new System.Windows.Documents.TextRange(l.ContentStart,l.ContentEnd).Text,Color=l.Foreground?.ToString(),l.IsEnabled})}));
        Check(Usability.Tree(overview).OfType<TextBlock>().Where(t=>t.Inlines.OfType<System.Windows.Documents.Hyperlink>().Any()).All(t=>t.IsVisible&&t.ActualWidth>0&&t.ActualHeight>0),"Contextual links render and remain accessible");
        Check(overview.Snapshot?.Recap.Loot.Count>0,"Overview loads retained acquisitions without history inspector replacement");
        var plan=new PetBuild{Id="journal-draft-review",Name="Journal review",FollowDetectedSummon=false};controller.Profile.ActiveLoadout.PetBuilds.Add(plan);controller.Save();
        int journalCount=controller.JournalSnapshot().Count;PetBuildEditing.Remember(plan,"Bookkeeping");plan.LevelEvidence="Metadata refresh";controller.Save();
        Check(controller.JournalSnapshot().Count==journalCount,"Edit bookkeeping and metadata do not manufacture setup journal records");
        plan.Focus="Reviewed changed focus";controller.Save();Check(controller.JournalSnapshot().Count==journalCount+1,"Saved editable plan changes retain distinct setup evidence");
        overview.Refresh();await overview.Pending;Flush(overview);
        var prior=overview.Snapshot;var first=overview.LootRows.Children[0];string key=controller.OverviewFingerprint();
        controller.Profile.Meter.SessionId="independent-lab";controller.Profile.OverlayMeter!.SessionId="independent-overlay";overview.Refresh();await overview.Pending;
        Check(key==controller.OverviewFingerprint()&&ReferenceEquals(prior,overview.Snapshot)&&ReferenceEquals(first,overview.LootRows.Children[0]),"Unrelated meter changes retain stable Overview content");
        Check(AutomationProperties.GetName(overview.SessionPicker)=="Overview session"&&overview.SessionPicker.ActualHeight==28,"Overview session has accessible identity and 28-DIP bounds");
        var active=controller.Sessions!.Sessions.Last();var old=EquipmentPositions.Copy(active);old.Id="older-review";old.Start=active.Start.AddDays(-1);old.LastObserved=active.Start.AddHours(-1);old.NextLogin=active.Start;
        controller.SetPreviewSessions(new(2,[old,active],[new(active.Id,"10/23 10:00 · 2 fights"),new(old.Id,"10/22 10:00 · 0 fights")]));
        controller.SelectOverviewSession(old.Id);overview.Refresh();await overview.Pending;
        Check(overview.Snapshot?.Recap.Session.Id==old.Id&&controller.Profile.Meter.SessionId=="independent-lab","Historical Overview selection is independent and fixed");
        controller.SelectOverviewSession("");overview.Refresh();await overview.Pending;Check(overview.Snapshot?.Recap.Session.Id==active.Id,"Current follows latest retained session");
        foreach(var id in new[]{"Overview","CombatLab","Tracker"}){Check(bar.Open(id),"Workspace opens · "+id);Flush(bar);}
        var guard=tracker.CanHide;tracker.CanHide=()=>false;Check(!bar.Open("Overview")&&bar.State.Active=="Tracker","Dirty Tracker Cancel protects Overview switch");tracker.CanHide=guard;
        bar.Move("Overview",-1);bar.Move("Overview",-1);Flush(bar);Check(bar.State.Order[0]=="Overview"&&bar.WorkspaceButtons.Children[0]==bar.Buttons["Overview"],"All three buttons reorder with visual order");
        Check(bar.Buttons.All(p=>p.Value.ActualHeight==24&&p.Value.Cursor==Cursors.Hand),"Toolbar controls remain 24 DIP with pointer cursors");
        bar.Open("Overview");Flush(overview);var bounds=overview.SessionPicker.TransformToAncestor((Visual)overview.Content).TransformBounds(new Rect(overview.SessionPicker.RenderSize));
        overview.SessionPicker.Focus();Flush(overview);Check(bounds==overview.SessionPicker.TransformToAncestor((Visual)overview.Content).TransformBounds(new Rect(overview.SessionPicker.RenderSize)),"Keyboard focus reserves session geometry");
        var setup=controller.IntegratedSnapshot();
        setup.Bard.Songs=Enumerable.Range(1,8).Select(n=>new BardSong{Gem=n,Identity=new("fixture-"+n,n==8?"Long reviewed song name with independent rank":"Reviewed song "+n,4),Available=true,Mana=0,CooldownMs=0,NonTargetedArea=true,Keep=n==7}).ToList();controller.SaveIntegrated(setup);
        var editor=new BardSongEditor(controller,()=>{});var dialog=new ReviewDialog("Song set",600,620){Opacity=0,ShowActivated=false,Left=-15000,Top=-15000};dialog.Editor.Children.Add(editor);dialog.Actions.Children.Add(UI.Button("Close",dialog.Close));dialog.Show();Flush(dialog);
        Check(!editor.Save.IsEnabled,"Song Save starts disabled");
        var combo=editor.Gems.Children.OfType<Grid>().Skip(1).First().Children.OfType<ComboBox>().Single();combo.Focus();Flush(dialog);Check(!editor.Save.IsEnabled,"Selecting song evidence does not dirty the song set");
        var rank=editor.Gems.Children.OfType<Grid>().Skip(1).First().Children.OfType<TextBox>().Single();rank.Text="bad";Check(!editor.Save.IsEnabled,"Invalid rank disables Save");rank.Text="21";Check(editor.Save.IsEnabled,"Recorded rank above ten remains editable");rank.Text="4";Check(!editor.Save.IsEnabled,"Restored normalized values disable Save");
        var settings=new SettingsWindow(controller){Opacity=0,ShowActivated=false,Left=-15000,Top=-15000};settings.Show();settings.SelectSection(SettingsSection.Phone);Flush(settings);
        Check(!Usability.Tree(settings).OfType<Button>().Single(b=>Equals(b.Content,"Apply connection")).IsEnabled,"Phone connection action starts disabled when unchanged");
        var notebook=new CampNotebookView(controller,new(){Zone="Synthetic zone",Mob="Synthetic guardian"});var evidence=new ReviewDialog("Source evidence",600,500){Opacity=0,ShowActivated=false,Left=-15000,Top=-15000};evidence.Editor.Children.Add(notebook);evidence.Actions.Children.Add(UI.Button("Close",evidence.Close));evidence.Show();notebook.IsExpanded=true;Flush(evidence);
        var saveNotes=Usability.Tree(notebook).OfType<Button>().Single(b=>Equals(b.Content,"Save notes"));Check(!saveNotes.IsEnabled,"Camp notes Save starts disabled");var notes=Usability.Tree(notebook).OfType<TextBox>().Single(t=>t.AcceptsReturn);notes.Text="Review note";Check(saveNotes.IsEnabled,"Changed camp notes enable Save");notes.Text="";Check(!saveNotes.IsEnabled,"Restoring camp notes disables Save");
        var toggle=Usability.Tree(notebook).OfType<ToggleButton>().First();var before=toggle.RenderSize;toggle.Focus();Flush(evidence);Check(toggle.RenderSize==before&&toggle.ActualHeight==28,"Shared disclosure focus has stable 28-DIP geometry");
        foreach(var theme in ThemeCatalog.Choices.Skip(1))
        {
            controller.Profile.Theme=theme.Id;Appearance.Apply(theme.Id);
            foreach(double scale in new[]{1d,1.5,2d,3d})foreach(bool narrow in new[]{false,true})
            {
                double width=narrow?640:915,height=narrow?420:650;
                void Capture(Window window,string name,double w,double h)
                {
                    var watch=Stopwatch.StartNew();var bitmap=PreviewRenderer.Capture((FrameworkElement)window.Content,w,h,scale);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(folder,$"{theme.Id}-{name}-{(narrow?"minimum":"normal")}-{scale:0.0}x.png")))png.Save(output);
                    Check(bitmap.PixelWidth==(int)Math.Ceiling(w*scale)&&bitmap.PixelHeight==(int)Math.Ceiling(h*scale),$"Render dimensions · {theme.Id}/{name}/{width}/{scale}");timings.Add(new{Name=name,Theme=theme.Id,Width=w,Scale=scale,Milliseconds=watch.Elapsed.TotalMilliseconds});
                }
                Capture(overview,"overview",width,height);Flush(overview);overview.ArrangeSections();
                Check(Grid.GetRow(overview.Sections.Children[1])==(overview.Sections.ActualWidth>=760?0:1),$"Balanced/stacked sections · {theme.Id}/{width}/{scale}");
                Capture(dialog,"bard",narrow?460:600,narrow?420:620);Flush(dialog);
                foreach(var row in editor.Gems.Children.OfType<Grid>().Skip(1))foreach(var control in row.Children.OfType<Control>())
                {Check(Math.Abs(control.ActualHeight-28)<.1,$"Gem control height · {theme.Id}/{width}/{scale}/{control.GetType().Name}");geometry.Add(new{Component="Bard",Theme=theme.Id,Scale=scale,Viewport=width,Control=control.GetType().Name,control.ActualWidth,control.ActualHeight});}
                Capture(settings,"phone",width,height);Capture(evidence,"camp",narrow?460:600,narrow?420:500);
                foreach(var disclosure in new[]{notebook}){Flush(disclosure);var button=Usability.Tree(disclosure).OfType<ToggleButton>().First();Check(button.ActualHeight==28,$"Disclosure bounds · {theme.Id}/{width}/{scale}");}
            }
        }
        Check(!Usability.Tree(lab).OfType<Button>().Any(b=>b.Content?.ToString()?.Contains("Session recap",StringComparison.OrdinalIgnoreCase)==true),"Combat Lab contains no recap shortcut");
        Json.Write(Path.Combine(folder,"ui-repair-checks.json"),new{Checks=checks,Geometry=geometry,Timings=timings,PhysicalMultiMonitorAcceptance="Not exercised; synthetic native/scale tests are separate",LiveGameAcceptance="Not exercised"});
        settings.Close();dialog.Close();evidence.Close();bar.TryHide();controller.Profile.Theme="coldain";Appearance.Apply("coldain");
    }
    private sealed class ReviewDialog(string title,double width,double height):FrostDialog(title,width,height)
    {internal new Grid Editor=>base.Editor;internal new WrapPanel Actions=>base.Actions;}

}
