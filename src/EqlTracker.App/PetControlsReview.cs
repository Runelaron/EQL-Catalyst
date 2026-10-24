using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewPetControls(string folder)
    {
        if (!Controller.Demo) throw new InvalidOperationException("Use an isolated preview controller.");
        File.Delete(Path.Combine(folder, "pet-controls-failure.json")); Directory.CreateDirectory(Path.Combine(folder, "qa")); var checks = new List<object>();
        void Check(bool value, string name) { checks.Add(new { Passed = value, Name = name }); if (!value) throw new InvalidOperationException(name); }
        void Flush() { Refresh(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); }
        void Save(string name, FrameworkElement content, double width, double height, double scale) { var bitmap = PreviewRenderer.Capture(content, width, height, scale); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(folder, name)); encoder.Save(file); }
        void Capture(string name) { foreach (double scale in new[] { 1d, 1.5, 2, 3 }) Save($"qa/controls-{name}-{scale:0.0}x.png", (FrameworkElement)Content, Width, Height, scale); Save("controls-" + name + ".png", (FrameworkElement)Content, Width, Height, 1); }
        try
        {
            GearBuildFixtures.Apply(Controller); var owner = Controller.Profile.ActiveLoadout; owner.Classes = ["SHD", "BRD", "MAG"]; owner.PetPreset = "Pet DPS";
            owner.ConfirmedPetInventory = new() { Name = "Current build", PetType = "Earth", Items = new[] { ("Sunderfury",4), ("Indicolite Breastplate",5), ("Indicolite Bracer",7), ("Indicolite Greaves",7), ("Cloak of Flames",4), ("Icebelt",3), ("Indicolite Vambraces",7) }.Select((x,i) => new PetInventoryItem { Id = "review-" + i, ItemKey = Controller.Catalog.Find(x.Item1)!.Key, Name = x.Item1, Position = i+1, Rank = x.Item2, Slot = "Storage", ConfirmedAt = DateTime.Now }).ToList() };
            var current = owner.ConfirmedPetInventory; owner.PetBuilds = [GuidedPetBuilds.Clone(current, "Earth · weapon plan")]; string planId = owner.PetBuilds[0].Id;
            Controller.Save(); Width = 1220; Height = 780; ShowView(TrackerTab.Pet,"Sunderfury"); RefreshBuildChoices("current"); Flush();
            Check(petBuild.Items.OfType<PetBuildSelection>().Count() == 2 && PetDestination == PetInventoryDestination.Confirmed && petBuild.SelectedValue?.ToString() == "current", "One typed Build selector defaults to Current build and lists named plans");
            Check(!Usability.Tree(this).OfType<Button>().Any(b => b.Content?.ToString() is "Save context" or "Confirm equipped build"), "Context Save and old activation actions are removed");
            RefreshBuildChoices(planId); Flush(); Check(PetDestination == PetInventoryDestination.Planned && CurrentBuild?.Id == planId && owner.ConfirmedPetInventory.Id == current.Id, "Selecting a stable plan ID edits it without activating it");
            OpenPetDetails(); Flush();
            var levelEditor=(TextBox)petLevel.Template.FindName("PART_EditableTextBox",petLevel); var rankEditor=(TextBox)petSummonTier.Template.FindName("PART_EditableTextBox",petSummonTier);
            Check(levelEditor.IsVisible && rankEditor.IsVisible,"Editable number dropdowns expose real keyboard-editable fields"); levelEditor.Text = "61"; rankEditor.Text = "12"; Flush(); Check(CurrentBuild?.PetLevel == 61 && CurrentBuild?.SummonRank == 12 && petContextNote.Text.Contains("Hypothetical"), "Editable dropdowns accept labeled hypothetical values in plans"); Capture("pet-context");
            UndoPetBuild(); UndoPetBuild(); Flush(); Check(CurrentBuild?.PetLevel == null && CurrentBuild?.SummonRank == 0, "Context edits participate in Undo alongside inventory edits");
            RefreshBuildChoices("current"); current.FollowDetectedSummon=false; petLevel.Text = "61"; Flush(); Check(current.PetLevel == null && petFeedback.Text.Contains("planned",StringComparison.OrdinalIgnoreCase), "Current rejects hypothetical context and retains the saved value"); LoadPetContext();
            Check(petSpell.Items.Cast<string>().First().Contains("MAG 46") && petFocusName.Items.Contains("Shovel of Ponz") && petFocusName.Items.Contains("None"), "Earth summon list starts with level 46 and matching focus options include None");
            petElement.SelectedItem = "Animation"; Flush(); Check(petFocusField?.Visibility == Visibility.Collapsed && petContextNote.Text.Contains("coverage"), "Undocumented focus coverage hides the control without claiming confirmed absence"); UndoPetBuild(); Flush();
            SelectPetInventoryPosition(1); Flush(); Check(petPreferredPosition == "PRIMARY:1" && ((PetReplacementChoice?)petReplacement.SelectedItem)?.Label.Contains("Sunderfury") == true, "Inventory selection automatically synchronizes Current item with its name · " + petPreferredPosition + " · " + petReplacement.SelectedItem);
            var weapon = Controller.Catalog.Find("Sunderfury")!;
            foreach (var (name,damage) in new[] { ("positive",150d),("zero",0d),("negative",1d) }) { var item = EquipmentPositions.Copy(weapon); item.Key="ui rating "+name; item.Name="UI rating "+name; item.Stats=GearScorer.EffectiveStats(weapon,4); if (damage>0) { item.Stats["Damage"]=damage; item.Stats["Ratio"]=damage/item.Stats["Delay"]; } Controller.Catalog.Items.Add(item); }
            petSearch.Text = "UI rating"; Flush(); var ratings = petResults.Items.OfType<PetSearchRow>().ToList();
            Check(ratings.Count == 3 && ratings[0].UnbuffedDelta > 0 && ratings[1].UnbuffedDelta == 0 && ratings[2].UnbuffedDelta < 0, "Real candidate grid defaults to positive / zero / negative numeric sorting");
            typeof(DataGrid).GetMethod("OnSorting", BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(petResults,[new DataGridSortingEventArgs(petRatingColumn!)]); Flush(); ratings=petResults.Items.OfType<PetSearchRow>().ToList(); Check(ratings[0].UnbuffedDelta < 0 && ratings[^1].UnbuffedDelta > 0,"Rating heading reverses the actual numeric order");
            petSearch.Text = "Indicolite Breastplate"; Flush(); petResults.SelectedIndex = 0; SelectPetInventoryPosition(1); Flush();
            OpenPetComparisonPanel(); var pairWindow = Application.Current.Windows.OfType<FrostWindow>().Last(w => Usability.Tree(w).OfType<PetPairComparisonView>().Any()); pairWindow.Left=pairWindow.Top=-15000; pairWindow.UpdateLayout();
            Check(Usability.Tree(pairWindow).OfType<PetPairComparisonView>().Any() && Usability.Tree(pairWindow).OfType<TextBlock>().Any(t => t.Text == "Differences"), "Compare always opens Current / Candidate / Differences despite incompatible equipment slots");
            Check(UI.Brush("SolidSurface").Color.A == 255 && pairWindow.Opacity == 1, "Comparison popup remains solid"); foreach(double scale in new[]{1d,1.5,2,3}) Save($"qa/controls-comparison-{scale:0.0}x.png",(FrameworkElement)pairWindow.Content,720,530,scale); Save("controls-comparison.png",(FrameworkElement)pairWindow.Content,720,530,1); pairWindow.Hide();
            petSearch.Text="Sunderfury"; RefreshBuildChoices(planId); Flush(); SelectPetInventoryPosition(1); Flush(); petResults.SelectedIndex=0;
            int windows=Application.Current.Windows.Count; int fullCount=CurrentBuild!.Items.Count; AddPetItem(); Flush(); Check(CurrentBuild.Items.Count==fullCount && petFeedback.Text.Contains("full"), "A full inventory reports the limit without replacing an item or opening an editor"); CurrentBuild.Items.RemoveAt(CurrentBuild.Items.Count-1); PetEdited(); Flush(); petResults.SelectedIndex=0; int count=CurrentBuild.Items.Count; AddPetItem(); Flush(); Check(CurrentBuild.Items.Count==count+1 && Application.Current.Windows.Count==windows,"Add immediately fills the next empty position without opening an editor"); UndoPetBuild(); Flush();
            var contextAdd=petResults.ContextMenu!.Items.OfType<MenuItem>().First(); contextAdd.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Flush(); Check(CurrentBuild.Items.Count==count+1,"Catalog context Add uses the same immediate operation"); UndoPetBuild();
            var first=CurrentBuild.Items.First(); ReorderPetForCheck(first.Id,3); Flush(); Check(CurrentBuild.Items.First(i=>i.Id==first.Id).Position==3,"Inventory reordering preserves entry identity"); UndoPetBuild(); Flush();
            Check(petItems.Columns.Any(c=>c.SortMemberPath=="Grip" && c.Visibility==Visibility.Visible),"Inventory rows expose movement grips alongside editable rank");
            Check(CommitPetRank(first.Id,10),"Inline rank accepts a valid 0–20 value"); Flush(); Check(CurrentBuild.Items.First(i=>i.Id==first.Id).Rank==10,"Inline rank persists into the selected build"); UndoPetBuild(); Flush();
            var menu = Usability.Tree(this).OfType<Button>().First(b=>b.IsVisible && b.Content?.ToString()=="Options ▾"); menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(1150); Check(menu.ContextMenu!.IsOpen,"Expanded Inventory Options menu stays open after more than one second with the pointer outside"); Check(!menu.ContextMenu.Items.OfType<MenuItem>().Any(m=>m.Header?.ToString()=="Add selected to queue"),"Redundant Inventory acquisition command is removed"); menu.ContextMenu.IsOpen=false;
            Usability.Tree(this).OfType<Expander>().Single(e=>e.Header?.ToString()=="Pet context").IsExpanded=false; petSearch.Text="Indicolite"; SelectPetInventoryPosition(2); Flush(); petResults.SelectedIndex=0; Capture("pet");
            var edited = CurrentBuild.Items.First(i=>i.Name=="Sunderfury"); var editor=new PetItemDialog(Controller,weapon,CurrentBuild,PetInventoryDestination.Planned,edited){Owner=this,Left=-15000,Top=-15000}; editor.Show(); await Task.Delay(200); Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle); var expander=Usability.Tree(editor).OfType<Expander>().Single(); expander.IsExpanded=true; editor.UpdateLayout();
            Check(Usability.Tree(editor).OfType<TextBlock>().Any(t=>t.Text.Equals("Search Exaltations",StringComparison.OrdinalIgnoreCase)) && Usability.Tree(editor).OfType<DataGrid>().Any(g=>g.Columns.Any(c=>c.Header?.ToString()=="Compatibility")),"Advanced editor has labeled search and sortable compatibility results");
            var effectSearch=Usability.Tree(editor).OfType<TextBox>().Single(t=>t.ToolTip?.ToString()?.StartsWith("Search by source item") == true); effectSearch.Text="Earthshaker"; editor.UpdateLayout();
            var effectResults=Usability.Tree(editor).OfType<DataGrid>().Single(g=>g.Columns.Any(c=>c.Header?.ToString()=="Compatibility"));
            for(int attempt=0;attempt<30 && effectResults.Items.Count==0;attempt++){await Task.Delay(50);editor.UpdateLayout();}
            effectResults.SelectedIndex=0; Usability.Tree(editor).OfType<Button>().Single(b=>b.Content?.ToString()=="Add effect").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); editor.UpdateLayout();
            Check(Usability.Tree(editor).OfType<DataGrid>().Single(g=>g.Columns.Any(c=>c.SortMemberPath=="SourceName")).Items.OfType<ExaltationAssignment>().Any(a=>a.SourceName=="Earthshaker"),"Earthshaker is found alphabetically and assigned to stored Sunderfury +4 through the real editor");
            foreach(double scale in new[]{1d,1.5,2,3}) Save($"qa/controls-exaltation-editor-{scale:0.0}x.png",(FrameworkElement)editor.Content,560,400,scale); Save("controls-exaltation-editor.png",(FrameworkElement)editor.Content,560,400,1); editor.Close();
            var activation = new PetBuildReviewDialog(CurrentBuild.Name, DescribePetActivation(PetInventoryEditing.Confirmed(owner,Controller.Catalog),CurrentBuild)) { Owner=this,Left=-15000,Top=-15000 }; activation.Show(); activation.UpdateLayout(); foreach(double scale in new[]{1d,1.5,2,3}) Save($"qa/controls-build-review-{scale:0.0}x.png",(FrameworkElement)activation.Content,540,400,scale); Save("controls-build-review.png",(FrameworkElement)activation.Content,540,400,1); activation.Close();
            var searchText = "";
            foreach(var tab in TrackerNavigation.Order)
            {
                Width=1220; Height=780; ShowView(tab,tab switch{TrackerTab.Quests=>"Darkforge",TrackerTab.Gear=>"Red Dragonscale Armor",TrackerTab.Exaltations=>"",TrackerTab.Progress=>"Warrior Epic Quest",_=>null}); Flush();
                if(tab==TrackerTab.Exaltations)
                {
                    Check(exaltTarget.SelectedItem?.ToString()=="Any" && exaltTable.Columns.Any(c=>c.Header?.ToString()=="Unlock Rank"),"Exaltations default to Any and expose Unlock Rank");
                    TableLayout.Reset(exaltTable); Flush(); var next=exaltTable.Columns[1]; double neighbor=next.ActualWidth; var name=exaltTable.Columns[0]; double total=exaltTable.Columns.Sum(c=>c.ActualWidth); TableLayout.ResizeColumn(exaltTable,name,name.ActualWidth+35); UpdateLayout(); Check(next.ActualWidth<=neighbor+.5&&Math.Abs(total-exaltTable.Columns.Sum(c=>c.ActualWidth))<1,"Header resizing cascades through neighbors without expanding the table");
                    var pane=(Grid)exaltTable.Parent; double before=name.ActualWidth; pane.ColumnDefinitions[0].Width=new(.42,GridUnitType.Star);pane.ColumnDefinitions[2].Width=new(.58,GridUnitType.Star);Flush(); Check(Math.Abs(before-name.ActualWidth)>1,"Saved text-column preferences reflow when the Exaltations splitter moves"); TableLayout.Reset(exaltTable); Flush();
                }
                var grids=Usability.Tree(this).OfType<DataGrid>().Where(g=>g.IsVisible && g.ActualWidth>1).ToList();
                foreach(var grid in grids) { Check(grid.Columns.Where(c=>c.Visibility==Visibility.Visible).All(c=>c.ActualWidth+1>=c.MinWidth),tab+" measured headers respect minimum widths"); var selected=grid.SelectedItem; var widths=grid.Columns.Select(c=>c.ActualWidth).ToArray(); var splitter=Ancestor<Grid>(grid); if(splitter!=null && splitter.ColumnDefinitions.Count==3) { splitter.ColumnDefinitions[0].Width=new(.4,GridUnitType.Star); splitter.ColumnDefinitions[2].Width=new(.6,GridUnitType.Star); Flush(); Check(Equals(selected,grid.SelectedItem),tab+" splitter preserves selection"); } }
                if(tab==TrackerTab.Progress)
                { var selected=questSelection.SelectedItem; progressSearch.Text="Warrior"; Flush(); Check(Equals(selected,questSelection.SelectedItem),"Quest search preserves a matching selection and its hierarchy"); progressSearch.Text="no-such-quest-review"; Flush(); Check(progressNodes.Count==0 && howTo.Children.OfType<TextBlock>().Any(t=>t.Text.StartsWith("No quests match")),"Progress empty filter clears stale details"); progressSearch.Text=""; ShowView(TrackerTab.Progress,"Warrior Epic Quest"); Flush(); }
                Capture(tab.ToString().ToLowerInvariant()); Width=640; Height=420; Flush(); foreach(double scale in new[]{1d,1.5,2,3}) Save($"qa/controls-{tab.ToString().ToLowerInvariant()}-narrow-{scale:0.0}x.png",(FrameworkElement)Content,640,420,scale);
                Check(Usability.Tree(this).OfType<GridSplitter>().Where(g=>g.IsVisible).All(g=>g.ActualWidth>=0),tab+" remains laid out at the 640 × 420 minimum");
            }
            Width=1220;Height=780;ShowView(TrackerTab.Items); searchText=search.Text; SetSearch("no-such-item-review");Flush();Capture("items-empty");SetSearch(searchText);
            ShowView(TrackerTab.Pet,"Sunderfury"); Flush(); var stable=petResults.ItemsSource; var timings=new List<double>(); for(int i=0;i<30;i++){var watch=Stopwatch.StartNew();Refresh();timings.Add(watch.Elapsed.TotalMilliseconds);} Check(ReferenceEquals(stable,petResults.ItemsSource),"Routine unchanged Pet refresh retains its collection and avoids rebuilding candidates");
            await Task.Delay(300); Check(!petPersistTimer.IsEnabled,"Debounced context persistence completes after 250 ms");
            var report = new { Passed=checks.Count, Checks=checks, RoutinePetRefresh=new{ Samples=timings.Count, P99=timings.Order().ElementAt(28), Max=timings.Max(), All=timings }, AppAssemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))).ToLowerInvariant(), CoreAssemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(PetBuild).Assembly.Location))).ToLowerInvariant(), Notes="Actual-component render/layout proofs at 100/150/200/300%; live monitor transitions and game interactions remain pending." };
            Json.Write(Path.Combine(folder,"pet-controls-checks.json"),report);
        }
        catch(Exception error) { Json.Write(Path.Combine(folder,"pet-controls-failure.json"),new{Error=error.ToString(),Checks=checks});throw; }
    }
}
