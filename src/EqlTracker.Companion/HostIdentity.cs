using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace EqlTracker.Companion;

public sealed class CompanionHostSettings
{
    public bool Enabled { get; set; }
    public string Address { get; set; } = "";
    public int Port { get; set; } = 47831;
    public string BrowserHostname { get; set; } = "";
    public string BrowserCertificateThumbprint { get; set; } = "";
}
public static class LanBoundary
{
    public static bool Private(IPAddress address) { var b = address.GetAddressBytes(); return b.Length == 4 && (b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168); }
    public static IReadOnlyList<(IPAddress Address, IPAddress Mask)> Interfaces() => NetworkInterface.GetAllNetworkInterfaces().Where(i => i.OperationalStatus == OperationalStatus.Up && i.NetworkInterfaceType != NetworkInterfaceType.Loopback).SelectMany(i => i.GetIPProperties().UnicastAddresses).Where(a => Private(a.Address)).Select(a => (a.Address, a.IPv4Mask)).ToArray();
    public static bool SameSubnet(IPAddress local, IPAddress remote, IPAddress mask)
    {
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4(); var a = local.GetAddressBytes(); var b = remote.GetAddressBytes(); var m = mask.GetAddressBytes();
        return a.Length == 4 && b.Length == 4 && Private(remote) && Enumerable.Range(0, 4).All(i => (a[i] & m[i]) == (b[i] & m[i]));
    }
}
public sealed class HostIdentity : IDisposable
{
    public string ServerId { get; }
    public X509Certificate2 Certificate { get; }
    public string Fingerprint => Convert.ToHexStringLower(SHA256.HashData(Certificate.RawData));
    private HostIdentity(string id, X509Certificate2 certificate) { ServerId = id; Certificate = certificate; }
    internal static HostIdentity Fixture()
    {
        string id=Guid.NewGuid().ToString("N");using var key=RSA.Create(2048);var request=new CertificateRequest("CN=catalyst-"+id+".local",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var san=new SubjectAlternativeNameBuilder();san.AddDnsName("localhost");san.AddDnsName("catalyst-"+id+".local");san.AddIpAddress(IPAddress.Loopback);request.CertificateExtensions.Add(san.Build());request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new System.Security.Cryptography.OidCollection{new("1.3.6.1.5.5.7.3.1")},true));using var created=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(2));return new(id,X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12),null));
    }
    public static HostIdentity Load(string directory, IPAddress address)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        Directory.CreateDirectory(directory); if(OperatingSystem.IsLinux())File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute); string idPath = Path.Combine(directory, "server-id.txt"), certPath = Path.Combine(directory, OperatingSystem.IsWindows()?"identity.dpapi":"identity.encrypted");
        string id = File.Exists(idPath) ? File.ReadAllText(idPath) : Guid.NewGuid().ToString("N");
        if (File.Exists(certPath))
        {
            var certificate = X509CertificateLoader.LoadPkcs12(LocalSecretProtection.Unprotect(directory, File.ReadAllBytes(certPath)), null);
            // Identity is stable when the LAN address changes; QR trust explicitly scopes this certificate.
            return new(id, certificate);
        }
        using var key = RSA.Create(3072); var request = new CertificateRequest("CN=Catalyst-" + id, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(address); san.AddDnsName("catalyst-" + id + ".local"); request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true)); request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new System.Security.Cryptography.OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));
        byte[] bytes = created.Export(X509ContentType.Pkcs12); Atomic(certPath, LocalSecretProtection.Protect(directory, bytes)); File.WriteAllText(idPath, id);
        return new(id, X509CertificateLoader.LoadPkcs12(bytes, null));
    }
    private sealed record DeviceRegistry(string Identity,IReadOnlyList<CompanionDevice> Devices);
    public static List<CompanionDevice> ReadDevices(string directory,string identity)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException(); string path = Path.Combine(directory, OperatingSystem.IsWindows()?"devices.dpapi":"devices.encrypted");
        if(!File.Exists(path))return [];
        var registry=JsonSerializer.Deserialize<DeviceRegistry>(LocalSecretProtection.Unprotect(directory,File.ReadAllBytes(path)));return registry?.Identity==identity?registry.Devices.ToList():[];
    }
    public static void SaveDevices(string directory, IReadOnlyList<CompanionDevice> devices,string identity)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException(); Atomic(Path.Combine(directory, OperatingSystem.IsWindows()?"devices.dpapi":"devices.encrypted"), LocalSecretProtection.Protect(directory,JsonSerializer.SerializeToUtf8Bytes(new DeviceRegistry(identity,devices))));
    }
    private static void Atomic(string path, byte[] bytes) { string temp = path + ".tmp"; File.WriteAllBytes(temp, bytes); if(OperatingSystem.IsLinux())File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite); File.Move(temp, path, true); }
    public void Dispose() => Certificate.Dispose();
}
