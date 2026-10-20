using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using EqlTracker.Core;

namespace EqlTracker.App;

public static class MeterVisualContext
{
    public static string Key(CharacterProfile profile, DpsViewState view, DateTime sessionStart) => (profile.Meter.Scope == "Session" ? "session:" + sessionStart.Ticks : profile.Meter.Scope + ":" + view.Fights.FirstOrDefault()?.Id) + "|" + view.Filter;
    public static string Key(DpsSelection selection,DpsViewState view,DateTime sessionStart)=>(selection.Scope=="Session"?"session:"+(view.Session?.Id??sessionStart.Ticks.ToString()):selection.Scope+":"+view.Fights.FirstOrDefault()?.Id)+"|"+view.Filter;
}

// Only presentation values interpolate. Targets and combat records remain exact.
public sealed class AnimatedNumber : TextBlock
{
    public AnimatedNumber() => SetResourceReference(ForegroundProperty, "Foreground");
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(AnimatedNumber), new PropertyMetadata(0d, (d, _) => ((AnimatedNumber)d).Update()));
    public static readonly DependencyProperty SmoothProperty = DependencyProperty.Register(nameof(Smooth), typeof(bool), typeof(AnimatedNumber), new PropertyMetadata(true, (d, _) => ((AnimatedNumber)d).Update()));
    private static readonly DependencyProperty DisplayedProperty = DependencyProperty.Register("Displayed", typeof(double), typeof(AnimatedNumber), new PropertyMetadata(0d, (d, _) => ((AnimatedNumber)d).WriteText()));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool Smooth { get => (bool)GetValue(SmoothProperty); set => SetValue(SmoothProperty, value); }
    public string Format { get; set; } = "N0";
    public string Suffix { get; set; } = "";
    public bool Abbreviate { get; set; }
    public void SetNumber(double value, bool smooth, bool snap = false)
    { Smooth = smooth; Value = value; if (snap) Snap(); else if (Text.Length == 0) WriteText(); }
    public void Snap() { BeginAnimation(DisplayedProperty, null); SetValue(DisplayedProperty, Value); WriteText(); }
    private void Update()
    {
        double previous = (double)GetValue(DisplayedProperty);
        BeginAnimation(DisplayedProperty, null); SetValue(DisplayedProperty, Value);
        if (IsLoaded && Smooth && SystemParameters.ClientAreaAnimation && Math.Abs(previous - Value) > .0001)
            BeginAnimation(DisplayedProperty, new DoubleAnimation(previous, Value, TimeSpan.FromMilliseconds(320)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        else WriteText();
    }
    private void WriteText()
    {
        double value = (double)GetValue(DisplayedProperty);
        Text = Abbreviate && Math.Abs(value) >= 1000000 ? (value / 1000000).ToString("0.#") + "m" + Suffix : Abbreviate && Math.Abs(value) >= 1000 ? (value / 1000).ToString("0.#") + "k" + Suffix : value.ToString(Format, CultureInfo.CurrentCulture) + Suffix;
    }
}

public sealed class ActorBadge : FrameworkElement
{
    public ActorBadge() => IconArtwork.Observe(this);
    public const string TotalTooltip = "Total damage · Self + owned pet.";
    private static Geometry Shape(string path) { var geometry = Geometry.Parse(path); geometry.Freeze(); return geometry; }
    private static readonly Geometry totalSword = Shape("M2,1 L6,2 L20,18 L18,20 L3,5 Z M16,18 L19,15 M19,20 L22,23");
    private static readonly Geometry totalAxe = Shape("M2,22 L18,4 M15,3 C18,5 20,2 21,1 C24,5 22,9 19,10 L16,7 L13,10 L11,8 Z");
    private static readonly Geometry totalShield = Shape("M5.5,8 L12,6 L18.5,8 L18,15 C17.5,18 15,21 12,23 C9,21 6.5,18 6,15 Z");
    private static readonly Geometry totalRidge = Shape("M12,9 L12,19 M8,10 L8.5,15");
    private string actor = "Pet";
    private string description = "";
    private bool observed;
    public string Element { get; private set; } = "Unknown";
    public void Set(string value, bool identified, string tooltip, string element = "Unknown")
    {
        if (value == "Combined") { identified = false; element = "Unknown"; tooltip = TotalTooltip; }
        if (actor == value && observed == identified && Element == element && description == tooltip) return;
        actor = value; observed = identified; Element = element; description = tooltip; ToolTip = tooltip; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight); if (size <= 0) return;
        if (IconArtwork.DrawPainted(dc, IconArtwork.ActorRole(actor, Element), new(0, 0, size, size), VisualTreeHelper.GetDpi(this).PixelsPerDip))
        { if (observed) dc.DrawEllipse(UI.Brush("Mint"), null, new(size * 22 / 24, size * 2 / 24), size / 12, size / 12); return; }
        dc.PushTransform(new ScaleTransform(size / 24, size / 24));
        var brush = UI.Brush(actor == "Player" ? "Mint" : actor == "Combined" ? "Violet" : Element switch { "Earth" => "PetEarth", "Air" => "PetAir", "Fire" => "PetFire", _ => "Accent" });
        if (actor == "Player")
        {
            var pen = new Pen(brush, 2); dc.DrawLine(pen, new(5, 4), new(19, 20)); dc.DrawLine(pen, new(19, 4), new(5, 20));
            dc.DrawLine(pen, new(2, 17), new(8, 22)); dc.DrawLine(pen, new(16, 22), new(22, 17));
        }
        else if (actor == "Combined")
        {
            var gold = UI.Brush("Melee"); var outline = new Pen(gold, 1.35) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.PushOpacity(.28); dc.DrawGeometry(gold, null, totalSword); dc.DrawGeometry(gold, null, totalAxe); dc.Pop();
            dc.DrawGeometry(null, outline, totalSword); dc.DrawGeometry(null, outline, totalAxe);
            // An opaque violet face separates the shield from the crossed weapons at small sizes.
            dc.DrawGeometry(UI.Brush("SolidInnerSurface"), null, totalShield);
            dc.PushOpacity(.28); dc.DrawGeometry(brush, null, totalShield); dc.Pop();
            dc.DrawGeometry(null, new Pen(brush, 1.6) { LineJoin = PenLineJoin.Round }, totalShield);
            dc.PushOpacity(.65); dc.DrawGeometry(null, new Pen(brush, 1.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, totalRidge); dc.Pop();
        }
        else if(actor=="Group")
        {
            var ink=UI.Brush("Accent");foreach(var point in new[]{new Point(5,9),new Point(12,5),new Point(19,9)})dc.DrawEllipse(ink,null,point,2.6,2.6);
            dc.DrawRoundedRectangle(ink,null,new Rect(1,13,7,7),2,2);dc.DrawRoundedRectangle(ink,null,new Rect(8,10,8,12),2,2);dc.DrawRoundedRectangle(ink,null,new Rect(16,13,7,7),2,2);
        }
        else if (actor == "Pet" && PetElementDetector.Elements.Contains(Element))
        {
            string shape = Element switch
            {
                "Earth" => "M2,20 L4,9 10,3 16,5 22,14 20,21Z M4,9 L13,11 16,5 M13,11 L11,21 M13,11 L22,14",
                "Fire" => "M12,1 C18,8 10,9 20,13 C25,23 1,27 4,14 C5,9 9,10 12,1Z M12,12 C8,18 9,23 14,21 C18,19 13,17 12,12Z",
                "Air" => "M1,7 L15,7 C24,7 22,0 17,2 M3,12 L20,12 C26,12 24,20 19,19 M1,17 L11,17 C17,17 16,24 11,22",
                _ => "M12,1 C10,7 3,12 3,16 C3,27 23,27 21,15 C20,10 15,7 12,1Z M6,17 C8,22 15,23 18,18"
            };
            dc.DrawGeometry(Element == "Air" ? null : new SolidColorBrush(Color.FromArgb(55, brush.Color.R, brush.Color.G, brush.Color.B)), new Pen(brush, 1.6) { LineJoin = PenLineJoin.Round }, Geometry.Parse(shape));
        }
        else
        {
            foreach (var (x, y) in new[] { (4d, 10d), (9d, 5d), (15d, 5d), (20d, 10d) }) dc.DrawEllipse(brush, null, new(x, y), 2.5, 3);
            dc.DrawGeometry(brush, null, Geometry.Parse("M 5,18 C 5,13 9,11 12,11 C 15,11 19,13 19,18 C 19,23 15,20 12,20 C 9,20 5,23 5,18 Z"));
        }
        if (observed) dc.DrawEllipse(UI.Brush("Mint"), null, new(22, 2), 2, 2);
        dc.Pop();
    }
}

public sealed class DamageGlyph : FrameworkElement
{
    public DamageGlyph() => IconArtwork.Observe(this);
    public static readonly DependencyProperty IsDamageModeProperty = DependencyProperty.Register(nameof(IsDamageMode), typeof(bool), typeof(DamageGlyph), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool IsDamageMode { get => (bool)GetValue(IsDamageModeProperty); set => SetValue(IsDamageModeProperty, value); }
    public static readonly DependencyProperty ChannelProperty = DependencyProperty.Register(nameof(Channel), typeof(string), typeof(DamageGlyph), new FrameworkPropertyMetadata("Unknown", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AbilityProperty = DependencyProperty.Register(nameof(Ability), typeof(string), typeof(DamageGlyph), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MonkKickProperty = DependencyProperty.Register(nameof(MonkKick), typeof(bool), typeof(DamageGlyph), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool MonkKick { get => (bool)GetValue(MonkKickProperty); set => SetValue(MonkKickProperty, value); }
    public string Channel { get => (string)GetValue(ChannelProperty); set => SetValue(ChannelProperty, value); }
    public string Ability { get => (string)GetValue(AbilityProperty); set => SetValue(AbilityProperty, value); }
    private static readonly Dictionary<string, Geometry> shapes = CreateShapes();
    public static string ShapeKey(string channel, string ability)
    {
        if (channel != "Physical") return shapes.ContainsKey(channel) ? channel : "Unknown";
        string name = ability.ToLowerInvariant();
        if (name.Contains("bash")) return "Shield";
        if (name.Contains("flying kick") || name.Contains("round kick")) return "MartialKick";
        if (name.Contains("kick")) return "Kick";
        if (name.Contains("backstab")) return "Dagger";
        if (name.Contains("claw") || name.Contains("rake") || name.Contains("maul")) return "Claw";
        if (name.Contains("punch") || name.Contains("strike")) return "Fist";
        if (name.Contains("pierc")) return "Spear";
        if (name.Contains("crush")) return "Mace";
        if (name.Contains("shoot")) return "Bow";
        if (name.Contains("bit")) return "Fang";
        if (name.Contains("cleav") || name.Contains("frenz")) return "Axe";
        if (name.Contains("slash") || name.Contains("reav")) return "Sword";
        return "Physical";
    }
    private static Dictionary<string, Geometry> CreateShapes()
    {
        var paths = new Dictionary<string,string>
        {
            ["Healing"] = "M6,1 L10,1 10,6 15,6 15,10 10,10 10,15 6,15 6,10 1,10 1,6 6,6Z",
            ["Physical"] = "M3,13 L13,3 M2,3 L13,14 M1,11 L5,15 M11,1 L15,5",
            ["Sword"] = "M2,14 L13,3 14,1 11,2 1,13Z M3,9 L7,13",
            ["Shield"] = "M2,2 L8,1 14,2 13,9 Q12,13 8,15 Q4,13 3,9Z M8,4 L8,11 M5,7 L11,7",
            ["MartialKick"] = "M5,1 L9,2 8,8 14,10 15,13 Q12,15 9,13 L4,10 2,7Z M2,2 L4,3 M1,5 L3,5 M10,5 L14,4",
            ["Kick"] = "M3,1 L9,1 9,7 11,8 15,8 15,12 13,14 4,14 2,12 3,7Z M3,4 L9,4 M3,7 L9,7 M4,10 L8,11 M3,12 L14,12",
            ["Fist"] = "M3,7 L3,3 Q4,1 5,3 L5,6 6,2 Q7,0 8,2 L8,6 9,2 Q11,1 11,3 L11,7 12,4 Q14,3 14,5 L14,10 11,15 5,14 1,9 Q0,6 3,7Z",
            ["Dagger"] = "M2,14 L7,9 M5,6 L10,11 M8,8 L14,1 13,7 10,10",
            ["Spear"] = "M2,15 L11,6 M9,6 L12,1 15,1 15,4 11,7Z",
            ["Mace"] = "M3,14 L8,8 M7,3 L11,1 15,5 13,9 9,9 5,5Z M9,3 L13,7",
            ["Bow"] = "M3,2 Q15,8 3,14 L3,2 M1,8 L15,8 M12,5 L15,8 12,11",
            ["Fang"] = "M2,2 Q8,5 14,2 Q12,11 8,15 Q8,8 2,2Z",
            ["Claw"] = "M2,2 Q7,5 4,14 M7,1 Q12,5 9,15 M12,2 Q17,6 13,14",
            ["Axe"] = "M3,14 L12,3 M8,2 Q11,4 15,1 L15,8 Q11,8 8,5Z",
            ["Magic"] = "M8,1 L10,6 15,8 10,10 8,15 6,10 1,8 6,6Z",
            ["Cold"] = "M8,1 L8,15 M2,4 L14,12 M2,12 L14,4 M5,2 L8,5 11,2 M5,14 L8,11 11,14",
            ["Fire"] = "M8,1 C13,7 7,6 14,10 C16,17 0,17 2,9 C3,6 5,8 8,1Z",
            ["Disease"] = "M3,3 L13,13 M13,3 L3,13 M8,1 L8,15 M1,8 L15,8 M4,5 L5,4 11,4 12,5 12,11 11,12 5,12 4,11Z",
            ["Poison"] = "M8,1 C6,6 2,9 3,12 C5,17 14,16 14,11 C14,8 10,5 8,1Z M6,11 L6,12 M10,11 L10,12",
            ["Corruption"] = "M3,2 L13,2 15,8 11,11 11,15 5,15 5,11 1,8Z M5,6 L6,7 M10,7 L11,6",
            ["Unknown"] = "M8,1 L15,8 8,15 1,8Z M8,5 L8,9 M8,12 L8,12.2"
        };
        return paths.ToDictionary(p=>p.Key,p=> { var g=Geometry.Parse(p.Value);g.Freeze();return g; });
    }
    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight); if (size <= 0) return;
        if (IsDamageMode) { IconArtwork.DrawDamage(dc, new(0, 0, size, size), VisualTreeHelper.GetDpi(this).PixelsPerDip); return; }
        string shape = MonkKick && ShapeKey(Channel, Ability) == "Kick" ? "MartialKick" : ShapeKey(Channel, Ability);
        if (IconArtwork.DrawPainted(dc, IconArtwork.GlyphRole(shape), new(0, 0, size, size), VisualTreeHelper.GetDpi(this).PixelsPerDip)) return;
        dc.PushTransform(new ScaleTransform(size / 16, size / 16));
        var brush = UI.Brush(Channel == "Healing" ? "Mint" : Channel == "Physical" ? "Melee" : "Spell");
        dc.DrawGeometry(null, new Pen(brush, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, shapes[shape]); dc.Pop();
    }
}

public sealed class MeterOverview : Border
{
    private readonly AnimatedNumber damage = new(), maximum = new(), rolling = new() { Format = "N1" };
    private readonly TextBlock[] labels;
    public MeterOverview()
    {
        BorderThickness = new(0, 1, 0, 0); SetResourceReference(BorderBrushProperty, "Edge"); Margin = new(0, 1, 0, 0); Padding = new(0, 1, 0, 0);
        var grid = new Grid(); labels = [UI.Text("Dmg", 9, "Muted"), UI.Text("Max", 9, "Muted"), UI.Text("5s", 9, "Muted")];
        int i = 0; foreach (var number in new[] { damage, maximum, rolling }) { number.FontSize = 11; number.FontWeight = FontWeights.SemiBold; number.Margin = new(0); labels[i].Margin = new(3, 0, 0, 0); grid.ColumnDefinitions.Add(new()); var stack = UI.Row(number, labels[i]); Grid.SetColumn(stack, i++); grid.Children.Add(stack); }
        Child = grid;
        SizeChanged += (_, _) => { bool narrow = ActualWidth < 190; damage.Abbreviate = maximum.Abbreviate = narrow; labels[0].Text = narrow ? "D" : "Dmg"; damage.Snap(); maximum.Snap(); };

    }
    public void Set(long total, long max, double live, bool detail, bool smooth, bool snap, bool compact)
    {
        damage.SetNumber(total, smooth, snap); maximum.SetNumber(max, smooth, snap); rolling.SetNumber(live, smooth, snap);
        maximum.Visibility = detail ? Visibility.Visible : Visibility.Hidden;
        foreach (var label in labels) label.FontSize = compact ? 8 : 9;
        Padding = new(0, 1, 0, 0); Margin = new(0, 1, 0, 0);
        ToolTip = $"Exact total: {total:N0} damage\nMaximum hit: {(detail ? max.ToString("N0") : "unknown; summary only")}\nRolling DPS: {live:N1} over the last 5 seconds";
    }
}
