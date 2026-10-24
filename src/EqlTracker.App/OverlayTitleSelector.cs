using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace EqlTracker.App;

public sealed partial class OverlayWindow
{
    private Button? titleDisplaySelector;
    private void BuildTitleDisplaySelector()
    {
        var menu = new ContextMenu();
        FillDisplayChoices(menu);
        OverlayMenuDensity.Attach(menu, Controller.Profile);
        var button = titleDisplaySelector = OverlayMenus.Button(menu, "Choose DPS display", "", timedDismissal: true);
        button.Tag = "overlay-title-display-selector";
        button.Width = button.Height = double.NaN; button.MinWidth = button.MinHeight = 0;
        button.Margin = new(0); button.Padding = new(1, 0, 1, 0);
        button.HorizontalAlignment = HorizontalAlignment.Stretch; button.VerticalAlignment = VerticalAlignment.Stretch;
        button.Cursor = Cursors.Arrow;
        badge.VerticalAlignment = VerticalAlignment.Center; badge.HorizontalAlignment = HorizontalAlignment.Center;
        ToolTipService.SetIsEnabled(badge, false); button.Content = badge;
        button.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Down or Key.F4 || e.Key == Key.System && e.SystemKey == Key.Down)
            { if (!menu.IsOpen) OverlayMenus.Toggle(menu, button); e.Handled = true; }
        };
        menu.Opened += (_, _) =>
        {
            if (InputManager.Current.MostRecentInputDevice is KeyboardDevice)
                Dispatcher.BeginInvoke(() => menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.IsChecked)?.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        };
        HeaderIcon.Content = button; HeaderIcon.Visibility = Visibility.Visible;
        RefreshTitleDisplaySelector();
    }
    private void RefreshTitleDisplaySelector()
    {
        if (titleDisplaySelector == null) return;
        var selection = Controller.Profile.OverlayMeter!;
        string actor = selection.DisplayMode == EqlTracker.Core.OverlayCombatMode.Group ? "Group" : selection.Actor == "Player" ? "Self" : selection.Actor;
        titleDisplaySelector.ToolTip = "Main display: " + actor + ". Choose Pet, Self, Combined or Group. The secondary readout stays unchanged.";
        AutomationProperties.SetName(titleDisplaySelector, "DPS display: " + actor);
        AutomationProperties.SetHelpText(titleDisplaySelector, "Choose the main Pet, Self, Combined or Group display independently of the secondary readout. Down arrow opens the choices.");
    }
    private void SizeTitleDisplaySelector(bool compact)
    {
        if (titleDisplaySelector == null) return;
        HeaderIcon.Width = compact ? 20 : 24; HeaderIcon.Height = compact ? 17 : 24;
        HeaderIcon.Margin = new(0, 0, compact ? 3 : 6, 0);
        badge.Width = badge.Height = compact ? 14 : 20;
    }
}
