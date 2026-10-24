using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class PetArtworkPreferences
{
    internal const string StateKey="pet-artwork-preferences-v1";
    public string Undead {get;set;}="skeleton-a";
    public string Fire {get;set;}="wizard-staff";
    internal static string Resolve(string? value)=>value is "wraith-a" or "wraith-b"?value:"skeleton-a";
    internal static string ResolveFire(string? value)=>value=="wizard-crystal"?value:"wizard-staff";
    internal static PetArtworkPreferences Read(TrackerStore store)
    {try{var p=store.Get<PetArtworkPreferences>(StateKey);return new(){Undead=Resolve(p?.Undead),Fire=ResolveFire(p?.Fire)};}catch(System.Text.Json.JsonException){return new();}}
    internal void Write(TrackerStore store)=>store.Set(StateKey,new PetArtworkPreferences{Undead=Resolve(Undead),Fire=ResolveFire(Fire)});
}

public sealed partial class TrackerController
{
    private PetArtworkPreferences? petArtworkPreferences;
    internal string UndeadArtwork
    {
        get{lock(Sync)return (petArtworkPreferences??=PetArtworkPreferences.Read(store)).Undead;}
    }
    internal void SetUndeadArtwork(string? value)
    {
        lock(Sync)
        {
            string resolved=PetArtworkPreferences.Resolve(value);
            if(UndeadArtwork==resolved)return;
            petArtworkPreferences=new(){Undead=resolved,Fire=FireArtwork};
            if(!Demo){var snapshot=EquipmentPositions.Copy(petArtworkPreferences);QueuePersistence(()=>snapshot.Write(store));}
        }
    }
    internal string FireArtwork {get{lock(Sync)return (petArtworkPreferences??=PetArtworkPreferences.Read(store)).Fire;}}
    internal void SetFireArtwork(string? value)
    {
        lock(Sync)
        {
            string resolved=PetArtworkPreferences.ResolveFire(value);if(FireArtwork==resolved)return;
            petArtworkPreferences=new(){Undead=UndeadArtwork,Fire=resolved};
            if(!Demo){var snapshot=EquipmentPositions.Copy(petArtworkPreferences);QueuePersistence(()=>snapshot.Write(store));}
        }
    }
}
