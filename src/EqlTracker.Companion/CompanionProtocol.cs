using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EqlTracker.Companion;

public sealed record CompanionRow(string Label, string Value, string Detail = "", string Id = "");
public sealed record CompanionSection(string Title, IReadOnlyList<CompanionRow> Rows);
public sealed record CompanionSnapshot(long Revision, DateTimeOffset At, string ContextId, string ContextLabel,
    IReadOnlyDictionary<string, string> Theme, CompanionSection Build, CompanionSection Goals, CompanionSection Session, CompanionSection Holdings)
{
    public const int Protocol = 1;
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string,IReadOnlyList<CompanionRow>> History {get;init;}=new Dictionary<string,IReadOnlyList<CompanionRow>>();
    public CompanionSnapshot ForDevice(CompanionDevice device) => this with { Holdings = device.ShareHoldings ? Holdings with { Rows = Holdings.Rows.Where(r => device.Contexts.Contains(r.Id)).ToArray() } : new("Holdings not shared", []) };
}
public sealed record CompanionEnvelope(int Protocol, string Type, long Revision, long? BaseRevision, DateTimeOffset At, object? Data, string Status = "Connected");
public sealed class CompanionDevice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Verifier { get; set; } = "";
    public DateTimeOffset ApprovedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Revoked { get; set; }
    public bool ShareHoldings { get; set; }
    public HashSet<string> Contexts { get; set; } = [];
}
public sealed record PairingChallenge(string Challenge, DateTimeOffset ExpiresAt, string Address, string ServerId, string CertificateSha256, int Protocol = CompanionSnapshot.Protocol, string Certificate = "");
public sealed record PairingRequest(string Challenge, string Name, string Kind, int Protocol);
public sealed record PairingPoll(string RequestId, string PollSecret);
public sealed record PairingTicket(string RequestId, string PollSecret);
public sealed record PendingDevice(string Id, string Name, string Kind, DateTimeOffset ExpiresAt);
public sealed record PairingOutcome(string Status, string? Credential = null);
/// <summary>Pairing challenges and delivery credentials are memory-only; only credential verifiers persist.</summary>
public sealed class PairingAuthority
{
    private readonly object sync = new();
    private readonly Func<DateTimeOffset> clock;
    private readonly Action<IReadOnlyList<CompanionDevice>> persist;
    private readonly Dictionary<string, DateTimeOffset> challenges = [];
    private readonly Dictionary<string, (PendingDevice Device, string PollHash, string? Credential, string? DeviceId)> pending = [];
    private readonly List<CompanionDevice> devices;
    public event Action<string>? Revoked;
    public PairingAuthority(IEnumerable<CompanionDevice>? devices = null, Action<IReadOnlyList<CompanionDevice>>? persist = null, Func<DateTimeOffset>? clock = null)
    { this.devices = devices?.ToList() ?? []; this.persist = persist ?? (_ => { }); this.clock = clock ?? (() => DateTimeOffset.UtcNow); }
    public static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public string CreateChallenge()
    {
        lock (sync) { Cleanup(); if (challenges.Count >= 8) challenges.Clear(); string secret = Secret(); challenges[Hash(secret)] = clock().AddMinutes(2); return secret; }
    }
    public PairingTicket? Request(PairingRequest request)
    {
        lock (sync)
        {
            Cleanup(); if (request.Protocol != CompanionSnapshot.Protocol || request.Kind is not ("android" or "browser") || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 64 || request.Challenge == null || request.Challenge.Length > 128) return null;
            if (!challenges.Remove(Hash(request.Challenge), out var until) || until <= clock() || pending.Count >= 8) return null;
            string id = Guid.NewGuid().ToString("N"), poll = Secret(); pending[id] = (new(id, request.Name.Trim(), request.Kind, until), Hash(poll), null, null); return new(id, poll);
        }
    }
    public IReadOnlyList<PendingDevice> Pending { get { lock (sync) { Cleanup(); return pending.Values.Where(p => p.DeviceId == null).Select(p => p.Device).ToArray(); } } }
    public IReadOnlyList<CompanionDevice> Devices { get { lock (sync) return devices.Select(d => JsonSerializer.Deserialize<CompanionDevice>(JsonSerializer.Serialize(d))!).ToArray(); } }
    public bool Approve(string requestId, string context, bool holdings = false)
    {
        lock (sync)
        {
            Cleanup(); if (context.Length == 0 || !pending.TryGetValue(requestId, out var p) || p.DeviceId != null) return false;
            var device = new CompanionDevice { Name = p.Device.Name, Kind = p.Device.Kind, ApprovedAt = clock(), ExpiresAt = clock().AddDays(30), Contexts = [context], ShareHoldings = holdings };
            string credential = device.Id + "." + Secret(); device.Verifier = Hash(credential); devices.Add(device); persist(devices);
            pending[requestId] = (p.Device, p.PollHash, credential, device.Id); return true;
        }
    }
    public void Reject(string requestId) { lock (sync) pending.Remove(requestId); }
    public PairingOutcome Poll(PairingPoll poll)
    {
        lock (sync)
        {
            Cleanup(); if (string.IsNullOrEmpty(poll.PollSecret)||string.IsNullOrEmpty(poll.RequestId)||poll.PollSecret.Length > 128 || !pending.TryGetValue(poll.RequestId, out var p) || !Equal(p.PollHash, Hash(poll.PollSecret))) return new("Expired");
            if (p.Credential == null) return new("Awaiting desktop approval");
            pending.Remove(poll.RequestId); return new("Approved", p.Credential);
        }
    }
    public CompanionDevice? Authenticate(string? credential, string kind)
    {
        if (credential == null || credential.Length > 256) return null; string id = credential.Split('.')[0];
        lock (sync) return devices.FirstOrDefault(d => d.Id == id && d.Kind == kind && !d.Revoked && d.ExpiresAt > clock() && Equal(d.Verifier, Hash(credential)));
    }
    public void Revoke(string id) { lock (sync) { var device = devices.FirstOrDefault(d => d.Id == id); if (device == null) return; device.Revoked = true; persist(devices); } Revoked?.Invoke(id); }
    public void SetSharing(string id, HashSet<string> contexts, bool holdings)
    { lock (sync) { var device = devices.Single(d => d.Id == id && !d.Revoked); device.Contexts = contexts; device.ShareHoldings = holdings; persist(devices); } Revoked?.Invoke(id); }
    private void Cleanup()
    {
        var now = clock(); foreach (var key in challenges.Where(p => p.Value <= now).Select(p => p.Key).ToArray()) challenges.Remove(key);
        foreach (var key in pending.Where(p => p.Value.Device.ExpiresAt <= now).Select(p => p.Key).ToArray()) pending.Remove(key);
    }
    private static bool Equal(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}
