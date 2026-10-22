namespace EqlTracker.Core;

public enum TooltipLevel { Essential, All, None }

public static class TooltipPreferences
{
    public static readonly int[] Delays = [250, 500, 750, 1000, 1500];
    public const int DefaultDelay = 750;
    public static int Delay(int saved) => Delays.Contains(saved) ? saved : DefaultDelay;
}
