using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class CombatGroupPanel
{
    private GroupPaneLayout groupPanes=null!;
    internal event System.Action? SongCoverageRequested;
    private void ToggleSongCoverage(bool show){if(show)SongCoverageRequested?.Invoke();}
}
