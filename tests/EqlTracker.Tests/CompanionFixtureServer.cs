using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using EqlTracker.Companion;

internal static class CompanionFixtureServer
{
    internal static async Task Run(string folder,string projection)
    {
        Directory.CreateDirectory(folder);var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        await using var host=CompanionHost.LoopbackFixture(port);await host.StartAsync(Path.Combine(AppContext.BaseDirectory,"companion-web"));
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);var snapshot=JsonSerializer.Deserialize<CompanionSnapshot>(File.ReadAllText(projection),options)!;
        host.Publish(snapshot);var qr=host.CreatePairing(false) with{Address="https://10.0.2.2:"+port};File.WriteAllText(Path.Combine(folder,"pairing.json"),JsonSerializer.Serialize(qr,options));
        var end=DateTimeOffset.UtcNow.AddMinutes(6);while(DateTimeOffset.UtcNow<end&&!File.Exists(Path.Combine(folder,"stop")))
        {
            foreach(var request in host.Pairing.Pending)host.Pairing.Approve(request.Id,snapshot.ContextId);
            File.WriteAllText(Path.Combine(folder,"status.json"),JsonSerializer.Serialize(new{Devices=host.Pairing.Devices.Count(d=>!d.Revoked),Streams=host.StreamCount,Revision=snapshot.Revision,Protocol=1},options));await Task.Delay(250);
        }
    }
}
