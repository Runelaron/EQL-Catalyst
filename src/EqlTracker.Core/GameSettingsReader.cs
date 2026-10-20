namespace EqlTracker.Core;

public static class GameSettingsReader
{
    // The client rewrites these files while running. A failed or changing read
    // is temporary; callers retain their last valid settings and retry later.
    public static bool TryReadText(string path, out string text)
    {
        text = "";
        try
        {
            var before = new FileInfo(path); long size = before.Length; var modified = before.LastWriteTimeUtc;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream); string candidate = reader.ReadToEnd();
            var after = new FileInfo(path);
            if (candidate.Length == 0 || after.Length != size || after.LastWriteTimeUtc != modified) return false;
            text = candidate; return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
