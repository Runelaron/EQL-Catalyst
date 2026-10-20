using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private readonly TextBlock petCandidateFilterNote = UI.Text("", 11, "Muted");
    private FrameworkElement BuildPetExpansionFilter()
    {
        var menu = new ContextMenu();
        var button = OverlayMenus.Button(menu, "Filter pet candidates by exact item introduction expansion. This selection is independent of Gear; Unknown includes unverified or pending metadata.", "Expansion Filter ▾");
        button.Width = 180; button.Height=28;button.Margin=new(0);
        void Label()
        {
            int count = ItemExpansionFilter.Choices.Count(x => Controller.Profile.PetExpansions.Contains(x));
            var selected=ItemExpansionFilter.Choices.Where(x=>Controller.Profile.PetExpansions.Contains(x)).ToArray();
            string full=string.Join(" / ",selected);
            button.Content = (count==4?"All":count==0?"None":count==1||full.Length<=22?full:string.Join(" / ",selected.Select(x=>x switch{"Classic"=>"C","Kunark"=>"K","Velious"=>"V",_=>"?"})))+" ▾";
            button.ToolTip="Selected expansions: "+(count==0?"None":full)+". Exact introduction or documented provisional era; Unknown includes unverified or pending metadata. Independent of Gear.";
        }
        foreach (string name in ItemExpansionFilter.Choices)
        {
            var entry = new MenuItem { Header = name, IsCheckable = true, IsChecked = Controller.Profile.PetExpansions.Contains(name), StaysOpenOnClick = true, ToolTip = name == "Unknown" ? "Items without verified introduction metadata, including pending catalog results." : "Only items introduced in " + name + "; does not include earlier expansions." };
            entry.Click += (_, _) => {
                if (entry.IsChecked) { if (!Controller.Profile.PetExpansions.Contains(name)) Controller.Profile.PetExpansions.Add(name); }
                else Controller.Profile.PetExpansions.RemoveAll(x => x == name);
                Label(); petKey = ""; SchedulePetPersistence(); if (!initializing) RefreshPet();
            };
            menu.Items.Add(entry);
        }
        Label(); return (FrameworkElement)UI.Field("Expansion Filter",button);
    }
}
