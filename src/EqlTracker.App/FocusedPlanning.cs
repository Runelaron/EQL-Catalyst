using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private Expander itemFilters = null!;
    private readonly TextBox queueSearch = UI.Input("", 250);
    private readonly ComboBox queueState = UI.Select(["All goals", "Pending", "Satisfied", "Paused"], "All goals", 115);
    private readonly StackPanel queueInspector = new();
    private readonly TextBlock queueSummary = UI.Text("", 11, "Mint");
    private string queueSelectedItem = "";
    private UIElement BuildFocusedQueue()
    {
        var root = new DockPanel();
        var actions = new WrapPanel();
        actions.Children.Add(FocusedLayout.Search(queueSearch, "Search goals, items or zones…"));
        actions.Children.Add(FocusedLayout.Menu("+ Add goal ▾",
            ("Browse quests", () => ShowView(TrackerTab.Quests), "Choose a quest or individual armor reward, then add it to your queue."),
            ("Search items", () => ShowView(TrackerTab.Gear), "Find equipment, compare it and queue the acquisition."),
            ("Browse Exaltations", () => ShowView(TrackerTab.Exaltations), "Queue an effect without claiming it has been extracted.")));
        actions.Children.Add(queueState);
        var more = FocusedLayout.Menu("More ▾",
            ("Move up", () => MoveQueue(-1), "Change saved queue order; inventory allocation priority is separate."),
            ("Move down", () => MoveQueue(1), "Change saved queue order; inventory allocation priority is separate."),
            ("Pause / resume quest", ToggleQueuedQuest, "Pausing untracks the quest while preserving its pin and progress."),
            ("Remove pin", RemovePin, "Remove this queue entry. Quest tracking and inventory remain intact."));
        actions.Children.Add(more);
        var heading = UI.Stack(actions);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        
        var emptyActions = new WrapPanel(); emptyActions.Children.Add(UI.Button("Browse quests", () => ShowView(TrackerTab.Quests))); emptyActions.Children.Add(UI.Button("Search items", () => ShowView(TrackerTab.Gear)));
        var empty = UI.Stack(queueNoteState, emptyActions); empty.Margin = new(6); queueEmptyActions = emptyActions;
        var body = new DockPanel(); DockPanel.SetDock(empty, Dock.Top); body.Children.Add(empty); DockPanel.SetDock(queueSummary,Dock.Bottom);body.Children.Add(queueSummary); body.Children.Add(queueList);
        root.Children.Add(UI.Split(TrackerLeftPane(body), FocusedLayout.FloatingInspector("Next action & sources", queueInspector), 320, Controller.Profile, "queue-focused", 280, 180));
        queueSearch.ToolTip = "Search your pinned goals and their remaining ingredients. Clearing the search restores your saved order.";
        queueState.ToolTip = "Filter the view without pausing goals or changing saved queue order.";
        queueList.SelectionChanged += (_, _) => { queueSelectedItem = ""; ShowQueueInspector(); };
        queueSearch.TextChanged += (_, _) => { queueSignature = ""; if (!initializing) RefreshQueue(); };
        queueState.SelectionChanged += (_, _) => { queueSignature = ""; if (!initializing) RefreshQueue(); };
        queueList.ContextMenu = more.ContextMenu;
        ScrollViewer.SetHorizontalScrollBarVisibility(queueList, ScrollBarVisibility.Disabled);
        return root;
    }
    private WrapPanel queueEmptyActions = null!;
    private void ToggleQueuedQuest()
    {
        if (queueList.SelectedItem is not QueueEntry entry || Controller.Catalog.Quests.FirstOrDefault(q => q.Id == entry.Goal.QuestId) is not { } quest) return;
        QuestTracking.Track(Controller.Profile, quest, !QuestTracking.Selected(quest, Controller.Profile, Controller.Inventory, Controller.Catalog)); Changed();
    }
    private void ConfigureFocusedQueueTemplate()
    {
        var template = new DataTemplate(typeof(QueueEntry)); var expander = new FrameworkElementFactory(typeof(Expander));
        var title = new FrameworkElementFactory(typeof(StackPanel));
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Goal.Name")); name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); name.SetValue(TextBlock.FontSizeProperty, 13d); title.AppendChild(name);
        var state = new FrameworkElementFactory(typeof(TextBlock)); state.SetBinding(TextBlock.TextProperty, new Binding { Converter = new FocusedConverter(value => value is QueueEntry entry ? NextQueueAction(entry) : "") }); state.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); state.SetValue(TextBlock.FontSizeProperty, 11d); state.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); title.AppendChild(state);
        expander.SetValue(HeaderedContentControl.HeaderTemplateProperty, new DataTemplate { VisualTree = title }); expander.SetBinding(HeaderedContentControl.HeaderProperty, new Binding());
        expander.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);
        expander.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => { if (sender is Expander { DataContext: QueueEntry row } e) e.IsExpanded = Controller.Profile.ExpandedQueueEntries.Contains(row.Goal.Id); }));
        expander.AddHandler(Expander.ExpandedEvent, new RoutedEventHandler((sender, e) => { if (e.OriginalSource == sender && sender is Expander { DataContext: QueueEntry row }) Controller.Profile.ExpandedQueueEntries.Add(row.Goal.Id); }));
        expander.AddHandler(Expander.CollapsedEvent, new RoutedEventHandler((sender, e) => { if (e.OriginalSource == sender && sender is Expander { DataContext: QueueEntry row }) Controller.Profile.ExpandedQueueEntries.Remove(row.Goal.Id); }));
        var ingredients = new FrameworkElementFactory(typeof(ItemsControl)); ingredients.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Ingredients") { Converter = new FocusedConverter(value => value is IEnumerable<ObjectiveRow> rows ? rows.Where(r => r.Remaining > 0).OrderByDescending(r => QuestPlanner.InZone(r, Controller.Profile.Zone, Controller.Profile.CurrentExpansion)).ToList() : null) });
        var button = new FrameworkElementFactory(typeof(Button)); var line = new MultiBinding { StringFormat = "{0}    {1} · {2}" }; foreach (string path in new[] { "Item", "Quantity", "Zone" }) line.Bindings.Add(new Binding(path));
        var ingredientText = new FrameworkElementFactory(typeof(TextBlock)); ingredientText.SetBinding(TextBlock.TextProperty, line); ingredientText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); ingredientText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        button.SetBinding(ContentControl.ContentProperty, new Binding()); button.SetValue(ContentControl.ContentTemplateProperty, new DataTemplate { VisualTree = ingredientText });
        button.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left); button.SetValue(Control.FontSizeProperty, 11d); button.SetBinding(ToolTipProperty, new Binding("Instructions"));
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, e) => {
            if (sender is not FrameworkElement { DataContext: ObjectiveRow row }) return;
            var entry = queueList.Items.OfType<QueueEntry>().FirstOrDefault(q => q.Ingredients.Any(i => i.QuestId == row.QuestId && i.Key == row.Key));
            if (entry != null) queueList.SelectedItem = entry; queueSelectedItem = row.QuestId + "|" + row.Key; ShowQueueInspector(); e.Handled = true;
        }));
        ingredients.SetValue(ItemsControl.ItemTemplateProperty, new DataTemplate { VisualTree = button }); expander.AppendChild(ingredients);
        var card = new FrameworkElementFactory(typeof(Border)); card.SetValue(Border.CornerRadiusProperty, new CornerRadius(3)); card.SetValue(Border.PaddingProperty, new Thickness(6)); card.SetResourceReference(Border.BackgroundProperty, "InnerSurface"); card.AppendChild(expander); template.VisualTree = card; queueList.ItemTemplate = template;
        var container = new Style(typeof(ListBoxItem)); container.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); container.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2))); container.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Foreground")));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Selection"))); container.Triggers.Add(selected); queueList.ItemContainerStyle = container;
        VirtualizingPanel.SetScrollUnit(queueList, ScrollUnit.Pixel); ScrollViewer.SetCanContentScroll(queueList, true);
    }
    private sealed class FocusedConverter(Func<object?, object?> convert) : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => convert(value);
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
    private string NextQueueAction(QueueEntry entry)
    {
        if (entry.Status == "Paused") return "Paused · resume from More";
        var next = entry.Ingredients.FirstOrDefault(i => i.Remaining > 0 && QuestPlanner.InZone(i, Controller.Profile.Zone, Controller.Profile.CurrentExpansion)) ?? entry.Ingredients.FirstOrDefault(i => i.Remaining > 0);
        return next == null ? entry.Status : entry.Status + " · Next: " + next.Item;
    }
    private void ShowQueueInspector()
    {
        queueInspector.Children.Clear();
        if (queueList.SelectedItem is not QueueEntry entry) { queueInspector.Children.Add(UI.Text("Select a goal to see its next requirement and sources.", 12, "Muted")); return; }
        var row = entry.Ingredients.FirstOrDefault(i => i.QuestId + "|" + i.Key == queueSelectedItem)
            ?? entry.Ingredients.FirstOrDefault(i => i.Remaining > 0 && QuestPlanner.InZone(i, Controller.Profile.Zone, Controller.Profile.CurrentExpansion))
            ?? entry.Ingredients.FirstOrDefault(i => i.Remaining > 0) ?? entry.Objective;
        queueInspector.Children.Add(UI.Label(entry.Goal.Name));
        queueInspector.Children.Add(UI.Text(row?.Item ?? entry.Goal.Name, 16, "Accent", true));
        queueInspector.Children.Add(UI.Text(entry.Status, 11, "Mint"));
        queueInspector.Children.Add(UI.Button("View sources", () => BrowseQueueEntry(entry, row), true));
        if (row != null)
        {
            AddFocusedSources(queueInspector, row.Sources,Controller.Catalog.FindKey(row.Key));
            if (Controller.Catalog.FindKey(row.Key) is { } item) queueInspector.Children.Add(UI.Text(Controller.Inventory.Ownership(item), 11, "Mint"));
            if (row.ParentReward.Length > 0) queueInspector.Children.Add(FocusedLayout.Disclosure("Parent reward · " + row.ParentReward, UI.Text(row.Instructions, 11), "The selected ingredient can often be collected before later turn-ins."));
        }
        else if (Controller.Catalog.FindKey(entry.Goal.ItemKey) is { } item)
        { AddFocusedSources(queueInspector, item.Sources,item); queueInspector.Children.Add(UI.Text(Controller.Inventory.Ownership(item), 11, "Mint")); }
        else queueInspector.Children.Add(UI.Text("No outstanding ingredient list. Browse the goal for its checklist or acquisition notes.", 11, "Muted"));
        Usability.Tooltips(queueInspector);
    }
    private void BrowseQueueEntry(QueueEntry entry, ObjectiveRow? row)
    {
        if (Controller.Catalog.Quests.FirstOrDefault(q => q.Id == entry.Goal.QuestId) is { } quest)
        { ShowView(TrackerTab.Progress, quest.Name); if (row != null) SelectProgressNode(n => n.ItemKeys.Contains(row.Key)); }
        else if (entry.Goal.Kind == "Exaltation") ShowView(TrackerTab.Exaltations, Controller.Catalog.FindKey(entry.Goal.ItemKey)?.Name ?? entry.Goal.Name);
        else ShowView(TrackerTab.Gear, Controller.Catalog.FindKey(entry.Goal.ItemKey)?.Name ?? entry.Goal.Name);
    }
    private void AddFocusedSources(StackPanel pane,IEnumerable<ItemSource> sources,ItemDefinition? item=null)
    {
        var rows=sources.OrderByDescending(s=>Identity.ZoneKey(s.Zone)==Identity.ZoneKey(Controller.Profile.Zone)).ToList();
        if(rows.Count==0){pane.Children.Add(UI.Text("Source information pending · see the wiki reference.",11,"Muted"));return;}
        var scoped=new ItemDefinition{Key=item?.Key??"focused-source",Name=item?.Name??"Selected item",Aliases=item?.Aliases??[],Sources=rows};
        foreach(var card in QuestPresentationView.Sources(ProgressPresentation.SourcesFor([scoped.Key],new(){Items=[scoped]})))pane.Children.Add(card);
    }
    internal void OpenItemFilters() => itemFilters.IsExpanded = true;
}
