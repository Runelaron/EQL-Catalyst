namespace EqlTracker.Core;

public sealed record PetSummonOption(string PetType, string Name, string CasterClass, int RequiredLevel, string Url, long Revision)
{
    public string Id => PetType + "|" + CasterClass + "|" + Name;
    public List<string> Aliases { get; init; } = [];
    public int? BasePetLevel { get; init; }
    public string LevelEvidence { get; init; } = "";
    public string Label => $"{Name} · {CasterClass} {RequiredLevel}";
    public override string ToString() => Label;
}
public enum PetLevelMode { Unspecified, Automatic, Manual }
public sealed record PetLevelEstimate(int? Level, string Explanation);
public sealed record PetFocusOption(string PetType, string Name, string Kind, string Url, long Revision);
public sealed class PetContextCatalog
{
    public static PetContextCatalog Bundled { get; } = LoadBundled();
    private static PetContextCatalog LoadBundled()
    { try { return Load(Path.Combine(AppContext.BaseDirectory,"data","pet-context.json")); } catch(Exception e) when(e is IOException or System.Text.Json.JsonException) { return new(); } }
    public PetCastIdentity? ResolveCast(string raw)
    {
        string Normalize(string s)=>System.Text.RegularExpressions.Regex.Replace(s.Trim(),@"\s+"," ");
        raw=Normalize(raw);
        var names=Spells.SelectMany(s=>new[]{s.Name}.Concat(s.Aliases).Select(name=>(Spell:s,Name:Normalize(name)))).OrderByDescending(x=>x.Name.Length);
        foreach(var entry in names)
        {
            int? rank=null;
            if(raw.Equals(entry.Name,StringComparison.OrdinalIgnoreCase))rank=0;
            else if(raw.StartsWith(entry.Name+" ",StringComparison.OrdinalIgnoreCase))
            {
                string suffix=raw[(entry.Name.Length+1)..];
                if(suffix.StartsWith('+')&&int.TryParse(suffix[1..],out int n)&&n is >=0 and <=1000)rank=n;
                else if(suffix.Length>0&&System.Text.RegularExpressions.Regex.IsMatch(suffix,@"^(?=.)M?(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {int value=0,previous=0;foreach(char c in suffix.ToUpperInvariant().Reverse()){int v=c switch{'M'=>1000,'D'=>500,'C'=>100,'L'=>50,'X'=>10,'V'=>5,_=>1};value+=v<previous?-v:v;previous=v;}if(value<=1000)rank=value;}
            }
            if(rank.HasValue)return new(entry.Spell,raw,rank.Value);
        }
        return null;
    }
    public string Version { get; set; } = "";
    public List<PetSummonOption> Spells { get; set; } = [];
    public List<PetFocusOption> Focuses { get; set; } = [];
    public static PetContextCatalog Load(string path) => System.Text.Json.JsonSerializer.Deserialize<PetContextCatalog>(File.ReadAllText(path)) ?? new();
    public IReadOnlyList<PetSummonOption> Summons(string type) => Spells.Where(s => s.PetType == type && s.RequiredLevel <= 50).OrderByDescending(s => s.RequiredLevel).ThenBy(s => s.Name).ThenBy(s => s.CasterClass).ToList();
    public PetSummonOption? Find(string type, string name, string caster = "") => Spells.FirstOrDefault(s => s.PetType == type && (s.Name == name || s.Label == name || s.Id == name || s.Aliases.Contains(name)) && (caster.Length == 0 || s.CasterClass == caster));
    public PetLevelEstimate Estimate(PetBuild build, int ownerLevel)
    {
        var spell = Find(build.PetType, build.SummonSpell, build.SummonClass);
        if (spell?.BasePetLevel is not { } baseline) return new(null, "Automatic level unavailable: select a spell with a documented base summoned level.");
        if (ownerLevel < 1) return new(null, "Automatic level unavailable: confirm the character level.");
        int value = Math.Min(baseline + build.SummonRank, ownerLevel);
        return new(value, $"Auto: base {baseline} + rank {build.SummonRank} = {baseline + build.SummonRank}; capped at character level {ownerLevel} → {value}." + (build.Focus.Length > 0 ? " Focus level adjustment unresolved; estimate excludes it." : "") + "\n" + spell.LevelEvidence);
    }
    public string FocusEvidence(PetBuild build)
    {
        var spell = Find(build.PetType, build.SummonSpell, build.SummonClass);
        string note = spell == null && build.SummonSpell.Length > 0 ? "Legacy/custom summon · applicability needs confirmation. " : spell == null ? "Select a summon spell to review focus applicability. " : $"{spell.CasterClass} spell level {spell.RequiredLevel} · not the pet's level. ";
        if (build.Focus.Length == 0 || build.Focus == "None") return note;
        var focus = Focuses.FirstOrDefault(f => f.Name == build.Focus && f.PetType == build.PetType);
        if (focus == null) return note + "Saved focus is unmapped for this pet type; select a documented option or None.";
        if (focus.Kind == "Greater") return note + "Greater focus applicability conflicts between guide pages (Greater Conjuration only vs levels 4–60). Benefits unresolved; no numerical focus bonus applied.";
        if (focus.Kind == "Lesser") return note + (spell == null ? "Lesser focus requires a matching summon at spell level 4–49. " : spell.RequiredLevel is >= 4 and <= 49 ? "Matching lesser-focus spell range (4–49). " : "Outside the documented lesser-focus spell range (4–49). ") + "Spell rank and pet level are separate; level 50 alone does not prove ineffectiveness. Numerical benefits remain unverified.";
        return note + "Focus benefit/applicability unverified; no numerical bonus applied.";
    }
}
public sealed record PetCastIdentity(PetSummonOption Spell,string Raw,int Rank);
