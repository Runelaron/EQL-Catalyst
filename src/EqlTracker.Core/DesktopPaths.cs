namespace EqlTracker.Core;

// Platform paths are deliberately independent of the game process and its inputs.
internal static class DesktopPaths
{
    internal static string DataRoot()
    {
        if (OperatingSystem.IsWindows()) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EQLTracker");
        string? configured = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(configured is { Length: > 0 } && Path.IsPathRooted(configured) ? configured : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "eql-catalyst");
    }
    internal static string Resolve(string root, params string[] components)
    {
        string current = root;
        foreach (string component in components)
        {
            string exact = Path.Combine(current, component);
            if (File.Exists(exact) || Directory.Exists(exact) || OperatingSystem.IsWindows() || !Directory.Exists(current)) { current = exact; continue; }
            var matches = Directory.EnumerateFileSystemEntries(current).Where(p => Path.GetFileName(p).Equals(component, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("Ambiguous game-file casing: " + component);
            current = matches.SingleOrDefault() ?? exact;
        }
        return current;
    }
    internal static IEnumerable<string> GameFiles(string folder, string pattern) => !Directory.Exists(folder) ? [] : Directory.EnumerateFiles(folder).Where(p => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(p), ignoreCase: true));
}
