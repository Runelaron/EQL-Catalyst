using System.Security.Cryptography;
using System.Text;

namespace EqlTracker.Core;

public sealed class FileCheckpoint
{
    public string Path { get; set; } = "";
    public long Offset { get; set; }
    public long CreationTicks { get; set; }
    public string PrefixHash { get; set; } = "";
    public int PrefixLength { get; set; }
    public string Generation { get; set; } = Guid.NewGuid().ToString("N");
}
public sealed record TailLine(string Id, string Text);
public sealed record TailBatch(List<TailLine> Lines, FileCheckpoint Checkpoint, bool Reset, bool CaughtUp)
{ public string? Warning { get; init; } }

public static class LogTailer
{
    public static TailBatch Read(string path, FileCheckpoint? saved, int limit = 512 * 1024)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var created = File.GetCreationTimeUtc(path).Ticks;
        int prefixLength = saved?.PrefixLength ?? (int)Math.Min(256, file.Length);
        var prefix = new byte[Math.Min(prefixLength, (int)Math.Min(int.MaxValue, file.Length))]; file.ReadExactly(prefix);
        var hash = Convert.ToHexString(SHA256.HashData(prefix));
        var reset = saved != null && (saved.Path != path || saved.CreationTicks != created || saved.Offset > file.Length || saved.PrefixHash != hash);
        var checkpoint = saved == null || reset ? new FileCheckpoint { Path = path, CreationTicks = created, PrefixHash = hash, PrefixLength = prefix.Length } : new FileCheckpoint { Path = path, CreationTicks = created, PrefixHash = hash, PrefixLength = prefix.Length, Offset = saved.Offset, Generation = saved.Generation };
        file.Position = checkpoint.Offset;
        var buffer = new byte[Math.Min(limit, (int)Math.Min(int.MaxValue, file.Length - checkpoint.Offset))]; file.ReadExactly(buffer);
        var lines = new List<TailLine>(); int start = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != (byte)'\n') continue;
            var length = i - start; if (length > 0 && buffer[i - 1] == (byte)'\r') length--;
            lines.Add(new(checkpoint.Generation + ":" + (checkpoint.Offset + start), Encoding.UTF8.GetString(buffer, start, length)));
            start = i + 1;
        }
        if (start == 0 && buffer.Length == limit)
        {
            checkpoint.Offset += buffer.Length;
            return new(lines, checkpoint, reset, false) { Warning = "Skipped oversized log fragment; resuming at the next complete record." };
        }
        checkpoint.Offset += start;
        return new(lines, checkpoint, reset, checkpoint.Offset == file.Length || buffer.Length < limit);
    }
}
