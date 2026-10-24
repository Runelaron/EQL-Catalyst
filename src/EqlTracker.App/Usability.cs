using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EqlTracker.App;

public static partial class Usability
{
    public static readonly DependencyProperty IsPressedProperty = DependencyProperty.RegisterAttached("IsPressed", typeof(bool), typeof(Usability), new PropertyMetadata(false));
    public static bool GetIsPressed(DependencyObject obj) => (bool)obj.GetValue(IsPressedProperty);
    public static void SetIsPressed(DependencyObject obj, bool value) => obj.SetValue(IsPressedProperty, value);
    private static readonly DependencyProperty HasTooltipPolicyProperty = DependencyProperty.RegisterAttached("HasTooltipPolicy", typeof(bool), typeof(Usability));
    public static IEnumerable<DependencyObject> Tree(DependencyObject root)
    { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child; }
    public static void Tooltips(DependencyObject root)
    {
        foreach (var element in TooltipTree(root).OfType<FrameworkElement>().Distinct())
        {
            TooltipPolicy(element); ToolTipService.SetShowDuration(element, 5000); ToolTipService.SetShowOnDisabled(element, true);
            if (element is DataGridColumnHeader { ToolTip: null, Content: string caption } header)
                header.ToolTip = (caption switch { "+Rank" or "Rank" => "Recorded item upgrade rank. Catalog previews offer +0–10; existing physical-instance ranks are retained. Enter or focus loss saves editable values; Escape cancels.", "Ability" => "Logged ability name. Select to inspect measured results and available sourced spell information.", "DPS" => DisplayCadence.DpsHelp, "HPS" => "HP actually restored divided by the shared combat duration. Healing does not extend fights.", "Hits" => "Landed damage records; misses and full resists are separate outcomes, not added to this count.", "Average" or "Avg" => "Mean amount per landed damage or healing record in the selected scope.", "Crit %" => "Logged critical records as a percentage of landed records; not a cast success rate.", "Type"=>"Sourced equipment classification. Unknown types remain unverified; wearable slots do not establish a weapon subtype.","Exalt"=>"Configured and intrinsic effects. Hover a cell for the complete list; double-click to edit this position.","Rating" or "Upgrade rating"=>"Whole-loadout preference-point change for the selected role. Conditional and unavailable results are not verified upgrade recommendations.","Slot"=>"Equipment or recorded inventory position. Unknown positions retain their evidence state.", _ => caption })
                    + (header.Column?.CanUserSort == true ? " · Click to sort." : "") + " Drag either edge to resize; the nearest neighbor shrinks first. Reset columns restores measured widths.";
            CompleteTooltip(element);
            // A recycled table/tree cell must keep its binding when its DataContext changes.
            if (element.ToolTip is string description && !System.Windows.Data.BindingOperations.IsDataBound(element, FrameworkElement.ToolTipProperty)) element.ToolTip = new ToolTip { Content = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, MaxWidth = 410 } };
            if (!(bool)element.GetValue(HasTooltipPolicyProperty))
            { element.SetValue(HasTooltipPolicyProperty, true); tooltipElements.Add(new(element)); element.MouseLeave += (_, _) => { if (element.ToolTip is ToolTip tip) tip.IsOpen = false; }; }
        }
    }
    private static string Help(string text) => text switch
    {
        "Add to queue" or "Add selected to queue" => "Prioritize this goal in Queue. Quest pins also enable tracking.",
        "Add to build" => "Place the inspected candidate and preview rank in the selected local build. Use a valid selected slot, or choose from valid destinations. Undo restores the previous build; this does not establish inventory ownership.",
        "Add manual item" => "Record the known physical instance at its recorded rank, or a catalog item at +0, in the selected pet inventory. An inspector preview does not establish ownership or an upgraded physical copy.",
        "Add to planned build" or "Add to selected list" => "Add the selected item to the visible inventory or planned build immediately. An occupied position uses the next empty position; Undo reverses the addition.",
        "Undo" => "Reverse the latest change to this inventory or planned loadout, including additions, rank edits and reordering.",
        "Compare…" => "Compare Current item and Candidate with Differences below, including the inspected rank. Raw differences remain available when a rating or equipment compatibility is unavailable.",
        "Edit item…" or "Edit selected item…" => "Edit this position, item, rank, and configured effects.",
        "Save plan" or "Save context" => "Save the selected pet inventory's summon context. Inventory edits already save locally; actual game equipment is unchanged.",
        "View sources" or "View progress" => "View this goal's sources or checklist.",
        "Stat profiles…" or "Review Self profiles…" => "Review game evidence and confirm missing unbuffed/buffed totals and AA information used by preference comparisons.",
        "Save" or "Save changes" => "Validate and save these changes.",
        "Cancel" => "Discard these edits and close.",
        "Remove item" => "Remove this item from the local build. Undo is available.",
        "Add effect" => "Configure this effect in the selected position. Save commits the edit.",
        "Remove effect" => "Remove this configured effect. Save commits the edit.",
        "Save manual confirmation" or "Save planned item" => "Validate and save the edited item to the named destination. This never transfers or equips game items.",
        "Track / untrack" => "Track this quest in Progress, or stop tracking while keeping progress.",
        "Clear filters" => "Remove this view's filters and show all matching records again.",
        "Date range" => "Show date filters. Highlighted while the editor is open or a date filter is active.",
        "Export encounter" => "Save a ZIP snapshot of the selected encounter with CSV breakdowns, events, healing and versioned JSON. Active combat continues.",
        "Export scope" => "Export the current scope and filters, including duration denominators and available damage/healing records.",
        "Reset columns" => "Restore this table's default column widths.",
        "Edit weights" => "Edit preference points assigned to each stat. Scores do not predict damage or healing output.",
        "Confirm equipment" => "Supply the equipped baseline used by upgrade comparisons. Pet equipment requires manual confirmation.",
        "Save build" or "Save theorycraft build…" => "Explicitly save a named Theorycraft build. Current and ordinary named builds autosave; Theorycraft cannot activate into Current.",
        "Use as current build…" or "Use as current" => "Replace Current with this ordinary named build. You can save a named backup first; Undo retains the previous Current. Theorycraft cannot activate.",
        "Build ▾" or "Build" => "Create, duplicate, rename or remove a named build. Selecting a build edits that build without activating it.",
        "Tools ▾" or "Tools" => "Open workspace tools. Click again or press Escape to close.",
        "Filters…" => "Edit only the compact DPS tracker's saved filters. Combat Lab has independent filters; clearing preserves actor, scope, session and sorting.",
        "Click-through" => "Pass mouse input through this overlay to the game. "+((App)Application.Current).ShortcutLabel(EqlTracker.Core.OverlayShortcutAction.ClickThrough)+" restores interaction; this does not change the overlay's saved position.",
        "Smooth motion" => "Animate value and contribution-bar changes while preserving the exact published readings and existing DPS cadence.",
        "Auto Update" => "Update Current from complete inventory captures when enabled. Capture scheduling and the file-import check are separate; loot alone does not establish worn equipment.",
        "Theorycraft" => "Edit an isolated hypothetical build. Explicit saving is required; leaving unsaved changes offers Save, Discard or Cancel.",
        "Fight" or "FIGHT" or "Fight scope" => "Inspect the current or newest fight using this view's saved filters.",
        "Session" or "SESSION" or "Session scope" => "Inspect the selected saved play session, including retained historical encounters. Rates use summed combat durations, not time since tracker launch.",
        "Combined" or "Total" => "Self and timestamp-verified owned-pet contributions. Group members remain separate from personal totals.",
        "Pet" => "Timestamp-verified owned-pet contributions in this view's selected scope and filters.",
        "Self" or "Player" => "This character's contributions, excluding owned-pet and group-member totals.",
        "Group" or "Group/Raid" => "Known group/raid characters with their verified pets folded into owner rows. Missing membership or combat coverage remains unknown.",
        "Choose icon…" => "Choose a tray and running-window icon for this character. Selection saves immediately.",
        "Remove pin" => "Remove this Queue pin; keep tracking and progress.",
        "↑ Move up" or "↓ Move down" => "Move this goal in Queue.",
        "Sync now" => "Run the verified native Sync shortcut once, then wait for fresh class and inventory evidence. Requires a linked shortcut and an in-world game character.",
        _ => text.TrimEnd('…',' ','▾') + ". Uses the current window's selection and context."
    };
}
