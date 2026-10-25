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
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The desktop host uses Windows credential protection.");
        Directory.CreateDirectory(directory); string idPath = Path.Combine(directory, "server-id.txt"), certPath = Path.Combine(directory, "identity.dpapi");
        string id = File.Exists(idPath) ? File.ReadAllText(idPath) : Guid.NewGuid().ToString("N");
        if (File.Exists(certPath))
        {
            var certificate = X509CertificateLoader.LoadPkcs12(ProtectedData.Unprotect(File.ReadAllBytes(certPath), null, DataProtectionScope.CurrentUser), null);
            // Identity is stable when the LAN address changes; QR trust explicitly scopes this certificate.
            return new(id, certificate);
        }
        using var key = RSA.Create(3072); var request = new CertificateRequest("CN=Catalyst-" + id, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(address); san.AddDnsName("catalyst-" + id + ".local"); request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true)); request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new System.Security.Cryptography.OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));
        byte[] bytes = created.Export(X509ContentType.Pkcs12); Atomic(certPath, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)); File.WriteAllText(idPath, id);
        return new(id, X509CertificateLoader.LoadPkcs12(bytes, null));
    }
    private sealed record DeviceRegistry(string Identity,IReadOnlyList<CompanionDevice> Devices);
    public static List<CompanionDevice> ReadDevices(string directory,string identity)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); string path = Path.Combine(directory, "devices.dpapi");
        if(!File.Exists(path))return [];
        var registry=JsonSerializer.Deserialize<DeviceRegistry>(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser));return registry?.Identity==identity?registry.Devices.ToList():[];
    }
    public static void SaveDevices(string directory, IReadOnlyList<CompanionDevice> devices,string identity)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); Atomic(Path.Combine(directory, "devices.dpapi"), ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(new DeviceRegistry(identity,devices)), null, DataProtectionScope.CurrentUser));
    }
    private static void Atomic(string path, byte[] bytes) { string temp = path + ".tmp"; File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
    public void Dispose() => Certificate.Dispose();
}
