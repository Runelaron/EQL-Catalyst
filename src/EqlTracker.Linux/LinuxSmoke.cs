using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EqlTracker.Core;

namespace EqlTracker.Linux;
internal sealed partial class CatalystWindow
{
    private async Task Smoke(string folder)
    {
        var checks = new List<string>();
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); checks.Add(text); }
        try
        {
            Directory.CreateDirectory(folder); await Task.Delay(100);
            Check(controller?.Demo == true, "Smoke mode uses synthetic evidence only");
            foreach (string theme in new[] { "coldain", "sunstrider", "shadowman" })
            foreach (var size in new[] { (Width: 915d, Height: 650d, Name: "normal"), (Width: 640d, Height: 420d, Name: "minimum"), (Width: 915d, Height: 420d, Name: "short") })
            foreach (string page in new[] { "Tracker", "Combat Lab", "Overview" })
            {
                controller!.Profile.Theme = theme; ApplyTheme(); workspace = page; Width = size.Width; Height = size.Height; RenderWorkspace();
                await Dispatcher.UIThread.InvokeAsync(() => { UpdateLayout(); }, DispatcherPriority.Loaded); await Task.Delay(20);
                Check(root.Bounds.Width > 0 && root.Bounds.Height > 0, theme + "/" + size.Name + "/" + page + " has a finite native viewport");
                foreach (double scale in new[] { 1d, 1.5, 2, 3 })
                {
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)(Width * scale), (int)(Height * scale)), new Vector(96 * scale, 96 * scale)); bitmap.Render(root);
                    Check(bitmap.PixelSize.Width == (int)(Width * scale), "Synthetic scale " + scale + "/" + theme + "/" + size.Name + "/" + page);
                    if (scale == 1) bitmap.Save(Path.Combine(folder, theme + "-" + size.Name + "-" + page.Replace(' ', '-') + ".png"),PngBitmapEncoderOptions.Default);
                }
            }
            Json.Write(Path.Combine(folder, "linux-native-checks.json"), new { Passed = true, Checks = checks, Synthetic = true, ReleaseQualified = false, SteamDeckHardwareTested = false, UbuntuDesktopAcceptance = false, SyntheticLinuxDisplay = true, ParityComplete = false });
        }
        catch (Exception error)
        {
            Json.Write(Path.Combine(folder, "linux-native-checks.json"), new { Passed = false, Checks = checks, Error = error.ToString(), ReleaseQualified = false }); Environment.ExitCode = 1;
        }
        finally { closingApproved = true; DisposeController(); Close(); }
    }
}


