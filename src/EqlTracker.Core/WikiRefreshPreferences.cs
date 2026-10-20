namespace EqlTracker.Core;
public enum WikiRefreshMode { Daily, Startup, Manual }
public static class WikiRefreshPolicy
{
    public static bool Due(WikiRefreshMode mode,DateTime? last,DateTime now,bool startup)=>mode switch
    { WikiRefreshMode.Daily=>!last.HasValue||now-last.Value>=TimeSpan.FromDays(1),WikiRefreshMode.Startup=>startup,_=>false };
}
