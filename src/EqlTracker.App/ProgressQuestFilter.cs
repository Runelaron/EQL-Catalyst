using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private void FilterProgressQuests()
    {
        string key = $"{progressSearch.Text}|{progressTracked.IsChecked}|{Controller.QuestRevision}|{Controller.Catalog.Quests.Count}";
        if (progressQuestFilter == key) return; progressQuestFilter = key;
        string? selected = questSelection.SelectedItem?.ToString(); string query = progressSearch.Text.Trim();
        var quests = Controller.Catalog.Quests.Where(q => (progressTracked.IsChecked != true || QuestTracking.Selected(q, Controller.Profile, Controller.Inventory, Controller.Catalog)) && (q.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || (Controller.Catalog.FindKey(q.RewardKey)?.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))).OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase).ToList();
        bool was = initializing; initializing = true; questSelection.ItemsSource = quests.Select(q => q.Name).ToList(); questSelection.SelectedItem = quests.Any(q => q.Name == selected) ? selected : quests.FirstOrDefault()?.Name; initializing = was;
    }
}
