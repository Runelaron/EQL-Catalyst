using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Makaretu.Dns;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EqlTracker.Companion;

/// <summary>Allowlisted, read-only LAN projections. No tracker store, game controls or raw logs enter this subsystem.</summary>
public sealed class CompanionHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly CompanionHostSettings settings;
    private readonly HostIdentity identity;
    private readonly IPAddress local, mask;
    private readonly ConcurrentDictionary<string, (string Device, WebSocket Socket)> streams = [];
    private readonly ConcurrentDictionary<string, Channel<bool>> subscribers = [];
    private readonly ConcurrentDictionary<string, (DateTimeOffset From, int Count)> requests = [], pairAttempts = [];
    private readonly Dictionary<string, (byte[] Bytes, string Mime)> assets = [];
    private WebApplication? app;
    private ServiceDiscovery? discovery;
    private MulticastService? multicast;
    private X509Certificate2? browserCertificate;
    private CompanionSnapshot? current;
    private readonly bool loopbackFixture;
    private readonly CancellationTokenSource boundaryStop=new();
    private Task? boundaryMonitor;
    public bool Running { get; private set; }
    public string BoundaryStatus {get;private set;}="";
    public PairingAuthority Pairing { get; }
    public string Address => "https://" + local + ":" + settings.Port;
    public string BrowserAddress => browserCertificate == null ? "" : "https://" + settings.BrowserHostname.ToLowerInvariant() + ":" + settings.Port;
    public string ServerId => identity.ServerId;
    public int StreamCount => streams.Count;
    public string DiscoveryStatus { get; private set; } = "Not started";
    internal X509Certificate2 FixtureCertificate=>identity.Certificate;
    private CompanionHost(int port)
    { loopbackFixture=true;local=IPAddress.Loopback;mask=IPAddress.Parse("255.0.0.0");settings=new(){Enabled=true,Address=local.ToString(),Port=port,BrowserHostname="localhost"};identity=HostIdentity.Fixture();Pairing=new();Pairing.Revoked+=CloseDevice; }
    internal static CompanionHost LoopbackFixture(int port)=>new(port);
    public CompanionHost(CompanionHostSettings settings, string privateDirectory)
    {
        if (!settings.Enabled) throw new InvalidOperationException("Phone sharing is disabled.");
        if (!IPAddress.TryParse(settings.Address, out var address) || !LanBoundary.Private(address) || settings.Port is < 1024 or > 65535) throw new InvalidDataException("Choose a private LAN interface and port 1024–65535.");
        var nic = LanBoundary.Interfaces().FirstOrDefault(i => i.Address.Equals(address)); if (nic.Address == null) throw new InvalidDataException("The selected LAN interface is unavailable.");
        this.settings = settings; local = address; mask = nic.Mask; identity = HostIdentity.Load(privateDirectory, address);
        string registryIdentity=identity.ServerId+"/"+identity.Fingerprint+"/"+settings.BrowserCertificateThumbprint;
        Pairing = new(HostIdentity.ReadDevices(privateDirectory,registryIdentity), devices => HostIdentity.SaveDevices(privateDirectory, devices,registryIdentity)); Pairing.Revoked += CloseDevice;
    }
    public PairingChallenge CreatePairing(bool browser)
    {
        if (browser && BrowserAddress.Length == 0) throw new InvalidOperationException("Configure a trusted browser certificate and matching LAN hostname first.");
        return new(Pairing.CreateChallenge(), DateTimeOffset.UtcNow.AddMinutes(2), browser ? BrowserAddress : Address, ServerId, identity.Fingerprint, Certificate: Convert.ToBase64String(identity.Certificate.RawData));
    }
    public void Publish(CompanionSnapshot snapshot)
    { Volatile.Write(ref current, snapshot); foreach (var channel in subscribers.Values) channel.Writer.TryWrite(true); }
    public async Task StartAsync(string webDirectory, CancellationToken token = default)
    {
        if(!loopbackFixture)await RequirePrivateNetwork(token);
        if(loopbackFixture)browserCertificate=identity.Certificate;
        if (!loopbackFixture&&(settings.BrowserHostname.Length > 0 || settings.BrowserCertificateThumbprint.Length > 0))
        {
            if (Uri.CheckHostName(settings.BrowserHostname) != UriHostNameType.Dns) throw new InvalidDataException("Browser hostname must be a DNS name resolving to this LAN host.");
            var addresses = await System.Net.Dns.GetHostAddressesAsync(settings.BrowserHostname, token); if (!addresses.Contains(local)) throw new InvalidDataException("Browser hostname does not resolve to the selected LAN interface.");
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser); store.Open(OpenFlags.ReadOnly);
            browserCertificate = store.Certificates.Find(X509FindType.FindByThumbprint, settings.BrowserCertificateThumbprint, true).FirstOrDefault(c => c.HasPrivateKey && c.MatchesHostname(settings.BrowserHostname));
            if (browserCertificate == null) throw new InvalidDataException("A valid trusted certificate with a private key and matching hostname is required for the browser beta.");
        }
        foreach (var (file, mime) in new[] { ("index.html", "text/html; charset=utf-8"), ("app.js", "text/javascript; charset=utf-8"), ("app.css", "text/css; charset=utf-8") }) assets[file] = (await File.ReadAllBytesAsync(Path.Combine(webDirectory, file), token), mime);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ApplicationName = typeof(CompanionHost).Assembly.FullName,EnvironmentName="Production", Args = [] });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(server =>
        {
            server.AddServerHeader = false; server.Limits.MaxConcurrentConnections = 32; server.Limits.MaxConcurrentUpgradedConnections = 16; server.Limits.MaxRequestBodySize = 8192; server.Limits.MaxRequestHeadersTotalSize = 8192; server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10); server.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            server.Listen(local, settings.Port, listen => { listen.Protocols = HttpProtocols.Http1AndHttp2; listen.UseHttps(https => { https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13; https.ServerCertificateSelector = (_, name) => browserCertificate != null && string.Equals(name, settings.BrowserHostname, StringComparison.OrdinalIgnoreCase) ? browserCertificate : identity.Certificate; }); });
        });
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        app = builder.Build(); app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20), KeepAliveTimeout = TimeSpan.FromSeconds(15) });
        app.Use(async (context, next) =>
        {
            var response = context.Response; response.Headers.CacheControl = "no-store"; response.Headers.XContentTypeOptions = "nosniff"; response.Headers["Referrer-Policy"] = "no-referrer"; response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
            response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            var remote = context.Connection.RemoteIpAddress;
            if (!context.Request.IsHttps || remote == null || !(loopbackFixture?IPAddress.IsLoopback(remote):LanBoundary.SameSubnet(local, remote, mask))) { response.StatusCode = 403; return; }
            string host = context.Request.Host.Host;
            if (host != local.ToString() && host != "catalyst-" + ServerId + ".local" && !string.Equals(host, settings.BrowserHostname, StringComparison.OrdinalIgnoreCase)) { response.StatusCode = 421; return; }
            string origin = context.Request.Headers.Origin.ToString();
            if (origin.Length > 0 && !OriginAllowed(origin)) { response.StatusCode = 403; return; }
            if (!Limit(requests, remote.ToString(), 240, TimeSpan.FromMinutes(1))) { response.StatusCode = 429; return; }
            if (context.Request.ContentLength > 8192) { response.StatusCode = 413; return; }
            if(HttpMethods.IsPost(context.Request.Method)&&context.Request.ContentType?.StartsWith("application/json",StringComparison.OrdinalIgnoreCase)!=true){response.StatusCode=415;return;}
            if (context.Request.Path.StartsWithSegments("/api") && context.Request.Headers["X-Catalyst-Protocol"].Count > 0 && context.Request.Headers["X-Catalyst-Protocol"] != "1") { await ProtocolMismatch(context); return; }
            try { await next(context); }
            catch (Exception error) when (error is JsonException or Microsoft.AspNetCore.Http.BadHttpRequestException or FormatException or ArgumentException) { if (!response.HasStarted) response.StatusCode = 400; }
        });
        app.MapGet("/", async context => { if (BrowserAddress.Length == 0 || !string.Equals(context.Request.Host.Host, settings.BrowserHostname, StringComparison.OrdinalIgnoreCase)) { context.Response.StatusCode = 404; return; } await Asset(context, "index.html"); });
        app.MapGet("/app.js", context => Asset(context, "app.js")); app.MapGet("/app.css", context => Asset(context, "app.css"));
        app.MapGet("/identity", () => new { product = "Catalyst", protocol = CompanionSnapshot.Protocol, serverId = ServerId });
        app.MapPost("/pair/request", async context =>
        {
            if (!Limit(pairAttempts, context.Connection.RemoteIpAddress!.ToString(), 5, TimeSpan.FromMinutes(2))) { context.Response.StatusCode = 429; return; }
            var request = await context.Request.ReadFromJsonAsync<PairingRequest>(cancellationToken: context.RequestAborted);
            if (request?.Protocol != CompanionSnapshot.Protocol) { await ProtocolMismatch(context); return; }
            if (request.Kind == "browser" && !BrowserRequest(context)) { context.Response.StatusCode = 403; return; }
            var ticket = Pairing.Request(request); if (ticket == null) { context.Response.StatusCode = 403; return; } await context.Response.WriteAsJsonAsync(ticket);
        });
        app.MapPost("/pair/poll", async context =>
        {
            var request = await context.Request.ReadFromJsonAsync<PairingPoll>(cancellationToken: context.RequestAborted); if (request == null) { context.Response.StatusCode = 400; return; }
            var result = Pairing.Poll(request);
            if (result.Credential != null && Pairing.Authenticate(result.Credential, "browser") != null)
            {
                if (!BrowserRequest(context)) { context.Response.StatusCode = 403; return; }
                context.Response.Cookies.Append("__Host-catalyst", result.Credential, new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromDays(30), IsEssential = true }); result = result with { Credential = null };
            }
            await context.Response.WriteAsJsonAsync(result);
        });
        app.MapGet("/api/v1/session", async context => { var device = Auth(context); if (device == null) { context.Response.StatusCode = 401; return; } await context.Response.WriteAsJsonAsync(new { protocol = CompanionSnapshot.Protocol, csrf = Csrf(device), serverId = ServerId }); });
        app.MapGet("/api/v1/snapshot", async context =>
        { var device = Auth(context); if (device == null) { context.Response.StatusCode = 401; return; } await context.Response.WriteAsJsonAsync(Envelope(device, null)); });
        app.MapGet("/api/v1/search", async context =>
        {
            var device = Auth(context); if (device == null) { context.Response.StatusCode = 401; return; } var snapshot = Volatile.Read(ref current);
            if (snapshot == null || !device.Contexts.Contains(snapshot.ContextId)) { context.Response.StatusCode = 403; return; }
            string text = context.Request.Query["q"].ToString(); if (text.Length > 100) { context.Response.StatusCode = 400; return; }
            int offset = int.TryParse(context.Request.Query["offset"], out int n) ? Math.Clamp(n, 0, 10000) : 0;
            var rows = snapshot.Build.Rows.Concat(snapshot.Goals.Rows).Concat(snapshot.Session.Rows).Concat(snapshot.History.Values.SelectMany(r=>r)).DistinctBy(r=>(r.Label,r.Value,r.Detail)).Where(r => r.Label.Contains(text, StringComparison.OrdinalIgnoreCase) || r.Value.Contains(text, StringComparison.OrdinalIgnoreCase)).Skip(offset).Take(50).ToArray();
            await context.Response.WriteAsJsonAsync(new { snapshot.Revision, offset, rows, more = rows.Length == 50 });
        });
        app.MapGet("/api/v1/history",async context=>{
            var device=Auth(context);if(device==null){context.Response.StatusCode=401;return;}var snapshot=Volatile.Read(ref current);if(snapshot==null||!device.Contexts.Contains(snapshot.ContextId)){context.Response.StatusCode=403;return;}
            string kind=context.Request.Query["kind"].ToString();if(kind is not("loot" or "session" or "goals")){context.Response.StatusCode=400;return;}int offset=int.TryParse(context.Request.Query["offset"],out int n)?Math.Clamp(n,0,10000):0;var history=snapshot.History.GetValueOrDefault(kind,[]);var rows=history.Skip(offset).Take(50).ToArray();await context.Response.WriteAsJsonAsync(new{snapshot.Revision,snapshot.At,snapshot.ContextId,offset,rows,more=history.Count>offset+rows.Length});
        });
        app.MapPost("/api/v1/logout", context =>
        { var device = Auth(context); if (device == null) { context.Response.StatusCode = 401; return Task.CompletedTask; } if (device.Kind == "browser" && (!BrowserRequest(context) || context.Request.Headers["X-Catalyst-CSRF"] != Csrf(device))) { context.Response.StatusCode = 403; return Task.CompletedTask; } Pairing.Revoke(device.Id); context.Response.Cookies.Delete("__Host-catalyst", new CookieOptions { Secure = true, Path = "/" }); return Task.CompletedTask; });
        app.Map("/api/v1/stream", Stream);
        await app.StartAsync(token);
        Running=true;
        if(!loopbackFixture)boundaryMonitor=Task.Run(async()=>{
            try{while(!boundaryStop.IsCancellationRequested){await Task.Delay(TimeSpan.FromSeconds(20),boundaryStop.Token);await RequirePrivateNetwork(boundaryStop.Token);}}
            catch(OperationCanceledException)when(boundaryStop.IsCancellationRequested){}
            catch(Exception error)when(error is not OutOfMemoryException){Running=false;BoundaryStatus="Sharing paused because the selected private network is unavailable";foreach(var stream in streams.Values)stream.Socket.Abort();discovery?.Dispose();multicast?.Dispose();if(app!=null)await app.StopAsync(boundaryStop.Token);}
        });
        if(!loopbackFixture)try
        {
            multicast = new MulticastService(interfaces => interfaces.Where(n => n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(local))));
            discovery = new ServiceDiscovery(multicast); var profile = new ServiceProfile("Catalyst-" + ServerId, "_catalyst._tcp", (ushort)settings.Port, [local]); profile.AddProperty("protocol", "1"); profile.AddProperty("server", ServerId); discovery.Advertise(profile); multicast.Start(); DiscoveryStatus = "LAN discovery active";
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or InvalidOperationException) { DiscoveryStatus = "Discovery unavailable; use the pairing address"; }
    }
    private CompanionDevice? Auth(HttpContext context)
    {
        if (context.Request.Headers["X-Catalyst-Protocol"] != "1" && !context.WebSockets.IsWebSocketRequest) return null;
        string auth = context.Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.Ordinal)) return Pairing.Authenticate(auth[7..], "android");
        return BrowserRequest(context) ? Pairing.Authenticate(context.Request.Cookies["__Host-catalyst"], "browser") : null;
    }
    private bool BrowserRequest(HttpContext context) => BrowserAddress.Length > 0 && string.Equals(context.Request.Host.Host, settings.BrowserHostname, StringComparison.OrdinalIgnoreCase) && (context.Request.Headers.Origin.Count == 0 || OriginAllowed(context.Request.Headers.Origin.ToString())) && context.Request.Headers["Sec-Fetch-Site"].ToString() is not ("cross-site");
    private bool OriginAllowed(string origin) => origin == BrowserAddress;
    private static string Csrf(CompanionDevice device) => PairingAuthority.Hash("csrf-v1:" + device.Verifier);
    private async Task Asset(HttpContext context, string file) { var asset = assets[file]; context.Response.ContentType = asset.Mime; await context.Response.Body.WriteAsync(asset.Bytes, context.RequestAborted); }
    private static Task ProtocolMismatch(HttpContext context) { context.Response.StatusCode = 426; return context.Response.WriteAsJsonAsync(new { status = "Protocol mismatch", protocol = CompanionSnapshot.Protocol }); }
    private CompanionEnvelope Envelope(CompanionDevice device, CompanionSnapshot? prior,CompanionSnapshot? selected=null)
    {
        var snapshot = selected??Volatile.Read(ref current);
        if (snapshot == null) return new(1, "status", 0, null, DateTimeOffset.UtcNow, null, "Waiting for PC projection");
        if (!device.Contexts.Contains(snapshot.ContextId)) return new(1, "paused", snapshot.Revision, null, snapshot.At, null, "Sharing paused for the selected character");
        var allowed = snapshot.ForDevice(device);
        if (prior == null || prior.ContextId != allowed.ContextId||prior.ContextLabel!=allowed.ContextLabel) return new(1, "snapshot", allowed.Revision, null, allowed.At, allowed);
        var changes = new Dictionary<string, object>();
        if (JsonSerializer.Serialize(prior.Build, Json) != JsonSerializer.Serialize(allowed.Build, Json)) changes["build"] = allowed.Build;
        if (JsonSerializer.Serialize(prior.Goals, Json) != JsonSerializer.Serialize(allowed.Goals, Json)) changes["goals"] = allowed.Goals;
        if (JsonSerializer.Serialize(prior.Session, Json) != JsonSerializer.Serialize(allowed.Session, Json)) changes["session"] = allowed.Session;
        if (JsonSerializer.Serialize(prior.Theme, Json) != JsonSerializer.Serialize(allowed.Theme, Json)) changes["theme"] = allowed.Theme;
        if (JsonSerializer.Serialize(prior.Holdings, Json) != JsonSerializer.Serialize(allowed.Holdings, Json)) changes["holdings"] = allowed.Holdings;
        return new(1, "update", allowed.Revision, prior.Revision, allowed.At, changes);
    }
    private async Task Stream(HttpContext context)
    {
        var device = Auth(context); if (device == null) { context.Response.StatusCode = 401; return; }
        if (!context.WebSockets.IsWebSocketRequest || device.Kind == "browser" && !OriginAllowed(context.Request.Headers.Origin.ToString())) { context.Response.StatusCode = 403; return; }
        if (context.Request.Headers["Sec-WebSocket-Protocol"].ToString().Split(',', StringSplitOptions.TrimEntries).Contains("catalyst.v1") == false) { await ProtocolMismatch(context); return; }
        if (streams.Values.Count(s => s.Device == device.Id) >= 2) { context.Response.StatusCode = 429; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync("catalyst.v1"); string id = Guid.NewGuid().ToString("N"); var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        streams[id] = (device.Id, socket); subscribers[id] = channel; channel.Writer.TryWrite(true); CompanionSnapshot? prior = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var receive = Task.Run(async () => { var buffer = new byte[256]; while (!stop.IsCancellationRequested && socket.State == WebSocketState.Open) { var message = await socket.ReceiveAsync(buffer, stop.Token); if (message.MessageType == WebSocketMessageType.Close || message.Count > 0) break; } stop.Cancel(); }, stop.Token);
        try
        {
            while (await channel.Reader.WaitToReadAsync(stop.Token))
            {
                while (channel.Reader.TryRead(out _)) { }
                if (device.Revoked || device.ExpiresAt <= DateTimeOffset.UtcNow) break;
                var latest=Volatile.Read(ref current);var envelope = Envelope(device, prior,latest); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(envelope, Json), WebSocketMessageType.Text, true, timeout.Token);
                prior = latest != null && device.Contexts.Contains(latest.ContextId) ? latest.ForDevice(device) : null;
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException) { }
        finally { stop.Cancel(); socket.Abort(); streams.TryRemove(id, out _); subscribers.TryRemove(id, out _); try { await receive; } catch (Exception e) when (e is OperationCanceledException or WebSocketException) { } }
    }
    private void CloseDevice(string device) { foreach (var stream in streams.Values.Where(s => s.Device == device)) stream.Socket.Abort(); foreach (var channel in subscribers.Values) channel.Writer.TryWrite(true); }
    private static bool Limit(ConcurrentDictionary<string, (DateTimeOffset From, int Count)> limits, string key, int limit, TimeSpan window)
    { if (limits.Count > 512) foreach (var old in limits.Where(p => p.Value.From + window < DateTimeOffset.UtcNow).Select(p => p.Key).ToArray()) limits.TryRemove(old, out _); var now = DateTimeOffset.UtcNow; var entry = limits.AddOrUpdate(key, (now, 1), (_, previous) => previous.From + window < now ? (now, 1) : (previous.From, previous.Count + 1)); return entry.Count <= limit; }
    private async Task RequirePrivateNetwork(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "$i=(Get-NetIPAddress -IPAddress '" + local + "' -ErrorAction Stop).InterfaceIndex; if ((Get-NetConnectionProfile -InterfaceIndex $i -ErrorAction Stop).NetworkCategory -eq 'Private') { 'Private' }" }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Cannot verify the selected network profile.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15)); string category = await process.StandardOutput.ReadToEndAsync(timeout.Token); await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0 || category.Trim() != "Private") throw new InvalidOperationException("Phone sharing requires the selected Windows network to be Private. Change it in Windows Network settings, then retry.");
    }
    public async ValueTask DisposeAsync()
    { Running=false;boundaryStop.Cancel();if(boundaryMonitor!=null)try{await boundaryMonitor;}catch(OperationCanceledException){}Pairing.Revoked -= CloseDevice; foreach (var stream in streams.Values) stream.Socket.Abort(); foreach (var channel in subscribers.Values) channel.Writer.TryComplete(); discovery?.Dispose(); multicast?.Dispose(); if (app != null) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); try{await app.StopAsync(timeout.Token);}catch(OperationCanceledException){}await app.DisposeAsync(); } browserCertificate?.Dispose(); identity.Dispose();boundaryStop.Dispose(); }
}
