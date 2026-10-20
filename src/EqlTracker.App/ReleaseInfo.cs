using System.Reflection;

namespace EqlTracker.App;

internal static class ReleaseInfo
{
    internal static string Version => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";
    internal static string Label => Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "ReleaseLabel")?.Value ?? Version;
}
