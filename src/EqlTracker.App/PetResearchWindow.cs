using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed partial class PetResearchWindow : FrostDialog
{
    private readonly TrackerController controller;
    private readonly ListBox samples = new() { Height = 210 }, sheets = new() { Height = 130 }, models = new() { Height = 130 }, buffEvidence = new() { Height = 130 };
    private readonly TextBlock buffDetail = UI.Text("Select buff evidence. Ordinary haste and overhaste remain separate; a display ceiling does not establish stacking or a DPS multiplier.",11);
    private readonly TextBlock sampleDetail = UI.Text("Select a sample", 12), modelDetail = UI.Text("Select a model", 12), status = UI.Text("Local evidence only · no uploads", 11, "SecondaryText");
    private readonly TextBox sheetJson = UI.Input("", 620), sampleJson = UI.Input("", 620), modelJson = UI.Input("", 620);
    private readonly ComboBox sheetPet = UI.Select(PetDefinition.PlanningChoices,"Unknown",110);
    private readonly TextBox sheetRank=UI.Input("",45), sheetLevel=UI.Input("",45), sheetLabel=UI.Input("",220), sheetSpell=UI.Input("",230), sheetSources=UI.Input("",380);
    private readonly DataGrid statFields = new() { Height=220,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,HeadersVisibility=DataGridHeadersVisibility.Column };
    private readonly TextBox modelDelta=UI.Input("",90);
    private readonly TextBlock modelEstimate=UI.Text("Estimates require a validated, approved model in its exact tested context.",11,"SecondaryText");
    private sealed class StatEntry { public string Field {get;set;}=""; public string Value {get;set;}=""; }
    private readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private long revision = -1;
    private bool refreshing;
    private sealed record Choice(string Id, string Label) { public override string ToString() => Label; }
    internal PetResearchWindow(TrackerController controller) : base("Pet data · local research", 820, 780)
    {
        this.controller = controller;
        if (!TrackerController.PetResearchEnabled) throw new InvalidOperationException("Private build required");
        foreach (var editor in new[] { sheetJson, sampleJson, modelJson }) { editor.Width = double.NaN; editor.HorizontalAlignment = HorizontalAlignment.Stretch; editor.AcceptsReturn = true; editor.TextWrapping = TextWrapping.NoWrap; editor.Height = 240; ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto); ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Auto); }
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Samples", Content = UI.Stack(UI.Text("Encounter totals and durations are preserved. Recorded hit cadence is not swing speed or accuracy.", 12), samples, sampleDetail, FocusedLayout.Disclosure("Buff evidence",UI.Stack(buffEvidence,buffDetail),"Landing/fade timelines and reviewed inspections are separate. Cast attempts alone do not establish active buffs."), FocusedLayout.Disclosure("Review context", UI.Stack(sampleJson, UI.Button("Save reviewed context", SaveSample)), "Review rank, actual level, gear, buffs, target and positioning. Unknown fields remain blank.")) });
        statFields.Columns.Add(new DataGridTextColumn{Header="Stat",Binding=new System.Windows.Data.Binding("Field"),IsReadOnly=true,Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        statFields.Columns.Add(new DataGridTextColumn{Header="Observed value · blank = unknown",Binding=new System.Windows.Data.Binding("Value"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        var metadata=new WrapPanel(); foreach(var field in new UIElement[]{UI.Field("Label",sheetLabel),UI.Field("Pet",sheetPet),UI.Field("Rank",sheetRank),UI.Field("Actual level",sheetLevel),UI.Field("Summon spell",sheetSpell),UI.Field("Screenshot references (one per line)",sheetSources)}) metadata.Children.Add(field);
        tabs.Items.Add(new TabItem { Header = "Stat sheets", Content = UI.Stack(UI.Text("All attributes, resources, resists, paired AC/Attack, skills, weapons and absorption. Blank means unknown; negative HP is retained as invalid.", 12), sheets,metadata,statFields,UI.Row(UI.Button("New sheet", NewSheet), UI.Button("Save reviewed sheet", SaveSheet)),FocusedLayout.Disclosure("Context, evidence & raw JSON",sheetJson,"Edit additional context and evidence here. The observed-value table supplies stat values when saving.")) });
        tabs.Items.Add(new TabItem { Header = "Models", Content = UI.Stack(UI.Text("STA → HP · INT/WIS → mana · rank growth. Three controlled A/B/A repeats, an independent holdout and explicit review are required. No universal DPS multiplier.", 12), models, modelDetail, modelJson, UI.Row(UI.Button("New model", NewModel), UI.Button("Save candidate", SaveModel), UI.Button("Approve validated model", ApproveModel)),UI.Row(UI.Field("Input change",modelDelta),UI.Button("Estimate change",Estimate)),modelEstimate) });
        samples.SelectionChanged += (_, _) => { if (!refreshing) SelectSample(); }; sheets.SelectionChanged += (_, _) => { if (!refreshing) SelectSheet(); }; models.SelectionChanged += (_, _) => { if (!refreshing) SelectModel(); };
        buffEvidence.SelectionChanged += (_,_) => { if(refreshing || buffEvidence.SelectedItem is not Choice c) return; var b=controller.PetResearchSnapshot().Buffs.First(x=>x.Id==c.Id); buffDetail.Text=b.Spell+" · "+b.Transition+"\n"+string.Join(" · ",b.Displayed.Select(v=>v.Key+": "+v.Value))+"\nUpgrade rank: "+(b.UpgradeRank?.ToString()??"unknown")+" · AA rank: "+(b.AaRank?.ToString()??"unknown")+"\n"+b.Evidence; };
        Editor.Children.Add(UI.Stack(status, tabs));
        Actions.Children.Add(UI.Button("Import measurements…", Import));
        Actions.Children.Add(UI.Button("Save blank template…", SaveTemplate));
        Actions.Children.Add(UI.Button("Close", Close));
        timer.Tick += (_, _) => Refresh(); Loaded += (_, _) => { Refresh(); timer.Start(); }; Closed += (_, _) => timer.Stop();
    }
    private void Refresh()
    {
        var state = controller.PetResearchSnapshot(); if (revision == state.Revision) return; revision = state.Revision;
        refreshing = true;
        void Populate(ListBox list, IEnumerable<Choice> choices) { string? selected = (list.SelectedItem as Choice)?.Id; list.ItemsSource = choices.ToList(); list.SelectedItem = list.Items.Cast<Choice>().FirstOrDefault(c => c.Id == selected); }
        Populate(samples, state.Samples.Values.OrderByDescending(s => s.Start).Select(s => new Choice(s.EncounterId, $"{s.Start:MMM dd HH:mm} · {s.Context.Pet} · {s.Dps:N1} DPS · {s.Seconds:N0}s · {s.Context.Buffs}")));
        Populate(sheets, state.Sheets.OrderByDescending(s => s.CapturedAt).Select(s => new Choice(s.Id, s.Label + " · " + s.Context.Pet + " · rank " + s.Context.Rank + (s.Reviewed ? " · reviewed" : " · needs review"))));
        Populate(models, state.Models.Select(m => new Choice(m.Id, m.Input + " → " + m.Output + " · " + m.Gate)));
        Populate(buffEvidence,state.Buffs.OrderByDescending(b=>b.At).Select(b=>new Choice(b.Id,b.At.ToString("MMM dd HH:mm:ss")+" · "+b.Spell+" · "+b.Transition)));
        refreshing = false;
        status.Text = $"Local only · {state.Samples.Count} encounters · {state.Sheets.Count} stat sheets · {state.Buffs.Count} buff observations · revision {state.Revision}";
    }
    private void SelectSample()
    {
        var state = controller.PetResearchSnapshot(); if (samples.SelectedItem is not Choice c || !state.Samples.TryGetValue(c.Id, out var s)) return;
        sampleJson.Text = JsonSerializer.Serialize(s.Context, Json.Options);
        sampleDetail.Text = $"{s.Damage:N0} damage / {s.Seconds:N1}s = {s.Dps:N1} DPS · {s.RecordedHits} recorded hits / {s.RecordedMisses} recorded misses\nTargets: {s.Targets} · {s.Zone}\n" + string.Join(" · ", s.Components.Select(p => p.Key + ": " + p.Value+" damage; "+(s.ComponentRecordedHits.GetValueOrDefault(p.Key)/Math.Max(1,s.Seconds)).ToString("0.00")+" recorded hits/s")) + "\nBuff-period damage: " + string.Join(" · ", s.BuffPeriodDamage.Select(p => p.Key + ": " + p.Value + " damage / " + s.BuffPeriodSeconds.GetValueOrDefault(p.Key).ToString("0.0") + "s = "+(p.Value/Math.Max(1,s.BuffPeriodSeconds.GetValueOrDefault(p.Key))).ToString("0.0")+" DPS")) + "\nPhase targets and durations can differ; these are historical measurements, not causal buff multipliers.\n" + s.Evidence + "\n" + s.CalibrationWarning;
    }
    private void SaveSample() => Try(() => { if (samples.SelectedItem is not Choice c) return; var context = JsonSerializer.Deserialize<PetResearchContext>(sampleJson.Text, Json.Options) ?? throw new ArgumentException("Context required"); controller.EditPetResearch(s => { s.Samples[c.Id].Context = context; s.Samples[c.Id].Reviewed = true; s.Samples[c.Id].Evidence += " · Context reviewed locally " + DateTimeOffset.Now.ToString("O"); }); });
    private void LoadSheet(PetStatSheet sheet) { sheet.Validate();sheetJson.Text=JsonSerializer.Serialize(sheet,Json.Options);sheetLabel.Text=sheet.Label;sheetPet.SelectedItem=sheet.Context.Pet;sheetRank.Text=sheet.Context.Rank?.ToString()??"";sheetLevel.Text=sheet.Context.Level?.ToString()??"";sheetSpell.Text=sheet.Context.Summon;sheetSources.Text=string.Join(Environment.NewLine,sheet.EvidenceFiles);statFields.ItemsSource=sheet.Values.Select(v=>new StatEntry{Field=v.Key,Value=v.Value?.ToString("R",CultureInfo.InvariantCulture)??""}).ToList(); }
    private void SelectSheet() { if (sheets.SelectedItem is Choice c) LoadSheet(controller.PetResearchSnapshot().Sheets.Single(s => s.Id == c.Id)); }
    private void NewSheet() { sheets.SelectedItem = null; LoadSheet(new PetStatSheet { Label = "New measurement", CapturedAt = DateTimeOffset.Now }); }
    private void SaveSheet() => Try(() => { statFields.CommitEdit(DataGridEditingUnit.Cell,true);statFields.CommitEdit(DataGridEditingUnit.Row,true);var sheet = JsonSerializer.Deserialize<PetStatSheet>(sheetJson.Text, Json.Options) ?? throw new ArgumentException("Sheet required");
        int? Integer(TextBox box){if(box.Text.Trim().Length==0)return null;if(!int.TryParse(box.Text,out int value))throw new ArgumentException("Enter a whole rank/level or leave blank.");return value;}
        sheet.Label=sheetLabel.Text.Trim();sheet.Context.Pet=sheetPet.SelectedItem?.ToString()??"Unknown";sheet.Context.Rank=Integer(sheetRank);sheet.Context.Level=Integer(sheetLevel);sheet.Context.Summon=sheetSpell.Text.Trim();sheet.EvidenceFiles=sheetSources.Text.Split(["\r\n","\n"],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach(var field in statFields.Items.Cast<StatEntry>()){if(field.Value.Trim().Length==0)sheet.Values[field.Field]=null;else if(double.TryParse(field.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)&&double.IsFinite(value))sheet.Values[field.Field]=value;else throw new ArgumentException("Enter a finite number or leave "+field.Field+" blank.");}
        sheet.Validate(); sheet.Reviewed = true; controller.EditPetResearch(s => { s.Sheets.RemoveAll(x => x.Id == sheet.Id); s.Sheets.Add(sheet); }); LoadSheet(sheet); });
    private void SelectModel() { if (models.SelectedItem is Choice c) { var m = controller.PetResearchSnapshot().Models.Single(x => x.Id == c.Id); modelJson.Text = JsonSerializer.Serialize(m, Json.Options); modelDetail.Text = m.Gate + " · coefficient " + (m.Coefficient?.ToString("0.####", CultureInfo.InvariantCulture) ?? "Unknown"); } }
    private void NewModel() { models.SelectedItem = null; modelJson.Text = JsonSerializer.Serialize(new PetCalibrationModel(), Json.Options); }
    private void SaveModel() => Try(() => { var model = JsonSerializer.Deserialize<PetCalibrationModel>(modelJson.Text, Json.Options) ?? throw new ArgumentException("Model required"); model.LocallyApproved = false; model.ReviewEvidence = ""; controller.EditPetResearch(s => { s.Models.RemoveAll(x => x.Id == model.Id); s.Models.Add(model); }); modelDetail.Text = model.Gate; });
    private void ApproveModel() => Try(() => { if (models.SelectedItem is not Choice c) return; controller.EditPetResearch(s => { var model = s.Models.Single(m => m.Id == c.Id); if (model.Gate != "Ready for explicit local review") throw new ArgumentException(model.Gate); model.LocallyApproved = true; model.ReviewEvidence = "Explicit local review " + DateTimeOffset.Now.ToString("O"); }); });
    private void Estimate() => Try(()=>{if(models.SelectedItem is not Choice c)return;var model=controller.PetResearchSnapshot().Models.Single(m=>m.Id==c.Id);if(!double.TryParse(modelDelta.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double delta)||!double.IsFinite(delta))throw new ArgumentException("Enter a finite input change.");var value=model.EstimateDelta(delta,model.Context);modelEstimate.Text=value is {} result?"Estimated "+model.Output+" change: "+result.ToString("+0.##;-0.##;0")+" · exact model context only; no DPS conversion":"Unavailable: "+model.Gate+"; stay inside the tested input range.";});
    private void Import() { var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Local measurements (*.json)|*.json" }; if (picker.ShowDialog(this) == true) Try(() => controller.ImportPetResearch(picker.FileName)); }
    private void SaveTemplate() { var picker = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "pet-research-measurement.json" }; if (picker.ShowDialog(this) == true) Try(() => Json.Write(picker.FileName, new PetResearchState { Sheets = [new()] })); }
    private void Try(Action action) { try { action(); Refresh(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException or IOException) { status.Text = e.Message; } }
}
