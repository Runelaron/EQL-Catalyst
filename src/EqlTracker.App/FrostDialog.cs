using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EqlTracker.App;

/// <summary>Transient editors close normally; they never use panel hide-on-close behavior.</summary>
public class FrostDialog : Window
{
    protected readonly Grid Editor = new();
    protected readonly WrapPanel Actions = new() { Margin = new(0, 6, 0, 0) };
    protected readonly StackPanel Heading = new();
    public FrostDialog(string title, double width = 560, double height = 400)
    {
        PlanningScrollbars.Apply(this);
        Title = CatalystBranding.Name + " — " + title; Icon = CatalystBranding.WindowIcon;
        Width = width; Height = height; MinWidth = 400; MinHeight = 260;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this,new(){CaptionHeight=0,GlassFrameThickness=new(0),ResizeBorderThickness=new(5),CornerRadius=new(0),UseAeroCaptionButtons=false});
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = SnapsToDevicePixels = true; FontFamily = new FontFamily("Segoe UI"); FontSize = 12;
        Appearance.SolidResources(this); SetResourceReference(BackgroundProperty, "SolidSurface"); SetResourceReference(ForegroundProperty, "Foreground");
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new(0, 0, 0, 6) };
        var close = UI.Button("×", Close); close.Width = close.Height = 24; close.Padding = new(0); close.Margin = new(0); close.ToolTip = "Cancel this edit and close the window."; DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        Heading.Children.Add(UI.Text(title, 15, "Foreground", true)); header.Children.Add(Heading);
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is DependencyObject source && Usability.Tree(close).Contains(source)) return; e.Handled = true; DragMove(); };
        var scroll = UI.Scroll(Editor); scroll.Padding = new(0, 0, 4, 0); Grid.SetRow(scroll, 1); Grid.SetRow(Actions, 2); root.Children.Add(header); root.Children.Add(scroll); root.Children.Add(Actions);
        var frame = new Border { Padding = new(8), BorderThickness = new(1), CornerRadius = new(3), Child = root }; frame.SetResourceReference(Border.BackgroundProperty, "SolidSurface"); frame.SetResourceReference(Border.BorderBrushProperty, "Accent"); Content = frame;
        Appearance.RaisedButtons(root);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Loaded += (_, _) => { Usability.Tooltips(root); Constrain(); };
        DpiChanged += (_, _) => Constrain();
    }
    private void Constrain()
    {
        if (Left < -10000 || Top < -10000) return; // Isolated offscreen preview.
        var handle = new System.Windows.Interop.WindowInteropHelper(Owner ?? this).Handle;
        var screen = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea; var dpi = VisualTreeHelper.GetDpi(this);
        double width = screen.Width / dpi.DpiScaleX, height = screen.Height / dpi.DpiScaleY;
        MaxWidth = Math.Max(200, width - 16); MaxHeight = Math.Max(180, height - 16); MinWidth = Math.Min(MinWidth, MaxWidth); MinHeight = Math.Min(MinHeight, MaxHeight);
        Width = Math.Min(Width, MaxWidth); Height = Math.Min(Height, MaxHeight);
        Left = Math.Clamp(Left, screen.Left / dpi.DpiScaleX, (screen.Right / dpi.DpiScaleX) - Width);
        Top = Math.Clamp(Top, screen.Top / dpi.DpiScaleY, (screen.Bottom / dpi.DpiScaleY) - Height);
    }
}
