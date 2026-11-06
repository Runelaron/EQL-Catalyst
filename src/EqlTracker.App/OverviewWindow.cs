using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class OverviewWindow : FrostWindow
{
    internal readonly ComboBox SessionPicker=UI.Select([],"",130);
    internal readonly Grid Sections=new();
    internal readonly StackPanel LootRows=new(),ProgressRows=new();
    internal readonly TextBlock Status=UI.Text("Reading session…",10,"SecondaryText");
    internal readonly CheckBox UpgradeRating=new(){Content="Upgrade Rating",FontSize=11,Margin=new(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};
    internal readonly ToggleButton SetupToggle=FooterToggle("Setup observations"),ConditionsToggle=FooterToggle("Session conditions");
    private readonly TextBlock fights=UI.Text("—",16,"Melee",true),combat=UI.Text("—",16,"Melee",true),self=UI.Text("—",16,"OverviewSelfDamage",true),pet=UI.Text("—",16,"OverviewPetDamage",true),healing=UI.Text("—",16,"Healing",true),coin=UI.Text("Unknown",14,"Melee"),spent=UI.Text("Unknown",14,"Melee");
    private readonly TextBlock elapsed=UI.Text("",11,"SecondaryText"),conditions=UI.Text("",11,"SecondaryText"),setup=UI.Text("",11),lootEvidence=UI.Text("",11),progressEvidence=UI.Text("",11);
    private readonly Border loot,progress;
    private readonly ScrollViewer scroll;
    internal ScrollViewer ContentScroll=>scroll;
    private readonly OverviewItemPeek peek;
    internal OverviewItemPeek ItemPeek=>peek;
    private readonly Dictionary<string,(DockPanel Line,TextBlock Name,TextBlock Count,TextBlock Rating)> lootLines=[];
    private readonly HashSet<string> metadataRequested=[];
    private readonly DispatcherTimer refresh=new(){Interval=TimeSpan.FromMilliseconds(300)};
    private CancellationTokenSource? work,ratingsWork;
    private string requested="",completed="",choicesKey="",lootKey="",progressKey="",ratingsRequested="",ratingsCompleted="",sessionShown="";
    private bool updating,closed;
    private long generation,ratingsGeneration;
    internal SessionOverview? Snapshot {get;private set;}
    internal Task Pending {get;private set;}=Task.CompletedTask;
    internal Task RatingsPending {get;private set;}=Task.CompletedTask;
    internal OverviewWindow(TrackerController controller):base(controller,"Overview","",915,650,surface:WindowSurface.Theme)
    {
        CatalystHeading.Apply(HeadingTitle,"Overview");peek=new(controller);
        AutomationProperties.SetName(SessionPicker,"Overview session");SessionPicker.Height=28;
        SessionPicker.ToolTip="Current follows the latest retained session. This choice is independent of Combat Lab, overlays and phone context.";
        SessionPicker.SelectionChanged+=(_,_)=>{if(!updating&&SessionPicker.SelectedItem is SessionChoice choice){peek.Dismiss(true);ratingsWork?.Cancel();Controller.SelectOverviewSession(choice.Id);Refresh();}};
        var top=new WrapPanel();top.Children.Add(UI.Field("Session",SessionPicker));
        var analysis=Link("Combat details",()=>HandoffLab());analysis.Margin=new(8,19,0,0);analysis.VerticalAlignment=VerticalAlignment.Center;top.Children.Add(analysis);
        elapsed.Margin=new(8,19,0,0);elapsed.VerticalAlignment=VerticalAlignment.Center;top.Children.Add(elapsed);
        var summary=new WrapPanel{Margin=new(0,4,0,8)};
        foreach(var entry in new[]{("Fights",fights),("Combat time",combat),("Self · damage / DPS",self),("Pet · damage / DPS",pet),("Healing done",healing),("Coin earned",coin),("Coin spent",spent)})
        {var card=UI.Card(UI.Stack(UI.Label(entry.Item1),entry.Item2),new(0,0,4,4));card.MinWidth=106;card.Padding=new(6,3,6,3);summary.Children.Add(card);}
        summary.ToolTip="DPS uses summed encounter combat duration and the existing denominator. Healing is recorded Self/Pet effective healing during fights. Money shows recorded receipts and purchase totals, not wallet balances.";
        UpgradeRating.IsChecked=Controller.OverviewRatings;UpgradeRating.ToolTip="U: unbuffed; B: configured usual buffs. Replacement gains against saved Current player gear now, using its Gear role and weights. Equipable items only; not predicted DPS.";
        UpgradeRating.Click+=(_,_)=>{Controller.SetOverviewRatings(UpgradeRating.IsChecked==true);RefreshRatings();};
        loot=Section("Loot",LootRows,lootEvidence,UpgradeRating);progress=Section("Quest progress",ProgressRows,progressEvidence);
        Sections.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});Sections.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        Sections.RowDefinitions.Add(new(){Height=GridLength.Auto});Sections.RowDefinitions.Add(new(){Height=GridLength.Auto});Sections.Children.Add(loot);Sections.Children.Add(progress);Sections.SizeChanged+=(_,_)=>ArrangeSections();
        var setupBody=UI.Card(UI.Stack(UI.Label("Setup observations"),setup));var conditionBody=UI.Card(UI.Stack(UI.Label("Session conditions"),conditions));setupBody.Visibility=conditionBody.Visibility=Visibility.Collapsed;
        SetupToggle.Checked+=(_,_)=>setupBody.Visibility=Visibility.Visible;SetupToggle.Unchecked+=(_,_)=>setupBody.Visibility=Visibility.Collapsed;
        ConditionsToggle.Checked+=(_,_)=>conditionBody.Visibility=Visibility.Visible;ConditionsToggle.Unchecked+=(_,_)=>conditionBody.Visibility=Visibility.Collapsed;
        var content=UI.Stack(top,summary,Sections,setupBody,conditionBody);scroll=UI.Scroll(content);
        var footer=new Grid{Margin=new(0,4,0,0)};footer.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var actions=new WrapPanel();SetupToggle.Margin=new(0,0,4,0);actions.Children.Add(SetupToggle);actions.Children.Add(ConditionsToggle);footer.Children.Add(actions);
        Status.HorizontalAlignment=HorizontalAlignment.Right;Status.TextAlignment=TextAlignment.Right;Status.VerticalAlignment=VerticalAlignment.Bottom;Status.Margin=new(8,0,14,0);Status.MaxWidth=280;Grid.SetColumn(Status,1);footer.Children.Add(Status);
        Body.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});Body.RowDefinitions.Add(new(){Height=GridLength.Auto});Grid.SetRow(footer,1);Body.Children.Add(footer);Body.Children.Add(scroll);ArrangeSections();
        refresh.Tick+=(_,_)=>Refresh();Loaded+=(_,_)=>{refresh.Start();Refresh();};
        Closed+=(_,_)=>{closed=true;generation++;ratingsGeneration++;refresh.Stop();work?.Cancel();ratingsWork?.Cancel();peek.Dismiss(true);};
        IsVisibleChanged+=(_,_)=>{if(!IsVisible){work?.Cancel();ratingsWork?.Cancel();requested=ratingsRequested="";peek.Dismiss(true);}else Refresh();};
        Deactivated+=(_,_)=>{if(!peek.IsKeyboardFocusWithin)peek.Dismiss();};
    }
    private static ToggleButton FooterToggle(string label)
    {var button=PetFilterToggle.Create();button.Content=label;button.Height=28;button.FontSize=11;button.Margin=new(0);return button;}
    internal void ArrangeSections()
    {
        bool wide=Sections.ActualWidth>=760;
        Grid.SetColumn(progress,wide?1:0);Grid.SetRow(progress,wide?0:1);Grid.SetColumnSpan(loot,wide?1:2);Grid.SetColumnSpan(progress,wide?1:2);
        loot.Margin=wide?new(0,0,4,8):new(0,0,0,8);progress.Margin=wide?new(4,0,0,8):new(0,0,0,8);
    }
    private static Border Section(string title,StackPanel rows,TextBlock evidence,UIElement? extra=null)
    {
        var heading=UI.Text(title,14,"Melee",true);heading.Margin=new(0,0,0,4);var header=new WrapPanel();header.Children.Add(heading);if(extra!=null)header.Children.Add(extra);
        var body=new Grid{MinHeight=128};body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.Children.Add(header);Grid.SetRow(rows,1);body.Children.Add(rows);
        var disclosure=CompactEvidence(evidence);Grid.SetRow(disclosure,2);body.Children.Add(disclosure);var card=UI.Card(body,new(0,0,0,8));card.Padding=new(8);return card;
    }
    internal static Expander CompactEvidence(UIElement content)
    {
        var expander=new Expander{Header="Evidence",Content=content,Margin=new(0),ToolTip="Original receipt/action identities and qualifications. Browsing never edits saved progress."};
        expander.Template=(ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Expander"><StackPanel>
          <ToggleButton Height="14" FontSize="10" Padding="3,0" Margin="0" HorizontalAlignment="Left" Cursor="Hand" FocusVisualStyle="{x:Null}" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" IsChecked="{Binding IsExpanded,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}">
            <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border x:Name="Face" BorderThickness="1" BorderBrush="Transparent" Background="{DynamicResource InnerSurface}" Padding="3,0"><StackPanel Orientation="Horizontal"><TextBlock x:Name="Arrow" Text="▸" Width="12" FontSize="10" Foreground="{DynamicResource SecondaryText}"/><TextBlock Text="Evidence" FontSize="10" Foreground="{DynamicResource SecondaryText}"/></StackPanel></Border><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Arrow" Property="Text" Value="▾"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Spell}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template>
          </ToggleButton>
          <ContentPresenter x:Name="EvidenceBody" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" ContentSource="Content" Visibility="Collapsed" Margin="0,4,0,0"/>
        </StackPanel><ControlTemplate.Triggers><Trigger Property="IsExpanded" Value="True"><Setter TargetName="EvidenceBody" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers></ControlTemplate>
        """);return expander;
    }
    private static TextBlock Link(string title,Action action)
    {
        var text=UI.Text("",12,"Accent");text.Visibility=Visibility.Visible;var link=new Hyperlink(new Run(title));System.Windows.Data.BindingOperations.SetBinding(link,TextElement.ForegroundProperty,new System.Windows.Data.Binding(nameof(TextBlock.Foreground)){Source=text});link.Click+=(_,_)=>action();text.Inlines.Add(link);AutomationProperties.SetName(link,title);return text;
    }
    private void HandoffLab(){if(Snapshot!=null&&Application.Current is App app)app.OpenOverviewCombat(Snapshot.Recap.Session.Id);}
    private void HandoffProgress(JournalActivity row){if(Application.Current is App app)app.OpenOverviewProgress(row);}
    internal void Refresh()
    {
        if(closed)return;peek.Refresh();RefreshRatings();
        var choices=Controller.Sessions?.Choices??[];var rows=new[]{new SessionChoice("",choices.FirstOrDefault() is {} latest?"Current · "+latest.Label:"Current · 0 fights")}.Concat(choices).ToArray();
        string nextChoices=string.Join('|',rows.Select(r=>r.Id+":"+r.Label))+"|"+Controller.OverviewSession;
        if(nextChoices!=choicesKey&&!SessionPicker.IsDropDownOpen){updating=true;choicesKey=nextChoices;SessionPicker.ItemsSource=rows;SessionPicker.SelectedItem=rows.FirstOrDefault(r=>r.Id==Controller.OverviewSession);updating=false;}
        string key=Controller.OverviewFingerprint();if(key==completed||key==requested&&work?.IsCancellationRequested!=true)return;
        work?.Cancel();work?.Dispose();work=new();requested=key;long request=++generation;Pending=Load(key,request,work.Token);
    }
    private async Task Load(string key,long request,CancellationToken token)
    {
        try{var result=await Controller.ReadOverview(token);if(closed||token.IsCancellationRequested||request!=generation||key!=Controller.OverviewFingerprint())return;completed=key;requested="";Snapshot=result;Present(result);}
        catch(OperationCanceledException){}
        catch(Exception error)when(error is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException){if(!closed&&request==generation){requested="";Status.Text="Session refresh unavailable";Status.ToolTip=error.Message+"\nCompleted results retained.";}}
    }
    private void Present(SessionOverview? view)
    {
        if(view==null){Snapshot=null;foreach(var f in new[]{fights,combat,self,pet})f.Text="—";foreach(var f in new[]{self,pet,healing})f.SetResourceReference(TextBlock.ForegroundProperty,"SecondaryText");foreach(var f in new[]{healing,coin,spent})f.Text="Unknown";elapsed.Text="";LootRows.Children.Clear();ProgressRows.Children.Clear();lootLines.Clear();lootKey=progressKey="";setup.Text=conditions.Text=lootEvidence.Text=progressEvidence.Text="";Status.Text=Controller.OverviewSession.Length==0?"No sessions yet":"Selected session unavailable";peek.Dismiss(true);return;}
        if(sessionShown!=view.Recap.Session.Id){sessionShown=view.Recap.Session.Id;peek.Dismiss(true);ratingsWork?.Cancel();ratingsRequested=ratingsCompleted="";}
        self.SetResourceReference(TextBlock.ForegroundProperty,"OverviewSelfDamage");pet.SetResourceReference(TextBlock.ForegroundProperty,"OverviewPetDamage");var recap=view.Recap;fights.Text=recap.Encounters.ToString("N0");combat.Text=TimeSpan.FromSeconds(view.CombatSeconds).ToString(@"h\:mm\:ss");self.Text=$"{recap.PlayerDamage:N0} / {view.SelfDps:N1}";pet.Text=$"{recap.PetDamage:N0} / {view.PetDps:N1}";
        healing.Text=view.Healing?.ToString("N0")??"Unknown";healing.SetResourceReference(TextBlock.ForegroundProperty,view.Healing.HasValue?"Healing":"SecondaryText");healing.ToolTip="Effective healing · "+view.HealingCoverage+" coverage";
        static string Money(long? value)=>value.HasValue?$"{value.Value/1000d:0.###} pp":"Unknown";
        coin.Text=Money(recap.Earned);coin.SetResourceReference(TextBlock.ForegroundProperty,recap.Earned.HasValue?"Melee":"SecondaryText");spent.SetResourceReference(TextBlock.ForegroundProperty,recap.Spent.HasValue?"Melee":"SecondaryText");coin.ToolTip="Recorded income only\nCoin received: "+Money(recap.Coin)+"\nItem sale proceeds (including logged automatic sales): "+Money(recap.MerchantIncome)+"\nNot net profit or a complete wallet statement.";spent.Text=Money(recap.Spent);spent.ToolTip=Controller.PurchaseCoverage+"\nExplicit purchase prices, counted once as transaction totals. Missing records are Unknown, not zero.";
        elapsed.Text=$@"Elapsed session {TimeSpan.FromSeconds(view.ElapsedSeconds):h\:mm\:ss}";Status.Text=view.Qualification;Status.ToolTip=(recap.HistoryIncomplete?"Partial history. ":"")+"Last observed activity does not prove logout.\n"+Controller.SessionImportStatus;
        string next=PetComparisonEngine.Key(recap.Loot);
        if(next!=lootKey)
        {
            double offset=scroll.VerticalOffset;lootKey=next;var ids=recap.Loot.Select(r=>r.Identity).ToHashSet();foreach(var id in lootLines.Keys.Where(k=>!ids.Contains(k)).ToArray()){LootRows.Children.Remove(lootLines[id].Line);lootLines.Remove(id);}
            foreach(var row in recap.Loot)
            {
                if(!lootLines.TryGetValue(row.Identity,out var widgets))
                {
                    var line=new DockPanel{Margin=new(0,0,0,4)};var count=UI.Text("",12,"Melee",true);var rating=UI.Text("",11,"Mint");rating.Margin=new(6,0,0,0);rating.Visibility=Visibility.Collapsed;var numbers=UI.Row(count,rating);DockPanel.SetDock(numbers,Dock.Right);line.Children.Add(numbers);
                    TextBlock? label=null;label=Link(Identity.Canonical(row.Name)+(row.Receipts.FirstOrDefault()?.Rank is {} rank&&rank>0?" +"+rank:""),()=>{var current=Snapshot?.Recap.Loot.FirstOrDefault(l=>l.Identity==row.Identity);if(current!=null)peek.Show(current,label!);});label.Margin=new(0,0,8,0);line.Children.Add(label);widgets=(line,label,count,rating);lootLines[row.Identity]=widgets;
                }
                widgets.Count.Text="×"+row.Quantity;
            }
            LootRows.Children.Clear();foreach(var row in recap.Loot)LootRows.Children.Add(lootLines[row.Identity].Line);
            scroll.ScrollToVerticalOffset(offset);if(recap.Loot.Count==0)LootRows.Children.Add(UI.Text("No recorded acquisitions.",12,"SecondaryText"));
            lootEvidence.Text=string.Join("\n",recap.Loot.SelectMany(l=>l.Receipts).Select(r=>$"{r.At:MMM dd HH:mm:ss} · {r.Quantity} × {r.Name} · {r.Disposition}\n{r.Zone} · {r.Source}\n{r.Id}"));
        }
        next=PetComparisonEngine.Key(recap.Progress);
        if(next!=progressKey){progressKey=next;ProgressRows.Children.Clear();foreach(var row in recap.Progress){var label=Link(row.Name,()=>HandoffProgress(row));label.Margin=new(0,0,0,4);ProgressRows.Children.Add(label);}if(recap.Progress.Count==0)ProgressRows.Children.Add(UI.Text("No recorded progress actions.",12,"SecondaryText"));progressEvidence.Text=string.Join("\n",recap.Progress.Select(r=>$"{r.At:MMM dd HH:mm:ss} · {r.Name}\n{r.Source} · {r.Id}"))+"\nManual checklist actions and logged evidence do not automatically establish quest completion.";}
        setup.Text=recap.Setup.Count==0?"No setup changes recorded.":string.Join("\n",recap.Setup.Select(r=>$"{r.Kind} · {r.Name}\n{r.Source} · {r.At:MMM dd HH:mm:ss}"));
        conditions.Text=recap.Session.Evidence+"\n"+view.Qualification+"\n"+(recap.HistoryIncomplete?"Partial history\n":"")+Controller.SessionImportStatus+"\nAcquisitions are historical receipts, not current ownership. Unknown income is not zero. Setup changes do not prove causal performance gains.";RefreshRatings();
    }
    private void RefreshRatings()
    {
        if(closed)return;
        if(UpgradeRating.IsChecked!=true||Snapshot==null){ratingsWork?.Cancel();ratingsRequested="";foreach(var row in lootLines.Values)row.Rating.Visibility=Visibility.Collapsed;return;}
        var context=Controller.OverviewRatingContext(Snapshot.Recap.Loot);string session=Snapshot.Recap.Session.Id;string key=session+"|"+context.Key;
        foreach(var row in Snapshot.Recap.Loot){var item=context.Catalog.FindKey(row.ItemKey)??context.Catalog.Find(row.Name);if(lootLines.TryGetValue(row.Identity,out var widgets))widgets.Rating.Visibility=item!=null&&OverviewLootRatings.Equipable(item)?Visibility.Visible:Visibility.Collapsed;if((item==null||!item.MetadataComplete)&&metadataRequested.Add(row.Name)&&!Controller.Demo)Controller.RequestGearMetadata(row.Name);}
        if(key==ratingsCompleted||key==ratingsRequested&&ratingsWork?.IsCancellationRequested!=true)return;
        ratingsWork?.Cancel();ratingsWork?.Dispose();ratingsWork=new();ratingsRequested=key;long request=++ratingsGeneration;var rows=Snapshot.Recap.Loot.ToArray();RatingsPending=LoadRatings();
        async Task LoadRatings()
        {
            var token=ratingsWork.Token;
            try
            {
                var result=await Task.Run(()=>{var evaluation=context.Create();var values=new Dictionary<string,OverviewLootRating>();foreach(var row in rows){token.ThrowIfCancellationRequested();var item=context.Catalog.FindKey(row.ItemKey)??context.Catalog.Find(row.Name);if(item!=null&&OverviewLootRatings.Equipable(item))values[row.Identity]=evaluation.Evaluate(item,row.Receipts.FirstOrDefault()?.Rank);}return values;},token);
                if(closed||token.IsCancellationRequested||request!=ratingsGeneration||Snapshot?.Recap.Session.Id!=session||Controller.OverviewSelectedSession?.Id!=session||UpgradeRating.IsChecked!=true||Controller.OverviewRatingContext(Snapshot.Recap.Loot).Key!=context.Key)return;
                foreach(var (id,rating) in result)if(lootLines.TryGetValue(id,out var row)){row.Rating.Text=rating.Display;row.Rating.ToolTip=rating.Explanation;row.Rating.Visibility=Visibility.Visible;AutomationProperties.SetName(row.Rating,"Upgrade Rating · "+rating.Display);}
                ratingsCompleted=key;ratingsRequested="";
            }
            catch(OperationCanceledException){}
            catch(Exception error)when(error is InvalidOperationException or ArgumentException){if(request==ratingsGeneration){ratingsRequested="";foreach(var row in lootLines.Values)row.Rating.ToolTip="Rating unavailable: "+error.Message;}}
        }
    }
}
