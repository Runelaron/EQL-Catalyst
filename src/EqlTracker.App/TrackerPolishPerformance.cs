using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows.Threading;
using EqlTracker.Core;
namespace EqlTracker.App;
internal static class TrackerPolishPerformance
{
    internal static async Task Run(TrackerController controller,OverlayWindow overlay,string folder)
    {
        controller.Profile.SmoothMeterMotion=false;controller.Profile.OverlayMeter=new(){Actor="Combined"};overlay.ShowActivated=false;overlay.Left=overlay.Top=-15000;overlay.Width=440;overlay.Height=410;overlay.Show();controller.StartProjectionValidation();
        async Task WaitFor(Func<bool> ready){var watch=Stopwatch.StartNew();while(!ready()){if(watch.ElapsedMilliseconds>12000)throw new InvalidOperationException("Performance snapshot timeout");await Task.Delay(30);}}
        await WaitFor(()=>controller.Published?.OverlayCombat!=null);var samples=new List<object>();
        void Measure(int run,int index,string stage){long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();overlay.Refresh();samples.Add(new{Run=run,Index=index,Stage=stage,Milliseconds=watch.Elapsed.TotalMilliseconds,Allocated=GC.GetAllocatedBytesForCurrentThread()-allocated});}
        Measure(0,0,"First reading");
        for(int run=1;run<=3;run++)
        {
            controller.Profile.OverlayMeter!.Kind=run==2?"Melee":"";var expected=controller.Profile.OverlayMeter.Kind;await WaitFor(()=>controller.Published!.OverlayCombat!.Filter.Kind==expected);
            Measure(run,0,"Context or cadence reading");Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            for(int i=1;i<=100;i++)Measure(run,i,"Unchanged published snapshot");await Task.Delay(220);
        }
        Json.Write(Path.Combine(folder,"tracker-polish-performance.json"),new{Samples=samples,Diagnostics=UpdateDiagnostics.Drain(),AppSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),CoreSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CharacterProfile).Assembly.Location))),Scope="Three warmed passes through the production-equivalent published-snapshot path. Includes first/context readings and every sample; illustrative retained fixture, no live-game or full-combat benchmark claim."});
    }
}
