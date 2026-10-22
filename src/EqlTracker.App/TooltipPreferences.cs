using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

public static partial class Usability
{
    private static readonly List<WeakReference<FrameworkElement>> tooltipElements = [];
    private static TooltipLevel tooltipLevel = TooltipLevel.Essential;
    private static int tooltipDelay = EqlTracker.Core.TooltipPreferences.DefaultDelay;
    private static bool tooltipOpeningHook;
    internal static readonly DependencyProperty RoutineTooltipProperty = DependencyProperty.RegisterAttached("RoutineTooltip", typeof(bool), typeof(Usability), new PropertyMetadata(false));
    internal static void RoutineTooltip(DependencyObject element) => element.SetValue(RoutineTooltipProperty, true);
    internal static bool IsRoutineTooltip(DependencyObject element) => (bool)element.GetValue(RoutineTooltipProperty) || element is Slider or Thumb or GridSplitter or ScrollBar || element is TextBlock { Text: "⠿" or "⋮" or "⋮⋮" or "⋮⋮⋮" };
    private static void TooltipPolicy(FrameworkElement element)
    {
        ToolTipService.SetInitialShowDelay(element, tooltipDelay);
        ToolTipService.SetBetweenShowDelay(element, tooltipDelay);
        ToolTipService.SetIsEnabled(element, tooltipLevel != TooltipLevel.None && (tooltipLevel == TooltipLevel.All || !IsRoutineTooltip(element)));
    }
    internal static void UpdateTooltipPreferences(CharacterProfile profile)
    {
        tooltipLevel = Enum.IsDefined(profile.Tooltips) ? profile.Tooltips : TooltipLevel.Essential;
        tooltipDelay = EqlTracker.Core.TooltipPreferences.Delay(profile.TooltipDelayMilliseconds);
        if (!tooltipOpeningHook)
        {
            tooltipOpeningHook = true;
            EventManager.RegisterClassHandler(typeof(FrameworkElement), ToolTipService.ToolTipOpeningEvent, new ToolTipEventHandler((sender, e) =>
            {
                bool routine = sender is DependencyObject owner && IsRoutineTooltip(owner);
                for (DependencyObject? node = Mouse.DirectlyOver as DependencyObject; !routine && node is Visual; node = VisualTreeHelper.GetParent(node)) routine = IsRoutineTooltip(node);
                if (tooltipLevel == TooltipLevel.None || tooltipLevel == TooltipLevel.Essential && routine) e.Handled = true;
            }), true);
        }
        for (int i = tooltipElements.Count - 1; i >= 0; i--)
        {
            if (!tooltipElements[i].TryGetTarget(out var element)) { tooltipElements.RemoveAt(i); continue; }
            TooltipPolicy(element);
            if (element.ToolTip is ToolTip tip) tip.IsOpen = false;
        }
    }
}
