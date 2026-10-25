using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class OverviewWindow : FrostWindow
{
    internal readonly ComboBox SessionPicker=UI.Select([],"",130);
    internal readonly Grid Sections=new();
    internal readonly StackPanel LootRows=new(),ProgressRows=new();
    internal readonly TextBlock Status=UI.Text("Reading retained session…",11,"SecondaryText");
    private readonly TextBlock fights=UI.Text("—",16,"Melee",true),combat=UI.Text("—",16,"Melee",true),self=UI.Text("—",16,"Foreground",true),pet=UI.Text("—",16,"Foreground",true),healing=UI.Text("—",16,"Foreground",true),coin=UI.Text("Unknown",14),sales=UI.Text("Unknown",14);
    private readonly TextBlock elapsed=UI.Text("",11,"SecondaryText"),conditions=UI.Text("",11,"SecondaryText"),setup=UI.Text("",11),lootEvidence=UI.Text("",11),progressEvidence=UI.Text("",11);
    private readonly Border loot,progress;
    private readonly ScrollViewer scroll;
    private readonly DispatcherTimer refresh=new(){Interval=TimeSpan.FromMilliseconds(300)};
    private CancellationTokenSource? work;
    private string requested="",completed="",choicesKey="",lootKey="",progressKey="";
    private bool updating,closed;
    private long generation;
    internal SessionOverview? Snapshot {get;private set;}
    internal Task Pending {get;private set;}=Task.CompletedTask;
    internal OverviewWindow(TrackerController controller):base(controller,"Overview","",915,650,surface:WindowSurface.Theme)
    {
        CatalystHeading.Apply(HeadingTitle,"Overview");
        AutomationProperties.SetName(SessionPicker,"Overview session");SessionPicker.Height=28;
        SessionPicker.ToolTip="Current follows the latest retained session. This choice is independent of Combat Lab, overlays and phone context.";
        SessionPicker.SelectionChanged+=(_,_)=>{if(!updating&&SessionPicker.SelectedItem is SessionChoice choice){Controller.SelectOverviewSession(choice.Id);Refresh();}};
        var top=new WrapPanel();top.Children.Add(UI.Field("Session",SessionPicker));
        var analysis=Link("Combat details",()=>HandoffLab());analysis.Margin=new(8,19,0,0);analysis.VerticalAlignment=VerticalAlignment.Center;top.Children.Add(analysis);
        var summary=new WrapPanel{Margin=new(0,4,0,8)};
        foreach(var entry in new[]{("Fights",fights),("Combat time",combat),("Self · damage / DPS",self),("Pet · damage / DPS",pet),("Healing done",healing),("Coin receipts",coin),("Merchant income",sales)})
        {var card=UI.Card(UI.Stack(UI.Label(entry.Item1),entry.Item2),new(0,0,4,4));card.MinWidth=106;card.Padding=new(6,3,6,3);summary.Children.Add(card);}
        summary.ToolTip="DPS uses summed encounter combat duration and the existing rate denominator. Elapsed session time includes time outside combat. Healing is recorded Self/Pet effective healing during fights. Coin receipts and merchant sales are independent observed income.";
        loot=Section("Loot",LootRows,lootEvidence);progress=Section("Quest progress",ProgressRows,progressEvidence);
        Sections.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});Sections.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        Sections.RowDefinitions.Add(new(){Height=GridLength.Auto});Sections.RowDefinitions.Add(new(){Height=GridLength.Auto});Sections.Children.Add(loot);Sections.Children.Add(progress);Sections.SizeChanged+=(_,_)=>ArrangeSections();
        var content=UI.Stack(top,summary,elapsed,Status,Sections,
            FocusedLayout.Disclosure("Session conditions & evidence",conditions,"Retained history may be incomplete; last observed activity is not verified logout."),
            FocusedLayout.Disclosure("Setup observations",setup,"Planned changes and observed setup records are separate from quest progress and causal performance claims."));
        scroll=UI.Scroll(content);Body.Children.Add(scroll);ArrangeSections();
        refresh.Tick+=(_,_)=>Refresh();Loaded+=(_,_)=>{refresh.Start();Refresh();};
        Closed+=(_,_)=>{closed=true;generation++;refresh.Stop();work?.Cancel();};
        IsVisibleChanged+=(_,_)=>{if(!IsVisible){work?.Cancel();requested="";}else Refresh();};
    }
    internal void ArrangeSections()
    {
        bool wide=Sections.ActualWidth>=760;
        Grid.SetColumn(progress,wide?1:0);Grid.SetRow(progress,wide?0:1);
        Grid.SetColumnSpan(loot,wide?1:2);Grid.SetColumnSpan(progress,wide?1:2);
        loot.Margin=wide?new(0,0,4,8):new(0,0,0,8);progress.Margin=wide?new(4,0,0,8):new(0,0,0,8);
    }
    private static Border Section(string title,StackPanel rows,TextBlock evidence)
    {
        var heading=UI.Text(title,14,"Melee",true);heading.Margin=new(0,0,0,4);
        var card=UI.Card(UI.Stack(heading,rows,FocusedLayout.Disclosure("Evidence",evidence,"Original receipt/action identities and evidence; browsing never edits progress.")),new(0,0,0,8));card.Padding=new(8);return card;
    }
    private static TextBlock Link(string title,Action action)
    {
        var text=UI.Text("",12,"Accent");text.Visibility=Visibility.Visible;var link=new Hyperlink(new Run(title));System.Windows.Data.BindingOperations.SetBinding(link,TextElement.ForegroundProperty,new System.Windows.Data.Binding(nameof(TextBlock.Foreground)){Source=text});link.Click+=(_,_)=>action();text.Inlines.Add(link);AutomationProperties.SetName(link,title);return text;
    }
    private void HandoffLab(){if(Snapshot!=null&&Application.Current is App app)app.OpenOverviewCombat(Snapshot.Recap.Session.Id);}
    private void HandoffItem(string name){if(Application.Current is App app)app.OpenOverviewItem(name);}
    private void HandoffProgress(JournalActivity row){if(Application.Current is App app)app.OpenOverviewProgress(row);}
    internal void Refresh()
    {
        if(closed)return;
        var choices=Controller.Sessions?.Choices??[];
        var rows=new[]{new SessionChoice("",choices.FirstOrDefault() is {} latest?"Current · "+latest.Label:"Current · 0 fights")}.Concat(choices).ToArray();
        string nextChoices=string.Join('|',rows.Select(r=>r.Id+":"+r.Label))+"|"+Controller.OverviewSession;
        if(nextChoices!=choicesKey&&!SessionPicker.IsDropDownOpen)
        {updating=true;choicesKey=nextChoices;SessionPicker.ItemsSource=rows;SessionPicker.SelectedItem=rows.FirstOrDefault(r=>r.Id==Controller.OverviewSession);updating=false;}
        string key=Controller.OverviewFingerprint();if(key==completed||key==requested&&work?.IsCancellationRequested!=true)return;
        work?.Cancel();work?.Dispose();work=new();requested=key;long request=++generation;Pending=Load(key,request,work.Token);
    }
    private async Task Load(string key,long request,CancellationToken token)
    {
        try
        {
            var result=await Controller.ReadOverview(token);
            if(closed||token.IsCancellationRequested||request!=generation||key!=Controller.OverviewFingerprint())return;
            completed=key;requested="";Snapshot=result;Present(result);
        }
        catch(OperationCanceledException){}
        catch(Exception error) when(error is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {if(!closed&&request==generation){requested="";Status.Text="Session refresh unavailable; completed results retained.";Status.ToolTip=error.Message;}}
    }
    private void Present(SessionOverview? view)
    {
        if(view==null){Snapshot=null;foreach(var field in new[]{fights,combat,self,pet})field.Text="—";foreach(var field in new[]{healing,coin,sales})field.Text="Unknown";elapsed.Text="";LootRows.Children.Clear();ProgressRows.Children.Clear();lootKey=progressKey="";setup.Text=conditions.Text=lootEvidence.Text=progressEvidence.Text="";Status.Text=Controller.OverviewSession.Length==0?"No retained sessions yet.":"The selected historical session is unavailable. Choose Current or another retained session.";return;}
        var recap=view.Recap;fights.Text=recap.Encounters.ToString("N0");combat.Text=TimeSpan.FromSeconds(view.CombatSeconds).ToString(@"h\:mm\:ss");
        self.Text=$"{recap.PlayerDamage:N0} / {view.SelfDps:N1}";pet.Text=$"{recap.PetDamage:N0} / {view.PetDps:N1}";
        healing.Text=view.Healing?.ToString("N0")??"Unknown";healing.ToolTip="Effective healing · "+view.HealingCoverage+" coverage";
        static string Money(long? copper)=>copper.HasValue?$"{copper.Value/1000d:0.###} pp":"Unknown";
        coin.Text=Money(recap.Coin);sales.Text=Money(recap.MerchantIncome);
        elapsed.Text=$@"Elapsed session {TimeSpan.FromSeconds(view.ElapsedSeconds):h\:mm\:ss} · combat time sums retained fights";Status.Text=view.Qualification;
        string next=PetComparisonEngine.Key(recap.Loot);
        if(next!=lootKey)
        {
            lootKey=next;LootRows.Children.Clear();
            foreach(var row in recap.Loot){var line=new DockPanel{Margin=new(0,0,0,4)};var count=UI.Text("×"+row.Quantity,12,"Melee",true);DockPanel.SetDock(count,Dock.Right);line.Children.Add(count);var name=Link(row.Name+(row.Receipts.FirstOrDefault()?.Rank is {} rank&&rank>0?" +"+rank:""),()=>HandoffItem(row.Name));name.Margin=new(0,0,8,0);line.Children.Add(name);LootRows.Children.Add(line);}
            if(recap.Loot.Count==0)LootRows.Children.Add(UI.Text("No recorded acquisitions.",12,"SecondaryText"));
            lootEvidence.Text=string.Join("\n",recap.Loot.SelectMany(l=>l.Receipts).Select(r=>$"{r.At:MMM dd HH:mm:ss} · {r.Quantity} × {r.Name} · {r.Disposition}\n{r.Zone} · {r.Source}\n{r.Id}"));
        }
        next=PetComparisonEngine.Key(recap.Progress);
        if(next!=progressKey)
        {
            progressKey=next;ProgressRows.Children.Clear();foreach(var row in recap.Progress){var label=Link(row.Name,()=>HandoffProgress(row));label.Margin=new(0,0,0,4);ProgressRows.Children.Add(label);}
            if(recap.Progress.Count==0)ProgressRows.Children.Add(UI.Text("No recorded progress actions.",12,"SecondaryText"));
            progressEvidence.Text=string.Join("\n",recap.Progress.Select(r=>$"{r.At:MMM dd HH:mm:ss} · {r.Name}\n{r.Source} · {r.Id}"))+"\nManual checklist actions and logged evidence do not automatically establish quest completion.";
        }
        setup.Text=recap.Setup.Count==0?"No retained setup changes.":string.Join("\n",recap.Setup.Select(r=>$"{r.Kind} · {r.Name}\n{r.Source} · {r.At:MMM dd HH:mm:ss}"));
        conditions.Text=recap.Session.Evidence+"\n"+view.Qualification+"\n"+Controller.SessionImportStatus+"\nAcquisitions are historical receipts, not current ownership. Unknown income is not zero. DPS describes these fights; setup changes do not prove causal gains.";
    }
}
