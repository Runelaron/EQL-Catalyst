namespace EqlTracker.Core;

public static class CombatIdentity
{
    public static string Key(string name) => name.Trim().ToLowerInvariant();
    public static bool Same(string a, string b) => a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);
}
