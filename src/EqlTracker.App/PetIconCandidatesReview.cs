using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class PetIconCandidate
{
    public string Family {get;set;}="";public string Variant {get;set;}="";public string Status {get;set;}="Review";public int Round {get;set;}
    public string File {get;set;}="";
    public string Role {get;set;}="";public string Name {get;set;}="";public string Feature {get;set;}="";public string Color {get;set;}="";public string Highlight {get;set;}="";public string Shape {get;set;}="";public string Detail {get;set;}="";public string Mark {get;set;}="";
}
/// <summary>Diagnostic-only art: never registered with the live identity/artwork resolver.</summary>
internal sealed class PetIconCandidateView(PetIconCandidate art,string folder,bool vibrant,bool silhouette=false):FrameworkElement
{
    private static readonly Dictionary<(string,int),BitmapImage> decoded=[];
    internal static int DevicePixels(double dip,double dpi)=>(int)Math.Ceiling(dip*dpi);
    internal static bool Detailed(double dip,bool vibrant)=>ApprovedPetArtwork.Detailed(dip,vibrant?IconArtworkStyle.Vibrant:IconArtworkStyle.Clean);
    protected override void OnRender(DrawingContext dc)
    {
        double size=Math.Min(ActualWidth,ActualHeight);if(size<=0)return;
        if(Detailed(size,vibrant)&&!silhouette)
        {
            string path=Path.Combine(folder,art.File.Length>0?art.File:art.Role+".png");int pixels=DevicePixels(size,VisualTreeHelper.GetDpi(this).DpiScaleX);
            if(!decoded.TryGetValue((path,pixels),out var image)){image=new();image.BeginInit();image.UriSource=new(path);image.DecodePixelWidth=pixels;image.CacheOption=BitmapCacheOption.OnLoad;image.EndInit();image.Freeze();decoded[(path,pixels)]=image;}
            dc.DrawImage(image,new(0,0,size,size));return;
        }
        double canvas=art.Round>=4?27:26;dc.PushTransform(new ScaleTransform(size/canvas,size/canvas));dc.PushTransform(new TranslateTransform(1,1));
        Brush Fill(string color)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        dc.DrawGeometry(Fill(silhouette?"#E4E9EF":vibrant?art.Color:"#D8E4EB"),silhouette?null:new Pen(Fill("#07131E"),1.15){LineJoin=PenLineJoin.Round},Geometry.Parse(art.Shape));
        if(!silhouette){dc.PushOpacity(.8);dc.DrawGeometry(Fill("#122A3B"),null,Geometry.Parse(art.Detail));dc.Pop();dc.DrawGeometry(Fill(vibrant?art.Highlight:"#FFFFFF"),null,Geometry.Parse(art.Mark));}
        dc.Pop();dc.Pop();
    }
}
public sealed partial class OverlayWindow
{
    internal Task ReviewPetIconCandidates(string folder)
    {
        string[] args=Environment.GetCommandLineArgs();int argument=Array.IndexOf(args,"--icon-candidates");
        if(argument<0)throw new InvalidOperationException("Pass the review-only --icon-candidates directory");
        string assets=Path.GetFullPath(args[argument+1]);var choices=Json.Read<List<PetIconCandidate>>(Path.Combine(assets,"vectors.json"));
        if(choices.Select(c=>c.Role).Distinct().Count()!=choices.Count||PetDefinition.All.Any(p=>!choices.Any(a=>a.Family==p.Type))||!choices.Any(c=>c.Family=="Unresolved"))throw new InvalidOperationException("Missing family concept or duplicate variant identity");
        var button=titleDisplaySelector!;var original=button.Content;var selector=PetResearch.Hash(Controller.Profile.OverlayMeter!);var footer=PetResearch.Hash(Controller.Profile.OverlayFooterDisplay!);var files=new List<string>();var watch=System.Diagnostics.Stopwatch.StartNew();
        Opacity=0;Left=Top=-15000;ShowActivated=false;Show();SetMeterSize(300,260);Refresh();
        void Flush(){UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        BitmapSource ControlImage(int dip,PetIconCandidate candidate,bool vibrant,double scale=1)
        {
            button.Content=new PetIconCandidateView(candidate,assets,vibrant){Width=dip,Height=dip};HeaderIcon.Width=button.Width=dip+4;HeaderIcon.Height=button.Height=dip+4;Flush();
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawRectangle(new VisualBrush(button){Stretch=Stretch.Fill},null,new(0,0,dip+4,dip+4));
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling((dip+4)*scale),(int)Math.Ceiling((dip+4)*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
        }
        void Save(string name,FrameworkElement content,double width,double height)
        {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(content,width,height,1)));using var stream=File.Create(Path.Combine(folder,name+".png"));encoder.Save(stream);files.Add(name+".png");}
        foreach(string theme in new[]{"coldain","sunstrider","shadowman"})foreach(bool vibrant in new[]{false,true})foreach(var family in choices.GroupBy(c=>c.Family))
        {
            Appearance.Apply(theme);var board=new StackPanel{Background=UI.Brush("SolidSurface")};board.Children.Add(UI.Text(theme+" · "+(vibrant?"Vibrant":"Clean")+" · real DPS selector",14,"Accent",true));
            var heading=new StackPanel{Orientation=Orientation.Horizontal};heading.Children.Add(new TextBlock{Text="Icon DIP",Width=120,Foreground=UI.Brush("Foreground"),FontSize=11});foreach(int dip in new[]{13,14,16,20,24,32,64})heading.Children.Add(new TextBlock{Text=dip.ToString(),Width=dip==64?88:dip==32?64:48,TextAlignment=TextAlignment.Center,Foreground=UI.Brush("Foreground"),FontSize=11});board.Children.Add(heading);
            foreach(var choice in family)
            {
                var row=new StackPanel{Orientation=Orientation.Horizontal,Height=80};row.Children.Add(new TextBlock{Text=choice.Name+"\n"+choice.Status,Width=120,VerticalAlignment=VerticalAlignment.Center,Foreground=UI.Brush("Foreground"),FontSize=12});
                foreach(int dip in new[]{13,14,16,20,24,32,64}){var image=new Image{Source=ControlImage(dip,choice,vibrant,2),Width=dip+4,Height=dip+4,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(new Border{Child=image,Width=dip==64?88:dip==32?64:48,VerticalAlignment=VerticalAlignment.Center});}board.Children.Add(row);
            }
            Save("pet-icons-"+family.Key.ToLowerInvariant()+"-"+theme+"-"+(vibrant?"vibrant":"clean"),board,620,50+family.Count()*80);
        }
        foreach(bool silhouette in new[]{false,true})foreach(var family in choices.GroupBy(c=>c.Family))
        {
            var board=new StackPanel{Background=UI.Brush("SolidSurface")};board.Children.Add(UI.Text(silhouette?"Silhouette comparisons":"Grayscale / no color dependence",14,"Accent",true));
            foreach(var art in family){var row=new StackPanel{Orientation=Orientation.Horizontal,Height=76};row.Children.Add(new TextBlock{Text=art.Name,Width=120,FontSize=12,Foreground=UI.Brush("Foreground"),VerticalAlignment=VerticalAlignment.Center});foreach(int size in new[]{13,14,16,20,24,32,64})row.Children.Add(new Border{Width=size==64?80:54,Child=new PetIconCandidateView(art,assets,false,silhouette){Width=size,Height=size,VerticalAlignment=VerticalAlignment.Center}});board.Children.Add(row);}Save("pet-icons-"+family.Key.ToLowerInvariant()+"-"+(silhouette?"silhouettes":"grayscale"),board,640,35+family.Count()*76);
        }
        var samples=new List<object>();foreach(double dpi in new[]{1d,1.5,2d,3d})foreach(int dip in new[]{13,14,16,20,24,32,64})samples.Add(new{DIP=dip,DPI=dpi,DevicePixels=PetIconCandidateView.DevicePixels(dip,dpi),Detailed=PetIconCandidateView.Detailed(dip,true)});
        button.Content=original;button.Width=button.Height=double.NaN;SizeTitleDisplaySelector(false);Refresh();Hide();
        if(selector!=PetResearch.Hash(Controller.Profile.OverlayMeter!)||footer!=PetResearch.Hash(Controller.Profile.OverlayFooterDisplay!))throw new InvalidOperationException("Review changed a DPS selection");
        Json.Write(Path.Combine(folder,"pet-icon-review-checks.json"),new{Passed=true,Families=choices.GroupBy(c=>c.Family).Select(g=>new{Family=g.Key,Variants=g.Select(c=>new{c.Variant,c.Status,c.Round})}),Generic=true,NewConcepts=choices.Count(c=>c.Status=="Review"),ProductionArtwork=choices.Where(c=>c.Status is "Default" or "Settings alternative" or "In use").Select(c=>new{c.Role,c.Family,c.Status}),SelectorsUnchanged=true,Files=files,SizePolicy=samples,Milliseconds=watch.Elapsed.TotalMilliseconds,Scope="Metadata-driven family boards. Candidate content temporarily inserted in actual WPF DPS selector in synthetic preview; 2x render samples shown at DIP sizes. Vibrant uses DPI-sized paintings at every size; Clean and silhouette studies retain vectors."});return Task.CompletedTask;
    }
}
