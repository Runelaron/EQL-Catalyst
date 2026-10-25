using System.Windows.Controls;
using EqlTracker.Core;
namespace EqlTracker.App;

public sealed partial class SettingsWindow
{
    private Button? settingsSave;
    private string settingsBaseline="";
    private string SettingsInputsKey()=>PetComparisonEngine.Key(new{Folder=folder.Text.Trim(),Classes=currentClasses.Text.Trim().ToUpperInvariant(),Fifty=reached50.Text.Trim().ToUpperInvariant(),Priority=priority.Text.Trim().ToUpperInvariant(),Timeout=timeout.Text.Trim(),Resume=resume.Text.Trim(),Race=RaceNames.Canonical(race.Text),RaceLevel=raceLevel.Text.Trim(),Loadout=loadout.SelectedItem?.ToString(),Pet=petType.SelectedItem?.ToString(),Hide=hide.IsChecked,Sounds=sounds.IsChecked,Click=click.IsChecked,Locked=locked.IsChecked,Borderless=borderless.IsChecked,Shortcuts=shortcutSettings.Draft});
    private void RefreshSettingsSave()
    {
        if(settingsSave==null||settingsBaseline.Length==0)return;
        bool valid=timingInputs.TryRead(out _,out _);
        if(raceLevel.Text.Trim().Length>0)valid&=int.TryParse(raceLevel.Text,out int level)&&level is >=1 and <=50;
        var classes=Controller.Catalog.Quests.Select(q=>q.Class).ToHashSet();
        string[] Parse(string text)=>text.ToUpperInvariant().Split([',','/',' '],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        valid&=Parse(currentClasses.Text).Distinct().Count()<=3&&Parse(currentClasses.Text).Concat(Parse(reached50.Text)).Concat(Parse(priority.Text)).All(classes.Contains);
        valid&=OverlayShortcuts.Validate(shortcutSettings.Draft,out _);
        settingsSave.IsEnabled=valid&&SettingsInputsKey()!=settingsBaseline;
    }
}
