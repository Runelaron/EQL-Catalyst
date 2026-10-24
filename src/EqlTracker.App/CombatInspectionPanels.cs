using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class AdaptiveCombatOverview : Grid
{
    private readonly UIElement overview;
    private readonly Expander compact;
    private bool small;
    public AdaptiveCombatOverview(UIElement content, TrackerController controller)
    {
        overview = content;
        compact = new() { Header = "Summary & timeline", IsExpanded = controller.Profile.CombatLab.CompactOverviewExpanded, ToolTip = "Show summary cards and the sampled timeline. Collapsed by default at minimum height so ability details remain usable." };
        compact.Expanded += (_, _) => { if (small) { controller.Profile.CombatLab.CompactOverviewExpanded = true; controller.Save(); } };
        compact.Collapsed += (_, _) => { if (small) { controller.Profile.CombatLab.CompactOverviewExpanded = false; controller.Save(); } };
        Children.Add(overview);
    }
    public void Adapt(bool value)
    {
        if (small == value) return; small = value;
        if (value) { Children.Remove(overview); compact.Content = overview; Children.Add(compact); }
        else { Children.Remove(compact); compact.Content = null; Children.Add(overview); }
    }
}
public sealed class ResponsiveCombatSplit : Grid
{
    private bool? narrow;
    private readonly GridSplitter grip;
    private readonly UIElement left, right;
    private readonly CharacterProfile profile;
    private readonly string key;
    public ResponsiveCombatSplit(UIElement left, UIElement right, CharacterProfile profile, string key)
    {
        this.left = left; this.right = right; this.profile = profile; this.key = key;
        grip = new() { Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ToolTip = "Resize ability table and details" };
        Children.Add(left); Children.Add(grip); Children.Add(right);
        SizeChanged += (_, _) => ArrangePanels();
        grip.DragCompleted += (_, _) => { profile.PanelSplits[key + (narrow == true ? ":stack" : "")] = narrow == true ? RowDefinitions[2].ActualHeight / Math.Max(1, ActualHeight - 5) : ColumnDefinitions[2].ActualWidth / Math.Max(1, ActualWidth - 5); };
        ArrangePanels();
    }
    private void ArrangePanels()
    {
        bool next = ActualWidth > 0 && ActualWidth < 760; if (narrow == next) return; PaneLayout.Cancel(this); narrow = next;
        ColumnDefinitions.Clear(); RowDefinitions.Clear();
        double fraction = Math.Clamp(profile.PanelSplits.GetValueOrDefault(key + (next ? ":stack" : ""), next ? .52 : .42), .2, .75);
        if (next)
        {
            RowDefinitions.Add(new() { Height = new(1 - fraction, GridUnitType.Star) }); RowDefinitions.Add(new() { Height = new(5) }); RowDefinitions.Add(new() { Height = new(fraction, GridUnitType.Star) });
            Grid.SetColumn(left, 0); Grid.SetColumn(grip, 0); Grid.SetColumn(right, 0); Grid.SetRow(left, 0); Grid.SetRow(grip, 1); Grid.SetRow(right, 2); grip.ResizeDirection = GridResizeDirection.Rows; grip.Cursor = System.Windows.Input.Cursors.SizeNS;
            PaneLayout.Register(this, [], [], []);
        }
        else
        {
            ColumnDefinitions.Add(new() { Width = new(1 - fraction, GridUnitType.Star) }); ColumnDefinitions.Add(new() { Width = new(5) }); ColumnDefinitions.Add(new() { Width = new(fraction, GridUnitType.Star) });
            Grid.SetRow(left, 0); Grid.SetRow(grip, 0); Grid.SetRow(right, 0); Grid.SetColumn(left, 0); Grid.SetColumn(grip, 1); Grid.SetColumn(right, 2); grip.ResizeDirection = GridResizeDirection.Columns; grip.Cursor = System.Windows.Input.Cursors.SizeWE;
            ColumnDefinitions[0].MinWidth = 230; ColumnDefinitions[2].MinWidth = 180;
            PaneLayout.Register(this, [left,right], [ColumnDefinitions[0],ColumnDefinitions[2]], [grip], profile, key);
        }
    }
}
public sealed class AbilityInspector : Border
{
    private readonly StackPanel body = new();
    private AbilityInspection? shown;
    public AbilityInspector() { Padding = new(8); CornerRadius = new(3); SetResourceReference(BackgroundProperty, "DetailSurface"); SetResourceReference(BorderBrushProperty, "Edge"); BorderThickness = new(1); Child = UI.Scroll(body); Set(null); }
    public void Set(AbilityInspection? value)
    {
        if (ReferenceEquals(value, shown) && body.Children.Count > 0) return; shown = value; body.Children.Clear(); body.Children.Add(UI.Label("Ability Details"));
        if (value == null) { body.Children.Add(UI.Text("Select an ability to inspect its measured results and available spell reference.", 12, "SecondaryText")); return; }
        body.Children.Add(UI.Text(value.Ability, 15, value.Kind == "Healing" ? "Mint" : "Accent", true));
        void Line(InspectionLine line, Panel host)
        {
            var row = new Grid { Margin = new(0, 0, 0, 4) }; row.ColumnDefinitions.Add(new() { Width = new(105) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var label = UI.Text(line.Label, 11, "SecondaryText"); var text = UI.Text(line.Value, 12); label.Margin = text.Margin = new(0); Grid.SetColumn(text, 1); row.Children.Add(label); row.Children.Add(text); host.Children.Add(row);
        }
        foreach (string label in new[] { "Damage", "Effective healing", "Hits", "Heals", "Full resists", "Normal range", "Critical range", "Largest event", "Known overheal", "Linked healing", "Type" })
            if (value.Lines.FirstOrDefault(l => l.Label == label) is { } line) Line(line, body);
        var measured = new StackPanel(); foreach (var line in value.Lines.Where(l => l.Label is "Targets" or "Recipients" or "Scope" or "Recorded ability" or "Logged spell rank" or "Caster level" or "Avoidance" or "Resistance amount")) Line(line, measured);
        body.Children.Add(new Expander { Header = "Context, targets & limitations", Content = measured, ToolTip = "Inspect the denominator, logged rank, target totals and limits of inference. A lower hit does not establish partial resistance." });
        var connected=UI.Stack();foreach(var line in value.Lines.Where(l=>l.Label is "Spell identity" or "Buff evidence" or "Spell parameters" or "Focus evidence"))Line(line,connected);
        body.Children.Add(FocusedLayout.Disclosure("Spell, buff & focus evidence",connected,"Client/reference parameters and historical effects are separate from measured totals. Current equipment is not historical caster equipment."));
        if (value.Reference is { } r)
        {
            body.Children.Add(UI.Text("Base spell · " + r.Classes.Replace("\n", " · "), 11, "SecondaryText"));
            if (r.Limitation.StartsWith("Conflicting")) body.Children.Add(UI.Text("Conflicting source values · expected damage unresolved", 11, "Violet"));
            var reference = UI.Stack(UI.Text(r.Description, 12), UI.Text(r.Effects, 12), UI.Text(r.Details, 11, "SecondaryText"), UI.Text(r.Limitation, 11, r.Limitation.StartsWith("Conflicting") ? "Violet" : "SecondaryText"));
            var source = UI.Button("Spell reference ↗", () => UI.OpenUrl(r.Url)); source.ToolTip = $"Open the cited base-spell page. Wiki revision {r.Revision}; cached {r.FetchedAt:MMM/dd HH:mm}. Reference values are not a verified upgraded-spell prediction."; reference.Children.Add(source);
            body.Children.Add(new Expander { Header = "Spell reference & effects", Content = reference, ToolTip = $"Sourced base-spell data · wiki revision {r.Revision}. Open for effect descriptions, timing, source link and unresolved mechanics." });
        }
        Usability.Tooltips(body);
    }
}
public sealed class CombatGroupPanel : Grid
{
    private readonly DataGrid members = UI.Table(("Member", "Name", 130, null), ("Total", "Total", 75, "N0"), ("Rate", "Rate", 65, "N1"), ("Share", "Share", 60, "0.#'%'"));
    private readonly System.Collections.ObjectModel.ObservableCollection<GroupPeriod> periodRows=[];
    private readonly ListBox periods = new() { DisplayMemberPath = "Label", MinHeight = 65 };
    private readonly TextBlock title = UI.Text("Select a member",14,"Accent",true), evidence = UI.Text("",11,"SecondaryText"), destinations = UI.Text("",12), coverage = UI.Text("",11,"SecondaryText"), splitTotals = UI.Text("",11,"SecondaryText"), abilityTitle = UI.Text("Top 10 abilities by DPS",12,"SecondaryText",true);
    private readonly DamageRowsView abilities = new();
    private readonly TextBlock remainingDestinations=UI.Text("",12);
    private readonly Expander moreDestinations=new(){Header="View More >",IsExpanded=false,Margin=new(0,2,0,4)};
    private string destinationMember="";
    private readonly TrackerController controller;
    private CombatInspectionSnapshot? shown;
    private IReadOnlyList<GroupPeriod>? priorPeriods; private string priorPeriodId="";
    private bool updating, healing;
    public event Action<string>? PeriodSelected;
    public CombatGroupPanel(TrackerController controller)
    {
        this.controller=controller;var profile=controller.Profile;
        GroupMemberPresentation.Configure(members);
        var periodStyle=new Style(typeof(ListBoxItem));periodStyle.Setters.Add(new Setter(ToolTipProperty,new System.Windows.Data.Binding("Description")));periods.ItemContainerStyle=periodStyle;
        periods.SetValue(VirtualizingStackPanel.IsVirtualizingProperty,true);periods.SetValue(VirtualizingStackPanel.VirtualizationModeProperty,VirtualizationMode.Recycling);ScrollViewer.SetHorizontalScrollBarVisibility(periods,ScrollBarVisibility.Disabled);
        periods.ItemsSource=periodRows;
        periods.SelectionChanged+=(_,_)=>{if(!updating&&periods.SelectedItem is GroupPeriod period)PeriodSelected?.Invoke(period.Id);};
        var history = new DockPanel();var label=UI.Label("Recent groups");DockPanel.SetDock(label,Dock.Top);history.Children.Add(label);history.Children.Add(periods);
        var detail = new Grid();for(int i=0;i<5;i++)detail.RowDefinitions.Add(new(){Height=GridLength.Auto});
        moreDestinations.Content=remainingDestinations;moreDestinations.ToolTip="Show the remaining target or recipient entries in this member inspector. The whole inspector scrolls; this list has no separate scrollbar.";
        moreDestinations.Expanded+=(_,_)=>moreDestinations.Header="View Less <";moreDestinations.Collapsed+=(_,_)=>moreDestinations.Header="View More >";
        detail.Children.Add(UI.Stack(title,evidence,splitTotals));var targetsLabel=UI.Label("Targets / recipients");Grid.SetRow(targetsLabel,1);detail.Children.Add(targetsLabel);var targets=UI.Stack(destinations,moreDestinations);Grid.SetRow(targets,2);detail.Children.Add(targets);Grid.SetRow(abilityTitle,3);detail.Children.Add(abilityTitle);Grid.SetRow(abilities,4);detail.Children.Add(abilities);
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});Children.Add(coverage);
        detail.MinHeight=330;detail.RowDefinitions[4].Height=new(220);var all = new GroupPaneLayout(history,members,UI.Card(UI.Scroll(detail)),profile,controller.Save);
        var viewport=new ScrollViewer{Content=all,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=false};
        void FitPanes(){double width=viewport.ViewportWidth>0?viewport.ViewportWidth:viewport.ActualWidth;double min=width>=980?180:width>=620?260:380;double height=Math.Max(min,viewport.ViewportHeight);if(Math.Abs(all.Height-height)>.5||double.IsNaN(all.Height))all.Height=height;}
        viewport.SizeChanged+=(_,_)=>FitPanes();viewport.ScrollChanged+=(_,e)=>{if(e.ViewportHeightChange!=0||e.ViewportWidthChange!=0)FitPanes();};Grid.SetRow(viewport,1);Children.Add(viewport);
        members.SelectionChanged+=(_,_)=>{if(!updating)Select();};
        RefreshPeriods();
    }
    public void RefreshPeriods()
    {
        if(ReferenceEquals(priorPeriods,controller.GroupPeriods)&&priorPeriodId==controller.Profile.Meter.GroupPeriodId)return;
        priorPeriods=controller.GroupPeriods;priorPeriodId=controller.Profile.Meter.GroupPeriodId;updating=true;
        string? prior=(periods.SelectedItem as GroupPeriod)?.Id;
        var scroll=Usability.Tree(periods).OfType<ScrollViewer>().FirstOrDefault();double offset=scroll?.VerticalOffset??0;
        var next=controller.GroupPeriods.Reverse().ToArray();var ids=next.Select(p=>p.Id).ToHashSet();for(int i=periodRows.Count-1;i>=0;i--)if(!ids.Contains(periodRows[i].Id))periodRows.RemoveAt(i);
        for(int i=0;i<next.Length;i++){var value=next[i];int index=periodRows.ToList().FindIndex(p=>p.Id==value.Id);if(index<0)periodRows.Insert(i,value);else{if(index!=i)periodRows.Move(index,i);if(periodRows[i].LastObserved!=value.LastObserved||periodRows[i].End!=value.End||periodRows[i].Live!=value.Live||periodRows[i].Members.Count!=value.Members.Count||periodRows[i].RaidEvidence.Count!=value.RaidEvidence.Count)periodRows[i]=value;}}
        if(scroll!=null)Dispatcher.BeginInvoke(()=>scroll.ScrollToVerticalOffset(offset));
        string id=controller.Profile.Meter.GroupPeriodId.Length>0?controller.Profile.Meter.GroupPeriodId:prior??"";periods.SelectedItem=periods.Items.Cast<GroupPeriod>().FirstOrDefault(p=>p.Id==id);
        periods.ToolTip=controller.GroupImportStatus;updating=false;
    }
    public void Set(CombatInspectionSnapshot snapshot,bool healing)
    {
        RefreshPeriods();if(ReferenceEquals(shown,snapshot)&&this.healing==healing)return;shown=snapshot;this.healing=healing;updating=true;
        string? selected=(members.SelectedItem as GroupMemberRow)?.Id;members.Columns[1].Header=healing?"HP":"Damage";members.Columns[2].Header=healing?"HPS":"DPS";
        UI.SetRows(members,snapshot.Members.ToList());members.SelectedItem=snapshot.Members.FirstOrDefault(m=>m.Id==selected)??snapshot.Members.FirstOrDefault();
        coverage.Text=snapshot.Members.Any(m=>m.Total.HasValue)?$"Known members: {snapshot.Members.Count} · {snapshot.Members.Sum(m=>m.Total??0):N0} {(healing?"HP restored":"damage")} · {snapshot.Members.Sum(m=>m.Rate??0):N1} {(healing?"HPS":"DPS")}":$"Known members: {snapshot.Members.Count} · totals unavailable";coverage.ToolTip=snapshot.Coverage;updating=false;Select();
    }
    private void Select()
    {
        if(members.SelectedItem is not GroupMemberRow row){title.Text="No members in this selection";destinations.Text="";remainingDestinations.Text="";moreDestinations.Visibility=Visibility.Collapsed;moreDestinations.IsExpanded=false;destinationMember="";splitTotals.Text="";abilities.SetRows([],"group-empty",false);return;}
        title.Text=row.Name+(row.Total.HasValue?$" · {row.Total:N0}":" · totals unavailable");evidence.Text=row.Evidence;evidence.ToolTip=shown?.Coverage;splitTotals.Text=row.Total.HasValue?$"Character {row.CharacterTotal:N0} · verified pets {row.PetTotal:N0}":"Membership evidence retained; combat details unavailable";
        string destinationKey=row.Id+"|"+healing;if(destinationKey!=destinationMember){destinationMember=destinationKey;moreDestinations.IsExpanded=false;}
        destinations.Text=row.Details.Count==0?"No retained target or recipient details":string.Join("\n",row.Details.Take(5).Select(d=>d.Label+" · "+d.Value));
        remainingDestinations.Text=string.Join("\n",row.Details.Skip(5).Select(d=>d.Label+" · "+d.Value));moreDestinations.Visibility=row.Details.Count>5?Visibility.Visible:Visibility.Collapsed;
        abilityTitle.Text=$"Top {Math.Min(10,row.Abilities.Count)} abilities by {(healing?"HPS":"DPS")}"+(row.Abilities.Count>10?$" · {row.Abilities.Count} total":"");
        abilities.SetRows(row.Abilities.OrderByDescending(a=>a.Dps).Take(10).ToList(),"group:"+row.Id,controller.Profile.SmoothMeterMotion);
    }
}
