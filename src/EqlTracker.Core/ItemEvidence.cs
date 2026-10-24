using System.Text.RegularExpressions;
namespace EqlTracker.Core;

public enum InventoryBadgeStyle { Chest, FrierenMimic }
public enum ContentAvailability { Unverified, Current, Future }
public enum EffectPurpose { Unknown, Combat, Healing, Utility, Appearance }
public enum RecipeKind { Unknown, Quest, Tradeskill }
public sealed class ItemEffectEvidence
{
    public string Text { get; set; } = "";
    public string SocketType { get; set; } = "Unknown";
    public EffectPurpose Purpose { get; set; }
    public string Source { get; set; } = "";
}
public sealed class ExpansionProvenance
{
    public bool Inferred { get; set; }
    public string Expansion { get; set; } = "Unknown";
    public List<string> Zones { get; set; } = [];
    public string Source { get; set; } = "";
}
public static class ItemPresentation
{
    public static string Slots(IEnumerable<string> slots)
    {
        string Display(string s) => EquipmentPositions.Normalize(s) is "Range" ? "Ranged" : EquipmentPositions.Normalize(s);
        int Order(string s) => Display(s) switch { "Primary" => 0, "Secondary" => 1, "Ranged" => 2, "Ammo" => 3, _ => 4 };
        return string.Join(" / ", slots.Select(EquipmentPositions.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Order).ThenBy(Display, StringComparer.OrdinalIgnoreCase).Select(Display));
    }
    public static string Expansion(ItemDefinition item) => item.IntroducedExpansion != "Unknown" ? item.IntroducedExpansion : item.ExpansionProvenance.Expansion;
    public static string ExpansionNote(ItemDefinition item) => item.IntroducedExpansion == "Unknown" && item.ExpansionProvenance.Inferred
        ? "Expansion assumed from " + string.Join(", ", item.ExpansionProvenance.Zones) + "; introduction unverified" : "";
    public static string TypeRole(ItemDefinition item) => ItemEquipmentIcons.For(item)[0].Role;
}
public sealed record ItemOwnershipEvidence(bool Known, string Description, bool QuantityKnown, int? Quantity);
public static class ItemOwnership
{
    public static ItemOwnershipEvidence For(ItemDefinition item, InventorySnapshot inventory)
    {
        var rows = inventory.Evidence(item); bool known = rows.Count > 0;
        int quantity = rows.Where(e => !e.IsCollection && !e.Estimated).Sum(e => e.Count ?? 0);
        return new(known, known ? inventory.Ownership(item) : "No recorded player inventory ownership", rows.All(e => !e.IsCollection && !e.Estimated), known && rows.All(e => !e.IsCollection && !e.Estimated) ? quantity : null);
    }
}
public static class ItemEffects
{
    public static string Canonical(string effect) => Regex.Replace(Regex.Replace(effect.Trim(), @"^(?:(?:effect|focus(?:[_ ]effect)?|click(?:[_ ]effect)?|worn(?:[_ ]effect)?|proc(?:[_ ]effect)?):\s*)+", "", RegexOptions.IgnoreCase), @"\s+", " ").Trim();
    public static string NameKey(string effect) => Identity.Key(Canonical(effect).Split('(')[0].Trim());
    public static IEnumerable<string> Active(ItemDefinition item, IEnumerable<string>? removed) => item.Effects.Where(e => !(removed ?? []).Any(r => NameKey(r) == NameKey(e) && (Kind(r) == "Unknown" || Kind(e) == "Unknown" || Kind(r) == Kind(e))));
    public static string Kind(string effect)
    {
        var explicitKind = Regex.Match(effect, @"^(?:Effect:\s*)?(Focus|Click|Worn|Proc|Ornamentation)(?:[_ ]Effect)?\s*:", RegexOptions.IgnoreCase);
        if (explicitKind.Success) return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(explicitKind.Groups[1].Value.ToLowerInvariant());
        if (Regex.IsMatch(effect, @"\(\s*(?:Combat|Proc)\b|\bProc Effect\s*:", RegexOptions.IgnoreCase)) return "Proc";
        if (Regex.IsMatch(effect, @"^Focus(?:[_ ]Effect)?\s*:", RegexOptions.IgnoreCase) || effect.Contains("Focus Effect", StringComparison.OrdinalIgnoreCase)) return "Focus";
        if (Regex.IsMatch(effect, @"(?:^|\W)Worn(?:\W|$)", RegexOptions.IgnoreCase)) return "Worn";
        if (Regex.IsMatch(effect, @"(?:^|\W)Click(?:\W|$)|Casting Time\s*:|Must Equip", RegexOptions.IgnoreCase)) return "Click";
        return effect.Contains("Focus", StringComparison.OrdinalIgnoreCase) ? "Focus" : "Unknown";
    }
    public static EffectPurpose Purpose(string text, string type)
    {
        if (type == "Ornamentation") return EffectPurpose.Appearance;
        if (Regex.IsMatch(text, @"Summon|Gate\b|Teleport|Levitat|Invisib|Illusion|Vision|Identify|Bind Affinity|Water Breath", RegexOptions.IgnoreCase)) return EffectPurpose.Utility;
        if (Regex.IsMatch(text, @"Heal|Regenerat|Restoration|Cure", RegexOptions.IgnoreCase)) return EffectPurpose.Healing;
        return type == "Proc" ? EffectPurpose.Combat : EffectPurpose.Unknown;
    }
    public static void Normalize(ItemDefinition item)
    {
        bool Explicit(string e) => Regex.IsMatch(e, @"^(?:Effect:\s*)?(Focus|Click|Worn|Proc)(?:[_ ]Effect)?\s*:", RegexOptions.IgnoreCase);
        item.Effects = item.Effects.GroupBy(Canonical, StringComparer.OrdinalIgnoreCase).SelectMany(g => g.Where(Explicit).Select(Kind).Distinct().Count() > 1 ? g.Where(Explicit).DistinctBy(Kind) : [g.OrderByDescending(Explicit).First()]).ToList();
        item.EffectEvidence = item.Effects.Select(e => new ItemEffectEvidence { Text = e, SocketType = Kind(e), Purpose = Purpose(e, Kind(e)), Source = item.WikiUrl + " revision " + item.Revision }).ToList();
        if (item.Effects.Any(e => Regex.IsMatch(e, @"Summon (?:Horse|Mount)\b", RegexOptions.IgnoreCase)) && (item.Key == "black silken bridle" || item.Sources.Any(s => s.Method is "Vendor" or "Merchant" || s.Method.Contains("Buy", StringComparison.OrdinalIgnoreCase))))
        { item.ContentAvailability = ContentAvailability.Future; item.AvailabilityEvidence = "Current-game override · buyable mounts unavailable; user report 2026-10-18. Wiki merchant listings retained as conflicting evidence."; }
    }
    public static void Apply(Catalog catalog) { foreach (var item in catalog.Items) Normalize(item); }
}

public sealed record MaterialProgress(RecipeKind Kind, int Owned, int Required, bool CoverageKnown)
{
    public bool HasAny => Owned > 0;
    public string Label => Owned + " / " + Required + (CoverageKnown ? " components" : " documented components · coverage incomplete");
}
public sealed class MaterialIndex
{
    private readonly Dictionary<(string, RecipeKind), MaterialProgress> progress = [];
    public MaterialProgress For(string key, RecipeKind kind) => progress.GetValueOrDefault((key, kind), new(kind, 0, 0, false));
    public MaterialIndex(Catalog catalog, InventorySnapshot inventory, CharacterProfile profile)
    {
        int Stock(string key) => (catalog.FindKey(key) is { } item ? inventory.Evidence(item) : inventory.Entries.Where(e => Identity.Key(e.Name) == key)).Where(e => !e.IsAugment && !e.IsCollection && e.Count > 0).Sum(e => e.Count ?? 0);
        void Add(string output, RecipeKind kind, IEnumerable<ItemQuantity> requirements, bool complete)
        {
            var needs = requirements.GroupBy(i => i.ItemKey).Select(g => (Key: g.Key, Count: g.Sum(i => i.Count))).ToList();
            var p = new MaterialProgress(kind, needs.Sum(n => Math.Min(n.Count, Stock(n.Key))), needs.Sum(n => n.Count), complete);
            if (p.Owned > For(output, kind).Owned) progress[(output, kind)] = p;
        }
        foreach (var quest in catalog.Quests)
        {
            var steps = QuestRecipes.Steps(quest, profile);
            var produced = steps.SelectMany(s => s.Outputs.Select(o => o.ItemKey)).ToHashSet();
            var leaf = steps.SelectMany(s => s.Inputs).Concat(quest.Requirements).Where(i => i.ItemKey != quest.RewardKey && !produced.Contains(i.ItemKey)).DistinctBy(i => (i.ItemKey, i.Count)).ToList();
            Add(quest.RewardKey, RecipeKind.Quest, leaf, quest.RecipeVersion > 0);
            foreach (var step in steps.Where(s => s.Kind == RecipeKind.Tradeskill)) foreach (var output in step.Outputs) Add(output.ItemKey, RecipeKind.Tradeskill, step.Inputs, true);
        }
        foreach (var item in catalog.Items) foreach (var recipe in item.CraftRecipes.Where(r => r.Kind == RecipeKind.Tradeskill)) Add(item.Key, RecipeKind.Tradeskill, recipe.Inputs, true);
    }
}
public static class ItemEvidenceFixtures
{
    public static void Apply(Catalog catalog)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "data", "crafting-evidence.json");
        if (!File.Exists(file)) return;
        foreach (var fixture in Json.Read<List<PetItemRules.WikiFixture>>(file))
        {
            var parsed = WikiClient.ParseItem(fixture.Title, fixture.Text, fixture.Revision);
            if (catalog.Find(parsed.Name) is { } item && parsed.CraftRecipes.Count > 0) item.CraftRecipes = parsed.CraftRecipes;
        }
    }
}
