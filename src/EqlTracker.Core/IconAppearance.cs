using System.Text.Json;
using System.Text.Json.Serialization;

namespace EqlTracker.Core;

[JsonConverter(typeof(IconArtworkStyleConverter))]
public enum IconArtworkStyle { Clean, Vibrant }
[JsonConverter(typeof(DamageEmblemConverter))]
public enum DamageEmblem { Fire, ArcaneOrb, Lightning, ArcaneSigil, MagicMissiles, ElementalVortex }

public sealed class IconArtworkStyleConverter : JsonConverter<IconArtworkStyle>
{
    public override IconArtworkStyle Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    { if (reader.TokenType == JsonTokenType.String && Enum.TryParse<IconArtworkStyle>(reader.GetString(), true, out var value) && Enum.IsDefined(value)) return value; reader.Skip(); return IconArtworkStyle.Vibrant; }
    public override void Write(Utf8JsonWriter writer, IconArtworkStyle value, JsonSerializerOptions options) => writer.WriteStringValue(Enum.IsDefined(value) ? value.ToString() : "Vibrant");
}
public sealed class DamageEmblemConverter : JsonConverter<DamageEmblem>
{
    public override DamageEmblem Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    { if (reader.TokenType == JsonTokenType.String && Enum.TryParse<DamageEmblem>(reader.GetString(), true, out var value) && Enum.IsDefined(value)) return value; reader.Skip(); return DamageEmblem.Fire; }
    public override void Write(Utf8JsonWriter writer, DamageEmblem value, JsonSerializerOptions options) => writer.WriteStringValue(Enum.IsDefined(value) ? value.ToString() : "Fire");
}

public static class ItemExpansionFilter
{
    public static IReadOnlyList<string> Choices { get; } = new[] { "Classic", "Kunark", "Velious", "Unknown" };
    public static string Identity(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : Choices.FirstOrDefault(x => x.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? value.Trim();
    public static bool Includes(IEnumerable<string> selected, string? introduction) => selected.Contains(Identity(introduction), StringComparer.OrdinalIgnoreCase);
    public static string Key(IEnumerable<string> selected) => string.Join(",", selected.Select(Identity).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
}
