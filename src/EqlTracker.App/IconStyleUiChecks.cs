using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class IconStyleUiChecks
{
    internal static void Run(TrackerController controller,QuestWindow quest,Action<bool,string> check,string folder)
    {
        var p=controller.Profile;var beforeStyle=p.IconStyle;var beforeEmblem=p.DamageEmblem;string beforeIcon=p.AppIconId;
        var settings=new SettingsWindow(controller){Left=-15000,Top=-15000,ShowActivated=false};settings.Show();
        void Flush(){Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);settings.UpdateLayout();}
        void Save(string name,FrameworkElement content,double width,double height,double scale){var image=PreviewRenderer.Capture(content,width,height,scale);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(folder,"qa",name));encoder.Save(file);}
        string Digest(FrameworkElement content){var image=PreviewRenderer.Capture(content,32,32,1);var pixels=new byte[32*32*4];image.CopyPixels(pixels,32*4,0);return Convert.ToHexString(SHA256.HashData(pixels));}
        Directory.CreateDirectory(Path.Combine(folder,"qa"));
        try
        {
            var style=Usability.Tree(settings).OfType<ComboBox>().Single(c=>c.Items.Contains("Clean")&&c.Items.Contains("Vibrant"));
            var glyphs=new[]{"Healing","Physical","Sword","Shield","MartialKick","Kick","Fist","Dagger","Spear","Mace","Bow","Fang","Claw","Axe","Magic","Cold","Fire","Disease","Poison","Corruption","Unknown"};
            var roles=CatalystBranding.Choices.Select(c=>IconArtwork.AppRole(c.Id)).Concat(glyphs.Select(IconArtwork.GlyphRole)).Concat(new[]{"actor-self","actor-total","pet-generic","pet-earth","pet-air","pet-fire","pet-water"}).Concat(IconArtwork.DamageChoices.Select(c=>c.Role)).Distinct().ToArray();
            check(roles.Length==52,"Painted runtime mapping covers 47 roles plus five Damage alternatives");
            foreach(string role in roles){var image=IconArtwork.Painted(role);check(image is{IsFrozen:true,PixelWidth:256},"Frozen painted resource loads: "+role);}
            check(IconArtwork.Painted("missing-artwork-test")==null,"Missing painted resource permits the matching Clean fallback");
            foreach(var choice in IconArtwork.DamageChoices)foreach(double scale in new[]{1d,1.5,2d,3d}){var a=IconArtwork.Painted(choice.Role,14*scale);check(a!=null&&a.PixelWidth>=14*scale&&ReferenceEquals(a,IconArtwork.Painted(choice.Role,14*scale)),choice.Label+" uses a cached DPI-sized image at "+scale);}
            check(IconArtwork.DamageChoices.Select(c=>c.Number).SequenceEqual(new[]{1,3,5,7,9,11}),"Only the six crossing-diagonal Damage emblems are selectable");
            var gallery=Usability.Tree(settings).OfType<Expander>().Single(e=>e.Header?.ToString()?.StartsWith("Damage emblem")==true);gallery.IsExpanded=true;Flush();
            var buttons=Usability.Tree(gallery).OfType<Button>().Where(b=>b.Tag is DamageEmblem).ToArray();
            check(buttons.Length==6,"Settings exposes six visual Damage-emblem tiles");
            foreach(var button in buttons)
            {
                var choice=IconArtwork.DamageChoices.Single(c=>c.Id==(DamageEmblem)button.Tag);
                check(Usability.Tree(button).OfType<DamageEmblemPreview>().Count()==2&&Usability.Tree(button).OfType<TextBlock>().Any(t=>t.Text==choice.Label),choice.Label+" renders artwork and its readable label inside the raised button");
            }
            settings.SelectSection(SettingsSection.Sync);Flush();
            var folderInput=Usability.Tree(settings).OfType<TextBox>().Single(t=>t.Text==p.GameFolder);folderInput.Text="Z:\\missing-unrelated-game-folder";
            settings.SelectSection(SettingsSection.Appearance);Flush();
            foreach(var value in Enum.GetValues<IconArtworkStyle>())
            {
                style.SelectedItem=value.ToString();Flush();
                check(p.IconStyle==value&&IconArtwork.Style==value&&p.AppIconId==beforeIcon,"Icon style changes immediately without validating the game-folder field or changing app identity: "+value);
                check(Application.Current.Windows.Cast<Window>().All(w=>ReferenceEquals(w.Icon,CatalystBranding.Image(p.AppIconId))),"All open window icons follow "+value);
                var digests=new List<string>();
                foreach(var choice in IconArtwork.DamageChoices)
                {
                    var button=buttons.Single(b=>b.Tag is DamageEmblem id&&id==choice.Id);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush();
                    check(p.DamageEmblem==choice.Id&&IconArtwork.Emblem==choice.Id&&Usability.GetIsPressed(button),choice.Label+" applies and stays selected in "+value);
                    digests.Add(Digest(new DamageEmblemPreview()));
                }
                check(digests.Distinct().Count()==6,"All six "+value+" Damage compositions render distinctly");
                p.DamageEmblem=DamageEmblem.Fire;IconArtwork.Apply(p);settings.RefreshIconPresentation();gallery.IsExpanded=false;Flush();
                foreach(double scale in new[]{1d,1.5,2d,3d})Save($"icon-settings-{value.ToString().ToLowerInvariant()}-{scale:0.0}x.png",(FrameworkElement)settings.Content,940,610,scale);
                gallery.IsExpanded=true;gallery.BringIntoView();Flush();
                foreach(double scale in new[]{1d,1.5,2d,3d})Save($"icon-emblems-{value.ToString().ToLowerInvariant()}-{scale:0.0}x.png",(FrameworkElement)settings.Content,940,610,scale);
            }
            var sheet=UI.Stack(UI.Text("Clean / Vibrant · six crossing-diagonal Damage emblems",18,"Melee",true));sheet.Background=UI.Brush("SolidSurface");
            foreach(var choice in IconArtwork.DamageChoices){var row=UI.Row(UI.Text(choice.Label,12),new DamageEmblemPreview{StyleOverride=IconArtworkStyle.Clean,Emblem=choice.Id,Width=48,Height=48},new DamageEmblemPreview{StyleOverride=IconArtworkStyle.Vibrant,Emblem=choice.Id,Width=48,Height=48});row.Margin=new(8);sheet.Children.Add(row);}
            Save("icon-style-comparison.png",sheet,690,470,2);
            quest.CheckPetExpansionFilter(check);
            quest.ShowView(TrackerTab.Pet,"Indicolite");quest.Refresh();Flush();
            foreach(double scale in new[]{1d,1.5,2d,3d})Save($"pet-expansion-{scale:0.0}x.png",(FrameworkElement)quest.Content,1220,800,scale);
        }
        finally{settings.Close();p.IconStyle=beforeStyle;p.DamageEmblem=beforeEmblem;p.AppIconId=beforeIcon;((App)Application.Current).ApplySettings();}
    }
}

public sealed partial class QuestWindow
{
    internal void CheckPetExpansionFilter(Action<bool,string> check)
    {
        var p=Controller.Profile;var saved=p.PetExpansions.ToList();string gear=ItemExpansionFilter.Key(p.GearExpansions),query=petSearch.Text,selected=p.ActiveLoadout.SelectedPetBuildId;
        string? ownership=petOwnership.SelectedItem?.ToString(),slot=petSlot.SelectedItem?.ToString();PetBuild? temporary=null;
        void Flush(){RefreshPet();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        try
        {
            ShowView(TrackerTab.Pet);SelectPetDestination(PetInventoryDestination.Confirmed);petSearch.Text="";petOwnership.SelectedItem="All items";petSlot.SelectedItem="All slots";p.PetExpansions=ItemExpansionFilter.Choices.ToList();petKey="";Flush();
            var menu=Usability.Tree(this).OfType<Button>().Single(b=>b.Content?.ToString()?.StartsWith("Item expansion ·")==true).ContextMenu!;
            check(menu.Items.OfType<MenuItem>().All(m=>m.IsCheckable&&m.StaysOpenOnClick),"Pet expansion choices remain checked in a conventional multi-select menu");
            string inventory=System.Text.Json.JsonSerializer.Serialize(SelectedPetInventory,Json.Options);
            void Select(params string[] names){foreach(var m in menu.Items.OfType<MenuItem>()){bool on=names.Contains(m.Header.ToString());if(m.IsChecked!=on){m.IsChecked=on;m.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));}}petKey="";Flush();}
            Select("Kunark");check(petResults.Items.OfType<PetSearchRow>().All(r=>ItemExpansionFilter.Identity(r.Item.IntroducedExpansion)=="Kunark"),"Pet single expansion excludes earlier/later/unknown candidates");
            Select("Classic","Velious");check(petResults.Items.OfType<PetSearchRow>().All(r=>r.Item.IntroducedExpansion is "Classic" or "Velious"),"Pet multi-select includes exact expansions only");
            Select();check(petResults.Items.Count==0&&petCandidateFilterNote.Text.Contains("Select an item expansion"),"Empty Pet expansion selection shows an actionable empty state");
            Select("Unknown");check(petResults.Items.OfType<PetSearchRow>().All(r=>ItemExpansionFilter.Identity(r.Item.IntroducedExpansion)=="Unknown"),"Unknown pet metadata is kept separate from known expansions");
            check(System.Text.Json.JsonSerializer.Serialize(SelectedPetInventory,Json.Options)==inventory&&gear==ItemExpansionFilter.Key(p.GearExpansions),"Candidate expansion filtering leaves Current inventory and Gear filters unchanged");
            temporary=EquipmentPositions.Copy(SelectedPetInventory!);temporary.Id=Guid.NewGuid().ToString("N");temporary.Name="Expansion filter proof";p.ActiveLoadout.PetBuilds.Add(temporary);RefreshBuildChoices(temporary.Id);petKey="";Flush();
            string plan=System.Text.Json.JsonSerializer.Serialize(temporary,Json.Options);Select("Velious");check(petResults.Items.OfType<PetSearchRow>().All(r=>r.Item.IntroducedExpansion=="Velious")&&System.Text.Json.JsonSerializer.Serialize(temporary,Json.Options)==plan,"Planned-build candidate expansion filtering does not edit its contents");
            Select(ItemExpansionFilter.Choices.ToArray());
        }
        finally{if(temporary!=null)p.ActiveLoadout.PetBuilds.Remove(temporary);p.PetExpansions=saved;petSearch.Text=query;petOwnership.SelectedItem=ownership;petSlot.SelectedItem=slot;RefreshBuildChoices(selected);petKey="";Flush();}
    }
}
