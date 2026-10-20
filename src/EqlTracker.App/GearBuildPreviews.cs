using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class GearBuildPreviews
{
    internal static void Render(string folder,TrackerController c,QuestWindow q)
    {
        int index=c.Profile.Loadouts.FindIndex(p=>p.Name==c.Profile.SelectedLoadout);var saved=EquipmentPositions.Copy(c.Profile.ActiveLoadout);
        void Flush(){q.Refresh();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);q.UpdateLayout();}
        void Save(string name,FrameworkElement view,double width,double height,double scale=1)
        {var bitmap=PreviewRenderer.Capture(view,width,height,scale);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=System.IO.File.Create(System.IO.Path.Combine(folder,name));encoder.Save(file);}
        try
        {
            GearBuildFixtures.Apply(c);q.SetGearTarget(false);q.ShowView(TrackerTab.Gear,"Red Dragonscale Armor");q.SelectGearPosition("CHEST:1");Flush();
            Save("gear-upgrades.png",(FrameworkElement)q.Content,1220,690);
            foreach(double dpi in new[]{1d,1.5,2d,3d})Save($"qa/gear-upgrades-{dpi:0.0}x.png",(FrameworkElement)q.Content,940,610,dpi);
            c.SeedStatEvidencePreview();var stat=new StatProfileDialog(c,c.Profile.ActiveLoadout,false,c.Profile.ActiveLoadout.EquipmentPositions,()=>{}){Left=-15000,Top=-15000};stat.Show();
            Save("gear-stat-profiles.png",(FrameworkElement)stat.Content,900,650);
            var statTabs=Usability.Tree(stat).OfType<TabControl>().First();statTabs.SelectedIndex=2;stat.UpdateLayout();Save("stat-profile-prefill.png",(FrameworkElement)stat.Content,900,650);statTabs.SelectedIndex=3;stat.UpdateLayout();Save("stat-profile-evidence.png",(FrameworkElement)stat.Content,900,650);statTabs.SelectedIndex=0;
            foreach(double dpi in new[]{1d,1.5,2d,3d})Save($"qa/gear-stat-profiles-{dpi:0.0}x.png",(FrameworkElement)stat.Content,900,650,dpi);stat.Close();
            q.ShowView(TrackerTab.Pet,"Bloodmoon");q.SelectPetDestination(PetInventoryDestination.Planned);Flush();q.SelectPetInventoryPosition(3);Flush();
            var candidate=Usability.Tree(q).OfType<DataGrid>().Single(g=>g.IsVisible && g.Columns.Any(col=>col.Header?.ToString()=="Slots"));candidate.SelectedIndex=0;Flush();
            Save("pet-guided-build.png",(FrameworkElement)q.Content,1220,690);
            foreach(double dpi in new[]{1d,1.5,2d,3d})Save($"qa/pet-guided-build-{dpi:0.0}x.png",(FrameworkElement)q.Content,940,610,dpi);
            foreach(double dpi in new[]{1d,1.5,2d,3d})Save($"qa/pet-guided-minimum-{dpi:0.0}x.png",(FrameworkElement)q.Content,640,420,dpi);
        }
        finally{c.Profile.Loadouts[index]=saved;c.Save();q.ShowView(TrackerTab.Items);}
    }
}
