using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class TrackerController
{
    internal T? ReadLinuxPresentation<T>() where T : class { lock(Sync)return store.Get<T>("linux-presentation-v1"); }
    internal void SaveLinuxPresentation<T>(T value) { if(Demo)return;lock(Sync){var copy=EquipmentPositions.Copy(value);QueuePersistence(()=>store.Set("linux-presentation-v1",copy));} }
}
