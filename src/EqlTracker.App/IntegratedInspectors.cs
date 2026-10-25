using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class CompatibilityInspector : StackPanel
{
    private string fingerprint="";
    internal void Show(CompatibilityComparison? result)
    {
        string next=result==null?"none":PetComparisonEngine.Key(result);if(next==fingerprint)return;fingerprint=next;
        Children.Clear(); Visibility = result?.Conclusion.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Visibility != Visibility.Visible) return;
        if (result == null) return;
        Children.Add(UI.Text(result.Conclusion, 12, "Mint", true));
        var details = UI.Stack(UI.Text("Mechanical contributions · preference points and measured DPS remain separate.", 11, "SecondaryText"));
        foreach (var delta in result.Changes.Where(c => c.UsableChange != 0||c.Family=="Ordinary displayed attack speed")) details.Children.Add(UI.Text((delta.Family.Contains("Haste/")?"Listed parameter · ":"")+delta.Family + ": " + (delta.UsableChange?.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture) ?? "Unknown"), 11));
        foreach (var c in result.After.Contributions) details.Children.Add(UI.Text($"{c.Effect.Identity.Name} · rank {c.Effect.Identity.UpgradeRank?.ToString() ?? "Unknown"} · {c.Effect.Family}: {c.State}\n{c.Explanation}\n{c.Effect.Source}", 11, "SecondaryText"));
        foreach (string note in result.After.Notes) details.Children.Add(UI.Text(note, 11, "SecondaryText"));
        Children.Add(FocusedLayout.Disclosure("Why?", details, "Sources, ranks, usable contributions and assumptions for this setup"));
    }
}
internal sealed class OtherHoldingsView : Expander
{
    private string fingerprint="";
    internal OtherHoldingsView()=>SetResourceReference(StyleProperty,typeof(Expander));
    internal void Show(TrackerController controller, ItemDefinition item)
    {
        var stock = controller.OtherHoldings(item); string next=PetComparisonEngine.Key(stock);if(next==fingerprint)return;fingerprint=next; Visibility = stock.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Header = "Other characters · " + string.Join(", ", stock.Select(s => s.Character).Distinct());
        var content = new StackPanel();
        foreach (var row in stock) content.Children.Add(UI.Text($"{row.Character} · {row.Item.Location} · {(row.Item.Quantity?.ToString() ?? "Present")} · rank {row.Item.Rank}{(row.Item.Extracted ? " · extracted effect" : "")}\nSnapshot {row.CapturedAt:MMM dd HH:mm} · {(DateTime.Now - row.CapturedAt).TotalDays:0.#} days old", 11));
        content.Children.Add(UI.Text("Saved stock on this server. Does not satisfy this character’s requirements or reserve an item.", 11, "SecondaryText")); Content = content;
    }
}
internal sealed class CampNotebookView : Expander
{
    internal CampNotebookView(TrackerController controller, ItemSource source)
    {
        SetResourceReference(StyleProperty,typeof(Expander));Header = "Camp notes"; IsExpanded = false; Margin = new(0, 3, 0, 3);
        Expanded += (_, _) => { if (Content == null) Build(); };
        void Build()
        {
            var setup = controller.IntegratedSnapshot(); var note = setup.Camps.Get(source);
            var notes = UI.Input(note.Notes, 200); notes.Width = double.NaN; notes.AcceptsReturn = true; notes.TextWrapping = TextWrapping.Wrap; notes.MinHeight = 64; notes.MaxLength = 16000;
            var status = UI.Text(setup.Camps.Timer?.SourceId == note.Id ? setup.Camps.Timer.Label(DateTime.Now) : "", 11, "SecondaryText");
            var minutes = WorkspaceForms.Control(UI.Input("", 55));string baseline=notes.Text.Trim();
            var save=WorkspaceForms.Control(UI.Button("Save notes",()=>{}));save.IsEnabled=false;notes.TextChanged+=(_,_)=>save.IsEnabled=notes.Text.Trim()!=baseline;save.Click+=(_,_)=>{var edited=controller.IntegratedSnapshot();edited.Camps.Get(source).Notes=notes.Text.Trim();controller.SaveIntegrated(edited);baseline=notes.Text.Trim();save.IsEnabled=false;status.Text="Notes saved";};var body=UI.Stack(notes,save);
            var observations = controller.JournalSnapshot().Where(a => a.Zone.Equals(source.Zone, StringComparison.OrdinalIgnoreCase) && (a.Kind == "Death" && a.Name.Equals(source.Mob, StringComparison.OrdinalIgnoreCase) || a.Kind == "Item" && a.Source.Equals(source.Mob, StringComparison.OrdinalIgnoreCase))).OrderByDescending(a => a.At).Take(30).ToArray();
            var deaths = observations.Where(a => a.Kind == "Death").ToArray(); var from = new ComboBox { ItemsSource = deaths, DisplayMemberPath = "At", SelectedIndex = -1, MinWidth = 100, ToolTip = "Optional observed death as the start; otherwise use the current time" };
            body.Children.Add(UI.Text("One silent estimate per character · minutes", 11, "SecondaryText"));
            body.Children.Add(WorkspaceForms.Flow(minutes, UI.Button("Start estimate", () =>
            {
                if (!double.TryParse(minutes.Text, out double value) || !double.IsFinite(value) || value <= 0 || value > 43200) { status.Text = "Enter 0–43,200 minutes."; return; }
                var edited = controller.IntegratedSnapshot(); edited.Camps.Start(source, (from.SelectedItem as JournalActivity)?.At ?? DateTime.Now, TimeSpan.FromMinutes(value)); controller.SaveIntegrated(edited); status.Text = edited.Camps.Timer!.Label(DateTime.Now);
            }), UI.Button("Clear", () => { var edited = controller.IntegratedSnapshot(); edited.Camps.Timer = null; controller.SaveIntegrated(edited); status.Text = ""; })));
            if (deaths.Length > 0) body.Children.Add(UI.Field("Start from observed death (optional)", from));
            body.Children.Add(status);
            var clock = new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};
            clock.Tick+=(_,_)=>{var timer=controller.CampEstimate;status.Text=timer?.SourceId==note.Id?timer.Label(DateTime.Now):"";}; Loaded+=(_,_)=>clock.Start();Unloaded+=(_,_)=>clock.Stop();
            if(IsLoaded)clock.Start();
            body.Children.Add(UI.Button("Record visit", () => { var edited = controller.IntegratedSnapshot(); edited.Camps.Get(source).Visits.Add(DateTime.Now); controller.SaveIntegrated(edited); Content = null; Build(); }));
            foreach (var visit in note.Visits.OrderDescending().Take(10)) body.Children.Add(UI.Text("Visit · " + visit.ToString("MMM dd HH:mm"), 11));
            foreach (var row in observations) body.Children.Add(UI.Text($"{row.At:MMM dd HH:mm:ss} · {(row.Kind == "Death" ? "Observed death" : row.Quantity + " × " + row.Name + " · " + row.Disposition)}", 11));
            body.Children.Add(UI.Text("Recorded names and zone variant only. No spawn instance or drop probability is inferred.", 11, "SecondaryText")); foreach(var control in Usability.Tree(body).OfType<Control>().Where(c=>c is Button or ComboBox))WorkspaceForms.Control(control);Content = body;
        }
    }
}
