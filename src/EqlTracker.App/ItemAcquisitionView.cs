using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class ItemAcquisitionView : StackPanel
{
    private string key = "";
    internal void Show(ItemDefinition item, Catalog catalog, InventorySnapshot inventory)
    {
        var rows = ItemAcquisition.For(item, catalog, inventory);
        string next = PetComparisonEngine.Key(new{rows,item.Sources,item.Revision,item.MetadataVersion}); if (next == key) return; key = next;
        Children.Clear(); Children.Add(UI.Text("Obtain From", 12, "Melee", true));
        UIElement Fact(AcquisitionFact row)
        {
            var text = UI.Text(row.Kind == "Inventory" ? row.Detail : row.Kind + ": " + row.Name + (row.Zone.Length > 0 ? " · " + row.Zone : ""), 11);
            text.ToolTip = row.Detail + (row.Url.Length > 0 ? "\n" + row.Url : "");
            if (row.Kind == "Inventory") { var icon = new ItemRoleGlyph { Role = "inventory", Width = 20, Height = 20, Margin = new(0, 0, 5, 0), ToolTip = row.Detail }; return UI.Row(icon, text); }
            return text;
        }
        foreach (var row in rows.Where(r=>r.Kind is "Inventory" or "Quest")) Children.Add(Fact(row));
        var sources=QuestPresentationView.Sources(ProgressPresentation.SourcesFor([item.Key],catalog).Where(s=>s.Source.Method!="Quest")).ToArray();
        foreach(var card in sources.Take(2))Children.Add(card);
        if(sources.Length>2)Children.Add(FocusedLayout.Disclosure("More sources ("+(sources.Length-2)+")",UI.Stack(sources.Skip(2).ToArray()),"Additional acquisition zones and evidence"));
        if (rows.Count == 0) Children.Add(UI.Text("Acquisition not documented in the reviewed sources.", 11, "SecondaryText"));
        var notes=PublicContentNotes.Item(item.Key).ToList();if(item.AvailabilityEvidence==WikiContentQuality.QuestRewardConflict&&!notes.Any(n=>n.Contains("Source disagreement")))notes.Add(item.AvailabilityEvidence+" "+item.WikiUrl);if(notes.Count>0)Children.Add(FocusedLayout.Disclosure("Source notes",UI.Stack(notes.Select(n=>(UIElement)UI.Text(n,11,"SecondaryText")).ToArray()),"Revision differences and unresolved acquisition claims"));
        Margin = new(0, 4, 0, 6); Usability.Tooltips(this);
    }
    internal static ItemAcquisitionView Create(ItemDefinition item)
    {
        var view = new ItemAcquisitionView(); if (Application.Current is App app) view.Show(item, app.Controller.Catalog, app.Controller.Inventory); return view;
    }
}
