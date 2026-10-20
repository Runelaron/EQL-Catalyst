using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class GearBuildUiChecks
{
    internal static void Run(TrackerController c,QuestWindow q,Action<bool,string> check,string folder)
    {
        var index=c.Profile.Loadouts.FindIndex(p=>p.Name==c.Profile.SelectedLoadout);var saved=EquipmentPositions.Copy(c.Profile.ActiveLoadout);var widths=new List<object>();
        void Flush(){q.Refresh();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);q.UpdateLayout();}
        IEnumerable<T> Find<T>()=>Usability.Tree(q).OfType<T>();
        try
        {
            GearBuildFixtures.Apply(c);q.SetGearTarget(false);q.ShowView(TrackerTab.Gear,"Red Dragonscale Armor");q.SelectGearPosition("CHEST:1");Flush();
            var table=Find<DataGrid>().Single(t=>t.Columns.Any(col=>col.SortMemberPath=="UnbuffedDelta") && t.Columns.Any(col=>col.SortMemberPath=="ItemScore"));
            var row=table.Items.OfType<GearRow>().Single();table.SelectedItem=row;Flush();
            check(q.GearEquipmentTable.Items.OfType<EquipmentPosition>().Count(p=>p.Slot=="Any Slot")==2,"Gear equipment rail distinguishes both Any Slot positions");
            check(row.Comparison?.Evaluation is {Unbuffed.Delta:1,Buffed.Delta:6},"Balanced Tank reconciles the Red Dragonscale trade-off in both contexts (1 / 6 points)");
            check(row.Comparison!.Evaluation!.Unbuffed.Changes.Any(s=>s.Stat=="INT" && s.ItemChange<0 && s.Weight==0),"Recommendation preserves unscored INT losses alongside scored DEX/STA losses");
            check(Find<TextBlock>().Any(t=>t.IsVisible && t.Text.StartsWith("Losses:") && t.Text.Contains("INT") && t.Text.Contains("Mana")),"Comparison leads with visible stat losses rather than burying unscored attributes");
            check(Find<BuildComparisonView>().Count(v=>v.IsVisible)==1 && table.Columns.Any(col=>col.SortMemberPath=="BuffedDelta"),"Gear presents both contexts equally through the shared comparison component");
            var before=EquipmentPositions.Fingerprint(c.Profile.ActiveLoadout.EquipmentPositions);
            Find<Button>().Single(b=>b.IsVisible && b.Content?.ToString()=="Add to build").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush();
            check(c.Profile.ActiveLoadout.EquipmentDraft?.Positions.Any(p=>p.Item?.Name=="Red Dragonscale Armor")==true && EquipmentPositions.Fingerprint(c.Profile.ActiveLoadout.EquipmentPositions)==before,"Try in draft replaces only the draft, preserving confirmed equipment");
            Find<Button>().Single(b=>b.IsVisible && b.Content?.ToString()=="Undo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush();
            check(EquipmentPositions.Fingerprint(c.Profile.ActiveLoadout.EquipmentDraft!.Positions)==before,"Gear undo restores the exact prior positions, ranks and effects");
            foreach(double width in new[]{640d,940,1220})
            {
                q.Width=width;q.Height=width==640?420:800;Flush();
                bool rail=q.GearEquipmentTable.IsVisible;
                check(rail==(width>=980),"Equipment rail yields to slot selector below 980 DIP · "+width);
                check(Find<ComboBox>().Any(b=>b.IsVisible && b.DisplayMemberPath=="Label"),"Compact Gear keeps an explicit physical position selector · "+width);
                widths.Add(new{Width=width,RailVisible=rail,Table=table.ActualWidth});
            }
            c.SeedStatEvidencePreview();var profileDialog=new StatProfileDialog(c,c.Profile.ActiveLoadout,false,c.Profile.ActiveLoadout.EquipmentPositions,()=>{}){Left=-15000,Top=-15000};profileDialog.Show();profileDialog.UpdateLayout();
            check(Usability.Tree(profileDialog).OfType<ComboBox>().Any(b=>b.Items.Contains("Usual buffs")),"Stat editor offers independent Unbuffed and Usual buffs contexts");
            check(Usability.Tree(profileDialog).OfType<CheckBox>().Any(b=>b.Content?.ToString()=="AA bonuses already in totals" && b.IsChecked==true),"Stat capture defaults to avoiding AA double counting");profileDialog.Close();
            var prefillDialog=new StatProfileDialog(c,c.Profile.ActiveLoadout,false,c.Profile.ActiveLoadout.EquipmentPositions,()=>{}){Left=-15000,Top=-15000};prefillDialog.Show();prefillDialog.UpdateLayout();
            Usability.Tree(prefillDialog).OfType<TabControl>().First().SelectedIndex=2;prefillDialog.UpdateLayout();
            var aaGrid=Usability.Tree(prefillDialog).OfType<DataGrid>().Single(g=>g.Columns.Any(col=>col.Header?.ToString()=="Rank evidence"));
            check(aaGrid.Items.OfType<GearAaModifier>().Single(a=>a.Name=="Combat Fury").Rank==4,"Stat profiles prefill exact logged AA ranks on open");
            check(aaGrid.Items.OfType<GearAaModifier>().Single(a=>a.Name=="Combat Stability").Rank==null,"Learned-only AA evidence never invents a rank");
            Usability.Tree(prefillDialog).OfType<TabControl>().First().SelectedIndex=0;prefillDialog.UpdateLayout();
            check(Usability.Tree(prefillDialog).OfType<DataGrid>().Any(g=>g.Columns.Any(col=>col.Header?.ToString()=="Gear est.")),"Worn estimates remain separate from observed totals");
            check(Usability.Tree(prefillDialog).OfType<CheckBox>().Single(b=>b.Content?.ToString()=="AA list complete").IsChecked==false,"Game prefill never asserts AA completeness");prefillDialog.Close();
            q.ShowView(TrackerTab.Pet,"Bloodmoon");q.SelectPetDestination(PetInventoryDestination.Planned);Flush();
            var buildMenu = Find<Button>().Single(b => b.IsVisible && b.Content?.ToString() == "Build ▾").ContextMenu;
            check(buildMenu.Items.OfType<MenuItem>().Any(m => m.Header?.ToString() == "New build from current") && Find<Button>().Any(b=>b.IsVisible && b.Content?.ToString()=="Undo"),"Focused Pet retains confirmed-to-draft copying in Build and visible item undo");
            var inventory=Find<DataGrid>().Single(t=>t.IsVisible && t.Columns.Any(col=>col.Header?.ToString()=="+Rank"));
            check(inventory.Items.Count==PetEquipmentPlanner.Capacity(c.Profile.ActiveLoadout.Classes),"Pet planner displays numbered empty positions as well as equipped items");
            q.Width=640;q.Height=420;Flush();var searchTable=Find<DataGrid>().Single(t=>t.IsVisible && t.Columns.Any(col=>col.Header?.ToString()=="Slots"));
            check(searchTable.ActualHeight>=65 && Find<Button>().Any(b=>b.IsVisible && b.Content?.ToString()=="Compare…"),"Minimum Pet layout retains candidate rows and a separate comparison action");
            Json.Write(System.IO.Path.Combine(folder,"gear-build-checks.json"),new{Checks="Whole-build contexts, physical positions, drafts, stat capture, guided Pet",Widths=widths,InGameAcceptance="Pending"});
        }
        finally{c.Profile.Loadouts[index]=saved;c.Save();q.Width=1220;q.Height=800;q.ShowView(TrackerTab.Items);}
    }
}
