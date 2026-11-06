using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace EqlTracker.Companion;

// Linux stores only an authenticated ciphertext on disk. Its key lives in the
// user's Secret Service collection. Locked/unavailable stores fail closed.
internal static class LocalSecretProtection
{
    private static string Secret(string directory, string? value)
    {
        string id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory))));
        var start = new ProcessStartInfo("secret-tool") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in value == null ? new[] { "lookup", "application", "eql-catalyst", "profile", id } : new[] { "store", "--label=EQL Catalyst local sharing", "application", "eql-catalyst", "profile", id }) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new IOException();
            try
            {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
            if (value != null) process.StandardInput.Write(value);
            process.StandardInput.Close();
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            string result = output.GetAwaiter().GetResult().Trim(); _ = error.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new IOException();
            return result;
            }
            catch(OperationCanceledException){if(!process.HasExited)process.Kill();throw;}
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or OperationCanceledException)
        { throw new InvalidOperationException("Sharing requires an available, unlocked Linux Secret Service and secret-tool. No plaintext fallback is used."); }
    }
    private static byte[] Key(string directory, bool create)
    {
        string value;
        try { value = Secret(directory, null); }
        catch (InvalidOperationException) when (create)
        {
            // A missing entry and an unavailable service both return nonzero.
            // Store must succeed and read back exactly before any disk write.
            value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            Secret(directory, value);
            if (Secret(directory, null) != value) throw new InvalidOperationException("Secure key storage verification failed.");
        }
        if (value.Length == 0 && create) { value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); Secret(directory, value); if (Secret(directory, null) != value) throw new InvalidOperationException("Secure key storage verification failed."); }
        byte[] key = Convert.FromBase64String(value);
        return key.Length == 32 ? key : throw new InvalidDataException("Invalid secure-storage key.");
    }
    internal static byte[] Protect(string directory, byte[] plaintext)
    {
        if (OperatingSystem.IsWindows()) return ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        return Encrypt(directory,plaintext,Key(directory,true));
    }
    internal static byte[] Encrypt(string directory,byte[] plaintext,byte[] key)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16], ciphertext = new byte[plaintext.Length];
        try { using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(Path.GetFullPath(directory))); return new byte[] { 1 }.Concat(nonce).Concat(tag).Concat(ciphertext).ToArray(); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    internal static byte[] Unprotect(string directory, byte[] protectedBytes)
    {
        if (OperatingSystem.IsWindows()) return ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        if (protectedBytes.Length < 29 || protectedBytes[0] != 1) throw new InvalidDataException("Unsupported protected-storage format.");
        return Decrypt(directory,protectedBytes,Key(directory,false));
    }
    internal static byte[] Decrypt(string directory,byte[] protectedBytes,byte[] key)
    {
        if(protectedBytes.Length<29||protectedBytes[0]!=1)throw new InvalidDataException("Unsupported protected-storage format.");
        byte[] plaintext = new byte[protectedBytes.Length - 29];
        try { using var aes = new AesGcm(key, 16); aes.Decrypt(protectedBytes.AsSpan(1, 12), protectedBytes.AsSpan(29), protectedBytes.AsSpan(13, 16), plaintext, Encoding.UTF8.GetBytes(Path.GetFullPath(directory))); return plaintext; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
