using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

/// <summary>Tracker-only chrome owns its gaps; TabPanel subtracts external TabItem margins.</summary>
public sealed class TrackerTabChrome : Decorator
{
    public static readonly DependencyProperty HeaderAccessoryProperty=DependencyProperty.RegisterAttached("HeaderAccessory",typeof(UIElement),typeof(TrackerTabChrome),new FrameworkPropertyMetadata(null));
    public static UIElement? GetHeaderAccessory(DependencyObject value)=>(UIElement?)value.GetValue(HeaderAccessoryProperty);
    public static void SetHeaderAccessory(DependencyObject value,UIElement accessory)=>value.SetValue(HeaderAccessoryProperty,accessory);
    public static readonly DependencyProperty CompactHeadersProperty=DependencyProperty.RegisterAttached("CompactHeaders",typeof(bool),typeof(TrackerTabChrome),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.Inherits|FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static bool GetCompactHeaders(DependencyObject value)=>(bool)value.GetValue(CompactHeadersProperty);
    public static void SetCompactHeaders(DependencyObject value,bool compact)=>value.SetValue(CompactHeadersProperty,compact);
    public static readonly DependencyProperty StyleIdProperty = DependencyProperty.Register(nameof(StyleId), typeof(TrackerTabStyle), typeof(TrackerTabChrome), new FrameworkPropertyMetadata(TrackerTabStyle.Dwarf, FrameworkPropertyMetadataOptions.AffectsRender));
    public TrackerTabStyle StyleId { get => (TrackerTabStyle)GetValue(StyleIdProperty); set => SetValue(StyleIdProperty,value); }
    public static readonly DependencyProperty SelectedProperty = State(nameof(Selected));
    public static readonly DependencyProperty HoveredProperty = State(nameof(Hovered));
    public static readonly DependencyProperty FocusedProperty = State(nameof(Focused));
    private static DependencyProperty State(string name) => DependencyProperty.Register(name,typeof(bool),typeof(TrackerTabChrome),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
    public bool Selected { get => (bool)GetValue(SelectedProperty); set => SetValue(SelectedProperty,value); }
    public bool Hovered { get => (bool)GetValue(HoveredProperty); set => SetValue(HoveredProperty,value); }
    public bool Focused { get => (bool)GetValue(FocusedProperty); set => SetValue(FocusedProperty,value); }
    private readonly TextBlock label;
    private string labelColor="";
    private double LeadingGap => Tag is TrackerTab.Queue ? 12 : 0;
    private const double RightGap=6,BottomGap=2,Inset=.75,VerticalBand=5;
    internal Rect OutlineBounds => new(Inset+LeadingGap,VerticalBand+Inset,Math.Max(0,ActualWidth-RightGap-2*Inset-LeadingGap),Math.Max(0,ActualHeight-BottomGap-2*VerticalBand-2*Inset));
    internal bool ShowInsideMarks => !Selected && (Hovered||Focused);
    public TrackerTabChrome()
    {
        SetResourceReference(StyleIdProperty,"TrackerTabStyle");
        SetBinding(TagProperty, new Binding("Tag") { RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(TabItem),1) });
        label = new() { FontSize=14, FontWeight=FontWeights.SemiBold, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center };
        label.SetBinding(TextBlock.TextProperty,new Binding("Header") { RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(TabItem),1) });
        Child=label;
        SetBinding(SelectedProperty,new Binding("IsSelected") { RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(TabItem),1) });
        // TabItem.IsMouseOver includes the selected page's content. Only this header chrome owns hover.
        SetBinding(HoveredProperty,new Binding(nameof(IsMouseOver)) { RelativeSource=new RelativeSource(RelativeSourceMode.Self) });
        SetBinding(FocusedProperty,new Binding("IsKeyboardFocused") { RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(TabItem),1) });
    }
    internal TrackerTabChrome(string title,TrackerTabStyle style,bool selected) : this()
    {
        label.Text=title;StyleId=style;Selected=selected;Hovered=Focused=false;
        IsHitTestVisible=false;
    }
    protected override Size MeasureOverride(Size available)
    { double padding=GetCompactHeaders(this)?16:32;Child!.Measure(new(Math.Max(0,available.Width-padding-RightGap-2*Inset-LeadingGap),Math.Max(0,available.Height-20-BottomGap-2*Inset))); return new(Math.Max(GetCompactHeaders(this)?40:64,Child.DesiredSize.Width+padding+LeadingGap)+RightGap+2*Inset,Math.Max(38,Child.DesiredSize.Height+20)+BottomGap+2*Inset); }
    protected override Size ArrangeOverride(Size final)
    {double padding=GetCompactHeaders(this)?8:16;Child!.Arrange(new Rect(padding+Inset+LeadingGap,VerticalBand+Inset,Math.Max(0,final.Width-padding*2-RightGap-2*Inset-LeadingGap),Math.Max(0,final.Height-BottomGap-2*VerticalBand-2*Inset))); return final; }
    internal static Geometry Shape(TrackerTabStyle style,double w,double h)
    {
        string path=style switch {
            TrackerTabStyle.WoodElf => $"M0,{h/2} Q2,3 {w/2},1 Q{w-2},3 {w},{h/2} Q{w-2},{h-3} {w/2},{h-1} Q2,{h-3} 0,{h/2} Z",
            TrackerTabStyle.Human => $"M9,2 H{w-9} V7 H{w-3} L{w},10 V{h-5} L{w-5},{h} H5 L0,{h-5} V10 L3,7 H9 Z",
            TrackerTabStyle.Barbarian => $"M9,0 H{w-5} L{w},8 {w-3},{h-7} {w-11},{h} H6 L0,{h-9} 3,5 Z",
            TrackerTabStyle.Erudite => $"M8,4 H15 V0 H{w-15} V4 H{w-8} L{w},11 {w-5},{h} H5 L0,11 Z",
            TrackerTabStyle.HighElf => $"M8,4 H{w/2-12} L{w/2},0 {w/2+12},4 H{w-8} L{w},12 V{h-7} L{w-7},{h} H7 L0,{h-7} V12 Z",
            TrackerTabStyle.DarkElf => $"M11,0 H{w-11} L{w-4},10 {w},{h/2} {w-4},{h-10} {w-11},{h} H11 L4,{h-10} 0,{h/2} 4,10 Z",
            TrackerTabStyle.HalfElf => $"M0,10 Q0,0 12,0 H{w-10} L{w},10 V{h-7} L{w-7},{h} H10 Q0,{h} 0,{h-10} Z",
            TrackerTabStyle.Troll => $"M8,2 L{w*.36},0 {w*.67},2 {w-8},1 {w},9 {w-3},{h/2} {w-1},{h-8} {w-9},{h} {w*.42},{h-2} 7,{h} 0,{h-9} 2,11 Z",
            TrackerTabStyle.Ogre => $"M5,0 H{w-5} L{w},7 {w-8},{h-4} {w-13},{h} H13 L8,{h-4} 0,7 Z",
            TrackerTabStyle.Halfling => $"M0,11 Q0,3 14,3 H{w-14} Q{w},3 {w},11 V{h-11} Q{w},{h-3} {w-14},{h-3} H14 Q0,{h-3} 0,{h-11} Z",
            TrackerTabStyle.Gnome => $"M8,0 H{w-8} L{w-4},4 V{h/2-4} H{w} V{h/2+4} H{w-4} V{h-4} L{w-8},{h} H8 L4,{h-4} V{h/2+4} H0 V{h/2-4} H4 V4 Z",
            TrackerTabStyle.Iksar => $"M8,0 H{w-8} V4 H{w-4} V8 H{w} V{h-8} H{w-4} V{h-4} H{w-8} V{h} H8 V{h-4} H4 V{h-8} H0 V8 H4 V4 H8 Z",
            TrackerTabStyle.Froglok => $"M9,0 H{w-9} L{w-3},3 {w},9 V{h-9} L{w-3},{h-3} {w-9},{h} H9 L3,{h-3} 0,{h-9} V9 L3,3 Z",
            _ => $"M5,0 H{w-5} L{w},5 V{h-5} L{w-5},{h} H5 L0,{h-5} V5 Z"
        };
        // Geometry strings use invariant numeric formatting even on non-English systems.
        var prior=CultureInfo.CurrentCulture;
        if (prior.NumberFormat.NumberDecimalSeparator!=".") return MakeInvariant(style,w,h);
        var g=Geometry.Parse(path);g.Freeze();return g;
    }
    private static Geometry MakeInvariant(TrackerTabStyle style,double w,double h)
    { var old=CultureInfo.CurrentCulture;try { CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;return Shape(style,w,h); } finally { CultureInfo.CurrentCulture=old; } }
    protected override void OnRender(DrawingContext dc)
    {
        if (LeadingGap > 0) dc.DrawLine(new Pen(UI.Brush("Edge"),1),new Point(LeadingGap/2,12),new Point(LeadingGap/2,Math.Max(12,ActualHeight-12)));
        var bounds=OutlineBounds;double w=bounds.Width,h=bounds.Height;if(w<32||h<20)return;
        dc.PushTransform(new TranslateTransform(bounds.X,bounds.Y));
        var edge=new Pen(UI.Brush(Selected?"Melee":Hovered?"Accent":"Edge"),1);
        dc.DrawGeometry(UI.Brush(Selected?"Selection":"InnerSurface"),edge,Shape(StyleId,w,h));
        var ornament=new Pen(UI.Brush("Muted"),.8);
        void L(params Point[] ps) { for(int i=1;i<ps.Length;i++)dc.DrawLine(ornament,ps[i-1],ps[i]); }
        switch(StyleId)
        {
            case TrackerTabStyle.Dwarf: L(new(6,4),new(13,4));L(new(w-13,h-4),new(w-6,h-4));break;
            case TrackerTabStyle.WoodElf: case TrackerTabStyle.HalfElf: case TrackerTabStyle.Froglok: case TrackerTabStyle.Halfling: case TrackerTabStyle.Troll:
                L(new(5,Math.Min(18,h-5)),new(8,9),new(18,6));L(new(8,10),new(13,7),new(10,11));L(new(w-18,h-6),new(w-8,h-9),new(w-5,Math.Max(5,h-18)));break;
            case TrackerTabStyle.Human: L(new(12,5),new(w-12,5)); foreach(double x in new[]{8d,w-8})L(new(x-2,h/2-2),new(x+2,h/2-2),new(x+2,h/2+1),new(x,h/2+4),new(x-2,h/2+1),new(x-2,h/2-2));break;
            case TrackerTabStyle.Iksar: L(new(8,14),new(8,7),new(15,7));L(new(12,13),new(12,11),new(15,11));L(new(w-15,h-7),new(w-8,h-7),new(w-8,h-14));break;
            case TrackerTabStyle.HighElf: L(new(5,14),new(9,7),new(18,8));L(new(w-18,8),new(w-9,7),new(w-5,14));L(new(w/2-5,7),new(w/2,5),new(w/2+5,7));break;
            case TrackerTabStyle.Ogre: L(new(8,5),new(w-8,5));L(new(9,11),new(12,19));L(new(w-9,11),new(w-12,19));break;
            case TrackerTabStyle.Gnome: dc.DrawEllipse(null,ornament,new(10,8),2,2);dc.DrawEllipse(null,ornament,new(w-10,h-8),2,2);L(new(15,5),new(w-15,5));break;
            case TrackerTabStyle.Barbarian: L(new(8,13),new(12,9),new(8,5),new(15,5));L(new(w-8,h-13),new(w-12,h-9),new(w-8,h-5),new(w-15,h-5));break;
            default: L(new(10,15),new(10,8),new(18,8));L(new(w-18,h-8),new(w-10,h-8),new(w-10,h-15));break;
        }
        if(Selected)dc.DrawLine(new Pen(UI.Brush("Melee"),1.6),new(16,h-1),new(w-16,h-1));
        if(ShowInsideMarks)foreach(double y in new[]{4d,h-4})foreach(double x in new[]{w/2-14,w/2-3,w/2+8})dc.DrawLine(new Pen(UI.Brush("SecondaryText"),1.4),new(x,y),new(x+6,y));
        string nextColor=Selected?"Melee":Hovered||Focused?"Foreground":"SecondaryText";if(labelColor!=nextColor){labelColor=nextColor;label.SetResourceReference(TextBlock.ForegroundProperty,nextColor);}
        dc.Pop();
    }
    internal static void Install(TabControl tabs)
    {
        var style=new Style(typeof(TabItem));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));style.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0)));style.Setters.Add(new Setter(Control.FocusVisualStyleProperty,null));
        style.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(TabItem)){VisualTree=new FrameworkElementFactory(typeof(TrackerTabChrome))}));tabs.Resources[typeof(TabItem)]=style;
    }
}
