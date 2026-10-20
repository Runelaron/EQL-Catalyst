using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EqlTracker.Core;

public sealed record NativeIniChange(string Section, string Key, string? Before, string After, string StandardAfter);
public sealed class NativeSetupFile
{
    public string RelativePath { get; set; } = "";
    public string BeforeHash { get; set; } = "";
    public string CompactHash { get; set; } = "";
    public string StandardHash { get; set; } = "";
    public List<NativeIniChange> Changes { get; set; } = [];
}
public sealed class NativeSetupPlan
{
    public int Version { get; set; } = 1;
    public string GameFolder { get; set; } = "";
    public string Character { get; set; } = "";
    public string Server { get; set; } = "";
    public int Bar { get; set; }
    public string Skin { get; set; } = "";
    public bool CompactAvailable { get; set; }
    public string CompactStatus { get; set; } = "Standard native bar";
    public Dictionary<string, int> Macros { get; set; } = [];
    public List<NativeSetupFile> Files { get; set; } = [];
    public bool LabelRepairOnly { get; set; }
}
public sealed record NativeSetupResult(int Bar, string Mode, string Message);

// Edits retain the original encoding, newline style, comments, and unrelated keys.
internal sealed class NativeIni
{
    private string text;
    private readonly Encoding encoding;
    private readonly byte[] preamble;
    private readonly string newline;
    public NativeIni(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 })) { encoding = new UTF32Encoding(false, false, true); preamble = [0xFF, 0xFE, 0, 0]; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF })) { encoding = new UTF32Encoding(true, false, true); preamble = [0, 0, 0xFE, 0xFF]; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = Encoding.Unicode; preamble = [0xFF, 0xFE]; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = Encoding.BigEndianUnicode; preamble = [0xFE, 0xFF]; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { encoding = new UTF8Encoding(false, true); preamble = [0xEF, 0xBB, 0xBF]; }
        else
        {
            preamble = [];
            try { _ = new UTF8Encoding(false, true).GetString(bytes); encoding = new UTF8Encoding(false, true); }
            catch (DecoderFallbackException) { encoding = Encoding.Latin1; }
        }
        text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
        newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }
    private List<string> Lines => text.Split('\n').ToList();
    public Dictionary<string, string> Section(string section)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); bool inside = false;
        foreach (var raw in Lines)
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { inside = line[1..^1].Equals(section, StringComparison.OrdinalIgnoreCase); continue; }
            if (!inside || line.StartsWith(';') || line.StartsWith('#')) continue;
            int equals = line.IndexOf('='); if (equals > 0) values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }
        return values;
    }
    public string? Get(string section, string key) => Section(section).GetValueOrDefault(key);
    public void Set(string section, string key, string? value)
    {
        var lines = Lines; int start = lines.FindIndex(x => x.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            if (value == null) return;
            if (text.Length > 0 && !text.EndsWith('\n')) text += newline;
            text += "[" + section + "]" + newline + key + "=" + value + newline; return;
        }
        int end = lines.FindIndex(start + 1, x => x.Trim().StartsWith('[')); if (end < 0) end = lines.Count;
        var matches = Enumerable.Range(start + 1, end - start - 1).Where(i =>
        {
            string line = lines[i].Trim(); int equals = line.IndexOf('=');
            return equals > 0 && line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
        }).ToList();
        if (matches.Count > 1) throw new InvalidDataException("Duplicate managed INI key: " + section + "/" + key);
        if (matches.Count == 1)
        {
            int i = matches[0];
            if (value == null) lines.RemoveAt(i);
            else lines[i] = lines[i][..(lines[i].IndexOf('=') + 1)] + value + (lines[i].EndsWith('\r') ? "\r" : "");
        }
        else if (value != null)
        {
            if (end > 0 && end == lines.Count && !lines[end - 1].EndsWith('\r') && lines[end - 1].Length > 0 && newline == "\r\n") lines[end - 1] += "\r";
            int insertion = end == lines.Count && lines[^1].Length == 0 ? end - 1 : end;
            lines.Insert(insertion, key + "=" + value + (newline == "\r\n" ? "\r" : ""));
        }
        text = string.Join('\n', lines);
        if (!text.EndsWith('\n')) text += text.EndsWith('\r') ? "\n" : newline;
    }
    public byte[] Bytes() => preamble.Concat(encoding.GetBytes(text)).ToArray();
}

public static class NativeSetup
{
    private static readonly string[] Labels = ["Log", "Classes", "Inventory", "Sync"];
    private static readonly string[] ShortNames = ["Log", "Who", "Inv", "Sync"];
    private static readonly string[][] Commands = [["/log on"], ["/who"], ["/outputfile inventory"], ["/pause 2, /log on", "/pause 2, /who", "/outputfile inventory"]];
    private static readonly Regex SafeName = new(@"^[A-Za-z][A-Za-z0-9_-]{0,60}$");
    public static bool GameRunning()
    {
        var games = Process.GetProcessesByName("eqgame");
        try { return games.Length > 0; } finally { foreach (var game in games) game.Dispose(); }
    }
    private static void EnsureClosed(Func<bool>? running)
    { if ((running ?? GameRunning)()) throw new InvalidOperationException("Close EverQuest before installing or restoring native buttons. No game files were changed."); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Target(string root, string relative)
    {
        var absolute = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathFullyQualified(relative) || !absolute.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Setup path escapes the selected game folder.");
        return absolute;
    }
    private static bool Occupied(string value) => value.Length > 0 && value != "0" && !value.StartsWith("0,", StringComparison.Ordinal);

    public static NativeSetupPlan Generate(string gameFolder, string character, string server, string folder)
    {
        if (!SafeName.IsMatch(character) || !SafeName.IsMatch(server)) throw new InvalidDataException("Invalid character or server name.");
        var root = Path.GetFullPath(gameFolder);
        string stem = character + "_" + server;
        string uiPath = Directory.GetFiles(root, "UI_" + stem + "_LO*.ini").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? throw new FileNotFoundException("No saved character UI was found.");
        string profileName = Path.GetFileName(uiPath)[3..];
        string profilePath = Target(root, profileName);
        var profile = new NativeIni(File.ReadAllBytes(profilePath)); var ui = new NativeIni(File.ReadAllBytes(uiPath));
        var knownNames = new[] { "EQLLog", "EQLClasses", "EQLInv", "EQLSync" };
        if (profile.Section("Socials").Any(kv => kv.Key.EndsWith("Name", StringComparison.OrdinalIgnoreCase) && (knownNames.Contains(kv.Value) || ShortNames.Contains(kv.Value) && profile.Get("Socials", kv.Key[..^4] + "Line1") == Commands[Array.IndexOf(ShortNames, kv.Value)][0])))
            throw new InvalidOperationException("Tracker macro names already exist. Choose the original setup package to update or restore the controls; a duplicate set was not created.");
        var existing = Enumerable.Range(1, 11).Select(bar => (Bar: bar, Section: bar == 1 ? "HotButtons" : "HotButtons" + bar)).ToList();
        var empty = existing.Where(x => !profile.Section(x.Section).Any(kv => Regex.IsMatch(kv.Key, @"^Page\d+Button\d+$", RegexOptions.IgnoreCase) && Occupied(kv.Value))).ToList();
        var chosen = empty.FirstOrDefault(x => x.Bar == 11);
        if (chosen.Bar == 0) chosen = empty.OrderByDescending(x => x.Bar).FirstOrDefault();
        if (chosen.Bar == 0) throw new InvalidOperationException("All native hotbutton bars are occupied. Free a bar before creating the native setup package.");
        string skin = ui.Get("Main", "UISkin") ?? "default";
        if (!Regex.IsMatch(skin, @"^[A-Za-z0-9_-]{1,80}$")) throw new InvalidDataException("Invalid saved skin name.");
        var plan = new NativeSetupPlan { GameFolder = root, Character = character, Server = server, Bar = chosen.Bar, Skin = skin };
        var profileFile = new NativeSetupFile { RelativePath = profileName }; var uiFile = new NativeSetupFile { RelativePath = Path.GetFileName(uiPath) };
        void Edit(NativeSetupFile file, NativeIni ini, string section, string key, string value, string? standard = null) => file.Changes.Add(new(section, key, ini.Get(section, key), value, standard ?? value));
        var free = Enumerable.Range(0, 120).Where(slot =>
        {
            string prefix = $"Page{slot / 12 + 1}Button{slot % 12 + 1}";
            return !profile.Section("Socials").Any(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && (kv.Key[prefix.Length..] == "Name" || kv.Key[prefix.Length..].StartsWith("Line", StringComparison.OrdinalIgnoreCase)) && kv.Value.Length > 0);
        }).OrderDescending().Take(4).ToList();
        if (free.Count < 4) throw new InvalidOperationException("Four unused social macro slots are required. Existing macros were preserved.");
        for (int i = 0; i < 4; i++)
        {
            int slot = free[i]; plan.Macros[Labels[i]] = slot; string prefix = $"Page{slot / 12 + 1}Button{slot % 12 + 1}";
            Edit(profileFile, profile, "Socials", prefix + "Name", ShortNames[i]);
            Edit(profileFile, profile, "Socials", prefix + "Color", "0");
            for (int line = 0; line < 5; line++) Edit(profileFile, profile, "Socials", prefix + "Line" + (line + 1), line < Commands[i].Length ? Commands[i][line] : "");
            Edit(profileFile, profile, chosen.Section, "Page1Button" + (i + 1), $"E{slot},@-1,0000000000000000,0,{(i == 2 ? "Inventory" : Labels[i])},");
        }
        string window = chosen.Bar == 1 ? "HotButtonWnd" : "HotButtonWnd" + chosen.Bar;
        foreach (var (key, value) in new Dictionary<string, string> { ["Show"] = "1", ["ShowKeyMap"] = "0", ["ShowSpinner"] = "0", ["PercentSize"] = "100", ["XRef"] = "left", ["YRef"] = "top", ["XPos"] = "0.625000%", ["YPos"] = "4.166667%", ["Width"] = "180", ["Height"] = "48", ["Locked"] = "0", ["ClickThrough"] = "0" })
            Edit(uiFile, ui, window, key, value, key == "Width" ? "525" : key == "Height" ? "53" : key == "ShowSpinner" ? "1" : value);
        plan.Files.AddRange([profileFile, uiFile]);
        string xmlRelative = Path.Combine("uifiles", skin, "EQUI_HotButtonWnd.xml"); string xmlPath = Target(root, xmlRelative);
        byte[]? compact = null;
        if (File.Exists(xmlPath) && skin.StartsWith("Rune4K_", StringComparison.OrdinalIgnoreCase))
        {
            try { compact = CompactXml(File.ReadAllBytes(xmlPath), window); plan.CompactAvailable = true; plan.CompactStatus = "Compact native layout preserves all 12 client button bindings; in-game verification pending"; }
            catch (InvalidDataException error) { plan.CompactStatus = "Standard native bar: " + error.Message; }
        }
        if (compact != null) plan.Files.Add(new() { RelativePath = xmlRelative });
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new InvalidOperationException("Choose an empty setup package folder.");
        Directory.CreateDirectory(folder);
        foreach (var file in plan.Files)
        {
            var original = File.ReadAllBytes(Target(root, file.RelativePath)); file.BeforeHash = Hash(original);
            var compactBytes = original; var standardBytes = original;
            if (file.Changes.Count > 0)
            {
                var a = new NativeIni(original); var b = new NativeIni(original);
                foreach (var change in file.Changes) { a.Set(change.Section, change.Key, change.After); b.Set(change.Section, change.Key, change.StandardAfter); }
                compactBytes = a.Bytes(); standardBytes = b.Bytes();
            }
            else compactBytes = compact!;
            file.CompactHash = Hash(compactBytes); file.StandardHash = Hash(standardBytes);
            foreach (var (directory, bytes) in new[] { ("before", original), ("compact", compactBytes), ("standard", standardBytes) })
            { string path = Target(folder, Path.Combine(directory, file.RelativePath)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
        }
        Json.Write(Path.Combine(folder, "manifest.json"), plan);
        var review = new StringBuilder($"# Native tracker controls\n\nCharacter: {character} / {server}\nSkin: {skin}\nUnused bar: {chosen.Bar}\n\n{plan.CompactStatus}\n\n");
        review.AppendLine("| Button | Native command |\n|---|---|");
        for (int i = 0; i < 4; i++) review.AppendLine($"| {Labels[i]} | {string.Join(" → ", Commands[i])} |");
        review.AppendLine("\n`before`, `compact`, and `standard` contain the exact reviewable file versions. `manifest.json` lists each managed INI key. Installation merges only those keys and creates fresh backups; unrelated changes made after package generation are retained.\n\nClose EverQuest before installation or restoration. Use the companion's Native Controls window or the setup script. Start EverQuest normally afterward. If the compact layout does not render correctly, apply the Standard mode from the same package. No executable is injected into the game.\n");
        File.WriteAllText(Path.Combine(folder, "REVIEW.md"), review.ToString());
        return plan;
    }

    // A separate package can be reviewed while the game runs. Installation changes
    // only the four names, leaving client-managed placement, scale, and bindings alone.
    public static NativeSetupPlan GenerateLabelRepair(string originalFolder, string folder)
    {
        var installed = ReadPlan(originalFolder);
        if (installed.LabelRepairOnly) throw new InvalidOperationException("Choose the original four-button setup package.");
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new InvalidOperationException("Choose an empty setup package folder.");
        string relative = installed.Files.Single(x => !x.RelativePath.StartsWith("UI_", StringComparison.OrdinalIgnoreCase) && x.RelativePath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)).RelativePath;
        byte[] before = File.ReadAllBytes(Target(installed.GameFolder, relative)); var ini = new NativeIni(before);
        var plan = new NativeSetupPlan { GameFolder = installed.GameFolder, Character = installed.Character, Server = installed.Server, Bar = installed.Bar, Skin = installed.Skin, CompactAvailable = true, CompactStatus = "Label-only repair: Log / Who / Inv / Sync", Macros = new(installed.Macros), LabelRepairOnly = true };
        var file = new NativeSetupFile { RelativePath = relative, BeforeHash = Hash(before) };
        for (int i = 0; i < Labels.Length; i++)
        {
            int slot = plan.Macros[Labels[i]]; string prefix = $"Page{slot / 12 + 1}Button{slot % 12 + 1}";
            string? current = ini.Get("Socials", prefix + "Name");
            if (current != "EQL" + (i == 2 ? "Inv" : Labels[i]) && current != ShortNames[i]) throw new InvalidOperationException("A tracker macro was renamed; no repair package was created.");
            for (int line = 0; line < 5; line++)
                if ((ini.Get("Socials", prefix + "Line" + (line + 1)) ?? "") != (line < Commands[i].Length ? Commands[i][line] : "")) throw new InvalidOperationException("A tracker macro command changed; no repair package was created.");
            file.Changes.Add(new("Socials", prefix + "Name", current, ShortNames[i], ShortNames[i])); ini.Set("Socials", prefix + "Name", ShortNames[i]);
        }
        byte[] after = ini.Bytes(); file.CompactHash = file.StandardHash = Hash(after); plan.Files.Add(file);
        foreach (var (directory, bytes) in new[] { ("before", before), ("compact", after), ("standard", after) })
        { string path = Target(folder, Path.Combine(directory, relative)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
        Json.Write(Path.Combine(folder, "manifest.json"), plan);
        File.WriteAllText(Path.Combine(folder, "REVIEW.md"), $"# Native button label repair\n\n{plan.Character} / {plan.Server}, bar {plan.Bar}.\n\nOnly four social macro names change to **Log / Who / Inv / Sync**, fitting the existing native 40-pixel buttons. Commands, bindings, skin XML, scale, and window position are retained.\n\nClose EverQuest before installation. Apply this package using the setup script's Compact action or the Native Controls window. Backups are made during installation. Restore this label package before restoring the original setup package at `{Path.GetFullPath(originalFolder)}`.\n");
        return plan;
    }

    private static byte[] CompactXml(byte[] original, string window)
    {
        string text = Encoding.UTF8.GetString(original); var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new InvalidDataException("Skin XML has no root.");
        var tile = root.Elements("TileLayoutBox").SingleOrDefault(x => (string?)x.Attribute("item") == "HB_HotButtonLayout") ?? throw new InvalidDataException("Hotbutton layout template is unavailable.");
        if (root.Elements().Any(x => ((string?)x.Attribute("item"))?.StartsWith("EQLTracker_", StringComparison.Ordinal) == true)) throw new InvalidDataException("A compact tracker layout is already installed. Use its saved package.");
        var target = root.Elements("Screen").SingleOrDefault(x => (string?)x.Attribute("item") == window) ?? throw new InvalidDataException("Native hotbutton window is unavailable.");
        if (!target.Elements("Pieces").Any(x => x.Value == "TileLayoutBox:HB_HotButtonLayout")) throw new InvalidDataException("Native window has a different layout; use Standard mode.");
        var clones = new List<XElement>();
        for (int i = 1; i <= 12; i++)
        {
            var originalButton = root.Elements("HotButton").SingleOrDefault(x => (string?)x.Attribute("item") == "HB_Button" + i) ?? throw new InvalidDataException("Native button template is missing.");
            var button = new XElement(originalButton); button.SetAttributeValue("item", "EQLTracker_Button" + i);
            // All native ScreenIDs remain present; empty slots have zero layout size.
            var size = button.Element("Size") ?? throw new InvalidDataException("Native button size is missing.");
            size.SetElementValue("CX", i <= 4 ? "40" : "0"); size.SetElementValue("CY", i <= 4 ? "40" : "0");
            clones.Add(button);
        }
        var small = new XElement(tile); small.SetAttributeValue("item", "EQLTracker_Layout");
        small.SetElementValue("FirstPieceTemplate", "false"); small.SetElementValue("Spacing", "2"); small.SetElementValue("SecondarySpacing", "0"); small.SetElementValue("RightAnchorOffset", "1");
        foreach (var piece in small.Elements("Pieces")) piece.Value = piece.Value.Replace("HB_Button", "EQLTracker_Button", StringComparison.Ordinal);
        clones.Add(small);
        var screenMatch = Regex.Match(text, "<Screen\\s+item\\s*=\\s*\"" + Regex.Escape(window) + "\"[\\s\\S]*?</Screen>");
        if (!screenMatch.Success) throw new InvalidDataException("Cannot isolate the native window safely.");
        string replacement = screenMatch.Value.Replace("TileLayoutBox:HB_HotButtonLayout", "TileLayoutBox:EQLTracker_Layout", StringComparison.Ordinal);
        text = text[..screenMatch.Index] + replacement + text[(screenMatch.Index + screenMatch.Length)..];
        int close = text.LastIndexOf("</XML>", StringComparison.Ordinal);
        if (close < 0) throw new InvalidDataException("Skin XML end tag is missing.");
        text = text.Insert(close, "\n<!-- EQLTracker compact native controls; restore using the setup package -->\n" + string.Join("\n", clones.Select(x => x.ToString())) + "\n");
        _ = XDocument.Parse(text); return new UTF8Encoding(false).GetBytes(text);
    }

    private static NativeSetupPlan ReadPlan(string folder)
    {
        var plan = Json.Read<NativeSetupPlan>(Path.Combine(folder, "manifest.json"));
        if (plan.Version != 1 || !SafeName.IsMatch(plan.Character) || !SafeName.IsMatch(plan.Server) || !Regex.IsMatch(plan.Skin, @"^[A-Za-z0-9_-]{1,80}$") || plan.Bar is < 1 or > 11 || (plan.LabelRepairOnly ? plan.Files.Count != 1 : plan.Files.Count is < 2 or > 3)) throw new InvalidDataException("Invalid native setup manifest.");
        if (plan.Macros.Count != 4 || Labels.Any(x => !plan.Macros.ContainsKey(x)) || plan.Macros.Values.Distinct().Count() != 4 || plan.Macros.Values.Any(x => x is < 0 or >= 120)) throw new InvalidDataException("Invalid native macro allocation.");
        foreach (var file in plan.Files)
        {
            bool ini = Regex.IsMatch(file.RelativePath, "^(UI_)?" + Regex.Escape(plan.Character + "_" + plan.Server) + @"_LO\d+\.ini$", RegexOptions.IgnoreCase);
            bool xml = file.RelativePath.Equals(Path.Combine("uifiles", plan.Skin, "EQUI_HotButtonWnd.xml"), StringComparison.OrdinalIgnoreCase);
            if (!ini && !xml) throw new InvalidDataException("Unexpected file in setup manifest.");
            if (plan.LabelRepairOnly && (!ini || file.RelativePath.StartsWith("UI_", StringComparison.OrdinalIgnoreCase) || file.Changes.Count != 4 || file.Changes.Select(x => x.Key).Distinct().Count() != 4 || file.Changes.Any(x => x.Section != "Socials" || !x.Key.EndsWith("Name", StringComparison.Ordinal)))) throw new InvalidDataException("Invalid label-only repair actions.");
            foreach (var change in file.Changes) ValidateChange(plan, file.RelativePath.StartsWith("UI_", StringComparison.OrdinalIgnoreCase), change);
            _ = Target(plan.GameFolder, file.RelativePath);
            foreach (var (directory, hash) in new[] { ("before", file.BeforeHash), ("compact", file.CompactHash), ("standard", file.StandardHash) })
                if (Hash(File.ReadAllBytes(Target(folder, Path.Combine(directory, file.RelativePath)))) != hash) throw new InvalidDataException("Setup package file failed its SHA-256 check.");
        }
        return plan;
    }
    private static void ValidateChange(NativeSetupPlan plan, bool ui, NativeIniChange change)
    {
        bool valid = false;
        if (ui)
        {
            string window = plan.Bar == 1 ? "HotButtonWnd" : "HotButtonWnd" + plan.Bar;
            var values = new Dictionary<string, string> { ["Show"] = "1", ["ShowKeyMap"] = "0", ["ShowSpinner"] = "0", ["PercentSize"] = "100", ["XRef"] = "left", ["YRef"] = "top", ["XPos"] = "0.625000%", ["YPos"] = "4.166667%", ["Width"] = "180", ["Height"] = "48", ["Locked"] = "0", ["ClickThrough"] = "0" };
            if (change.Section == window && values.TryGetValue(change.Key, out string? value))
                valid = change.After == value && change.StandardAfter == (change.Key == "Width" ? "525" : change.Key == "Height" ? "53" : change.Key == "ShowSpinner" ? "1" : value);
        }
        else for (int i = 0; i < 4; i++)
        {
            int slot = plan.Macros[Labels[i]]; string prefix = $"Page{slot / 12 + 1}Button{slot % 12 + 1}";
            if (change.Section == "Socials")
            {
                if (change.Key == prefix + "Name") valid |= change.After == ShortNames[i] || !plan.LabelRepairOnly && change.After == "EQL" + (i == 2 ? "Inv" : Labels[i]);
                if (change.Key == prefix + "Color") valid |= change.After == "0";
                for (int line = 0; line < 5; line++) if (change.Key == prefix + "Line" + (line + 1)) valid |= change.After == (line < Commands[i].Length ? Commands[i][line] : "");
            }
            string bar = plan.Bar == 1 ? "HotButtons" : "HotButtons" + plan.Bar;
            if (change.Section == bar && change.Key == "Page1Button" + (i + 1)) valid |= change.After == $"E{slot},@-1,0000000000000000,0,{Labels[i]},";
            if (change.StandardAfter != change.After) valid = false;
        }
        if (!valid) throw new InvalidDataException("Unexpected native setup action: " + change.Section + "/" + change.Key);
    }

    public static NativeSetupResult Apply(string folder, bool compact = true, Func<bool>? running = null)
    {
        EnsureClosed(running); var plan = ReadPlan(folder); compact &= plan.CompactAvailable;
        var writes = new List<(string Path, byte[] Before, byte[] After)>();
        foreach (var file in plan.Files)
        {
            string path = Target(plan.GameFolder, file.RelativePath); byte[] original = File.ReadAllBytes(path), after;
            if (file.Changes.Count > 0)
            {
                var ini = new NativeIni(original);
                if (plan.LabelRepairOnly)
                    for (int i = 0; i < Labels.Length; i++)
                    {
                        int slot = plan.Macros[Labels[i]]; string prefix = $"Page{slot / 12 + 1}Button{slot % 12 + 1}";
                        for (int line = 0; line < 5; line++) if ((ini.Get("Socials", prefix + "Line" + (line + 1)) ?? "") != (line < Commands[i].Length ? Commands[i][line] : "")) throw new InvalidOperationException("A tracker macro command changed after review; no game files were changed.");
                    }
                foreach (var change in file.Changes)
                {
                    string? value = ini.Get(change.Section, change.Key);
                    if (value != change.Before && value != change.After && value != change.StandardAfter) throw new InvalidOperationException("Managed field changed since review: " + change.Section + "/" + change.Key + ". Regenerate the package; no files were changed.");
                    ini.Set(change.Section, change.Key, compact ? change.After : change.StandardAfter);
                }
                if (!plan.LabelRepairOnly && !file.RelativePath.StartsWith("UI_", StringComparison.OrdinalIgnoreCase))
                {
                    string bar = plan.Bar == 1 ? "HotButtons" : "HotButtons" + plan.Bar;
                    var allowed = Enumerable.Range(1, 4).Select(i => "Page1Button" + i).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (ini.Section(bar).Any(kv => Regex.IsMatch(kv.Key, @"^Page\d+Button\d+$", RegexOptions.IgnoreCase) && !allowed.Contains(kv.Key) && Occupied(kv.Value))) throw new InvalidOperationException("The selected hotbar is now occupied. Regenerate the package; no files were changed.");
                }
                after = ini.Bytes();
            }
            else
            {
                string hash = Hash(original);
                if (hash != file.BeforeHash && hash != file.CompactHash && hash != file.StandardHash) throw new InvalidOperationException("The skin XML changed since review. Regenerate the package; no files were changed.");
                after = File.ReadAllBytes(Target(folder, Path.Combine(compact ? "compact" : "standard", file.RelativePath)));
            }
            if (!original.SequenceEqual(after)) writes.Add((path, original, after));
        }
        Commit(folder, plan.GameFolder, writes, running);
        string mode = compact ? "Compact" : "Standard";
        Json.Write(Path.Combine(folder, "installation.json"), new { InstalledUtc = DateTime.UtcNow, Mode = mode, Bar = plan.Bar });
        return new(plan.Bar, mode, plan.LabelRepairOnly ? "Native labels repaired: Log / Who / Inv / Sync. Window placement and bindings retained." : $"{mode} native controls installed on bar {plan.Bar}. Start EverQuest normally and press Sync.");
    }

    public static NativeSetupResult Restore(string folder, Func<bool>? running = null)
    {
        EnsureClosed(running); var plan = ReadPlan(folder); var writes = new List<(string Path, byte[] Before, byte[] After)>(); int retained = 0;
        foreach (var file in plan.Files)
        {
            string path = Target(plan.GameFolder, file.RelativePath); byte[] original = File.ReadAllBytes(path), after;
            if (file.Changes.Count > 0)
            {
                var ini = new NativeIni(original);
                foreach (var change in file.Changes)
                {
                    var current = ini.Get(change.Section, change.Key);
                    if (current == change.After || current == change.StandardAfter) ini.Set(change.Section, change.Key, change.Before);
                    else if (current != change.Before) retained++;
                }
                after = ini.Bytes();
            }
            else
            {
                string hash = Hash(original);
                if (hash == file.CompactHash || hash == file.StandardHash) after = File.ReadAllBytes(Target(folder, Path.Combine("before", file.RelativePath)));
                else if (hash == file.BeforeHash) after = original;
                else throw new InvalidOperationException("Skin XML was edited after setup. Restore would overwrite those edits; no files were changed.");
            }
            if (!original.SequenceEqual(after)) writes.Add((path, original, after));
        }
        Commit(folder, plan.GameFolder, writes, running);
        Json.Write(Path.Combine(folder, "restoration.json"), new { RestoredUtc = DateTime.UtcNow, RetainedUserChanges = retained });
        return new(plan.Bar, "Restored", $"Native tracker controls restored. {retained} subsequently edited fields retained.");
    }
    private static void Commit(string folder, string root, List<(string Path, byte[] Before, byte[] After)> writes, Func<bool>? running)
    {
        EnsureClosed(running); string backup = Path.Combine(folder, "applied-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
        foreach (var file in writes)
        {
            string path = Target(backup, Path.GetRelativePath(root, file.Path)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, file.Before);
        }
        var completed = new List<(string Path, byte[] Before)>();
        try
        {
            foreach (var file in writes)
            {
                EnsureClosed(running);
                if (!File.ReadAllBytes(file.Path).SequenceEqual(file.Before)) throw new IOException("Game file changed during setup: " + Path.GetFileName(file.Path));
                // Same-directory atomic replacement prevents a partially written INI or XML file.
                string staging = file.Path + ".eqltracker-" + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(staging, file.After); File.Move(staging, file.Path, true); completed.Add((file.Path, file.Before));
            }
        }
        catch
        {
            // A client launched mid-install must be closed before any rollback writes.
            if (!(running ?? GameRunning)()) foreach (var file in completed) File.WriteAllBytes(file.Path, file.Before);
            throw;
        }
    }
}
