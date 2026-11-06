using System.Runtime.InteropServices;

namespace EqlTracker.Core;

internal static class LinuxFileIdentity
{
    // Linux statx has a fixed 256-byte ABI. Operate on the already-open file,
    // avoiding a path/replacement race. ctime is not a creation identifier.
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int descriptor, string path, int flags, uint mask, [Out] byte[] value);
    internal static string Read(FileStream file)
    {
        if (!OperatingSystem.IsLinux()) return "";
        var buffer = new byte[256];
        if (Statx(file.SafeFileHandle.DangerousGetHandle().ToInt32(), "", 0x1000, 0x900, buffer) != 0 || (BitConverter.ToUInt32(buffer, 0) & 0x100) == 0)
            throw new IOException("Cannot verify Linux log-file identity; checkpoint retained.");
        string identity = $"{BitConverter.ToUInt32(buffer, 136):x}:{BitConverter.ToUInt32(buffer, 140):x}:{BitConverter.ToUInt64(buffer, 32):x}";
        if ((BitConverter.ToUInt32(buffer, 0) & 0x800) != 0) identity += $":{BitConverter.ToInt64(buffer, 80):x}:{BitConverter.ToUInt32(buffer, 88):x}";
        return identity;
    }
}
