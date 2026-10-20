using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class UsabilityUiChecks
{
    internal static void Run(TrackerController c, QuestWindow quest, DpsWindow lab, OverlayWindow meter, Action<bool, string> check, string folder)
    {
        IEnumerable<T> Find<T>(DependencyObject root) => Usability.Tree(root).OfType<T>();
        void Flush() { quest.Refresh(); lab.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); quest.UpdateLayout(); lab.UpdateLayout(); }
        var settings = new SettingsWindow(c) { Left = -15000, Top = -15000 }; settings.Show(); Flush();
        foreach (var section in Enum.GetValues<SettingsSection>())
        {
            settings.SelectSection(section); settings.UpdateLayout();
            check(settings.SelectedSection == section && Find<Button>(settings).Count(b => Usability.GetIsPressed(b)) == 1, "Settings rail selects one persistent category · " + section);
            check(Find<Button>(settings).Single(b => b.Content?.ToString() == "Save changes").IsVisible, "Settings footer remains available · " + section);
        }
        settings.SelectSection(SettingsSection.Appearance);
        check(settings.HeadingTitle.Inlines.OfType<System.Windows.Documents.Run>().Any(r => r.Text == "EQL Catalyst" && r.Foreground is LinearGradientBrush), "Settings uses the selected gold serif EQL Catalyst wordmark");
        check(Find<MeterPreview>(settings).Single().IsVisible && Find<ComboBox>(settings).Single(b => b.Items.Contains("Frost Blue")).IsVisible, "Appearance groups live theme, style and meter preview on one page");
        foreach (double width in new[] { 640d, 940 })
        { settings.Width = width; settings.Height = width == 640 ? 420 : 610; settings.UpdateLayout(); var save = Find<Button>(settings).Single(b => b.Content?.ToString() == "Save changes"); check(save.TranslatePoint(new(), settings).Y + save.ActualHeight <= settings.ActualHeight, "Settings footer fits constrained height · " + width); }
        check(Find<Button>(settings.Body).Where(b => b.IsVisible).All(b => b.ToolTip != null && ToolTipService.GetInitialShowDelay(b) == 650), "Visible Settings actions use delayed, disabled-aware tooltips"); settings.Close();

        var pointer = new MenuInteractionState(); var now = DateTime.UtcNow;
        check(!pointer.ShouldDismiss(now, false, false) && !pointer.ShouldDismiss(now.AddMilliseconds(999), false, false) && pointer.ShouldDismiss(now.AddSeconds(1), false, false), "Menu dismissal requires one continuous second outside");
        pointer.Reset(); pointer.ShouldDismiss(now, false, false); check(!pointer.ShouldDismiss(now.AddSeconds(1), true, false) && !pointer.ShouldDismiss(now.AddSeconds(2), false, false), "Submenu or menu re-entry cancels dismissal");
        check(!pointer.ShouldDismiss(now.AddSeconds(20), false, true), "Keyboard navigation suspends pointer dismissal");
        var menu = meter.HeadingTitle.ContextMenu!; menu.PlacementTarget = meter.HeadingTitle; OverlayMenus.Toggle(menu); check(menu.IsOpen, "Options toggle opens menu"); OverlayMenus.Toggle(menu); check(!menu.IsOpen, "Options toggle closes an already open menu");
        var sizeMenu = menu.Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()=="Appearance").Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()=="Window size");
        menu.IsOpen = true; sizeMenu.IsSubmenuOpen = true; Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        check(sizeMenu.Template.FindName("PART_Popup", sizeMenu) is Popup { IsOpen: true }, "Window sizes use an actual themed submenu"); menu.IsOpen = false;
        var selectedActor = menu.Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()=="Display").Items.OfType<MenuItem>().Single(m => m.Header?.ToString() == (c.Profile.OverlayMeter!.Actor == "Player" ? "Self" : c.Profile.OverlayMeter!.Actor)); menu.IsOpen = true;
        check(selectedActor.IsChecked, "Options mark the active actor from shared state"); menu.IsOpen = false;

        lab.Width = 1170; lab.Height = 740; lab.ShowView(0); Find<Expander>(lab).First(e => e.Header?.ToString() == "Filters").IsExpanded = true; Flush();
        var tabs = Find<TabControl>(lab).Single(); double damageY = tabs.TranslatePoint(new(), lab).Y;
        lab.ShowView(4); Flush(); double healingY = tabs.TranslatePoint(new(), lab).Y;
        check(healingY >= damageY && healingY - damageY < 100, $"Mode-specific filters remove reserved Damage space while keeping the shared header ({damageY:0.#} / {healingY:0.#})");
        check(Find<Timeline>(lab).Single().IsVisible && c.HealingView().Chart.Unit == "HPS", "Healing retains a filtered, scaled timeline in the shared shell");
        var date = Find<Button>(lab).Single(b => b.Content?.ToString() == "Date range"); date.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); check(Usability.GetIsPressed(date), "Date range retains its pressed state while open"); date.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(!Find<Button>(lab).Any(b => b.IsVisible && b.Content?.ToString() is "Tag loadout" or "Pet identity" or "Group roster"), "Rare correction actions are removed from the primary toolbar");
        lab.ShowView(0);

        quest.ShowView(TrackerTab.Items); quest.SetZoneFilter("All zones"); quest.SetSearch(""); Flush();
        var table = Find<DataGrid>(quest).Single(g => g.Columns.Any(col => col.Header?.ToString() == "H/N"));
        TableLayout.Reset(table); Flush();
        var tooltipCell = new TextBlock(); tooltipCell.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding("Item")); tooltipCell.DataContext = new { Item = "First item" }; tooltipCell.GetBindingExpression(FrameworkElement.ToolTipProperty)!.UpdateTarget(); Usability.Tooltips(tooltipCell); tooltipCell.DataContext = new { Item = "Next item" }; tooltipCell.GetBindingExpression(FrameworkElement.ToolTipProperty)?.UpdateTarget();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); tooltipCell.GetBindingExpression(FrameworkElement.ToolTipProperty)?.UpdateTarget();
        check(System.Windows.Data.BindingOperations.IsDataBound(tooltipCell, FrameworkElement.ToolTipProperty) && tooltipCell.ToolTip?.ToString() == "Next item", $"Recycled rows preserve tooltip bindings and follow the next item (bound={System.Windows.Data.BindingOperations.IsDataBound(tooltipCell, FrameworkElement.ToolTipProperty)}, value={tooltipCell.ToolTip}, type={tooltipCell.ToolTip?.GetType().Name}, status={tooltipCell.GetBindingExpression(FrameworkElement.ToolTipProperty)?.Status})");
        var second = Find<DataGridColumnHeader>(table).Single(h => h.Column == table.Columns[1]); double x = second.TranslatePoint(new(), table).X, width1 = table.Columns[0].ActualWidth;
        double shrink = Math.Min(20, width1 - table.Columns[0].MinWidth); TableLayout.ResizeColumn(table, table.Columns[0], width1 - shrink); table.UpdateLayout();
        check(Math.Abs(second.TranslatePoint(new(), table).X - x + shrink) < 1 && table.Columns.Where(c => c.Visibility == Visibility.Visible).Sum(c => c.ActualWidth) >= table.ActualWidth - 20, "Resizing Item redistributes available text space within measured minimums"); TableLayout.Reset(table);

        int loadoutIndex = c.Profile.Loadouts.FindIndex(l => l.Name == c.Profile.SelectedLoadout); var saved = System.Text.Json.JsonSerializer.Serialize(c.Profile.ActiveLoadout);
        try
        {
            quest.ShowView(TrackerTab.Pet, "Bloodmoon"); quest.SelectPetDestination(PetInventoryDestination.Planned); Flush();
            var catalog = Find<DataGrid>(quest).Single(g => g.Columns.Any(col => col.Header?.ToString() == "Slots")); catalog.SelectedIndex = 0;
            int beforeConfirmed = c.Profile.ActiveLoadout.PetEquipment.Count, beforePlanned = c.Profile.ActiveLoadout.PetBuilds.Sum(b => b.Items.Count);
            Find<Button>(quest).Single(b => b.IsVisible && b.Content?.ToString() == "Add to planned build").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            check(!Application.Current.Windows.OfType<PetItemDialog>().Any(w => w.IsVisible), "Pet Add commits immediately without opening an editor");
            check(c.Profile.ActiveLoadout.PetBuilds.Sum(b => b.Items.Count) == beforePlanned + 1 && c.Profile.ActiveLoadout.PetEquipment.Count == beforeConfirmed, "Add button updates the selected plan without changing confirmed inventory");
            quest.SelectPetDestination(PetInventoryDestination.Confirmed); Flush(); catalog.SelectedIndex = 0;
            catalog.ContextMenu.Items.OfType<MenuItem>().Single(m => m.Header?.ToString() == "Add to selected list").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Flush();
            check(c.Profile.ActiveLoadout.ConfirmedPetInventory!.Items.Any(i => i.ItemKey == "bloodmoon" && i.ConfirmedAt != null), "Right-click adds a manually confirmed pet item");
            quest.SelectPetDestination(PetInventoryDestination.Planned); Flush();
            var inventoryGrid = Find<DataGrid>(quest).Single(g => g.IsVisible && g.Columns.Any(col => col.Header?.ToString() == "+Rank"));
            { var ctor = typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single(); var drop = (DragEventArgs)ctor.Invoke([new DataObject("EQL.CatalogItem", "bloodmoon"), DragDropKeyStates.None, DragDropEffects.Copy, inventoryGrid, new Point(10, 10)]); drop.RoutedEvent = DragDrop.DropEvent; ((UIElement)inventoryGrid.Parent).RaiseEvent(drop); } Flush();
            check(c.Profile.ActiveLoadout.PetBuilds.Sum(b => b.Items.Count) == beforePlanned + 2, "Catalog drop commits immediately to the selected destination");
            quest.SelectPetDestination(PetInventoryDestination.Confirmed); Find<Expander>(quest).Single(e => e.Header?.ToString() == "Filters & Rank").IsExpanded = true; Flush(); var owned = Find<ComboBox>(quest).Single(b => b.Items.Contains("Character stock")); owned.SelectedItem = "Confirmed pet"; Flush();
            check(catalog.Items.Count > 0, "Confirmed pet ownership filter includes manually entered inventory");
            Find<Expander>(quest).Single(e => e.Header?.ToString() == "Filters & Rank").IsExpanded = false;
        }
        finally { c.Profile.Loadouts[loadoutIndex] = System.Text.Json.JsonSerializer.Deserialize<LoadoutProfile>(saved)!; c.Save(); quest.ShowView(TrackerTab.Queue); Flush(); }
        Json.Write(Path.Combine(folder, "usability-checks.json"), new { SettingsSections = 6, TooltipDelayMs = 650, MenuDismissMs = 1000, DamageTabsY = damageY, HealingTabsY = healingY, InGameAcceptance = "Pending next play session" });
    }
}
