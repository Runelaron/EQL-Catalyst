using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EqlTracker.Core;
namespace EqlTracker.App;

internal static class QuestPresentationView
{
    internal static StackPanel Instruction(QuestInstructionBlock block,Catalog catalog,bool allInputs=false)
    {
        var panel=new StackPanel{Tag=block.Step.Id};
        if(block.Heading.Length>0)panel.Children.Add(UI.Text(block.Heading,12,"Foreground",true));
        if(block.Instruction.Length>0)panel.Children.Add(UI.Text(block.Instruction,12));
        var step=block.Step;
        if(allInputs&&step.RouteGroup.Length>0)panel.Children.Add(UI.Text("Route: "+step.Route,11,"SecondaryText"));
        if(step.Inputs.Count>0 && (allInputs || step.Outputs.Count>0)) panel.Children.Add(UI.Text("Requires: "+string.Join(" · ",step.Inputs.Select(i=>$"{i.Count} × {catalog.FindKey(i.ItemKey)?.Name??i.ItemKey}"+(!i.Consumed?" (retained tool)":""))),11,"Muted"));
        if(step.RandomReward)panel.Children.Add(UI.Text("Random reward · ingredients per attempt.",12,"Melee"));
        if(step.Container.Length>0)panel.Children.Add(UI.Text("Container: "+step.Container,12));
        foreach(string text in step.Conditions.Select(c=>c.ToString()).DistinctBy(QuestPresentation.Normalize))panel.Children.Add(UI.Text(text,12,"Mint"));
        return panel;
    }
    internal static Border Source(QuestSourceBlock block)
    {
        var source=block.Source;var content=new StackPanel();
        content.Children.Add(UI.Text(block.Method.Equals("Drop",StringComparison.OrdinalIgnoreCase)?block.Actor:block.Method,13,"Foreground",true));
        content.Children.Add(UI.Text(block.Zone,12));
        if(block.Method.Equals("Drop",StringComparison.OrdinalIgnoreCase))content.Children.Add(UI.Text("Drop",11,"SecondaryText"));
        else if(block.Method!="Ground spawn"&&block.Actor!="Source not documented")content.Children.Add(UI.Text(block.Actor,12));
        foreach(string note in block.Additional)content.Children.Add(UI.Text(note,11,"SecondaryText"));
        var evidence=SourcePresentation.Evidence(block.Evidence);
        content.Children.Add(Evidence(block.Evidence));
        if(Application.Current is App app)content.Children.Add(new CampNotebookView(app.Controller,source));
        var card=UI.Card(content);card.Tag=source;card.ToolTip=evidence;return card;
    }
    private static Expander Evidence(IEnumerable<ItemSource> records)
    {
        var rows=records.ToArray();var content=UI.Stack(UI.Text(SourcePresentation.Evidence(rows),11,"SecondaryText"));
        foreach(string url in rows.Select(s=>s.WikiUrl).Where(u=>u.Length>0).Distinct())content.Children.Add(UI.Button("Open source wiki",()=>UI.OpenUrl(url)));
        return FocusedLayout.Disclosure("Evidence",content,"Source revisions, availability and conflicting evidence");
    }
    internal static IEnumerable<Border> Sources(IEnumerable<QuestSourceBlock> input)
    {
        foreach(var item in input.GroupBy(s=>s.Item.Key))
        {
            bool compact=item.Where(s=>s.Method is "Drop" or "Purchase").Count()>1;
            foreach(var zone in item.GroupBy(s=>s.Method is "Drop" or "Purchase"&&compact?Identity.ZoneKey(s.Source.Zone)+"|"+s.Method:"single|"+AcquisitionPresentation.ActorKey(s.Source.Mob)+"|"+s.Method+"|"+AcquisitionPresentation.CoordinateKey(s.Source)))
            {
                var actors=zone.ToArray();
                if(!compact||actors[0].Method is not ("Drop" or "Purchase")){foreach(var actor in actors)yield return Source(actor);continue;}
                var content=new StackPanel();content.Children.Add(UI.Text(actors[0].Zone,13,"Foreground",true));
                var details=new StackPanel();
                void Show(QuestSourceBlock selected){details.Children.Clear();details.Children.Add(UI.Text(selected.Method=="Purchase"?"Purchase":"Drop",11,"SecondaryText"));foreach(string note in selected.Additional)details.Children.Add(UI.Text(note,11,"SecondaryText"));details.Children.Add(Evidence(selected.Evidence));if(Application.Current is App app)details.Children.Add(new CampNotebookView(app.Controller,selected.Source));Usability.Tooltips(details);}
                if(actors.Length>1)
                {
                    var select=new ComboBox{Width=165,Height=28,HorizontalAlignment=HorizontalAlignment.Left,ItemsSource=actors.OrderBy(a=>a.Actor,StringComparer.OrdinalIgnoreCase).ToArray(),DisplayMemberPath="Actor",ToolTip=actors[0].Method=="Purchase"?"Choose a documented vendor":"Choose a documented mob"};
                    var style=new Style(typeof(ComboBoxItem),Application.Current.TryFindResource(typeof(ComboBoxItem)) as Style);style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding("Actor")));select.ItemContainerStyle=style;
                    content.Children.Add(UI.Label(actors[0].Method=="Purchase"?"Vendors":"Mobs"));content.Children.Add(select);select.SelectionChanged+=(_,_)=>{if(select.SelectedItem is QuestSourceBlock selected)Show(selected);};select.SelectedIndex=0;
                }
                else{content.Children.Add(UI.Text(actors[0].Actor,12));Show(actors[0]);}
                content.Children.Add(details);var card=UI.Card(content);card.Tag=actors;card.ToolTip=$"{actors[0].Zone} · {actors.Length} documented "+(actors[0].Method=="Purchase"?"vendors":"mobs");yield return card;
            }
        }
    }
}
