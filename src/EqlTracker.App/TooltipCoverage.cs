using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace EqlTracker.App;

public static partial class Usability
{
    private static IEnumerable<DependencyObject> TooltipTree(DependencyObject root)
    {
        foreach(var node in Tree(root)){yield return node;if(node is ContextMenu or MenuItem)foreach(var item in ((ItemsControl)node).Items.OfType<DependencyObject>())foreach(var child in TooltipTree(item))yield return child;}
    }
    internal static void DescribeField(string title,FrameworkElement control,TextBlock label)
    {
        if(title.Length==0)return;
        if(!BindingOperations.IsDataBound(control,FrameworkElement.ToolTipProperty)&&control.ToolTip==null)control.ToolTip=FieldHelp(title);
        label.SetBinding(FrameworkElement.ToolTipProperty,new Binding(nameof(FrameworkElement.ToolTip)){Source=control});
        if(AutomationProperties.GetName(control).Length==0)AutomationProperties.SetName(control,title);
    }
    private static string FieldHelp(string title)
    {
        string name=title.Trim().ToLowerInvariant();
        return name switch {
            "build"=>"Choose Current or a named build. Selecting a saved build edits it without activation; ordinary edits autosave with Undo. Theorycraft uses explicit saving.",
            "role"=>"Select the preference weights used for upgrade ratings. The information button explains each role and the contribution arithmetic.",
            "rank" or "preview rank" or "candidate rank"=>"Default retains physical-instance ranks and catalog +0. Explicit catalog previews offer +0–10 and do not change saved inventory ranks.",
            "pet type"=>"Select the documented pet family. Spell and focus choices follow this type without assuming spell ownership.",
            "spell"=>"Choose a canonical summon spell. Its required caster level is separate from the summoned pet's base level and your spell rank.",
            "spell rank"=>"Summon spell upgrade rank, separate from caster and pet level. Unsupported custom planned values remain hypothetical.",
            "level"=>"Pet level. Auto uses sourced base level plus spell rank, capped by a known owner level. Manual overrides remain explicit; unknown owner levels are not assumed to be 50.",
            "focus"=>"Documented focuses for the selected pet type. Applicability and numeric benefits are separate; unsupported or disputed bonuses are excluded.",
            "items" or "ownership"=>"All groups copies by stable item identity. Inventory Only shows physical copies. Material filters show documented partial progress and missing coverage.",
            "compatibility"=>"All shows all results; Build applies the selected build's restrictions; Incompatible shows verified conflicts. Missing restrictions remain unverified.",
            "current item"=>"The item being compared or replaced. Selecting a pet inventory row updates this choice automatically.",
            "time scope"=>"Fight or a selected saved Session. This window retains its own filters; personal rates preserve the existing combat-duration denominator.",
            "session"=>"Choose a saved verified or inferred login session. Explicit historical selections remain stable when new activity arrives.",
            "sort by"=>"Change row ordering only. Damage, healing and rate calculations are unchanged; the active table column shows its sort direction.",
            "actor"=>"Choose Self, Pet or their combined personal contributions. Timestamp-qualified ownership is required for pet attribution; group totals stay separate.",
            "target" or "recipient"=>"Limit results to the selected logged name. Same-named mobs or recipients are grouped; this is not an individual kill count.",
            "damage type" or "type"=>"Filter by the documented category. Unverified metadata stays Unknown instead of being inferred from an item or ability name.",
            "from · local date/time" or "until · local date/time"=>"Optional local date/time boundary. Invalid text retains the previous valid selection and displays an explanation.",
            "expansion filter"=>"Choose exact introduction expansions independently. Unknown includes missing evidence; provisional zone-derived classifications are explained in Item Details.",
            "raw known"=>"Confirm that the uncapped raw total is known from evidence. A capped displayed total alone does not establish raw stats or overflow.",
            "verified"=>"Confirm this numeric restriction or bonus against current evidence. Unverified limits remain uncertain instead of silently applying a guessed cap.",
            "includes aas"=>"Indicate whether this recorded value already includes AA bonuses, so verified bonuses are not counted twice.",
            _=>title+" for this view. Existing saved values and evidence remain unchanged until a valid selection is applied."
        };
    }
    private static string LabelFor(FrameworkElement element)
    {
        string name=AutomationProperties.GetName(element);if(name.Length>0)return name;
        if(element.Parent is Grid grid&&Grid.GetRow(element)>0)
            return grid.Children.OfType<TextBlock>().FirstOrDefault(t=>Grid.GetRow(t)==0)?.Text??"";
        if(element.Parent is Panel panel){int i=panel.Children.IndexOf(element);for(int j=i-1;j>=0;j--)if(panel.Children[j] is TextBlock text&&text.Text.Length>0)return text.Text;}
        for(DependencyObject? node=element;node is System.Windows.Media.Visual;node=System.Windows.Media.VisualTreeHelper.GetParent(node))if(node is DataGridCell cell&&cell.Column?.Header is string header)return header;
        return element.Name.Length>0?element.Name:"";
    }
    private static void CompleteTooltip(FrameworkElement element)
    {
        if(BindingOperations.IsDataBound(element,FrameworkElement.ToolTipProperty)||element.ToolTip!=null)return;
        string? help=null;
        if(element is ButtonBase {Content:string caption})help=Help(caption);
        else if(element is ToggleButton&&element.TemplatedParent is not Control){string label=LabelFor(element);if(label.Length>0)help=FieldHelp(label);}
        else if(element is MenuItem {Header:string header})help=Help(header);
        else if(element is MenuItem {Header:DependencyObject rich}){string richCaption=Tree(rich).OfType<TextBlock>().FirstOrDefault()?.Text??"";if(richCaption.Length>0)help=Help(richCaption);}
        else if(element is TabItem {Header:string tab})help=$"Open {tab}. The selected tab and its filters remain saved; use keyboard arrows to navigate tab headers.";
        else if(element is Expander {Header:string section})help=$"Expand or collapse {section.TrimEnd('>','<',' ')}. Other selections remain unchanged.";
        else if(element is ComboBox or TextBox or Slider)
        {
            if(element.TemplatedParent is Control)return;
            string title=LabelFor(element);
            help=title.Length>0?FieldHelp(title):element is Slider?"Preview the selected value with dragging or arrow keys. Saved data changes only through this control's stated action.":element is ComboBox?"Choose an available option for the current view. A disabled choice is unavailable in this context.":"Enter a value or search text for this view. Invalid values retain the previous valid state.";
        }
        if(help!=null)
        {
            string last=help+(!element.IsEnabled?"\nUnavailable for the current selection or saved state.":"");element.ToolTip=last;
            element.IsEnabledChanged+=(_,_)=>{
                if(BindingOperations.IsDataBound(element,FrameworkElement.ToolTipProperty))return;
                string? current=element.ToolTip is ToolTip {Content:TextBlock wrapped}?wrapped.Text:element.ToolTip as string;
                if(current!=last)return;last=help+(!element.IsEnabled?"\nUnavailable for the current selection or saved state.":"");element.ToolTip=last;
            };
            if(AutomationProperties.GetName(element).Length==0&&element is ButtonBase button&&button.Content is string text)AutomationProperties.SetName(element,text);
        }
    }
    internal static bool TooltipControl(FrameworkElement element)=>element is DataGridColumnHeader header?header.Column!=null&&!string.IsNullOrWhiteSpace(header.Content?.ToString()):element is ButtonBase or MenuItem or ComboBox or TextBox or Slider or TabItem or Expander;
    internal static bool HasTooltip(FrameworkElement element)=>BindingOperations.IsDataBound(element,FrameworkElement.ToolTipProperty)||element.ToolTip!=null;
}
