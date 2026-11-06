using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class BardComparisonPanel : Grid
{
    private readonly TrackerController controller;
    internal readonly ComboBox Sets=WorkspaceForms.Control(new ComboBox{Width=260},"Song set");
    internal readonly CheckBox Strength=WorkspaceForms.Control(new CheckBox{Content="Effective strength"},"Effective strength");
    internal readonly StackPanel Rows=new();
    internal readonly Expander Setup;
    private readonly TextBlock status=UI.Text("Reading song evidence",11,"SecondaryText"),context=UI.Text("",11,"SecondaryText"),history=UI.Text("",11,"SecondaryText");
    private readonly ScrollViewer scroll;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(400)};
    private readonly Dictionary<string,Grid> rowViews=[];
    private CancellationTokenSource? work;
    private long generation;
    private string requested="",complete="",choicesKey="",rowKey="";
    private bool updating;
    internal Task Pending {get;private set;}=Task.CompletedTask;
    internal BardComparisonView? View {get;private set;}
    internal BardComparisonPanel(TrackerController controller)
    {
        this.controller=controller;RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});RowDefinitions.Add(new(){Height=GridLength.Auto});
        var setStyle=new Style(typeof(ComboBoxItem),(Style)FindResource(typeof(ComboBoxItem)));setStyle.Setters.Add(new Setter(ToolTipProperty,new System.Windows.Data.Binding("Tip")));Sets.ItemContainerStyle=setStyle;
        Strength.ToolTip=BardSongComparison.EffectiveTooltip;Strength.IsChecked=controller.BardResearchSnapshot().Preferences.EffectiveStrength;
        Sets.SelectionChanged+=(_,_)=>{if(!updating&&Sets.SelectedItem is BardSetChoice choice){controller.SelectBardSet(choice.Id);Refresh();}};
        Strength.Click+=(_,_)=>{controller.SetBardStrength(Strength.IsChecked==true);Refresh();};
        var controls=WorkspaceForms.Flow(UI.Field("Song set",Sets),UI.ActionField(Strength));Children.Add(controls);
        Setup=FocusedLayout.Disclosure("Setup",new StackPanel(),"Known songs, confirmed AAs, effective worn focus and reviewed potency. Unknown values stay unknown.");
        Setup.Expanded+=(_,_)=>{if(Setup.Content is StackPanel {Children.Count:0})Setup.Content=SetupContent();};
        var evidence=FocusedLayout.Disclosure("Common sets & evidence",history,"Observed intervals and recurrence are not assumed live uptime. Selecting a common set pins comparison only.");
        var content=UI.Stack(context,Rows,Setup,evidence);scroll=UI.Scroll(content);Grid.SetRow(scroll,1);Children.Add(scroll);Grid.SetRow(status,2);status.MaxHeight=36;Children.Add(status);
        Loaded+=(_,_)=>{timer.Start();Refresh();};Unloaded+=(_,_)=>{timer.Stop();work?.Cancel();requested="";};IsVisibleChanged+=(_,_)=>{if(IsVisible)Refresh();else{work?.Cancel();requested="";}};
        timer.Tick+=(_,_)=>{if(IsVisible)Refresh();};SizeChanged+=(_,_)=>ArrangeRows();
    }
    internal void Refresh()
    {
        var request=controller.BardPresentation();updating=true;string choiceKey=string.Join('|',request.Choices.Select(c=>c.Id+":"+c.Label+":"+c.Tip))+request.Selected;
        if(choiceKey!=choicesKey&&!Sets.IsDropDownOpen){choicesKey=choiceKey;Sets.ItemsSource=request.Choices;Sets.SelectedItem=request.Choices.FirstOrDefault(c=>c.Id==request.Selected);}
        Sets.ToolTip=(Sets.SelectedItem as BardSetChoice)?.Tip;Strength.IsChecked=request.Input.EffectiveStrength;updating=false;
        history.Text=request.Evidence.Length==0?"No repeated sets have qualified yet.":request.Evidence;context.ToolTip=string.Join("\n",request.Input.Conditions);
        if(request.Key==complete||request.Key==requested&&work?.IsCancellationRequested!=true)return;
        work?.Cancel();work?.Dispose();work=new();var token=work.Token;long revision=++generation;requested=request.Key;Pending=Load();_=IndicatePending();
        async Task IndicatePending(){try{await Task.Delay(150,token);if(revision==generation&&!Pending.IsCompleted)status.Text="Updating song comparison";}catch(OperationCanceledException){}}
        async Task Load()
        {
            try
            {
                var result=await Task.Run(()=>BardSongComparison.Evaluate(request.Input,token),token);
                if(token.IsCancellationRequested||revision!=generation||request.Key!=controller.BardPresentation().Key)return;
                View=result;complete=request.Key;requested="";Present(result,request);
            }
            catch(OperationCanceledException){}
            catch(Exception error)when(error is InvalidOperationException or ArgumentException){if(revision==generation){requested="";status.Text="Comparison unavailable · completed results retained";status.ToolTip=error.Message;}}
        }
    }
    private void Present(BardComparisonView view,BardPresentationRequest request)
    {
        context.Text=(request.Selected=="detected"?request.Input.Set?.Origin=="Repeated observations"?"Following detected set":"Reviewed set · use unconfirmed":"Pinned for comparison")+" · "+view.Context;
        history.Text=request.Evidence.Length==0?"No repeated sets have qualified yet. Successful memorization establishes a known song, not a playing rotation.":request.Evidence;
        string next=PetComparisonEngine.Key(view.Rows);
        if(next!=rowKey)
        {
            rowKey=next;double offset=scroll.VerticalOffset;Rows.Children.Clear();rowViews.Clear();
            var headings=new Grid();headings.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});headings.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});TextBlock left=UI.Label("Your songs"),right=UI.Label("Suggested");Grid.SetColumn(right,1);headings.Children.Add(left);headings.Children.Add(right);Rows.Children.Add(headings);
            foreach(var row in view.Rows)
            {
                var grid=new Grid{Margin=new(0,0,0,6)};grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
                var song=new DockPanel{Margin=new(0,0,8,0)};var icon=UI.Text("♪",20,"Melee");icon.Margin=new(0,0,6,0);DockPanel.SetDock(icon,Dock.Left);song.Children.Add(icon);var name=UI.Text(row.Song.Identity.Name,13,"Accent",true);name.ToolTip=row.Song.Identity.Name;
                song.Children.Add(UI.Stack(name,UI.Text("Rank "+(row.Song.Identity.UpgradeRank?.ToString()??"Unknown")+(row.Song.Gem>0?" · gem "+row.Song.Gem:" · gem unknown")+" · "+(request.Input.Set?.Origin=="Repeated observations"?"Observed":request.Selected.StartsWith("eq:",StringComparison.Ordinal)?"Saved":"Reviewed"),10,"SecondaryText")));grid.Children.Add(song);
                var suggestion=new StackPanel();suggestion.Children.Add(UI.Text(row.Status+(row.Options.Count==1?" · "+row.Options[0].Song.Identity.Name+" +"+row.Options[0].Song.Identity.UpgradeRank:""),13,row.Options.Count>0?"Mint":"Foreground",true));suggestion.Children.Add(UI.Text(row.Reason,11,"SecondaryText"));var suggested=new DockPanel{LastChildFill=true};var whyToggle=PetFilterToggle.Create();whyToggle.Content="Why?";whyToggle.Width=72;whyToggle.ToolTip="Sources, rank precedence, modifiers and supported changes";DockPanel.SetDock(whyToggle,Dock.Right);suggested.Children.Add(whyToggle);suggested.Children.Add(suggestion);Grid.SetColumn(suggested,1);grid.Children.Add(suggested);
                var details=UI.Stack(UI.Text(row.Evidence,11,"SecondaryText"));
                foreach(var option in row.Options)
                {
                    details.Children.Add(UI.Text(option.Song.Label+"\n"+option.Evidence,11));var plan=WorkspaceForms.Control(UI.Button("Plan this swap",()=>{}));
                    plan.Click+=(_,_)=>{if(complete!=request.Key||controller.BardPresentation().Key!=request.Key){status.Text="Song context changed; review the refreshed result";return;}try{controller.SaveBardSwap(view.SetId,row.Song,option.Song);plan.IsEnabled=false;status.Text="Plan saved in Catalyst · change songs manually in EQ";}catch(InvalidDataException error){status.Text=error.Message;}};details.Children.Add(plan);
                }
                var why=UI.Card(details,new(0,4,0,0));why.Visibility=Visibility.Collapsed;whyToggle.Checked+=(_,_)=>why.Visibility=Visibility.Visible;whyToggle.Unchecked+=(_,_)=>why.Visibility=Visibility.Collapsed;Grid.SetRow(why,1);Grid.SetColumnSpan(why,2);grid.Children.Add(why);var card=UI.Card(grid,new(0,0,0,4));card.Padding=new(6,3,6,3);Rows.Children.Add(card);rowViews[BardRotations.VersionKey(row.Song.Identity)]=grid;
                AutomationProperties.SetName(grid,row.Song.Label+" · "+row.Status);
            }
            if(view.Rows.Count==0)Rows.Children.Add(UI.Text("No confirmed repeating set yet. Choose a saved EQ set or review Setup.",12,"SecondaryText"));scroll.ScrollToVerticalOffset(offset);ArrangeRows();
        }
        status.Text=view.Conditions.FirstOrDefault()??"Comparison uses supported group coverage";status.ToolTip=string.Join("\n",view.Conditions);
    }
    private void ArrangeRows()
    {
        bool narrow=ActualWidth<620;
        foreach(var grid in rowViews.Values)
        {
            while(grid.RowDefinitions.Count<3)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
            var right=grid.Children[1];var why=grid.Children[2];Grid.SetColumn(right,narrow?0:1);Grid.SetRow(right,narrow?1:0);Grid.SetColumnSpan(grid.Children[0],narrow?2:1);Grid.SetColumnSpan(right,narrow?2:1);Grid.SetRow(why,narrow?2:1);
        }
    }
    private UIElement SetupContent()
    {
        var current=controller.BardPresentation();var body=UI.Stack(UI.Text(current.SetupSummary,11,"SecondaryText"),new BardSongEditor(controller,()=>{complete="";Refresh();}));
        body.Children.Add(FocusedLayout.Disclosure("Reviewed combined modifiers",ModifierEditor(),"Final multipliers already including applicable AAs/focus. Separate effects and rank are never mixed implicitly."));
        body.Children.Add(FocusedLayout.Disclosure("Group landing evidence",BardEvidenceReview.Build(controller,controller.IntegratedSnapshot().Coverage.OrderByDescending(c=>c.At).Take(20)),"Recipient-qualified successful landings and reviewed durations"));return body;
    }
    private UIElement ModifierEditor()
    {
        var state=controller.BardResearchSnapshot();var prior=state.Modifiers.LastOrDefault();var rows=new StackPanel();var boxes=new Dictionary<string,TextBox>();
        var fields=new WrapPanel();foreach(string skill in new[]{"Singing","Brass","Stringed","Percussion","Wind"}){string previous=prior!=null&&prior.FinalMultipliers.TryGetValue(skill,out double old)?old.ToString(CultureInfo.InvariantCulture):"";var box=WorkspaceForms.Control(UI.Input(previous,65),skill+" final multiplier");boxes[skill]=box;fields.Children.Add(UI.Field(skill+" ×",box));}rows.Children.Add(fields);
        var amp=WorkspaceForms.Control(UI.Input(prior?.WithAmplification?.ToString(CultureInfo.InvariantCulture)??"",65));var rank=WorkspaceForms.Control(UI.Input(prior?.AmplificationRank?.ToString()??"",55));var source=WorkspaceForms.Control(UI.Input(prior?.Source??""));source.Width=double.NaN;
        rows.Children.Add(WorkspaceForms.Flow(UI.Field("With Amplification ×",amp),UI.Field("Amp rank",rank)));rows.Children.Add(UI.Field("Evidence",source));var feedback=UI.Text("",11,"SecondaryText");var save=WorkspaceForms.Control(UI.Button("Save modifiers",()=>{}));save.IsEnabled=false;
        string Key()=>PetComparisonEngine.Key(new{Fields=boxes.ToDictionary(k=>k.Key,k=>k.Value.Text.Trim()),Amp=amp.Text.Trim(),Rank=rank.Text.Trim(),Source=source.Text.Trim()});string baseline=Key();
        bool Valid()=>source.Text.Trim().Length>0&&boxes.Values.Append(amp).All(b=>b.Text.Trim().Length==0||double.TryParse(b.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double v)&&double.IsFinite(v)&&v is >=0 and <=100)&&(rank.Text.Trim().Length==0||int.TryParse(rank.Text,out int n)&&n is >=0 and <=1000)&&(amp.Text.Trim().Length==0||rank.Text.Trim().Length>0);
        foreach(var box in boxes.Values.Concat([amp,rank,source]))box.TextChanged+=(_,_)=>save.IsEnabled=Valid()&&Key()!=baseline;
        save.Click+=(_,_)=>{if(!Valid())return;var capture=new BardModifierCapture{Source=source.Text.Trim(),WithAmplification=double.TryParse(amp.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double a)?a:null,AmplificationRank=int.TryParse(rank.Text,out int r)?r:null};foreach(var (skill,box) in boxes)if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value))capture.FinalMultipliers[skill]=value;controller.SaveBardModifier(capture);baseline=Key();save.IsEnabled=false;feedback.Text="Reviewed modifiers saved for this equipment/AA context";Refresh();};rows.Children.Add(save);rows.Children.Add(feedback);return rows;
    }
}
