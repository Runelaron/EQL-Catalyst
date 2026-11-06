using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class EqFontReview
{
    internal static void Run(string folder,TrackerController controller,QuestWindow tracker,DpsWindow lab)
    {
        if(!controller.Demo)throw new InvalidOperationException("Synthetic review only");
        Directory.CreateDirectory(folder);var checks=new List<string>();var samples=new List<object>();
        void Check(bool value,string name)
        {
            if(!value){Json.Write(Path.Combine(folder,"font-checks.json"),new{Passed=false,Checks=checks,Failed=name});throw new InvalidOperationException(name);}
            checks.Add(name);
        }
        void Flush(Window window){window.UpdateLayout();window.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);window.UpdateLayout();}
        string originalHeading=controller.HeadingStyle;controller.SetHeadingStyle("quest");CatalystHeading.Use("quest");
        bool resolved=new Typeface(CatalystHeading.DisplayFont,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal).TryGetGlyphTypeface(out var glyph);
        Check(resolved&&glyph.FamilyNames.Values.Contains("EQL Quest Display"),"Pack resource resolves the reconstructed font without system installation or fallback");
        foreach(int code in Enumerable.Range(32,95).Concat(new[]{0xA0,0x2010,0x2011,0x2013,0x2014,0x2018,0x2019,0x201C,0x201D,0x2026,0x2212}))
        {
            Check(glyph.CharacterToGlyphMap.TryGetValue(code,out ushort index)&&index!=0&&glyph.AdvanceWidths[index]>0,$"Mapped glyph and positive advance U+{code:X4}");
            if(code!=32&&code!=0xA0)Check(!glyph.GetGlyphOutline(index,100,100).Bounds.IsEmpty,$"Nonempty outline U+{code:X4}");
        }
        controller.Profile.SmoothMeterMotion=false;
        var overview=new OverviewWindow(controller);var settings=new SettingsWindow(controller);
        var bar=new WorkspaceToolbarWindow(controller,id=>id switch{"Tracker"=>tracker,"CombatLab"=>lab,_=>overview},()=>{},new());
        foreach(var window in new FrostWindow[]{tracker,lab,overview,settings})
        {
            if(window.Placement is {} placement)placement.ReviewEnabled=true;
            window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Left=window.Top=-15000;
        }
        bar.Opacity=0;bar.ShowActivated=false;bar.ShowInTaskbar=false;bar.Left=bar.Top=-15000;bar.Show();
        foreach(var theme in ThemeCatalog.Choices.Skip(1))
        {
            Appearance.Apply(theme.Id);
            foreach(var (id,window) in new[]{("Tracker",(FrostWindow)tracker),("CombatLab",lab),("Overview",overview),("Settings",settings)})
            {
                if(id=="Settings")window.Show();else bar.Open(id);
                foreach(var size in new[]{(Name:"normal",Width:915d,Height:650d),(Name:"minimum-short",Width:640d,Height:420d)})
                {
                    window.Width=size.Width;window.Height=size.Height;
                    window.Subtitle.Text="ExampleHeroWithALongName · Nektropos Castle · Conjuration";window.Subtitle.Visibility=Visibility.Visible;Flush(window);
                    var runs=window.HeadingTitle.Inlines.OfType<Run>().ToArray();
                    Check(runs.All(r=>r.FontFamily!=CatalystHeading.DisplayFont),$"Actual {id} title preserves separate Classic branding/{theme.Id}/{size.Name}");
                    Check(window.HeadingTitle.ActualWidth>0&&window.HeadingTitle.ActualHeight>=17,$"Title keeps visible native bounds/{id}/{theme.Id}/{size.Name}");
                    Check(runs.Where(r=>r.FontFamily==CatalystHeading.DisplayFont).All(r=>r.FontWeight==FontWeights.Normal),$"Supplied regular strokes avoid synthetic bold/{id}/{theme.Id}/{size.Name}");
                    if(id!="Settings")
                    {
                        var header=(Visual)window.Content;
                        var title=window.HeadingTitle.TransformToAncestor(header).TransformBounds(new(window.HeadingTitle.RenderSize));
                        var context=window.Subtitle.TransformToAncestor(header).TransformBounds(new(window.Subtitle.RenderSize));
                        Check(context.Top>=title.Bottom-.5||context.Left>=title.Right+8,$"Long context clears title/{id}/{theme.Id}/{size.Name}");
                    }
                    foreach(double scale in new[]{1d,1.5,2d,3d})
                    {
                        var bitmap=PreviewRenderer.Capture((FrameworkElement)window.Content,size.Width,size.Height,scale);
                        var crop=new CroppedBitmap(bitmap,new(0,0,bitmap.PixelWidth,Math.Min(bitmap.PixelHeight,(int)Math.Ceiling(100*scale))));
                        string name=$"{theme.Id}-{id}-{size.Name}-{scale:0.0}x.png";
                        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(crop));using(var output=File.Create(Path.Combine(folder,name)))png.Save(output);
                        samples.Add(new{File=name,Theme=theme.Id,Surface=id,Width=size.Width,Height=size.Height,Scale=scale});
                    }
                }
                if(id=="Settings")window.Hide();
            }
        }
        var compact=UI.Text("");CatalystHeading.Apply(compact,"Combat Lab");compact.FontSize=11;
        compact.Measure(new(640,100));compact.Arrange(new(0,0,640,compact.DesiredSize.Height));
        Check(compact.DesiredSize.Height<=24,"Collapsed heading fits the existing compact title row");
        Check(((Run)compact.Inlines.FirstInline!).FontSize<14,"Collapsed emblem follows the existing font-size binding");
        bar.TryHide();settings.Close();overview.Close();controller.SetHeadingStyle(originalHeading);CatalystHeading.Use(originalHeading);
        Json.Write(Path.Combine(folder,"font-checks.json"),new{Passed=true,Checks=checks,Samples=samples,SyntheticRendering=true,PhysicalDpiAcceptance=false});
    }
}
