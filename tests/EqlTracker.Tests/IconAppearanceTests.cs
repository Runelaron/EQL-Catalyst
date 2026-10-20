using System.Text.Json;
using EqlTracker.Core;

internal static class IconAppearanceTests
{
    internal static void Register(Action<string,Action> test)
    {
        void Check(bool pass){if(!pass)throw new InvalidOperationException("Appearance/expansion invariant failed");}
        test("Legacy profile defaults to Vibrant Fire without changing app identity",()=>{
            var p=JsonSerializer.Deserialize<CharacterProfile>("{\"AppIconId\":\"class-shm\",\"GearExpansions\":[\"Kunark\"]}",Json.Options)!;
            Check(p.IconStyle==IconArtworkStyle.Vibrant&&p.DamageEmblem==DamageEmblem.Fire&&p.AppIconId=="class-shm");
            Check(p.PetExpansions.SequenceEqual(ItemExpansionFilter.Choices)&&p.GearExpansions.SequenceEqual(["Kunark"]));
        });
        test("All icon style and emblem preferences roundtrip independently",()=>{
            foreach(var style in Enum.GetValues<IconArtworkStyle>())foreach(var emblem in Enum.GetValues<DamageEmblem>()){
                var p=new CharacterProfile{IconStyle=style,DamageEmblem=emblem,AppIconId="gold-elements"};
                var q=JsonSerializer.Deserialize<CharacterProfile>(JsonSerializer.Serialize(p,Json.Options),Json.Options)!;
                Check(q.IconStyle==style&&q.DamageEmblem==emblem&&q.AppIconId==p.AppIconId);
            }
        });
        test("Invalid appearance preferences fall back without losing remaining data",()=>{
            foreach(string value in new[]{"\"future-value\"","999","null","{}"}){
                var p=JsonSerializer.Deserialize<CharacterProfile>("{\"IconStyle\":"+value+",\"DamageEmblem\":"+value+",\"AppIconId\":\"class-war\"}",Json.Options)!;
                Check(p.IconStyle==IconArtworkStyle.Vibrant&&p.DamageEmblem==DamageEmblem.Fire&&p.AppIconId=="class-war");
            }
        });
        test("Pet introduction expansion is exact rather than a range",()=>{
            Check(ItemExpansionFilter.Includes(["Kunark"],"Kunark"));Check(!ItemExpansionFilter.Includes(["Kunark"],"Classic"));Check(!ItemExpansionFilter.Includes(["Kunark"],"Velious"));
        });
        test("Pet multiple and empty expansions retain explicit choices",()=>{
            Check(ItemExpansionFilter.Includes(["Classic","Velious"],"Velious"));Check(!ItemExpansionFilter.Includes(["Classic","Velious"],"Kunark"));Check(!ItemExpansionFilter.Includes([],"Classic"));
        });
        test("Unknown and pending metadata do not invent expansion evidence",()=>{
            Check(ItemExpansionFilter.Includes(["Unknown"],null));Check(ItemExpansionFilter.Includes(["Unknown"],""));Check(ItemExpansionFilter.Includes(["Unknown"],"unknown"));Check(!ItemExpansionFilter.Includes(["Classic"],null));Check(!ItemExpansionFilter.Includes(["Unknown"],"Luclin"));
        });
        test("Expansion selection keys normalize order for background requests",()=>{
            Check(ItemExpansionFilter.Key(["Velious","classic","Classic"])==ItemExpansionFilter.Key(["Classic","Velious"]));Check(ItemExpansionFilter.Key([])!=ItemExpansionFilter.Key(ItemExpansionFilter.Choices));
        });
        test("Pet and Gear expansion selections persist independently",()=>{
            var p=new CharacterProfile{PetExpansions=["Velious","Unknown"],GearExpansions=["Kunark"]};
            var q=JsonSerializer.Deserialize<CharacterProfile>(JsonSerializer.Serialize(p,Json.Options),Json.Options)!;q.PetExpansions.Clear();
            Check(q.GearExpansions.SequenceEqual(["Kunark"])&&p.PetExpansions.Count==2);
        });
    }
}
