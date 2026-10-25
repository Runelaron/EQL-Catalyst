using System.Windows;
using System.Windows.Controls;

namespace EqlTracker.App;

public sealed partial class SettingsWindow
{
    internal sealed record UndeadOption(string Id,string Name,string Role);
    internal static readonly UndeadOption[] UndeadChoices=[new("skeleton-a","Skeleton A","pet-undead-skeleton"),new("wraith-a","Wraith A","pet-undead-wraith-a"),new("wraith-b","Wraith B","pet-undead-wraith-b")];
    internal readonly ComboBox UndeadChoice=new(){Width=170,DisplayMemberPath="Name",SelectedValuePath="Id"};
    private UIElement BuildUndeadAppearance()
    {
        return BuildElementalAppearance("Undead",UndeadChoice,UndeadChoices);
    }
}
