namespace EqlTracker.Core;

internal static class EncounterTimingSettings
{
    internal static readonly int[] Presets=[6,8,10,12];
    internal static bool TryRead(string idle,string resume,out int seconds,out int resumeSeconds,out string error)
    {
        resumeSeconds=0;
        if(!int.TryParse(idle,out seconds)||seconds is <2 or >120){error="Idle cutoff must be a whole number from 2 to 120 seconds.";return false;}
        if(!int.TryParse(resume,out resumeSeconds)||resumeSeconds is <0 or >300||resumeSeconds!=0&&resumeSeconds<seconds)
        {error="Resume must be 0 (disabled), or at least the idle cutoff and no more than 300 seconds.";return false;}
        error="";return true;
    }
}
