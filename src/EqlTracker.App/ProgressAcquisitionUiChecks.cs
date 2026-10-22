using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class ProgressAcquisitionUiChecks
{
    internal static void Run(TrackerController c, QuestWindow quest, Action<bool, string> check)
    {
        void Flush() { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); quest.UpdateLayout(); }
        string Text() => string.Join('\n', Usability.Tree(quest.ProgressAcquisitionPane).OfType<TextBlock>().Select(t => t.Text));
        var war = c.Catalog.Quests.Single(q => q.Class == "WAR" && q.Kind == "Epic");
        quest.ShowView(TrackerTab.Progress, war.Name); Flush();
        check(quest.SelectProgressNode(n => n.Kind == QuestNodeKind.Quest), "Progress selects the quest root independently of outstanding objectives"); Flush();
        check(war.Steps.All(s => Text().Contains(s.Instructions)), "Quest root retains the full Warrior progression");
        var scroll = Usability.Tree(quest.Body).OfType<ScrollViewer>().Single(s => s.Content is StackPanel stack && stack.Children.Contains(quest.ProgressAcquisitionPane));
        scroll.ScrollToBottom(); Flush();
        check(quest.SelectProgressNode(n => n.ItemKeys.Contains("block of permafrost")), "Progress selects the Warrior collection ingredient"); Flush();
        var pane = quest.ProgressAcquisitionPane;
        check(pane.Children[1] is WrapPanel && pane.Children.OfType<Border>().Any(b=>b.Tag is ItemSource { Zone: "Permafrost" }), "Ingredient source card is immediately below Add to queue");
        check(!Text().Contains("Kargek") && !Text().Contains("Wenden") && !Text().Contains("QUEST PROGRESSION"), "Block of Permafrost omits unrelated turn-ins and the full epic walkthrough");
        check(scroll.VerticalOffset == 0, "Selecting an ingredient resets the acquisition pane to its sources");
        quest.SelectProgressNode(n => n.ItemKeys.Contains("finely crafted dragon head hilt")); Flush();
        check(Text().Contains("Give the three items to Wenden Blackhammer") && Text().Contains("Severely Damaged Dragon Head Hilt") && Text().Contains("Rejesiam Ore") && Text().Contains("Ball of Everliving Golem"), "Intermediate detail retains its own turn-in and required inputs");
        check(!Text().Contains("Kargek") && !Text().Contains("Mentrax") && !Text().Contains("Denken"), "Intermediate detail omits sibling steps and the input's separate walkthrough");
        quest.ShowView(TrackerTab.Progress, "Magician Epic Quest"); quest.SelectProgressNode(n => n.ItemKeys.Contains("cyclops toes")); Flush();
        check(!Text().Contains("QUEST PROGRESSION") && pane.Children.OfType<Border>().Any(b=>b.Tag is ItemSource), "Covered Mage ingredients still show their sources first");
        quest.SelectProgressNode(n => n.ItemKeys.Contains("broom of trilon")); Flush();
        check(Text().Contains("Requires:") && Text().Contains("Cyclops Toes") && Text().Contains("Bank"), "Owned Mage intermediate keeps its local recipe and ownership evidence");
        quest.ShowView(TrackerTab.Items);
    }
}
