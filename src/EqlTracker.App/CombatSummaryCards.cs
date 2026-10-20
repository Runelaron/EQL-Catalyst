using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class CombatSummaryCards : WrapPanel
{
    public CombatSummaryCards(params UIElement[] cards)
    {
        foreach (var card in cards) Children.Add(card);
        SizeChanged += (_, _) => SizeCards(); Loaded += (_, _) => SizeCards();
    }
    private void SizeCards()
    {
        double width = ActualWidth; if (width <= 0) return;
        int count = Children.Count; bool wide = width >= 740;
        for (int i = 0; i < count; i++) if (Children[i] is FrameworkElement card)
        {
            double next = wide ? width * (i < 3 ? .14 : i == 3 ? .27 : .31) : width / (width >= 500 ? 3 : 2);
            // Healing has four numerical cards as well as the shared Stats card.
            if (count != 5) next = width / Math.Max(1,count);
            next = Math.Max(40,next - card.Margin.Left - card.Margin.Right - .5); if (Math.Abs(card.Width-next)>.5 || double.IsNaN(card.Width)) card.Width=next;
            card.VerticalAlignment=VerticalAlignment.Stretch;
        }
    }
}

public sealed partial class DpsWindow
{
    private readonly TextBlock maxCaption = UI.Text("",10,"SecondaryText");
    private readonly CombatStatsCard damageStats = new(), healingStats = new();
    private UIElement StatsCardForHealing() => healingStats;
    private UIElement StatsCard() => damageStats;
    private void FollowGroup(string id)
    { Controller.SelectGroupPeriod(id);selectionSignature="";lastPresented=null;shownSessions="";Refresh(); }
}

/// <summary>Presentation only: values come from the existing filtered combat snapshots.</summary>
internal sealed class CombatStatsCard : Border
{
    private readonly TextBlock[] fields = new TextBlock[6];
    private readonly string[] descriptions = new string[6];
    private string context = "", coverage = "";
    internal IReadOnlyList<TextBlock> Fields => fields;
    internal CombatStatsCard()
    {
        var grid = new Grid();
        for (int i=0;i<2;i++)grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        for (int i=0;i<3;i++)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        for (int i=0;i<fields.Length;i++)
        {
            var field=fields[i]=UI.Text("",10,"Melee");field.Margin=new(0,0,i%2==0?8:0,1);
            Grid.SetColumn(field,i%2);Grid.SetRow(field,i/2);grid.Children.Add(field);
        }
        var surface=UI.Card(UI.Stack(UI.Label("STATS"),grid),new(0));surface.Padding=new(4,1,4,1);
        Child=surface;Margin=new(0,0,0,2);AutomationProperties.SetName(this,"Combat statistics");
    }
    internal void SetContext(string value) { if(context==value)return;context=value;UpdateTooltip(); }
    private void UpdateTooltip()
    {
        string details=string.Join("\n",new[]{context,coverage}.Where(s=>s.Length>0));
        ToolTip="Statistics for the selected actor, scope and filters.\n"+string.Join("\n",fields.Select(t=>t.Text))+"\n"+details;
        for(int i=0;i<fields.Length;i++)fields[i].ToolTip=descriptions[i]+"\n"+details;
    }
    private void Present(double seconds,int? count,double? rate,long? total,long? hits,long? criticals,bool healing,string evidence)
    {
        var span=TimeSpan.FromSeconds(Math.Max(0,seconds));
        string duration=span.TotalHours>=1?$"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}":$"{(int)span.TotalMinutes}:{span.Seconds:00}";
        string[] labels=[healing?"Average HPS":"Average DPS",healing?"Number of recipients":"Number of Targets","Fight duration",healing?"Total healing":"Total damage done",healing?"Logged heals":"Landed hits","Crit rate"];
        string[] values=[rate?.ToString("N1")??"Unavailable",count?.ToString("N0")??"Unavailable",duration,total?.ToString("N0")??"Unavailable",hits?.ToString("N0")??"Unavailable",hits>0&&criticals.HasValue?(criticals.Value*100d/hits.Value).ToString("N1")+"%":"Unavailable"];
        for(int i=0;i<fields.Length;i++) { string text=$"{labels[i]}: {values[i]}";if(fields[i].Text!=text)fields[i].Text=text;descriptions[i]=i switch {
            0=>healing?"Effective HP restored divided by the existing filtered combat duration.":"Filtered damage divided by the existing combat duration; this is the scope average, not rolling DPS.",
            1=>healing?"Distinct recipient names in the filtered healing records.":"Distinct target names in the filtered damage records, not individual mobs or kills.",
            2=>$"{seconds:N2} seconds. Session scope sums fight durations rather than wall-clock session time.",
            3=>healing?"Effective HP restored in the selection; unavailable raw amounts and overheal are not invented.":"Outgoing damage for the selected personal actor and filters; unrelated actors and incoming damage are excluded.",
            4=>healing?"Logged healing records, including recorded HoT ticks; not the number of spell casts.":"Successful logged damage records. Misses and full resists are excluded; this is not a cast count.",
            _=>$"{criticals?.ToString("N0")??"Unknown"} critical records / {hits?.ToString("N0")??"unknown"} {(healing?"healing records":"landed hits")}. Weighted by record counts, not averaged ability percentages. No valid denominator or missing details remains unavailable. All active filters apply."}; }
        coverage=evidence;UpdateTooltip();
    }
    internal void SetDamage(DpsViewState view,string evidence)
    {
        var f=view.Filter;
        bool unavailable=view.Fights.Any(e=>!e.DetailAvailable&&(f.Target.Length>0||f.Ability.Length>0||f.Kind.Length>0||f.CriticalOnly||f.From>e.Start||f.Until<e.End));
        Present(view.Seconds,view.DetailComplete?view.Targets.Count:null,unavailable?null:view.Dps,unavailable?null:view.Damage,view.DetailComplete?view.Rows.Sum(r=>(long)r.Hits):null,view.DetailComplete?view.Rows.Sum(r=>(long)r.Criticals):null,false,evidence);
    }
    internal void SetHealing(HealingViewState view,string evidence)
        => Present(view.Seconds,view.DetailComplete&&view.TotalsAvailable?view.Recipients.Count:null,view.TotalsAvailable?view.Hps:null,view.TotalsAvailable?view.Effective:null,view.DetailComplete&&view.TotalsAvailable?view.Count:null,view.DetailComplete&&view.TotalsAvailable?view.Events.Count(e=>e.Critical):null,true,evidence);
}
