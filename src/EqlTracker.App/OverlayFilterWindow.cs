using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;
internal sealed class OverlayFilterWindow : ThemedUtilityWindow
{
    private readonly TrackerController controller;
    private DpsSelection Selection=>controller.Profile.OverlayMeter!;
    private readonly ComboBox target=UI.Select([],"",166),kind=UI.Select(["All damage","Melee","Spell","DoT"],"All damage",166),session=UI.Select([],"",166);
    private readonly TextBox ability=UI.Input("",166),from=UI.Input("",166),until=UI.Input("",166);
    private readonly CheckBox critical=new(){Content="Critical only"};
    private readonly Button classes=UI.Button("All classes ▾",()=>{});
    private readonly TextBlock info=UI.Text("",11,"SecondaryText"),error=UI.Text("",11,"Violet"),scope=UI.Text("",11,"SecondaryText");
    private readonly WrapPanel sessionFields=new();
    private readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(160)},refresh=new(){Interval=TimeSpan.FromMilliseconds(300)};
    private bool updating; private string choicesKey="";
    internal OverlayFilterWindow(TrackerController controller,Window owner):base("DPS tracker filters",owner,402,350)
    {
        this.controller=controller;classes.Width=166;classes.Height=28;classes.Margin=new(0);
        session.DisplayMemberPath="Label";sessionFields.Children.Add(UI.Field("Session",session));sessionFields.Children.Add(UI.Field("Classes",classes));Body.Children.Add(scope);Body.Children.Add(sessionFields);
        var fields=new WrapPanel();foreach(var control in new[]{UI.Field("Target",target),UI.Field("Damage type",kind),UI.Field("Spell / strike search",ability),UI.ActionField(critical),UI.Field("From · local date/time",from),UI.Field("Until · local date/time",until)})fields.Children.Add(control);Body.Children.Add(fields);Body.Children.Add(error);Body.Children.Add(info);
        foreach(var control in new[]{target,kind,session})control.SelectionChanged+=(_,_)=>{if(!updating)Apply();};critical.Click+=(_,_)=>Apply();
        foreach(var control in new[]{ability,from,until})control.TextChanged+=(_,_)=>{if(!updating){debounce.Stop();debounce.Start();}};
        debounce.Tick+=(_,_)=>{debounce.Stop();Apply();};classes.Click+=(_,_)=>OpenClasses();refresh.Tick+=(_,_)=>Refresh();
        Actions.Children.Add(UI.Button("Clear filters",()=>{debounce.Stop();Selection.ClearFilters();Load();Commit();}));Actions.Children.Add(UI.Button("Close",Close));
        Loaded+=(_,_)=>refresh.Start();Closing+=(_,_)=>{if(debounce.IsEnabled){debounce.Stop();Apply();}refresh.Stop();};Load();Refresh();
    }
    private void Commit(){controller.EnsureSessionLoaded();controller.Save();((OverlayWindow)Owner).Refresh();info.Text=$"{Selection.FilterCount} active filters · DPS tracker only";}
    private void Load()
    {
        updating=true;ability.Text=Selection.Ability;kind.SelectedItem=Selection.Kind.Length==0?"All damage":Selection.Kind;critical.IsChecked=Selection.CriticalOnly;from.Text=Selection.From?.ToString("yyyy-MM-dd HH:mm:ss")??"";until.Text=Selection.Until?.ToString("yyyy-MM-dd HH:mm:ss")??"";updating=false;choicesKey="";error.Text="";
    }
    private void Apply()
    {
        if(updating)return;
        DateTime? start=null,end=null;bool valid=true;
        if(from.Text.Trim().Length>0){if(DateTime.TryParse(from.Text,CultureInfo.CurrentCulture,DateTimeStyles.AllowWhiteSpaces,out var at))start=at;else valid=false;}
        if(until.Text.Trim().Length>0){if(DateTime.TryParse(until.Text,CultureInfo.CurrentCulture,DateTimeStyles.AllowWhiteSpaces,out var at))end=at;else valid=false;}
        if(start>end)valid=false;
        Selection.Target=target.SelectedItem?.ToString() is { } t&&t!="All targets"?t:"";Selection.Kind=kind.SelectedItem?.ToString() is { } k&&k!="All damage"?k:"";Selection.Ability=ability.Text.Trim();Selection.CriticalOnly=critical.IsChecked==true;
        if(valid){Selection.From=start;Selection.Until=end;error.Text="";}else error.Text="Enter valid local dates, with From no later than Until. Previous valid dates remain active.";
        if(session.SelectedItem is SessionChoice c&&Selection.SessionId!=c.Id){Selection.SessionId=c.Id;Selection.ClassCombinations.Clear();choicesKey="";}
        Commit();
    }
    internal void Refresh()
    {
        var p=Selection;scope.Text=$"{p.Scope} · {(p.Actor=="Player"?"Self":p.Actor=="Combined"?"Total":p.Actor)} · {p.Sort} sort";sessionFields.Visibility=p.Scope=="Session"?Visibility.Visible:Visibility.Collapsed;
        var view=controller.OverlayDpsView();string key=$"{controller.Sessions?.Revision}|{p.SessionId}|{p.Target}|{string.Join('|',view.TargetChoices)}";
        if(key!=choicesKey&&!target.IsDropDownOpen&&!session.IsDropDownOpen)
        {
            choicesKey=key;updating=true;target.ItemsSource=new[]{"All targets"}.Concat(view.TargetChoices).Concat(p.Target.Length>0?new[]{p.Target}:[]).Distinct().ToArray();target.SelectedItem=p.Target.Length>0?p.Target:"All targets";
            var choices=controller.Sessions?.Choices??[];session.ItemsSource=choices.Count>0?choices:new[]{new SessionChoice("","No saved sessions yet")};session.IsEnabled=choices.Count>0;session.SelectedItem=choices.FirstOrDefault(s=>s.Id==p.SessionId)??session.Items[0];updating=false;
        }
        classes.Content=p.ClassCombinations.Count==0?"All classes ▾":string.Join(" / ",p.ClassCombinations)+" ▾";classes.ToolTip="Class combinations in this tracker’s selected session: "+(p.ClassCombinations.Count==0?"All":string.Join(", ",p.ClassCombinations));target.ToolTip=p.Target.Length==0?"All targets":p.Target;session.ToolTip=(session.SelectedItem as SessionChoice)?.Label;info.Text=$"{p.FilterCount} active filters · DPS tracker only";
    }
    private void OpenClasses()
    {
        var menu=new ContextMenu();var p=Selection;void Update(){Commit();Refresh();foreach(var item in menu.Items.OfType<MenuItem>())item.IsChecked=item.Header?.ToString()=="All"?p.ClassCombinations.Count==0:p.ClassCombinations.Contains(item.Header?.ToString()??"");}
        OverlayMenus.Choice(menu,"All",()=>p.ClassCombinations.Count==0,()=>{p.ClassCombinations.Clear();Update();});
        var selected=controller.Sessions?.Sessions.FirstOrDefault(s=>s.Id==p.SessionId)??controller.Sessions?.Sessions.LastOrDefault();
        foreach(string combo in (selected?.Classes.Select(c=>c.Combination)??[]).Concat(new[]{"Unknown","Mixed"}).Distinct().Order()) {var item=OverlayMenus.Choice(menu,combo,()=>p.ClassCombinations.Contains(combo),()=>{if(!p.ClassCombinations.Remove(combo))p.ClassCombinations.Add(combo);Update();});item.StaysOpenOnClick=true;}
        classes.ContextMenu=menu;OverlayMenus.AutoDismiss(menu,false);OverlayMenus.Toggle(menu,classes);
    }
}
