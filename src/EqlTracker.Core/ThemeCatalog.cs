namespace EqlTracker.Core;

public sealed record ThemeChoice(string Id, string Name, string Family, string RenderKey);
public sealed record ThemeResolution(string Id, string Name, string Family, string RenderKey);

/// <summary>Display names are independent of persisted IDs and legacy rendering recipes.</summary>
public static class ThemeCatalog
{
    public static readonly ThemeChoice[] Choices =
    [
        new("Auto", "Auto", "Frost", "Rune4K_Frost_Clear"),
        new("coldain", "Coldain", "Frost", "Rune4K_Frost_Clear"),
        new("sunstrider", "Sunstrider", "Vellum", "Rune4K_Vellum_Clear"),
        new("shadowman", "Shadowman", "Shadowman", "Shadowman")
    ];
    private static readonly string[] Legacy = ["Classic EQ", "Frost Blue", "Midnight", "Warm Amber", "Rune4K_Frost_Clear", "Rune4K_Frost", "Rune4K_Vellum_Clear", "Rune4K_Vellum", "Rune4K_Krome"];
    public static ThemeResolution Resolve(string? value, string? gameSkin = null)
    {
        string key = value?.Trim() ?? "";
        if (key.Equals("Auto", StringComparison.OrdinalIgnoreCase)) key = gameSkin?.Trim() ?? "";
        var choice = Choices.Skip(1).FirstOrDefault(c => c.Id.Equals(key, StringComparison.OrdinalIgnoreCase) || c.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (choice != null) return new(choice.Id, choice.Name, choice.Family, choice.RenderKey);
        string? legacy = Legacy.FirstOrDefault(s => s.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (legacy != null)
        {
            string family = legacy == "Midnight" ? "Shadowman" : legacy == "Warm Amber" || legacy.Contains("Vellum") ? "Vellum" : "Frost";
            var match = Choices.Skip(1).First(c => c.Family == family);
            return new(match.Id, match.Name, family, legacy);
        }
        var fallback = Choices[1];
        return new(fallback.Id, fallback.Name, fallback.Family, fallback.RenderKey);
    }
    public static string SelectedName(string? saved) => saved == "Auto" ? "Auto" : Resolve(saved).Name;
}
