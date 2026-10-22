using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using EqlTracker.Core;
namespace EqlTracker.App;

internal static class UpdateDownloadReview
{
    // A message handler, not a local server: production URLs and signature checks stay intact.
    private sealed class FixtureHandler(string fixture):HttpMessageHandler
    {
        internal int Requests,Packages;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            Requests++;await Task.Delay(40,cancellation);string name=Path.GetFileName(request.RequestUri!.AbsolutePath);
            if(request.RequestUri.Host=="api.github.com")
            {
                var manifest=JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllBytes(Path.Combine(fixture,"update-manifest.json")))!;
                var files=manifest.Packages.Select(p=>p.Name).Concat(new[]{"update-manifest.json","update-manifest.sig"});
                var release=new[]{new{tag_name=manifest.Tag,draft=false,assets=files.Select(f=>new{name=f,size=new FileInfo(Path.Combine(fixture,f)).Length,browser_download_url="https://github.com/"+UpdateProtocol.Repository+"/releases/download/"+manifest.Tag+"/"+f})}};
                return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(release))};
            }
            if(name.EndsWith(".exe")||name.EndsWith(".zip"))Packages++;
            return new(HttpStatusCode.OK){Content=new StreamContent(File.OpenRead(UpdateFiles.Child(fixture,name)))};
        }
    }
    internal static async Task Run(string folder,string fixture,List<string> checks)
    {
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        async Task Wait(Func<bool> ready){for(int i=0;i<600&&!ready();i++)await Task.Delay(50);Check(ready(),"Update transport operation completed within its bound");}
        var input=UpdateProtocol.Verify(File.ReadAllBytes(Path.Combine(fixture,"update-manifest.json")),Convert.FromBase64String(File.ReadAllText(Path.Combine(fixture,"update-manifest.sig"))));
        string current=input.MinimumUpdaterVersion;Check(UpdateProtocol.Newer(input.Version,current),"Signed fixture represents a newer compatible release");
        string root=Path.Combine(folder,"transport-state");var handler=new FixtureHandler(fixture);
        using var first=new ApplicationUpdates(root,current,true,handler);
        await Task.Delay(100);Check(handler.Requests==0&&!first.Preferences.Automatic,"No request occurs before consent or manual checking");
        await first.Check();Check(first.State==ApplicationUpdateState.Available,"Manual check verifies published-version metadata and signatures");
        await first.Download();Check(first.State==ApplicationUpdateState.Ready&&handler.Packages==2,"Actual installer/portable streams download, verify and prepare successfully");
        var sharedHandler=new FixtureHandler(fixture);using var second=new ApplicationUpdates(root,current,true,sharedHandler);
        await Wait(()=>second.State==ApplicationUpdateState.Ready||second.State==ApplicationUpdateState.Error);
        Check(second.State==ApplicationUpdateState.Ready&&sharedHandler.Requests==0,"Another character restores the verified prepared update without downloading again");
        second.Automatic(false);Check(!UpdatePreferences.Read(root).Automatic,"Shared opt-out persists independently of a prepared update");
        string automaticRoot=Path.Combine(folder,"automatic-transport-state");var automaticHandler=new FixtureHandler(fixture);
        using var automatic=new ApplicationUpdates(automaticRoot,current,true,automaticHandler);automatic.Automatic(true);
        await Wait(()=>automatic.State is ApplicationUpdateState.Ready or ApplicationUpdateState.Error);
        Check(automatic.State==ApplicationUpdateState.Ready&&automaticHandler.Packages==2,"Opt-in checks and prepares the download without applying it");
        Check(!File.Exists(Path.Combine(automaticRoot,"updates","apply.json")),"Automatic downloads do not initiate installation");
        var changed=UpdatePreferences.Read(automaticRoot);changed.Automatic=false;changed.Save(automaticRoot,true);
        await Wait(()=>!automatic.Preferences.Automatic);Check(!automatic.Preferences.Automatic,"Another character's opt-out propagates live");
        string prepared=JsonSerializer.Deserialize<string>(File.ReadAllText(Path.Combine(root,"updates","ready.json")))!;
        File.AppendAllText(Path.Combine(prepared,input.Packages.Single(p=>p.Kind=="Portable").Name),"tampered");
        using var damaged=new ApplicationUpdates(root,current,true,new FixtureHandler(fixture));await Wait(()=>damaged.State==ApplicationUpdateState.Error);
        Check(damaged.State==ApplicationUpdateState.Error,"Restart rejects a modified cached package rather than marking it ready");
        Json.Write(Path.Combine(folder,"update-download-checks.json"),new{Passed=true,Assertions=checks.Count,MetadataRequests=handler.Requests,Packages=handler.Packages,ExternalRequests=0,SignatureVerificationBypassed=false,Scope="In-process transport using actual signed packages; no server, trusted-host override or production signature bypass."});
    }
}
