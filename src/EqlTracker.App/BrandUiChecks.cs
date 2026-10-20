using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace EqlTracker.App;

public static class BrandUiChecks
{
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flag);
    public static void Run(TrackerController controller, Action<bool,string> check)
    {
        check(CatalystBranding.Choices.Count == 18 && CatalystBranding.Choices.Select(c => c.Id).Distinct().Count() == 18, "Icon picker has 18 stable choices");
        string before = controller.Profile.AppIconId; var beforeStyle = controller.Profile.IconStyle;
        try
        {
            foreach (var style in Enum.GetValues<EqlTracker.Core.IconArtworkStyle>())
            foreach (var choice in CatalystBranding.Choices)
            {
                controller.Profile.IconStyle = style; IconArtwork.Apply(controller.Profile);
                var image = CatalystBranding.Image(choice.Id);
                using var icon = CatalystBranding.CreateTrayIcon(choice.Id);
                check(image.PixelWidth == 256 && icon.Handle != IntPtr.Zero, "Transparent production icon loads: " + style + " / " + choice.Name);
                controller.Profile.AppIconId = choice.Id; ((App)Application.Current).ApplyIcon();
                check(Application.Current.Windows.Cast<Window>().All(w => ReferenceEquals(w.Icon,CatalystBranding.Image(choice.Id))), "Open windows switch immediately: " + choice.Name);
            }
            check(CatalystBranding.Resolve("missing-choice").Id == "blue", "Invalid icon identifiers fall back to Blue Orb");
            void Settle() { System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => {}, System.Windows.Threading.DispatcherPriority.ApplicationIdle);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect(); }
            Settle();using var process = System.Diagnostics.Process.GetCurrentProcess();int gdi = GetGuiResources(process.Handle,0), user = GetGuiResources(process.Handle,1);
            for (int cycle=0;cycle<10;cycle++) { foreach(var style in Enum.GetValues<EqlTracker.Core.IconArtworkStyle>()) foreach(var choice in CatalystBranding.Choices) { controller.Profile.IconStyle=style;IconArtwork.Apply(controller.Profile);controller.Profile.AppIconId=choice.Id;((App)Application.Current).ApplyIcon(); }Settle(); }
            int gdiDelta=GetGuiResources(process.Handle,0)-gdi,userDelta=GetGuiResources(process.Handle,1)-user;
            EqlTracker.Core.Json.Write(Path.Combine(controller.DataFolder,"icon-handles.json"),new { Switches=360,GdiDelta=gdiDelta,UserDelta=userDelta });
            check(gdiDelta<4 && userDelta<4, $"Repeated icon switching releases native handles (GDI {gdiDelta}, USER {userDelta})");
        }
        finally { controller.Profile.AppIconId=before;controller.Profile.IconStyle=beforeStyle;IconArtwork.Apply(controller.Profile);((App)Application.Current).ApplyIcon(); }
    }
}
