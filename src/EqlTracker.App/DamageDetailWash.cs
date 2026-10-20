using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace EqlTracker.App;

/// <summary>A connected EQ bar with shallow fifth-interval divots, drawn behind strike details.</summary>
public sealed class DamageDetailWash : FrameworkElement
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(nameof(Target), typeof(double), typeof(DamageDetailWash), new PropertyMetadata(0d, Changed, (_, value) => double.IsFinite((double)value) ? Math.Clamp((double)value, 0, 100) : 0d));
    public static readonly DependencyProperty SmoothProperty = DependencyProperty.Register(nameof(Smooth), typeof(bool), typeof(DamageDetailWash), new PropertyMetadata(true, Changed));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(DamageDetailWash), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty DisplayedProperty = DependencyProperty.Register("Displayed", typeof(double), typeof(DamageDetailWash), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly Brush Track = Frozen(new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(16, 240, 247, 249), 0), new(Color.FromArgb(6, 240, 247, 249), .42), new(Color.FromArgb(26, 0, 0, 0), 1) }, new Point(0, 0), new Point(0, 1)));
    private static readonly Brush Highlight = Frozen(new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(25, 226, 237, 240), 0), new(Colors.Transparent, .45), new(Colors.Transparent, .58), new(Color.FromArgb(15, 0, 0, 0), 1) }, new Point(0, 0), new Point(0, 1)));
    private Geometry? shape;
    private Size shapeSize;
    private Color washColor;
    private Brush? wash;
    public double Target { get => (double)GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public bool Smooth { get => (bool)GetValue(SmoothProperty); set => SetValue(SmoothProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    internal double Displayed => (double)GetValue(DisplayedProperty);
    private static Brush Frozen(Brush brush) { brush.Freeze(); return brush; }
    private static void Changed(DependencyObject value, DependencyPropertyChangedEventArgs _) => ((DamageDetailWash)value).Update();
    private void Update()
    {
        double previous = Displayed;
        BeginAnimation(DisplayedProperty, null); SetValue(DisplayedProperty, Target);
        if (IsLoaded && Smooth && SystemParameters.ClientAreaAnimation && Math.Abs(previous - Target) > .001)
            BeginAnimation(DisplayedProperty, new DoubleAnimation(previous, Target, TimeSpan.FromMilliseconds(320)) { FillBehavior = FillBehavior.Stop, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }
    internal static Geometry CreateShape(Size size)
    {
        double radius = Math.Min(4, size.Height * .25);
        var outline = new RectangleGeometry(new Rect(size), Math.Min(4, size.Height * .28), Math.Min(4, size.Height * .28));
        var divots = new GeometryGroup();
        for (int fifth = 1; fifth < 5; fifth++)
        {
            double x = size.Width * fifth / 5;
            divots.Children.Add(new EllipseGeometry(new Point(x, -1), radius, radius));
            divots.Children.Add(new EllipseGeometry(new Point(x, size.Height + 1), radius, radius));
        }
        var geometry = Geometry.Combine(outline, divots, GeometryCombineMode.Exclude, null);
        geometry.Freeze(); return geometry;
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        if (shape == null || shapeSize != RenderSize) { shapeSize = RenderSize; shape = CreateShape(shapeSize); }
        var color = (Fill as SolidColorBrush)?.Color ?? Colors.Transparent;
        if (wash == null || washColor != color)
        {
            washColor = color;
            Color Tint(byte alpha, double brightness) => Color.FromArgb((byte)(alpha * color.A / 255), (byte)Math.Min(255, color.R * brightness), (byte)Math.Min(255, color.G * brightness), (byte)Math.Min(255, color.B * brightness));
            wash = Frozen(new LinearGradientBrush(new GradientStopCollection { new(Tint(81, 1.12), 0), new(Tint(71, 1), .34), new(Tint(51, .8), .66), new(Tint(66, 1), 1) }, new Point(0, 0), new Point(0, 1)));
        }
        dc.PushClip(shape);
        dc.DrawRectangle(Track, null, new Rect(RenderSize));
        if (Displayed > 0) dc.DrawRectangle(wash, null, new Rect(0, 0, ActualWidth * Math.Clamp(Displayed, 0, 100) / 100, ActualHeight));
        dc.DrawRectangle(Highlight, null, new Rect(RenderSize));
        dc.Pop();
    }
}
