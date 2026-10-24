using System.Windows.Controls;

namespace EqlTracker.App;

internal sealed class MeasurementGuideDialog : FrostDialog
{
    internal MeasurementGuideDialog() : base("Item & pet measurement checklist", 760, 660)
    {
        string path=Path.Combine(AppContext.BaseDirectory,"PET-MEASUREMENT-GUIDE.md");
        string text=File.Exists(path)?File.ReadAllText(path):"Capture the same item before and after upgrades. For pets, record patch, summon/rank, level, owner context, buffs and effective equipment; compare baseline → one item → restored baseline. Preserve exact screenshots and exports. Logs do not provide complete pet HP/mana totals. See docs/pet-measurement-guide.md and measurement-template.json.";
        Editor.Children.Add(UI.Text(text,12));
        Actions.Children.Add(UI.Button("Close",Close));
    }
}
