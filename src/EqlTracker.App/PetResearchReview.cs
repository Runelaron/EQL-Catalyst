using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed partial class PetResearchWindow
{
    internal void Review(string folder, Action<bool,string> check)
    {
        Refresh(); check(samples.Items.Count > 0 && sheets.Items.Count > 0, "Local samples and reviewed stat sheets are visible");
        samples.SelectedIndex = 0; check(sampleDetail.Text.Contains("damage /") && sampleDetail.Text.Contains("Buff-period damage"), "Sample evidence separates totals and buff periods");
        sampleJson.Text = sampleJson.Text.Replace("Rear", "Front"); string draft = sampleJson.Text;
        controller.EditPetResearch(_ => { }); Refresh(); check(sampleJson.Text == draft, "Background arrival preserves unsaved context edits");
        sheets.SelectedIndex = 0; var sheet = JsonSerializer.Deserialize<PetStatSheet>(sheetJson.Text,Json.Options)!;
        ((List<StatEntry>)statFields.ItemsSource).Single(x=>x.Field=="HP").Value="-1"; SaveSheet();
        var saved = controller.PetResearchSnapshot().Sheets.Single(s => s.Id == sheet.Id);
        check(saved.Values["HP"] == null && saved.InvalidValues["HP"] == -1 && saved.Values["ResistMagic"] == null, "Reviewed entry retains invalid HP separately and unknown resists");
        foreach (var (width,height,label) in new[] { (820d,780d,"normal"),(640d,650d,"minimum") })
        {
            Width = width; Height = height; UpdateLayout(); Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            var tabs = Usability.Tree((DependencyObject)Content).OfType<TabControl>().Single();
            foreach (var (index,name) in new[] { (0,"samples"),(1,"stats"),(2,"models") })
            {
                tabs.SelectedIndex = index; UpdateLayout();
                check(tabs.IsVisible && tabs.ActualWidth <= ActualWidth, "Research tab fits " + name + " at " + label);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture((FrameworkElement)Content,width,height,1)));
                using var file = File.Create(Path.Combine(folder,"research-"+name+"-"+label+".png")); encoder.Save(file);
            }
        }
        check(controller.PetResearchSnapshot().Models.All(m=>m.Gate!="Validated locally"), "Unvalidated models never become estimates");
    }
}
public sealed partial class QuestWindow
{
    internal async Task ReviewPetResearch(string folder)
    {
        if (!Controller.Demo) throw new InvalidOperationException("Synthetic review only");
        Directory.CreateDirectory(folder); var checks = new List<string>();
        void Check(bool ok,string name) { if (!ok) throw new InvalidOperationException(name); checks.Add(name); }
        GearBuildFixtures.Apply(Controller); ShowView(TrackerTab.Pet); RefreshBuildChoices("current"); RefreshPet(); UpdateLayout();
        Check(Usability.Tree((DependencyObject)Content).OfType<Button>().Any(b=>Equals(b.Content,"Pet data…")) == TrackerController.PetResearchEnabled, "Explicit local flag gates research toolbar");
        if (TrackerController.PetResearchEnabled)
        {
            Controller.EditPetResearch(state => {
                PetResearch.Collect(state,Controller.Combat.Encounters,Controller.Profile);
                state.Sheets.Add(new() { Label="Synthetic Air sheet", Reviewed=true, Context=new() { Pet="Air", Rank=10, Level=50 }, Values=PetResearch.StatFields.ToDictionary(k=>k,k=>(double?)null) });
                state.Models.Add(new());
            });
            var window = new PetResearchWindow(Controller) { Owner=this, Left=-15000, Top=-15000, ShowActivated=false, Opacity=0 };
            window.Show(); window.Review(folder,Check); window.Close();
            var row = PetComparisonEngine.Evaluate(Controller.Profile,Controller.Profile.ActiveLoadout.ConfirmedPetInventory!,Controller.Catalog,PetContextCatalog.Load(Path.Combine(AppContext.BaseDirectory,"data","pet-context.json")),PetBuffCatalog.References(),[],PetEquipmentMode.IntendedSlots,CancellationToken.None).FirstOrDefault();
            Check(row != null,"Synthetic comparison remains available");
            if(row != null) { row.Research=PetResearch.Summary(Controller.PetResearchSnapshot(),row.Pet); var card=new PetResultCard { DataContext=row }; Check(Usability.Tree(card).OfType<TextBlock>().Any(t=>t.Text.StartsWith("Measured DPS:")),"Comparison card leads with measured performance"); }
            // The existing background publisher performs the automatic snapshot backfill.
            Controller.StartProjectionValidation(); await Task.Delay(700);
            Check(Controller.PetResearchSnapshot().Samples.Count > 0,"Revisioned background publication collects samples automatically");
        }
        Json.Write(Path.Combine(folder,"pet-research-checks.json"),new { Passed=checks.Count,Checks=checks,LocalResearchEnabled=TrackerController.PetResearchEnabled,MechanicsVersion=PetDefinition.Version,AppSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),CoreSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CharacterProfile).Assembly.Location))) });
    }
}
