using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;
public sealed partial class QuestWindow
{
    private StackPanel? selectedEffectReferencePanel;
    private string selectedEffectReferenceKey="";
    private void RefreshSelectedEffectReference()
    {
        if(selectedEffectReferencePanel==null||exaltTable.SelectedItem is not ExaltationOption option)return;
        string key=option.Id+":"+option.Source.Revision+":"+Controller.EffectReferenceVersion(option.Effect);
        if(key==selectedEffectReferenceKey)return;
        selectedEffectReferenceKey=key;var snapshot=Controller.EffectReference(option.Effect,option.Source);selectedEffectReferencePanel.Children.Clear();
        foreach(var line in snapshot.Lines)selectedEffectReferencePanel.Children.Add(UI.Text(line.Label+": "+line.Value,12,line.Label=="Base effects"?"Mint":"Foreground"));
        selectedEffectReferencePanel.Children.Add(UI.Text(snapshot.Limitation,11,"SecondaryText"));
        if(snapshot.Reference is {} reference)
        {
            var link=UI.Button("Spell reference ↗",()=>UI.OpenUrl(reference.Url));link.ToolTip=$"{reference.Name} · wiki revision {reference.Revision} · checked {reference.FetchedAt.ToLocalTime():MMM/dd HH:mm}. Base spell rank and item rank are separate.";selectedEffectReferencePanel.Children.Add(link);
        }
    }
}
