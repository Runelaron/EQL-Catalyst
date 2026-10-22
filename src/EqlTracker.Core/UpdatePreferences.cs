using System.Text.Json;

namespace EqlTracker.Core;

internal sealed class UpdatePreferences
{
    public int Schema { get; set; } = 1;
    public bool Automatic { get; set; }
    public DateTimeOffset? LastChecked { get; set; }
    public DateTimeOffset? LastAttempt { get; set; }
    public string HighestVersion { get; set; } = "";
    internal bool Due(DateTimeOffset now) => Automatic && (LastChecked == null || now - LastChecked >= TimeSpan.FromDays(7)) && (LastAttempt == null || now - LastAttempt >= TimeSpan.FromHours(6));
    internal static UpdatePreferences Read(string root)
    { try { return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(Path.Combine(root, "update-preferences.json"))) ?? new(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); } }
    internal void Save(string root,bool changeAutomatic=false)
    {
        Directory.CreateDirectory(root);string file=Path.Combine(root,"update-preferences.json");
        using var lease=new FileStream(Path.Combine(root,"update-preferences.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(File.Exists(file))
        {
            var saved=Read(root);if(!changeAutomatic)Automatic=saved.Automatic;
            if(saved.LastChecked>LastChecked||LastChecked==null)LastChecked=saved.LastChecked;
            if(saved.LastAttempt>LastAttempt||LastAttempt==null)LastAttempt=saved.LastAttempt;
            if(saved.HighestVersion.Length>0&&(HighestVersion.Length==0||UpdateProtocol.Newer(saved.HighestVersion,HighestVersion)))HighestVersion=saved.HighestVersion;
        }
        UpdateFiles.WriteJson(file,this);
    }
}

internal static class UpdateFiles
{
    internal static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static string Child(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains(':') || relative.Contains('\\') || relative.Split('/').Any(p => p is ".." or "." or "")) throw new InvalidDataException("Unsafe update file name.");
        string destination = Path.GetFullPath(Path.Combine(root, relative));
        if (!destination.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update path escapes its directory.");
        return destination;
    }
    internal static void NoLinks(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(path)); dir != null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Updates cannot replace linked application directories.");
    }
    internal static Dictionary<string, string> VerifyPayload(string root)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "release-manifest.json")));
        var files = json.RootElement.GetProperty("Files").Deserialize<Dictionary<string, string>>() ?? throw new InvalidDataException("Missing payload file list.");
        if (!files.ContainsKey("EQLTracker.exe") || !files.ContainsKey("EQLUpdater.exe")) throw new InvalidDataException("Incomplete application payload.");
        foreach (var (name, hash) in files)
        {
            string path = Child(root, name); NoLinks(Path.GetDirectoryName(path)!);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked update file.");
            using var stream = File.OpenRead(path);
            if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream)) != hash) throw new InvalidDataException("An application file failed verification: " + name);
        }
        return files;
    }
}
