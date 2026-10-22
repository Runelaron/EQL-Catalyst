using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class PetPairComparisonView : StackPanel
{
    private readonly DataGrid differences=UI.Table(("Stat","Stat",90,null),("Current","Current",85,"0.###"),("Candidate","Candidate",85,"0.###"),("Change","Change",85,"+0.###;-0.###;0"));
    private readonly TextBlock warning=UI.Text("",12,"Melee",true);
    private readonly StackPanel left=new(),right=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private ItemPairComparison current;
    private readonly Func<ItemPairComparison>? factory;
    private readonly Func<string>? version;
    private string revision="";
    public PetPairComparisonView(ItemPairComparison value,Func<ItemPairComparison>? refresh=null,Func<string>? revision=null)
    {
        current=value;factory=refresh;version=revision;
        var sides=new Grid();sides.ColumnDefinitions.Add(new());sides.ColumnDefinitions.Add(new());
        var before=UI.Card(left);var after=UI.Card(right);after.Margin=new(5,0,0,0);Grid.SetColumn(after,1);sides.Children.Add(before);sides.Children.Add(after);Children.Add(sides);Children.Add(warning);
        differences.Height=220;TableLayout.SetKey(differences,"pet.item-pair.differences");Children.Add(UI.Card(UI.Stack(UI.Text("Differences",14,"Accent",true),UI.Text("Ranked item contributions; effects remain separate. — means unknown.",11,"SecondaryText"),differences)));
        Apply(value);timer.Tick+=(_,_)=>RefreshNow();Loaded+=(_,_)=>{timer.Start();RefreshNow();};Unloaded+=(_,_)=>timer.Stop();
        SizeChanged+=(_,_)=>{bool narrow=ActualWidth<470;sides.RowDefinitions.Clear();if(narrow){sides.RowDefinitions.Add(new(){Height=GridLength.Auto});sides.RowDefinitions.Add(new(){Height=GridLength.Auto});}Grid.SetRow(after,narrow?1:0);Grid.SetColumn(after,narrow?0:1);Grid.SetColumnSpan(before,narrow?2:1);Grid.SetColumnSpan(after,narrow?2:1);};
    }
    internal void RefreshNow()
    {
        if(Application.Current is not App app)return;
        string next=app.Controller.EquipmentRevision+"|"+app.Controller.PetRevision+"|"+(version?.Invoke()??"");if(next==revision)return;revision=next;
        if(factory!=null){Apply(factory());return;}
        var catalog=app.Controller.Catalog;
        ItemDefinition? Resolve(ItemPairSide side)=>side.Definition==null?null:catalog.FindKey(side.Definition.Key)??side.Definition;
        var candidate=Resolve(current.Candidate);if(candidate==null)return;
        Apply(ItemPairComparison.Create(Resolve(current.Current),current.Current.Instance,candidate,current.Candidate.Rank,current.Candidate.Instance,app.Controller.Profile.ActiveLoadout,catalog,current.Pet));
    }
    private void Apply(ItemPairComparison value)
    {
        current=value;
        void Side(StackPanel panel,string caption,ItemPairSide item)
        {
            if(item.Definition==null){panel.Children.Clear();panel.Children.Add(UI.Label(caption));panel.Children.Add(UI.Text(item.Name,16,"Accent",true));panel.Children.Add(UI.Text("Metadata unknown",12));return;}
            if(panel.Children.Count!=4||panel.Children[1] is not ItemHeaderIcons||panel.Children[2] is not ClassicItemCard)
            {panel.Children.Clear();panel.Children.Add(UI.Label(caption));panel.Children.Add(new ItemHeaderIcons());panel.Children.Add(new ClassicItemCard(item.Definition,item.Rank,item.Instance));panel.Children.Add(new ItemAcquisitionView());}
            var header=(ItemHeaderIcons)panel.Children[1];header.Title.Text=item.Name+" +"+item.Rank;
            var app=Application.Current as App;header.Show(item.Definition,app==null?new(false,"",false,null):ItemOwnership.For(item.Definition,app.Controller.Inventory));
            ((ClassicItemCard)panel.Children[2]).Update(item.Definition,item.Rank,item.Instance,app?.Controller.Profile.SmoothMeterMotion==true);
            if(app!=null)((ItemAcquisitionView)panel.Children[3]).Show(item.Definition,app.Controller.Catalog,app.Controller.Inventory);
        }
        Side(left,"Current item",value.Current);Side(right,"Candidate",value.Candidate);warning.Text=value.Warning;warning.Visibility=value.Warning.Length==0?Visibility.Collapsed:Visibility.Visible;
        UI.SetRows(differences,value.Differences.ToList());Usability.Tooltips(this);
    }
}
