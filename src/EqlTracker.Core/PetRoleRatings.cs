namespace EqlTracker.Core;

/// <summary>Pet preferences never represent damage, healing or avoidance coefficients.</summary>
public static class PetRoleRatings
{
    public const int Version = 1;
    public static readonly string[] Goals = ["DPS", "Balanced", "Tank"];
    public static string Normalize(string value) => value is "Pet DPS" or "Pet damage" ? "DPS" : value is "Pet Balanced" ? "Balanced" : value is "Pet Tank" ? "Tank" : value;
    public static readonly Dictionary<string,double> Tank = new() { ["AC"]=3, ["HP"]=.15, ["STA"]=.6, ["AGI"]=.5, ["ResistFire"]=.25, ["ResistCold"]=.25, ["ResistMagic"]=.25, ["ResistDisease"]=.25, ["ResistPoison"]=.25 };
    public static IEnumerable<string> Choices(CharacterProfile profile) => Goals.Concat(profile.Weights.Keys.Where(k => !GearScorer.Defaults.ContainsKey(k) && !SelfRoleRatings.Goals.Contains(k) && !Goals.Contains(k) && k is not ("Pet Tank" or "Pet Balanced")).Order(StringComparer.OrdinalIgnoreCase));
    public static Dictionary<string,double> Weights(CharacterProfile profile,string name,bool defense=false)
    {
        name=Normalize(name);
        if(name=="Tank" || name=="Balanced" && defense) return new(profile.PetGoalWeights.GetValueOrDefault("Tank") ?? Tank);
        if(name is "DPS" or "Balanced") return new(profile.PetGoalWeights.GetValueOrDefault("DPS") ?? profile.Weights.GetValueOrDefault("Pet DPS") ?? GearScorer.Defaults["Pet DPS"]);
        return new(profile.Weights.GetValueOrDefault(name) ?? GearScorer.Defaults["Pet DPS"]);
    }
    public static string Text(LoadoutComparison? comparison)
    {
        if(comparison==null)return "—";
        static string N(double? n)=>n?.ToString("+0.0;-0.0;0") ?? "—";
        if(comparison.PetDefense is {} d) return "Damage "+N(comparison.Unbuffed.Delta)+" · Defense "+N(d.Delta);
        return N(comparison.Unbuffed.Delta);
    }
    public static string Explain(string name) => Normalize(name) switch
    {
        "Balanced"=>"Damage and defense preference changes are shown separately. A tradeoff is not a scalar rating or predicted DPS; spell resources, utility and unresolved effects remain separate.",
        "Tank"=>"Editable defensive priorities: AC 3, HP 0.15, STA 0.6, AGI 0.5, each documented resist 0.25. Points are not time-to-live. Unknown defensive mechanics remain unresolved.",
        _=>"DPS priorities favor weapon ratio, ATK, combat attributes and the strongest worn haste. You can edit these weights. Points help compare equipment; they do not predict pet damage or include unverified abilities."
    };
    public static bool Dominates(double attack,double defense,double otherAttack,double otherDefense) => attack>=otherAttack && defense>=otherDefense && (attack>otherAttack || defense>otherDefense);
    public static void Apply(CharacterProfile profile)
    {
        if(profile.PetGoalVersion>=Version)return;
        string Preserve(string name)
        {
            if(name=="Pet DPS"||!profile.Weights.TryGetValue(name,out var old))return name;
            bool custom=!GearScorer.Defaults.TryGetValue(name,out var builtin)||old.Count!=builtin.Count||old.Any(p=>builtin.GetValueOrDefault(p.Key)!=p.Value);
            if(!custom)return name is "Tank" or "Balanced Tank" or "Legacy Tank" ? "Pet Tank" : name is "Melee" or "Pet damage" ? "Pet DPS" : name=="Caster" ? "Pet Balanced" : name;
            string key="Custom pet "+name;int suffix=2;while(profile.Weights.TryGetValue(key,out var existing)&&!(existing.Count==old.Count&&existing.All(p=>old.GetValueOrDefault(p.Key)==p.Value)))key="Custom pet "+name+" "+suffix++;
            profile.Weights.TryAdd(key,new(old));return key;
        }
        foreach(var owner in profile.Loadouts){owner.PetPreset=Preserve(owner.PetPreset);if(owner.ConfirmedPetInventory is {} current)current.Preset=Preserve(current.Preset);foreach(var build in owner.PetBuilds)build.Preset=Preserve(build.Preset);}
        profile.PetGoalVersion=Version;
    }
}
