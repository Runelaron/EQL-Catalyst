namespace EqlTracker.Core;

public sealed class BorderlessPlan
{
    public int Version { get; set; } = 1;
    public string GameFolder { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public List<NativeIniChange> Changes { get; set; } = [];
}
public static class BorderlessSetup
{
    public static BorderlessPlan Prepare(string gameFolder, string folder, int width, int height)
    {
        if (width is < 640 or > 16384 || height is < 480 or > 16384) throw new InvalidDataException("Invalid monitor dimensions.");
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new InvalidOperationException("Choose an empty display setup folder.");
        string root = Path.GetFullPath(gameFolder); byte[] bytes = File.ReadAllBytes(Path.Combine(root, "eqclient.ini")); var ini = new NativeIni(bytes);
        var plan = new BorderlessPlan { GameFolder = root, Width = width, Height = height };
        foreach (var (section, key, value) in new[] { ("VideoMode", "Fullscreen", "0"), ("VideoMode", "WindowedWidth", width.ToString()), ("VideoMode", "WindowedHeight", height.ToString()), ("Defaults", "Maximized", "1") }) plan.Changes.Add(new(section, key, ini.Get(section, key), value, value));
        Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, "before-eqclient.ini"), bytes);
        foreach (var change in plan.Changes) ini.Set(change.Section, change.Key, change.After);
        File.WriteAllBytes(Path.Combine(folder, "borderless-eqclient.ini"), ini.Bytes()); Json.Write(Path.Combine(folder, "display-plan.json"), plan);
        File.WriteAllText(Path.Combine(folder, "REVIEW.md"), $"# Borderless display setup\n\nGame: `{root}`\nWindowed rendering at {width}×{height}; the companion removes the window frame at runtime. Only Fullscreen, WindowedWidth, WindowedHeight, and Maximized change. Refresh rate, resolution for exclusive mode, camera, and unrelated settings are retained.\n\nClose EverQuest before applying or restoring. Backups are created on apply. Enable Borderless framing in Settings while the companion runs. Exit restores the prior runtime window frame; Restore display setup restores the managed saved settings.\n");
        return plan;
    }
    public static string Apply(string folder, bool restore = false, Func<bool>? running = null)
    {
        void Closed() { if ((running ?? NativeSetup.GameRunning)()) throw new InvalidOperationException("Close EverQuest before changing saved display settings. No game files were changed."); }
        Closed(); var plan = Json.Read<BorderlessPlan>(Path.Combine(folder, "display-plan.json"));
        if (plan.Version != 1 || plan.Width is < 640 or > 16384 || plan.Height is < 480 or > 16384 || plan.Changes.Count != 4) throw new InvalidDataException("Invalid display setup plan.");
        var allowed = new Dictionary<string, string> { ["VideoMode/Fullscreen"] = "0", ["VideoMode/WindowedWidth"] = plan.Width.ToString(), ["VideoMode/WindowedHeight"] = plan.Height.ToString(), ["Defaults/Maximized"] = "1" };
        if (plan.Changes.Select(x => x.Section + "/" + x.Key).Distinct().Count() != 4 || plan.Changes.Any(x => !allowed.TryGetValue(x.Section + "/" + x.Key, out var expected) || x.After != expected)) throw new InvalidDataException("Unexpected display setting.");
        string path = Path.Combine(Path.GetFullPath(plan.GameFolder), "eqclient.ini"); byte[] before = File.ReadAllBytes(path); var ini = new NativeIni(before); int retained = 0;
        foreach (var change in plan.Changes)
        {
            string? current = ini.Get(change.Section, change.Key);
            if (restore) { if (current == change.After) ini.Set(change.Section, change.Key, change.Before); else if (current != change.Before) retained++; }
            else { if (current != change.Before && current != change.After) throw new InvalidOperationException("A managed display setting changed since review: " + change.Key); ini.Set(change.Section, change.Key, change.After); }
        }
        byte[] after = ini.Bytes(); Closed();
        if (!before.SequenceEqual(File.ReadAllBytes(path))) throw new IOException("Display settings changed during setup.");
        if (!before.SequenceEqual(after))
        {
            string backup = Path.Combine(folder, "applied-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")); Directory.CreateDirectory(backup); File.WriteAllBytes(Path.Combine(backup, "eqclient.ini"), before);
            string temporary = path + ".eqltracker-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, after); Closed(); File.Move(temporary, path, true); } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        string result = restore ? $"Display setup restored; {retained} subsequently edited field(s) retained." : "Windowed rendering configured. Start EverQuest normally; the companion will fit it borderlessly to its monitor.";
        Json.Write(Path.Combine(folder, "last-display-operation.json"), new { Success = true, Restore = restore, Message = result }); return result;
    }
}
