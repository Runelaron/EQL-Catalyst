using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

// Update metadata is a separate trust boundary from Windows Authenticode.
internal readonly record struct UpdateVersion(int Major, int Minor, int Patch, int Beta) : IComparable<UpdateVersion>
{
    internal static UpdateVersion Parse(string text)
    {
        if(string.IsNullOrEmpty(text))throw new InvalidDataException("Missing release version.");
        var m = Regex.Match(text, @"\Av?(\d+)\.(\d+)\.(\d+)(?:-beta\.(\d+))?(?:\+[A-Za-z0-9.-]+)?\z", RegexOptions.CultureInvariant);
        if (!m.Success) throw new InvalidDataException("Unsupported release version.");
        int Part(int i) => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
        return new(Part(1), Part(2), Part(3), m.Groups[4].Success ? Part(4) : int.MaxValue);
    }
    public int CompareTo(UpdateVersion other)
    { foreach (int n in new[] { Major.CompareTo(other.Major), Minor.CompareTo(other.Minor), Patch.CompareTo(other.Patch), Beta.CompareTo(other.Beta) }) if (n != 0) return n; return 0; }
}

internal sealed record UpdatePackage(string Kind, string Name, long Bytes, string Sha256);
internal sealed record UpdateManifest(int Schema, string Product, string Repository, string Version, string Tag,
    string Commit, string Architecture, string MinimumUpdaterVersion, List<UpdatePackage> Packages);

internal static partial class UpdateProtocol
{
    internal const string Repository = "Runelaron/EQL-Catalyst";
    internal const long MaximumArchiveBytes = 1024L * 1024 * 1024;
    internal const long MaximumExtractedBytes = 2L * 1024 * 1024 * 1024;
    internal static UpdateManifest Verify(byte[] bytes, byte[] signature) => Verify(bytes, signature, UpdateTrust.PublicKey);
    internal static UpdateManifest Verify(byte[] bytes, byte[] signature, string publicKey)
    {
        if (bytes.Length > 65536 || signature.Length != 64) throw new InvalidDataException("Invalid update signature or metadata size.");
        using var key = ECDsa.Create(); key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        if (!key.VerifyData(bytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw new InvalidDataException("Update signature verification failed.");
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(bytes) ?? throw new InvalidDataException("Missing update metadata.");
        if (manifest.Schema != 1 || manifest.Product != "EQL Catalyst" || manifest.Repository != Repository || manifest.Architecture != "win-x64" || manifest.Tag != "v" + manifest.Version || !Regex.IsMatch(manifest.Commit ?? "", "\\A[0-9a-f]{40}\\z")) throw new InvalidDataException("Update metadata does not identify this application.");
        _ = UpdateVersion.Parse(manifest.Version); _ = UpdateVersion.Parse(manifest.MinimumUpdaterVersion);
        if (manifest.Packages == null || manifest.Packages.Count != 2 || manifest.Packages.Any(p=>p==null) || manifest.Packages.Select(p => p.Kind).Distinct().Count() != 2) throw new InvalidDataException("Incomplete update packages.");
        foreach (var p in manifest.Packages)
        {
            string suffix = p.Kind switch { "Installer" => "-win-x64-setup.exe", "Portable" => "-win-x64.zip", _ => throw new InvalidDataException("Unknown package type.") };
            if (p.Name != "EQL-Catalyst-" + manifest.Version + suffix || p.Bytes <= 0 || p.Bytes > MaximumArchiveBytes || !Regex.IsMatch(p.Sha256 ?? "", "\\A[0-9a-f]{64}\\z")) throw new InvalidDataException("Invalid package metadata.");
        }
        return manifest;
    }
    internal static bool Newer(string candidate, string current) => UpdateVersion.Parse(candidate).CompareTo(UpdateVersion.Parse(current)) > 0;
    internal static bool TrustedUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && uri.UserInfo.Length == 0 &&
        new[] { "api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" }.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    internal static void VerifyFile(string path, UpdatePackage package)
    {
        using var file = File.OpenRead(path);
        if (file.Length != package.Bytes || !Convert.ToHexStringLower(SHA256.HashData(file)).Equals(package.Sha256, StringComparison.Ordinal)) throw new InvalidDataException("The downloaded package failed its integrity check.");
    }
}
