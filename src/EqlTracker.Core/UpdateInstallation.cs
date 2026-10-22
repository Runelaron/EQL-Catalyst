using System.IO.Compression;
using System.Text.Json;

namespace EqlTracker.Core;

internal sealed record UpdateIntent(string AppDirectory, string DataRoot, string PayloadDirectory, string Installer,
    string Scope, string Character, string Server, string GameFolder, string Version, string JobDirectory);
internal sealed record UpdateJournal(string AppDirectory, string StageDirectory, string BackupDirectory, string Phase);
internal sealed record UpdateRelaunch(string DataRoot,string Character,string Server,string GameFolder);

internal static class UpdateInstallation
{
    internal static void VerifyExtracted(string archivePath, string payload)
    {
        using var archive = ZipFile.OpenRead(archivePath); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            int slash = entry.FullName.IndexOf('/'); if (slash <= 0) throw new InvalidDataException("Invalid payload archive root.");
            string relative = entry.FullName[(slash + 1)..]; if (!paths.Add(relative)) throw new InvalidDataException("Duplicate update path.");
            string path = UpdateFiles.Child(payload, relative); UpdateFiles.NoLinks(Path.GetDirectoryName(path)!);
            using var expected = entry.Open(); using var actual = File.OpenRead(path);
            if (actual.Length != entry.Length || !System.Security.Cryptography.SHA256.HashData(expected).SequenceEqual(System.Security.Cryptography.SHA256.HashData(actual))) throw new InvalidDataException("Prepared files no longer match the signed download.");
        }
        if (Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories).Any(f => !paths.Contains(Path.GetRelativePath(payload, f).Replace('\\', '/')))) throw new InvalidDataException("Unexpected files in the update payload.");
        UpdateFiles.VerifyPayload(payload);
    }
    internal static string Extract(string archivePath, string destination)
    {
        if (Directory.Exists(destination)) throw new InvalidDataException("Update extraction must use a fresh directory.");
        Directory.CreateDirectory(destination); long total = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 5000) throw new InvalidDataException("Unexpected archive file count.");
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName;
            if (name.EndsWith('/')) continue;
            string file = UpdateFiles.Child(destination, name);
            if (!names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Duplicate or linked archive entry.");
            total = checked(total + entry.Length); if (total > UpdateProtocol.MaximumExtractedBytes) throw new InvalidDataException("Update archive is too large.");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!); entry.ExtractToFile(file, false);
        }
        var roots = Directory.GetDirectories(destination);
        if (roots.Length != 1 || Directory.GetFiles(destination).Length != 0) throw new InvalidDataException("Unexpected portable package structure.");
        UpdateFiles.VerifyPayload(roots[0]); return roots[0];
    }
    internal static void CopyTree(string source, string destination, Func<string, bool>? include = null)
    {
        UpdateFiles.NoLinks(source); Directory.CreateDirectory(destination);
        foreach (string path in Directory.EnumerateFileSystemEntries(source))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("A linked file cannot be backed up or replaced automatically.");
            if (include?.Invoke(path) == false) continue;
            string target = Path.Combine(destination, Path.GetFileName(path));
            if (Directory.Exists(path)) CopyTree(path, target);
            else
            {
                File.Copy(path, target, true);using var before=File.OpenRead(path);using var after=File.OpenRead(target);
                if(before.Length!=after.Length||!System.Security.Cryptography.SHA256.HashData(before).SequenceEqual(System.Security.Cryptography.SHA256.HashData(after)))throw new IOException("An update backup could not be verified.");
            }
        }
    }
    internal static string ReplacePortable(string application, string payload, string journalPath)
    {
        application = Path.GetFullPath(application).TrimEnd(Path.DirectorySeparatorChar); UpdateFiles.NoLinks(application);
        if (application == Path.GetPathRoot(application) || !File.Exists(Path.Combine(application, "EQLTracker.exe"))) throw new InvalidDataException("Invalid application destination.");
        var next = UpdateFiles.VerifyPayload(payload); string token = Guid.NewGuid().ToString("N");
        string stage = application + ".update-" + token, backup = application + ".backup-" + token;
        UpdateFiles.WriteJson(journalPath, new UpdateJournal(application, stage, backup, "Preparing"));
        CopyTree(application, stage); var oldManifest = Path.Combine(application, "release-manifest.json");
        if (File.Exists(oldManifest))
        {
            using var old = JsonDocument.Parse(File.ReadAllBytes(oldManifest));
            foreach (var file in old.RootElement.GetProperty("Files").EnumerateObject())
                if (!next.ContainsKey(file.Name)) File.Delete(UpdateFiles.Child(stage, file.Name));
        }
        CopyTree(payload, stage); UpdateFiles.VerifyPayload(stage);
        UpdateFiles.WriteJson(journalPath, new UpdateJournal(application, stage, backup, "Prepared"));
        Directory.Move(application, backup);
        try { Directory.Move(stage, application); UpdateFiles.VerifyPayload(application); UpdateFiles.WriteJson(journalPath, new UpdateJournal(application, stage, backup, "Replaced")); }
        catch { if (Directory.Exists(application)) Directory.Move(application, stage + ".failed"); Directory.Move(backup, application); throw; }
        return backup;
    }
    internal static bool Recover(string journalPath)
    {
        if (!File.Exists(journalPath)) return false;
        var j = JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllText(journalPath)) ?? throw new InvalidDataException("Invalid update journal.");
        if (!j.StageDirectory.StartsWith(j.AppDirectory + ".update-", StringComparison.OrdinalIgnoreCase) || !j.BackupDirectory.StartsWith(j.AppDirectory + ".backup-", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe recovery paths.");
        UpdateFiles.NoLinks(Path.GetDirectoryName(j.AppDirectory)!);
        if (!Directory.Exists(j.AppDirectory) && Directory.Exists(j.BackupDirectory)) { Directory.Move(j.BackupDirectory, j.AppDirectory); return true; }
        return false;
    }
}
