using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class PetArtworkPreferences
{
    // Additive fields retain the existing character state key and Fire/Undead values.
    internal const string StateKey="pet-artwork-preferences-v1";
    public string Undead {get;set;}="skeleton-a";
    public string Fire {get;set;}="wizard-staff";
    public string Air {get;set;}="monk-fist";
    public string Water {get;set;}="rogue";
    public string Earth {get;set;}="classic";
    public string Monster {get;set;}="monster-a";
    internal static string Resolve(string? value)=>value is "wraith-a" or "wraith-b"?value:"skeleton-a";
    internal static string ResolveFire(string? value)=>value is "wizard-crystal" or "classic"?value:"wizard-staff";
    internal static string ResolveAir(string? value)=>value=="classic"?value:"monk-fist";
    internal static string ResolveWater(string? value)=>value is "classic" or "torrent"?value:"rogue";
    internal static string ResolveEarth(string? value)=>"classic";
    internal static string ResolveMonster(string? value)=>value is "aviak-overhead" or "dragon-overhead" or "aviak-diagonal" or "dragon-diagonal"?value:"monster-a";
    internal PetArtworkPreferences Normalized()=>new(){Undead=Resolve(Undead),Fire=ResolveFire(Fire),Air=ResolveAir(Air),Water=ResolveWater(Water),Earth=ResolveEarth(Earth),Monster=ResolveMonster(Monster)};
    internal static PetArtworkPreferences Read(TrackerStore store)
    {
        try
        {
            var saved=store.Get<System.Text.Json.JsonElement>(StateKey);
            string? Value(string name)=>saved.ValueKind==System.Text.Json.JsonValueKind.Object&&saved.TryGetProperty(name,out var value)&&value.ValueKind==System.Text.Json.JsonValueKind.String?value.GetString():null;
            return new(){Undead=Resolve(Value("Undead")),Fire=ResolveFire(Value("Fire")),Air=ResolveAir(Value("Air")),Water=ResolveWater(Value("Water")),Earth=ResolveEarth(Value("Earth")),Monster=ResolveMonster(Value("Monster"))};
        }
        catch(System.Text.Json.JsonException){return new();}
    }
    internal void Write(TrackerStore store)=>store.Set(StateKey,Normalized());
    internal string For(string family)=>family switch{"Undead"=>Undead,"Fire"=>Fire,"Air"=>Air,"Water"=>Water,"Earth"=>Earth,"Monster"=>Monster,_=>""};
}

public sealed partial class TrackerController
{
    private PetArtworkPreferences? petArtworkPreferences;
    internal PetArtworkPreferences ArtworkPreferences {get{lock(Sync)return (petArtworkPreferences??=PetArtworkPreferences.Read(store)).Normalized();}}
    internal string UndeadArtwork=>ArtworkPreferences.Undead;
    internal string FireArtwork=>ArtworkPreferences.Fire;
    internal void SetUndeadArtwork(string? value)=>SetPetArtwork("Undead",value);
    internal void SetFireArtwork(string? value)=>SetPetArtwork("Fire",value);
    internal void SetPetArtwork(string family,string? value)
    {
        lock(Sync)
        {
            var next=ArtworkPreferences;
            switch(family)
            {
                case "Undead":next.Undead=PetArtworkPreferences.Resolve(value);break;
                case "Fire":next.Fire=PetArtworkPreferences.ResolveFire(value);break;
                case "Air":next.Air=PetArtworkPreferences.ResolveAir(value);break;
                case "Water":next.Water=PetArtworkPreferences.ResolveWater(value);break;
                case "Earth":next.Earth=PetArtworkPreferences.ResolveEarth(value);break;
                case "Monster":next.Monster=PetArtworkPreferences.ResolveMonster(value);break;
                default:return;
            }
            if(ArtworkPreferences.For(family)==next.For(family))return;
            petArtworkPreferences=next;
            if(!Demo){var snapshot=next.Normalized();QueuePersistence(()=>snapshot.Write(store));}
        }
    }
}
