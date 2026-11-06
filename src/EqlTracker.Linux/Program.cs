using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Fonts.Inter;
using Avalonia.Themes.Fluent;

namespace EqlTracker.Linux;

internal static class Program
{
    internal static string[] Arguments { get; private set; } = [];
    [STAThread]
    public static void Main(string[] args) { Arguments = args; BuildApp().StartWithClassicDesktopLifetime(args); }
    public static AppBuilder BuildApp() => AppBuilder.Configure<CatalystApplication>().UsePlatformDetect().WithInterFont().LogToTrace();
}
internal sealed class CatalystApplication : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new CatalystWindow(Program.Arguments);
        base.OnFrameworkInitializationCompleted();
    }
}
