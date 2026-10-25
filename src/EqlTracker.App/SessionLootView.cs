using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private bool queueLootMode, updatingLoot;
    private ComboBox queueModeChoice = null!;
    private UIElement queueGoalsBody = null!, lootBody = null!;
    private WrapPanel lootActions = null!;
    private readonly TextBox lootSearch = UI.Input("", 210);
    private readonly ComboBox lootSession = new() { Width = 180, DisplayMemberPath = "Label" };
    private readonly ListBox lootList = new();
    private readonly TextBlock lootSummary = UI.Text("", 11, "Mint");
    private string lootKey = "", lootSessionKey = "";
    private UIElement BuildLootActions()
    {
        lootActions = new() { Visibility = Visibility.Collapsed };
        lootActions.Children.Add(FocusedLayout.Search(lootSearch, "Search session loot…")); lootActions.Children.Add(lootSession);
        lootActions.Children.Add(UI.Button("Older receipts", () => _ = Controller.LoadOlderJournal()));
        lootSearch.TextChanged += (_, _) => RefreshLoot(); lootSession.SelectionChanged += (_, _) => { if (!updatingLoot) RefreshLoot(); };
        return lootActions;
    }
    private UIElement BuildLootBody()
    {
        var body = new DockPanel { Visibility = Visibility.Collapsed }; DockPanel.SetDock(lootSummary, Dock.Bottom); body.Children.Add(lootSummary); body.Children.Add(lootList); lootBody = body;
        var template = new DataTemplate(); var stack = new FrameworkElementFactory(typeof(StackPanel)); stack.SetValue(MarginProperty, new Thickness(5));
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Name")); name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); stack.AppendChild(name);
        var quantity = new FrameworkElementFactory(typeof(TextBlock)); quantity.SetBinding(TextBlock.TextProperty, new Binding("Quantity") { StringFormat = "{0} received" }); quantity.SetValue(TextBlock.FontSizeProperty, 11d); stack.AppendChild(quantity);
        var purpose = new FrameworkElementFactory(typeof(TextBlock)); purpose.SetBinding(TextBlock.TextProperty, new Binding("Purpose")); purpose.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); purpose.SetValue(TextBlock.FontSizeProperty, 11d); stack.AppendChild(purpose); template.VisualTree = stack; lootList.ItemTemplate = template;
        lootList.SelectionChanged += (_, _) => { if (!updatingLoot) ShowLootInspector(); }; ScrollViewer.SetHorizontalScrollBarVisibility(lootList, ScrollBarVisibility.Disabled);
        return body;
    }
    private void SwitchQueueMode(bool loot, UIElement goals)
    {
        queueLootMode = loot; goals.Visibility = queueGoalsBody.Visibility = loot ? Visibility.Collapsed : Visibility.Visible;
        lootActions.Visibility = lootBody.Visibility = loot ? Visibility.Visible : Visibility.Collapsed;
        if (loot) RefreshLoot(); else { queueInputRevision = ""; RefreshQueue(); ShowQueueInspector(); }
    }
    internal void OpenSessionLoot(string sessionId)
    {
        ShowView(TrackerTab.Queue); queueModeChoice.SelectedIndex = 1; RefreshLoot();
        lootSession.SelectedItem = lootSession.Items.OfType<SessionChoice>().FirstOrDefault(s => s.Id == sessionId); RefreshLoot();
    }
    private void RefreshLoot()
    {
        if (!queueLootMode) return;
        string sessionsKey = Controller.Sessions?.Revision.ToString() ?? "";
        if (sessionsKey != lootSessionKey && !lootSession.IsDropDownOpen)
        {
            string? old = (lootSession.SelectedItem as SessionChoice)?.Id; updatingLoot = true;
            lootSession.ItemsSource = Controller.Sessions?.Choices ?? []; lootSession.SelectedItem = lootSession.Items.OfType<SessionChoice>().FirstOrDefault(s => s.Id == old) ?? lootSession.Items.OfType<SessionChoice>().FirstOrDefault(); updatingLoot = false; lootSessionKey = sessionsKey;
        }
        string id = (lootSession.SelectedItem as SessionChoice)?.Id ?? ""; string key = Controller.JournalRevision + "|" + id + "|" + lootSearch.Text;
        if (key == lootKey) return; lootKey = key;
        var session = Controller.Sessions?.Sessions.FirstOrDefault(s => s.Id == id);
        var rows = ActivityJournal.Loot(Controller.JournalSnapshot(), session, lootSearch.Text.Trim());
        string? selected = (lootList.SelectedItem as LootReceiptGroup)?.Identity; double offset = UI.FindScroll(lootList)?.VerticalOffset ?? 0;
        updatingLoot = true; lootList.ItemsSource = rows; lootList.SelectedItem = rows.FirstOrDefault(r => r.Identity == selected) ?? rows.FirstOrDefault(); updatingLoot = false;
        Dispatcher.BeginInvoke(()=>UI.FindScroll(lootList)?.ScrollToVerticalOffset(offset)); lootSummary.Text = rows.Count + " items · " + (session?.Inferred == true ? "Incomplete login history" : "Recorded acquisitions"); ShowLootInspector();
    }
    private void ShowLootInspector()
    {
        queueInspector.Children.Clear(); if (lootList.SelectedItem is not LootReceiptGroup row) { queueInspector.Children.Add(UI.Text("No recorded acquisitions in this selection.", 12, "Muted")); return; }
        queueInspector.Children.Add(UI.Text(row.Name, 16, "Accent", true)); queueInspector.Children.Add(UI.Text(row.Quantity + " acquired · " + row.Disposition, 12, "Mint"));
        queueInspector.Children.Add(UI.Text("Acquisition history · present ownership is separate.", 11, "SecondaryText"));
        var recorded=Controller.IntegratedSnapshot().Dispositions;var dispositions=row.Receipts.Select(r=>recorded.GetValueOrDefault(r.Id,"")).Where(t=>t.Length>0).Distinct().ToArray();if(dispositions.Length>0)queueInspector.Children.Add(UI.Text("Player recorded: "+string.Join(" / ",dispositions),11,"Mint"));
        foreach (var purpose in row.Receipts.SelectMany(r => r.Purposes).DistinctBy(p => (p.Kind, p.Id)))
            queueInspector.Children.Add(UI.Button(purpose.Label, () => ShowView(purpose.Kind == "Progress" || purpose.Kind == "Quest" ? TrackerTab.Progress : purpose.Kind == "Exaltation" ? TrackerTab.Exaltations : TrackerTab.Gear, purpose.Kind == "Progress" ? Controller.Catalog.Quests.FirstOrDefault(q => q.Id == purpose.Id)?.Name ?? purpose.Label : row.Name)));
        var history = new StackPanel();
        foreach (var receipt in row.Receipts) history.Children.Add(UI.Text($"{receipt.At:MMM dd HH:mm:ss} · {receipt.Quantity} × · {receipt.Disposition}\n{receipt.Zone} · {receipt.Source}{(receipt.Result.Length > 0 ? " → " + receipt.Result : "")}{(!receipt.PurposeRecorded ? "\nGoal purpose was not recorded at acquisition." : "")}", 11));
        queueInspector.Children.Add(FocusedLayout.Disclosure("Receipt history (" + row.Receipts.Count + ")", history, "Independent receipts retain their log identities, even in the same second"));
        var annotations=Controller.IntegratedSnapshot().Dispositions;var receipts=new ComboBox{ItemsSource=row.Receipts,DisplayMemberPath="At",SelectedIndex=0,MinWidth=110};var disposition=UI.Select(["Unknown","Kept","Banked","Given","Sold","Merged","Used"],"Unknown",100);var note=UI.Text("",11,"SecondaryText");
        void Selection(){if(receipts.SelectedItem is JournalActivity receipt){string text=annotations.GetValueOrDefault(receipt.Id,"");disposition.SelectedItem=text.Length>0?text:"Unknown";note.Text="Observed: "+receipt.Disposition+(text.Length>0?" · Player recorded: "+text:"");}}receipts.SelectionChanged+=(_,_)=>Selection();Selection();
        queueInspector.Children.Add(FocusedLayout.Disclosure("Record disposition",UI.Stack(receipts,disposition,UI.Button("Save recorded disposition",()=>{if(receipts.SelectedItem is not JournalActivity receipt)return;var setup=Controller.IntegratedSnapshot();setup.Dispositions[receipt.Id]=disposition.SelectedItem?.ToString()??"Unknown";Controller.SaveIntegrated(setup);annotations=setup.Dispositions;Selection();}),note),"Personal annotation only; original acquisition evidence and inventory allocation remain unchanged"));
        if (Controller.Catalog.FindKey(row.ItemKey) is {} item) { queueInspector.Children.Add(UI.Text(Controller.Inventory.Ownership(item), 11, "Mint")); AddFocusedSources(queueInspector, item.Sources, item); var stock = new OtherHoldingsView(); stock.Show(Controller, item); queueInspector.Children.Add(stock); }
    }
}
public partial class App
{
    internal void OpenSessionLoot(string id) { OpenQuests(); quests?.OpenSessionLoot(id); }
}
