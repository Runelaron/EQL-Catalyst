using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using EqlTracker.Companion;

internal static class CompanionTransportTests
{
    internal static void Register(Action<string,Action> test) => test("Companion actual TLS HTTP cookies origins payloads permissions and stream revocation",()=>Run().GetAwaiter().GetResult());
    private static async Task Run()
    {
        void Check(bool value,string detail){if(!value)throw new Exception(detail);}
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        await using var host=CompanionHost.LoopbackFixture(port);await host.StartAsync(Path.Combine(AppContext.BaseDirectory,"companion-web"));
        var empty=new CompanionSection("Fixture",[]);host.Publish(new(1,DateTimeOffset.UtcNow,"hero","Synthetic Hero",new Dictionary<string,string>(),new("Build",[new("Speed","171","Synthetic projected result")]),empty,empty,empty));
        var policy=new X509ChainPolicy{TrustMode=X509ChainTrustMode.CustomRootTrust,RevocationMode=X509RevocationMode.NoCheck};policy.CustomTrustStore.Add(host.FixtureCertificate);
        using var handler=new SocketsHttpHandler{SslOptions=new(){CertificateChainPolicy=policy},AllowAutoRedirect=false};using var client=new HttpClient(handler){BaseAddress=new Uri(host.Address)};client.DefaultRequestHeaders.Add("X-Catalyst-Protocol","1");
        using(var wrong=new HttpClient()) { bool rejected=false;try{await wrong.GetAsync(host.Address+"/identity");}catch(HttpRequestException){rejected=true;}Check(rejected,"Untrusted certificate accepted"); }
        var unapproved=await client.GetAsync("/api/v1/snapshot");Check(unapproved.StatusCode==HttpStatusCode.Unauthorized,"Unapproved client saw a projection");
        var challenge=host.CreatePairing(false);var request=await client.PostAsJsonAsync("/pair/request",new PairingRequest(challenge.Challenge,"Fixture phone","android",1));Check(request.IsSuccessStatusCode,"Pair request failed");var ticket=(await request.Content.ReadFromJsonAsync<PairingTicket>())!;
        var waiting=(await (await client.PostAsJsonAsync("/pair/poll",new PairingPoll(ticket.RequestId,ticket.PollSecret))).Content.ReadFromJsonAsync<PairingOutcome>())!;Check(waiting.Credential==null,"Credential delivered before desktop approval");
        Check(host.Pairing.Approve(ticket.RequestId,"hero"),"Desktop approval failed");var approved=(await (await client.PostAsJsonAsync("/pair/poll",new PairingPoll(ticket.RequestId,ticket.PollSecret))).Content.ReadFromJsonAsync<PairingOutcome>())!;client.DefaultRequestHeaders.Authorization=new("Bearer",approved.Credential);
        var snapshot=await client.GetStringAsync("/api/v1/snapshot");Check(snapshot.Contains("171")&&snapshot.Contains("Synthetic Hero"),"PC projection mismatch");
        using(var originRequest=new HttpRequestMessage(HttpMethod.Get,"/api/v1/snapshot")){originRequest.Headers.Add("Origin","https://attacker.invalid");Check((await client.SendAsync(originRequest)).StatusCode==HttpStatusCode.Forbidden,"Cross-origin request accepted");}
        client.DefaultRequestHeaders.Remove("X-Catalyst-Protocol");client.DefaultRequestHeaders.Add("X-Catalyst-Protocol","2");Check((await client.GetAsync("/api/v1/snapshot")).StatusCode==(HttpStatusCode)426,"Protocol mismatch not explicit");client.DefaultRequestHeaders.Remove("X-Catalyst-Protocol");client.DefaultRequestHeaders.Add("X-Catalyst-Protocol","1");
        Check((await client.PostAsync("/pair/request",new StringContent("{",Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.BadRequest,"Malformed JSON not rejected");
        Check((await client.PostAsync("/pair/request",new StringContent(new string('x',9000),Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.RequestEntityTooLarge,"Payload limit not enforced");
        host.Publish(new(2,DateTimeOffset.UtcNow,"other","PRIVATE OTHER CHARACTER",new Dictionary<string,string>(),empty,empty,empty,empty));var paused=await client.GetStringAsync("/api/v1/snapshot");Check(paused.Contains("paused")&&!paused.Contains("PRIVATE OTHER"),"Unauthorized character leaked");
        host.Publish(new(3,DateTimeOffset.UtcNow,"hero","Synthetic Hero",new Dictionary<string,string>(),empty,empty,empty,empty));
        using var stream=new ClientWebSocket();stream.Options.AddSubProtocol("catalyst.v1");stream.Options.SetRequestHeader("Authorization","Bearer "+approved.Credential);
        stream.Options.RemoteCertificateValidationCallback=(_,certificate,_,errors)=>{if(certificate==null||(errors&SslPolicyErrors.RemoteCertificateNameMismatch)!=0)return false;using var chain=new X509Chain();chain.ChainPolicy=policy;return chain.Build(X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()));};
        await stream.ConnectAsync(new Uri(host.Address.Replace("https:","wss:")+"/api/v1/stream"),CancellationToken.None);var buffer=new byte[16384];using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));await stream.ReceiveAsync(buffer,timeout.Token);
        host.Publish(new(4,DateTimeOffset.UtcNow,"hero","Synthetic Hero",new Dictionary<string,string>(),new("Changed",[new("HP","50")]),empty,empty,empty));var incremental=await stream.ReceiveAsync(buffer,timeout.Token);string packet=Encoding.UTF8.GetString(buffer,0,incremental.Count);Check(packet.Contains("\"baseRevision\":3")&&packet.Contains("\"revision\":4")&&packet.Contains("Changed"),"Incremental revision mismatch");
        string device=approved.Credential!.Split('.')[0];host.Pairing.Revoke(device);bool closed=false;try{var end=await stream.ReceiveAsync(buffer,timeout.Token);closed=end.MessageType==WebSocketMessageType.Close;}catch(WebSocketException){closed=true;}Check(closed,"Revocation did not terminate the stream");Check((await client.GetAsync("/api/v1/snapshot")).StatusCode==HttpStatusCode.Unauthorized,"Revoked credential remained valid");
        using var browserHandler=new SocketsHttpHandler{SslOptions=new(){CertificateChainPolicy=policy},CookieContainer=new CookieContainer()};using var browser=new HttpClient(browserHandler){BaseAddress=new Uri(host.BrowserAddress)};browser.DefaultRequestHeaders.Add("X-Catalyst-Protocol","1");browser.DefaultRequestHeaders.Add("Origin",host.BrowserAddress);
        var browserChallenge=host.CreatePairing(true);var browserTicket=(await (await browser.PostAsJsonAsync("/pair/request",new PairingRequest(browserChallenge.Challenge,"Fixture browser","browser",1))).Content.ReadFromJsonAsync<PairingTicket>())!;host.Pairing.Approve(browserTicket.RequestId,"hero");var delivery=await browser.PostAsJsonAsync("/pair/poll",new PairingPoll(browserTicket.RequestId,browserTicket.PollSecret));string cookie=string.Join(";",delivery.Headers.GetValues("Set-Cookie"));Check(cookie.Contains("secure",StringComparison.OrdinalIgnoreCase)&&cookie.Contains("httponly",StringComparison.OrdinalIgnoreCase)&&cookie.Contains("samesite=strict",StringComparison.OrdinalIgnoreCase),"Cookie protections missing");
        Check((await browser.GetAsync("/api/v1/snapshot")).IsSuccessStatusCode,"Browser cookie authentication failed");Check((await browser.PostAsJsonAsync("/api/v1/logout",new{})).StatusCode==HttpStatusCode.Forbidden,"CSRF token was not required");
        using var session=JsonDocument.Parse(await browser.GetStringAsync("/api/v1/session"));browser.DefaultRequestHeaders.Add("X-Catalyst-CSRF",session.RootElement.GetProperty("csrf").GetString());Check((await browser.PostAsJsonAsync("/api/v1/logout",new{})).IsSuccessStatusCode,"CSRF-authenticated logout failed");
    }
}
