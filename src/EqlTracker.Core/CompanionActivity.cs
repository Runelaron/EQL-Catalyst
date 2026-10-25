using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public sealed record GoalPurpose(string Kind, string Id, string Label);
public sealed class JournalActivity
{
    public string Id { get; set; } = "";
    public DateTime At { get; set; }
    public string Kind { get; set; } = "";
    public string ItemKey { get; set; } = "";
    public string Name { get; set; } = "";
    public int? Rank { get; set; }
    public int Quantity { get; set; }
    public string Source { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Disposition { get; set; } = "";
    public string Result { get; set; } = "";
    public long? Copper { get; set; }
    public bool Acquisition { get; set; }
    public bool PurposeRecorded { get; set; }
    public List<GoalPurpose> Purposes { get; set; } = [];
}
public sealed record LootReceiptGroup(string Identity, string ItemKey, string Name, int Quantity, string Purpose, string Disposition, DateTime Last, IReadOnlyList<JournalActivity> Receipts);
public static class ActivityJournal
{
    public static JournalActivity Receipt(string id, ItemObservation item, string zone, Catalog catalog, CharacterProfile? profile)
    {
        var definition = catalog.Find(item.Name);
        var purposes = new List<GoalPurpose>();
        if (definition != null && profile != null)
        {
            foreach (var quest in catalog.Quests.Where(q => QuestTracking.Selected(q, profile, new InventorySnapshot(), catalog) && q.Requirements.Concat(q.Steps.SelectMany(s => s.Inputs)).Any(i => i.ItemKey == definition.Key)))
                purposes.Add(new("Progress", quest.Id, quest.Name));
            foreach (var goal in profile.GoalQueue.Where(g => g.ItemKey == definition.Key)) purposes.Add(new(goal.Kind.ToString(), goal.Id, goal.Name));
        }
        // Sold/Merged corpse receipts still record acquisition; a later sale is not another receipt.
        return new() { Id = id + ":item", At = item.Time, Kind = "Item", ItemKey = definition?.Key ?? "", Name = item.Name, Rank = Identity.Rank(item.Name), Quantity = item.Count, Source = item.Source, Zone = zone, Disposition = item.Action, Result = item.Result, Acquisition = item.Action is "Looted" or "Received" or "Created" || item.Source.Length > 0 && item.Action is "Sold" or "Merged", Purposes = purposes, PurposeRecorded = profile != null };
    }
    public static IReadOnlyList<LootReceiptGroup> Loot(IEnumerable<JournalActivity> activity, PlaySession? session, string query = "") => activity
        .Where(a => a.Kind == "Item" && a.Acquisition && InSession(a, session) && (query.Length == 0 || a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || a.Purposes.Any(p => p.Label.Contains(query, StringComparison.OrdinalIgnoreCase))))
        .DistinctBy(a => a.Id).GroupBy(a => (a.ItemKey.Length > 0 ? a.ItemKey : Identity.Key(a.Name)) + ":" + a.Rank)
        .Select(g => new LootReceiptGroup(g.Key, g.First().ItemKey, g.First().Name, g.Sum(a => a.Quantity), string.Join(" · ", g.SelectMany(a => a.Purposes).Select(p => p.Label).Distinct()), string.Join(" / ", g.Select(a => a.Disposition).Distinct()), g.Max(a => a.At), g.OrderByDescending(a => a.At).ThenBy(a => a.Id).ToArray()))
        .OrderByDescending(g => g.Last).ThenBy(g => g.Identity).ToArray();
    public static bool InSession(JournalActivity activity, PlaySession? session) => session == null || activity.At >= session.Start && activity.At <= session.LastObserved && (!session.NextLogin.HasValue || activity.At < session.NextLogin);
    public static long? ParseCopper(string text)
    {
        long amount = 0; bool found = false;
        foreach (Match match in Regex.Matches(text, @"(?<n>[\d,]+) (?<unit>platinum|gold|silver|copper)(?: pieces?)?\b", RegexOptions.IgnoreCase))
        {
            if (!long.TryParse(match.Groups["n"].Value.Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out long n)) return null;
            int factor = match.Groups["unit"].Value.ToLowerInvariant() switch { "platinum" => 1000, "gold" => 100, "silver" => 10, _ => 1 };
            try { amount = checked(amount + n * factor); } catch (OverflowException) { return null; } found = true;
        }
        return found ? amount : null;
    }
}
public sealed class CampNotebook
{
    public Dictionary<string, CampNote> Sources { get; set; } = [];
    public CampTimer? Timer { get; set; }
    public static string Key(ItemSource source) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", source.Zone.Trim().ToLowerInvariant(), source.Mob.Trim().ToLowerInvariant(), source.Method.Trim().ToLowerInvariant()))));
    public CampNote Get(ItemSource source)
    {
        string key = Key(source); if (!Sources.TryGetValue(key, out var note)) Sources[key] = note = new() { Id = key, Zone = source.Zone, Mob = source.Mob }; return note;
    }
    public void Start(ItemSource source, DateTime from, TimeSpan estimate)
    {
        if (estimate <= TimeSpan.Zero || estimate > TimeSpan.FromDays(30)) throw new InvalidDataException("Enter an estimate between one second and 30 days.");
        Timer = new(Key(source), source.Mob, from, from + estimate);
    }
}
public sealed class CampNote
{
    public string Id { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Mob { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<DateTime> Visits { get; set; } = [];
}
public sealed record CampTimer(string SourceId, string Name, DateTime Started, DateTime Due)
{
    public string Label(DateTime now) => now >= Due ? "Estimate elapsed" : "Estimated respawn · " + Due.ToString("HH:mm:ss");
}
public sealed record Holding(string ItemKey, string Name, int? Quantity, string Location, int Rank, bool Extracted);
public sealed class HoldingsSummary
{
    public string Character { get; set; } = "";
    public string Server { get; set; } = "";
    public DateTime CapturedAt { get; set; }
    public List<Holding> Items { get; set; } = [];
    public static HoldingsSummary From(CharacterProfile profile, InventorySnapshot inventory, Catalog catalog) => new()
    {
        Character = profile.Character, Server = profile.Server, CapturedAt = inventory.CapturedAt,
        Items = inventory.Entries.Where(e => e.Count == null || e.Count > 0).Select(e => new Holding(catalog.FindKey(e.ConfirmedItemKey)?.Key ?? catalog.Find(e.Name, e.GameId == 0 ? null : e.GameId)?.Key ?? "", e.Name, e.Count, e.Location, e.Rank, e.IsAugment)).ToList()
    };
    public IEnumerable<Holding> Match(ItemDefinition item) => Items.Where(i => i.ItemKey == item.Key || i.ItemKey.Length == 0 && Identity.Key(i.Name) == Identity.Key(item.Name));
}
public sealed record OtherCharacterHolding(string Character, DateTime CapturedAt, Holding Item);
/// <summary>Small summary files, published only after the inventory transaction commits.</summary>
public static class HoldingsIndex
{
    public static void BackfillMissing(string root,string server,Catalog catalog)
    {
        if(!Directory.Exists(root))return;
        foreach(string folder in Directory.EnumerateDirectories(root).Take(1000))
        {
            string path=Path.Combine(folder,"tracker.db");if(!File.Exists(path))continue;
            string marker=Path.Combine(root,"holdings","migrated-"+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(folder.ToUpperInvariant()))))+".done";if(File.Exists(marker))continue;
            try
            {
                using var connection=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=path,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString());connection.Open();
                using var command=connection.CreateCommand();command.CommandText="SELECT key,json FROM state WHERE key IN ('profile','inventory')";using var reader=command.ExecuteReader();CharacterProfile? profile=null;InventorySnapshot? inventory=null;
                while(reader.Read()){if(reader.GetString(0)=="profile")profile=System.Text.Json.JsonSerializer.Deserialize<CharacterProfile>(reader.GetString(1),Json.Options);else inventory=System.Text.Json.JsonSerializer.Deserialize<InventorySnapshot>(reader.GetString(1),Json.Options);}
                if(profile==null||inventory==null||!profile.Server.Equals(server,StringComparison.OrdinalIgnoreCase))continue;Publish(root,HoldingsSummary.From(profile,inventory,catalog));File.WriteAllText(marker,"summary-v1");
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or Microsoft.Data.Sqlite.SqliteException){}
        }
    }
    public static void Publish(string root, HoldingsSummary summary)
    {
        string name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(summary.Server.ToUpperInvariant() + "/" + summary.Character.ToUpperInvariant())));
        Json.Write(Path.Combine(root, "holdings", name + ".json"), summary);
    }
    public static List<HoldingsSummary> Read(string root, string server, string current)
    {
        string directory = Path.Combine(root, "holdings"); if (!Directory.Exists(directory)) return [];
        var result = new List<HoldingsSummary>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json").Take(1000))
            try { var summary = Json.Read<HoldingsSummary>(path); if (summary.Server.Equals(server, StringComparison.OrdinalIgnoreCase) && !summary.Character.Equals(current, StringComparison.OrdinalIgnoreCase)) result.Add(summary); }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        return result;
    }
}
public sealed record SessionRecap(PlaySession Session, IReadOnlyList<LootReceiptGroup> Loot, IReadOnlyList<JournalActivity> Progress, IReadOnlyList<JournalActivity> Setup, long? Coin, long? MerchantIncome, long Damage, long PlayerDamage, long PetDamage, int Encounters)
{
    public bool HistoryIncomplete { get; init; }
    public static SessionRecap Build(PlaySession session, IEnumerable<JournalActivity> journal, IEnumerable<Encounter> encounters)
    {
        var activity = journal.Where(a => ActivityJournal.InSession(a, session)).DistinctBy(a => a.Id).ToArray();
        var fights = encounters.Where(f => f.Start >= session.Start && f.Start <= session.LastObserved && (!session.NextLogin.HasValue || f.Start < session.NextLogin)).DistinctBy(f => f.Id).ToArray();
        long? Total(string kind) { var rows = activity.Where(a => a.Kind == kind && a.Copper.HasValue).ToArray(); return rows.Length > 0 ? rows.Sum(a => a.Copper!.Value) : null; }
        return new(session, ActivityJournal.Loot(activity, session), activity.Where(a => a.Kind == "Progress").ToArray(), activity.Where(a => a.Kind is "Planned setup" or "Observed setup" or "Compatibility").ToArray(), Total("Coin"), Total("Merchant"), fights.Sum(f => f.Damage), fights.Sum(f => f.PlayerDamage), fights.Sum(f => f.PetDamage), fights.Length);
    }
}
