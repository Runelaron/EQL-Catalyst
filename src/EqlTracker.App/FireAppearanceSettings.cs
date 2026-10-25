using System.Windows;
using System.Windows.Controls;

namespace EqlTracker.App;

public sealed partial class SettingsWindow
{
    internal static readonly UndeadOption[] FireChoices=[new("wizard-staff","Classes — Wizard staff","pet-fire"),new("wizard-crystal","Classes — Wizard crystal","pet-fire-crystal"),new("classic","Classic","pet-fire-classic")];
    internal readonly ComboBox FireChoice=new(){Width=210,DisplayMemberPath="Name",SelectedValuePath="Id"};
    private UIElement BuildFireAppearance()
    {
        return BuildElementalAppearance("Fire",FireChoice,FireChoices);
    }
}
