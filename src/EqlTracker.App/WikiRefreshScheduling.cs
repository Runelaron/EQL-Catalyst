using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class TrackerController
{
    private Task? wikiRefreshWorker;
    private async Task WikiRefreshLoop()
    {
        bool startup=true;DateTime retry=DateTime.MinValue;
        try
        {
            // Let retained data and the first UI snapshot settle before network work.
            await Task.Delay(TimeSpan.FromSeconds(15),cancel.Token);
            while(!cancel.IsCancellationRequested)
            {
                bool due;lock(Sync)due=!refreshing&&DateTime.Now>=retry&&WikiRefreshPolicy.Due(Profile.WikiRefresh,Profile.LastWikiRefreshAt,DateTime.Now,startup);
                if(due){await RefreshWikiAsync();startup=false;retry=DateTime.Now.AddMinutes(30);}
                await Task.Delay(TimeSpan.FromMinutes(1),cancel.Token);
            }
        }
        catch(OperationCanceledException){}
    }
}
