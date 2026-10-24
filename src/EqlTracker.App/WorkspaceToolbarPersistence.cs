using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    internal WorkspaceToolbarState ReadWorkspaceToolbar()
    {lock(Sync){var state=store.Get<WorkspaceToolbarState>("workspace-toolbar-v1")??new();state.Normalize();return state;}}
    internal void SaveWorkspaceToolbar(WorkspaceToolbarState state)
    {if(Demo)return;lock(Sync){var snapshot=EquipmentPositions.Copy(state);snapshot.Normalize();QueuePersistence(()=>store.Set("workspace-toolbar-v1",snapshot));}}
}
