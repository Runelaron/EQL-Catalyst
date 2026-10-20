using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public sealed class WikiClient : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim requests = new(1);
    private DateTime lastRequest;
    private async Task<JsonDocument> Query(string url, CancellationToken token)
    {
        await requests.WaitAsync(token);
        try
        {
            var pause = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - lastRequest); if (pause > TimeSpan.Zero) await Task.Delay(pause, token);
            lastRequest = DateTime.UtcNow;
            using var response = await http.GetAsync(url + "&maxlag=5", token); response.EnsureSuccessStatusCode();
            var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (document.RootElement.TryGetProperty("error", out var error)) { string detail = error.ToString(); document.Dispose(); throw new InvalidDataException(detail); }
            return document;
        }
        finally { requests.Release(); }
    }
    public async Task<(List<string> Titles, string Continuation)> ItemTitlesAsync(string continuation, CancellationToken token)
    {
        using var doc = await Query("https://eqlwiki.com/api.php?action=query&list=embeddedin&eititle=Template:Itempage&einamespace=0&eilimit=500&format=json" + (continuation.Length > 0 ? "&eicontinue=" + Uri.EscapeDataString(continuation) : ""), token);
        return (doc.RootElement.GetProperty("query").GetProperty("embeddedin").EnumerateArray().Select(p => p.GetProperty("title").GetString()!).ToList(), doc.RootElement.TryGetProperty("continue", out var more) ? more.GetProperty("eicontinue").GetString() ?? "" : "");
    }
    public async Task<List<ItemDefinition>> SearchItemsAsync(string text, CancellationToken token)
    {
        using var doc = await Query("https://eqlwiki.com/api.php?action=query&list=search&srnamespace=0&srlimit=20&srsearch=" + Uri.EscapeDataString(text) + "&format=json", token);
        var names = doc.RootElement.GetProperty("query").GetProperty("search").EnumerateArray().Select(p => p.GetProperty("title").GetString()!).Prepend(text);
        return await FetchItemsAsync(names, token);
    }
    public WikiClient() => http.DefaultRequestHeaders.UserAgent.ParseAdd("EQLTracker/1.0 (local companion; cached batched requests)");
    public async Task<SpellReference?> SpellReferenceAsync(string name, CancellationToken token, bool exactName=false)
    {
        string title = exactName?name:SpellReference.BaseName(name);
        using var doc = await Query("https://eqlwiki.com/api.php?action=query&prop=revisions&rvprop=ids%7Ccontent&rvslots=main&redirects=1&format=json&titles=" + Uri.EscapeDataString(title), token);
        if (!doc.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return null;
        foreach (var page in pages.EnumerateObject()) { var p = page.Value; if (!p.TryGetProperty("revisions", out var revisions) || revisions.GetArrayLength() == 0) continue; var rev = revisions[0]; return SpellReference.Parse(p.GetProperty("title").GetString()!, rev.GetProperty("slots").GetProperty("main").GetProperty("*").GetString() ?? "", rev.GetProperty("revid").GetInt64()); }
        return null;
    }
    public async Task<List<ItemDefinition>> FetchItemsAsync(IEnumerable<string> names, CancellationToken token, Dictionary<string, long>? knownRevisions = null)
    {
        var result = new List<ItemDefinition>();
        foreach (var batch in names.Select(Identity.Canonical).Distinct().Chunk(50))
        {
            IEnumerable<string> changed = batch;
            if (knownRevisions != null)
            {
                var known = batch.Where(n => knownRevisions.ContainsKey(Identity.Key(n))).ToArray();
                if (known.Length > 0)
                {
                    var revisions = await CheckRevisionsAsync(known, token);
                    var unchanged = revisions.Where(r => knownRevisions.TryGetValue(Identity.Key(r.Title), out var previous) && previous == r.Revision).Select(r => Identity.Key(r.Title)).ToHashSet();
                    changed = batch.Where(n => !unchanged.Contains(Identity.Key(n))).ToList();
                    if (!changed.Any()) continue;
                }
            }
            var url = "https://eqlwiki.com/api.php?action=query&prop=revisions&rvprop=ids%7Ccontent&rvslots=main&redirects=1&format=json&titles=" + Uri.EscapeDataString(string.Join('|', changed));
            using var doc = await Query(url, token);
            if (!doc.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) continue;
            foreach (var page in pages.EnumerateObject())
            {
                var p = page.Value;
                if (!p.TryGetProperty("revisions", out var revisions) || revisions.GetArrayLength() == 0) continue;
                var rev = revisions[0]; var text = rev.GetProperty("slots").GetProperty("main").GetProperty("*").GetString() ?? "";
                if (!text.Contains("{{Itempage", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(ParseItem(p.GetProperty("title").GetString()!, text, rev.GetProperty("revid").GetInt64()));
            }
        }
        return result;
    }
    public async Task<List<SourceDocument>> CheckRevisionsAsync(IEnumerable<string> titles, CancellationToken token)
    {
        var url = "https://eqlwiki.com/api.php?action=query&prop=revisions&rvprop=ids%7Ctimestamp&redirects=1&format=json&titles=" + Uri.EscapeDataString(string.Join('|', titles.Take(50)));
        using var json = await Query(url, token); var result = new List<SourceDocument>();
        if (!json.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return result;
        foreach (var page in pages.EnumerateObject())
        {
            if (!page.Value.TryGetProperty("revisions", out var revisions) || revisions.GetArrayLength() == 0) continue;
            var title = page.Value.GetProperty("title").GetString()!;
            result.Add(new() { Title = title, Url = "https://eqlwiki.com/" + Uri.EscapeDataString(title.Replace(' ', '_')), Revision = revisions[0].GetProperty("revid").GetInt64(), Timestamp = revisions[0].GetProperty("timestamp").GetString()! });
        }
        return result;
    }
    public static ItemDefinition ParseItem(string title, string text, long revision)
    {
        static string Field(string t, string name) => WikiTemplateFields.Value(t, name);
        static string Plain(string t) => WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(t, @"\[\[(?:[^]|]+\|)?([^]]+)\]\]", "$1"), "<[^>]+>", " ")).Trim();
        string name = Plain(Field(text, "itemname")); if (name.Length == 0) name = title;
        var block = Plain(Field(text, "statsblock"));
        var item = new ItemDefinition { Name = name, Key = Identity.Key(name), WikiUrl = "https://eqlwiki.com/" + Uri.EscapeDataString(title.Replace(' ', '_')), Revision = revision, Aliases = name == title ? [] : [title], TwoHanded = block.Contains("Skill: 2H", StringComparison.OrdinalIgnoreCase) };
        string skill = Regex.Match(block, @"Skill:\s*(.+?)(?=\s+(?:Atk\s+Delay|Delay|DMG|AC|WT|Class):|$)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        item.Display = new() { Skill = skill, Type = skill.Equals("Archery", StringComparison.OrdinalIgnoreCase) ? "Bow" : skill, Source = item.WikiUrl, Revision = revision };
        item.Display.Flags = Regex.Matches(block, @"\b(?:MAGIC ITEM|LORE EQUIPPED|LORE ITEM|ARTIFACT|NO TRADE|NO DROP|NO RENT)\b", RegexOptions.IgnoreCase).Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();
        var weight = Regex.Match(block, @"\bWT:\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (weight.Success) item.Display.Weight = double.Parse(weight.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        item.Display.Size = Regex.Match(block, @"\bSize:\s*(TINY|SMALL|MEDIUM|LARGE|GIANT)", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
        var range = Regex.Match(block, @"\bRange:\s*(\d+)", RegexOptions.IgnoreCase);
        if (range.Success) item.Display.Range = int.Parse(range.Groups[1].Value);
        foreach (var stat in new[] { "AC", "HP", "MANA", "ATK", "STR", "STA", "DEX", "AGI", "INT", "WIS", "CHA", "Haste" })
        {
            var m = Regex.Match(block, @"\b" + stat + @":\s*\+?(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (m.Success) item.Stats[stat == "MANA" ? "Mana" : stat] = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        var dmg = Regex.Match(block, @"\bDMG:\s*(\d+)"); var delay = Regex.Match(block, @"Delay:\s*(\d+)");
        if (dmg.Success && delay.Success) { item.Stats["Damage"] = double.Parse(dmg.Groups[1].Value); item.Stats["Delay"] = double.Parse(delay.Groups[1].Value); if (item.Stats["Delay"] > 0) item.Stats["Ratio"] = item.Stats["Damage"] / item.Stats["Delay"]; }
        item.Slots = Regex.Match(block, @"Slot:\s*([A-Z0-9 /]+?)(?=\s+(?:Skill|AC|STR|STA|HP|Effect|WT|Size|Class|MANA):|$)").Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        item.Classes = Regex.Match(block, @"Class:\s*([A-Z ]+?)(?=\s+(?:Race|Deity|DMG|WT):|$)").Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var race = Regex.Match(block, @"Race:\s*(.+?)(?=\s+(?:Deity|DMG|WT):|$)").Groups[1].Value;
        if (race.Length > 0) item.Races.Add(race);
        item.Effects = Regex.Matches(Field(text, "statsblock"), @"Effect:\s*(.*?)(?=<br|\n|$)", RegexOptions.IgnoreCase).Select(m => Plain(m.Groups[1].Value)).ToList();
        foreach (var effect in new[] { "focus_effect", "worn_effect", "click_effect", "proc_effect" })
            if (Plain(Field(text, effect)) is { Length: > 0 } value) item.Effects.Add(effect.Replace('_', ' ') + ": " + value);
        var level = Regex.Match(block, @"Required Level:?\s*(\d+)", RegexOptions.IgnoreCase);
        if (level.Success) item.RequiredLevel = int.Parse(level.Groups[1].Value);
        var effectLevel = Regex.Match(block, @"at Level:?\s*(\d+)", RegexOptions.IgnoreCase);
        if (effectLevel.Success) item.EffectActivationLevel = int.Parse(effectLevel.Groups[1].Value);
        item.Deity = Regex.Match(block, @"Deity:\s*(.+?)(?=\s+(?:WT|Size|Race|DMG):|$)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        foreach (Match resistance in Regex.Matches(block, @"SV (\w+):\s*\+?(-?\d+)", RegexOptions.IgnoreCase))
            item.Stats["Resist" + System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(resistance.Groups[1].Value.ToLowerInvariant())] = double.Parse(resistance.Groups[2].Value);
        item.MetadataComplete = item.Slots.Count > 0 && item.Classes.Count > 0;
        if (item.Display.Type.Length == 0)
        {
            string documented = Plain(Field(text, "itemtype")); if (documented.Length == 0) documented = Plain(Field(text, "item_type"));
            if (documented.Length > 0) { item.Display.Type = documented; item.Display.TypeEvidence = "Documented item type"; }
            else if (Regex.IsMatch(text, @"\[\[Category:Shields?\]\]", RegexOptions.IgnoreCase)) { item.Display.Type = "Shield"; item.Display.TypeEvidence = "Shield category"; }
            else if (item.Slots.Count == 0 && Regex.IsMatch(text, @"\[\[Category:Quest Items\]\]", RegexOptions.IgnoreCase)) { item.Display.Type = "Quest item"; item.Display.TypeEvidence = "Quest Items category"; }
            else if (item.Slots.Count == 0 && Regex.IsMatch(text, @"\[\[Category:(?:Tradeskill|Tradeskill Items|Tradeskill Ingredients)\]\]", RegexOptions.IgnoreCase)) { item.Display.Type = "Tradeskill item"; item.Display.TypeEvidence = "Tradeskill category"; }
            else { string type = PetItemRules.Type(item); if (type != "Unknown") { item.Display.Type = type; item.Display.TypeEvidence = "Documented wearable slots and weapon stats"; } }
        }
        else item.Display.TypeEvidence = "Documented weapon skill";
        item.Display.Type = PetItemRules.CanonicalType(item.Display.Type);
        foreach (string field in new[] { "weapon_shape", "weapon_type", "itemtype", "item_type" })
        {
            string value = Plain(Field(text, field));
            if (value.Length > 0) ItemEquipmentIcons.ReadShape(item, value, item.WikiUrl + " revision " + revision + " · " + field + ": " + value);
            if (item.Display.WeaponShape != WeaponShape.Unknown) break;
        }
        var era = Regex.Match(text, @"(?:\{\{\s*|\[\[Category:\s*)(Classic|Temple|Kunark|Velious) Era", RegexOptions.IgnoreCase);
        if (era.Success) { item.IntroducedExpansion = era.Groups[1].Value.Equals("Temple", StringComparison.OrdinalIgnoreCase) ? "Classic" : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(era.Groups[1].Value.ToLowerInvariant()); item.ExpansionEvidence = item.WikiUrl + " revision " + revision; }
        item.MetadataVersion = PetItemRules.MetadataVersion;
        var zone = "Various";
        foreach (var line in Field(text, "dropsfrom").Split('\n'))
        {
            var links = Regex.Matches(line, @"\[\[([^]|]+)(?:\|[^]]*)?\]\]").Select(m => m.Groups[1].Value).ToList();
            if (!line.TrimStart().StartsWith('*') && links.Count > 0) zone = links[0];
            else if (line.TrimStart().StartsWith('*')) foreach (var mob in links.Count > 0 ? links : [Plain(line).TrimStart('*', ' ')])
                item.Sources.Add(new() { Zone = zone, Mob = mob, Availability = "Wiki documented", Era = "Unverified" });
        }
        string crafted = Field(text, "playercrafted");
        foreach (Match recipe in Regex.Matches(crafted, @"(?ms)^\*\s+\[\[(?<skill>Blacksmithing|Smithing|Tailoring|Baking|Brewing|Fletching|Jewelry Making|Jewelcraft|Pottery|Research)(?:\|[^]]*)?\]\].*?(?=^\*\s+\[\[|\z)"))
        {
            var inputs = Regex.Matches(recipe.Value, @"(\d+)\s*x\s*\[\[([^]|]+)(?:\|[^]]*)?\]\]", RegexOptions.IgnoreCase).Select(m => new ItemQuantity { ItemKey = Identity.Key(m.Groups[2].Value), Count = int.Parse(m.Groups[1].Value) }).ToList();
            if (inputs.Count == 0) continue;
            string container = Regex.Match(recipe.Value, @"\bIn\s*\[\[([^]|]+)", RegexOptions.IgnoreCase).Groups[1].Value;
            item.CraftRecipes.Add(new() { Id = item.Key + ":craft:" + item.CraftRecipes.Count, RecipeId = item.Key + ":craft:" + item.CraftRecipes.Count, Kind = RecipeKind.Tradeskill, Name = name, WikiUrl = item.WikiUrl, Revision = revision, Container = container, Inputs = inputs, Outputs = [new() { ItemKey = item.Key }], Instructions = Plain(recipe.Value) });
        }
        ItemEffects.Normalize(item); ItemExpansionEvidence.Apply(item);
        return item;
    }
    public void Dispose() => http.Dispose();
}
