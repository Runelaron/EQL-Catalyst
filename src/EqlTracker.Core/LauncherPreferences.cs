using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace EqlTracker.Core;

public sealed record LaunchProfile(string Character, string Server, string GameFolder)
{
    public override string ToString() => Character + " · " + Server;
    public bool Valid => LauncherPreferences.ValidName(Character) && LauncherPreferences.ValidName(Server);
    public string DataFolder(string root) => Valid ? Path.Combine(root, Character + "_" + Server) : throw new InvalidDataException("Invalid character/server name.");
}

public sealed class LauncherPreferences
{
    public int Version { get; set; } = 1;
    public LaunchProfile? LastProfile { get; set; }
    public static bool ValidName(string? value) => value != null && Regex.IsMatch(value, @"\A[A-Za-z][A-Za-z0-9_-]{0,60}\z");
    public static LauncherPreferences Read(string root)
    {
        try { return Json.Read<LauncherPreferences>(Path.Combine(root, "launcher.json")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Remember(string root, LaunchProfile profile)
    {
        if (!profile.Valid) throw new InvalidDataException("Invalid profile identity.");
        LastProfile = profile;
        Directory.CreateDirectory(root);
        string destination = Path.Combine(root, "launcher.json"), temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this, Json.Options)); File.Move(temp, destination, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static List<LaunchProfile> Existing(string root)
    {
        var result = new List<LaunchProfile>();
        if (!Directory.Exists(root)) return result;
        foreach (string folder in Directory.EnumerateDirectories(root))
        {
            string file = Path.Combine(folder, "tracker.db"); if (!File.Exists(file)) continue;
            try
            {
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); connection.Open();
                using var command = connection.CreateCommand(); command.CommandText = "SELECT json FROM state WHERE key='profile'";
                if (command.ExecuteScalar() is string json && JsonSerializer.Deserialize<CharacterProfile>(json, Json.Options) is { } p)
                {
                    var profile = new LaunchProfile(p.Character, p.Server, p.GameFolder);
                    if (profile.Valid && Path.GetFullPath(profile.DataFolder(root)).Equals(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)) result.Add(profile);
                }
            }
            catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException or JsonException) { /* An unreadable profile is left untouched. */ }
        }
        return result.OrderBy(p => p.Character, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Server, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static List<LaunchProfile> FromGame(string folder)
    {
        var result = new Dictionary<string, LaunchProfile>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return [];
        IEnumerable<string> files = Directory.EnumerateFiles(folder, "UI_*_LO*.ini").Concat(Directory.EnumerateFiles(folder, "*-Inventory.txt"));
        string logs = Path.Combine(folder, "Logs");
        if (Directory.Exists(logs)) files = files.Concat(Directory.EnumerateFiles(logs, "eqlog_*.txt"));
        foreach (string file in files)
        {
            var match = Regex.Match(Path.GetFileName(file), @"^(?:eqlog_|UI_)?(?<character>[A-Za-z][A-Za-z0-9-]*)_(?<server>[A-Za-z][A-Za-z0-9_-]*?)(?:_LO\d+\.ini|-Inventory\.txt|\.txt)$", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            var p = new LaunchProfile(match.Groups["character"].Value, match.Groups["server"].Value, Path.GetFullPath(folder));
            if (p.Valid) result[p.Character + "_" + p.Server] = p;
        }
        return result.Values.OrderBy(p => p.Character).ThenBy(p => p.Server).ToList();
    }
    public static CharacterProfile NewProfile(LaunchProfile launch)
    {
        if (!launch.Valid) throw new InvalidDataException("Invalid profile identity.");
        return new()
        {
            Character = launch.Character, Server = launch.Server, GameFolder = launch.GameFolder,
            CurrentClasses = [], OptionalClasses = [], Loadouts = [new() { Classes = [], Level = 0, Race = "", PetType = "Unknown" }]
        };
    }
}
