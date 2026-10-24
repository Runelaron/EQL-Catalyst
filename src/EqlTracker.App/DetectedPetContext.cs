using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    internal void RefreshDetectedPetContext()
    {
        lock(Sync)
        {
            var build=PetInventoryEditing.Confirmed(Profile.ActiveLoadout,Catalog);
            if(!PetDetectedContext.Apply(Profile,build,PetContextCatalog.Bundled))return;
            petRevision++;equipmentRevision++;profileNeedsPersistence=saveRequested=true;
        }
    }
}
