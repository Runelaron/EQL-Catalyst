using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class PetItemDialog : FrostDialog
{
    private sealed record AssignedRow(ExaltationAssignment Assignment, string Status) { public string SourceName => Assignment.SourceName; public string Effect => Assignment.Effect; public string State => Assignment.State; }
    private sealed record EffectChoice(ExaltationOption Option, string Compatibility)
    { public string Source => Option.Source.Name; public string Effect => Option.Effect; public string Type => Option.Type; public int UnlockRank => Option.UnlockRank; public override string ToString() => Option.Id; }
    public PetInventoryItem? Result { get; private set; }
    public bool ReplaceOccupied => true;
    internal Button SaveButton { get; private set; } = null!;
    internal ComboBox ItemChoice { get; private set; } = null!;
    internal bool MetadataReady { get; private set; }
    internal TextBlock ErrorText => error;
    internal TextBox PositionInput { get; private set; } = null!;
    private readonly TextBlock error = UI.Text("", 11, "Violet");
    public PetItemDialog(TrackerController controller, ItemDefinition? definition, PetBuild inventory, PetInventoryDestination destination, PetInventoryItem? existing = null, int? rankOverride = null, int? positionOverride = null, Func<PetBuild?>? latest = null) : base("Edit pet slot "+(positionOverride??existing?.Position??PetInventoryEditing.NextPosition(inventory)),460,360)
    {
        inventory=EquipmentPositions.Copy(inventory);PetSlotPlanning.Migrate(inventory);
        string initialRevision = PetWorkspaceDraft.Key(inventory);
        string editingId = existing?.Id ?? "";
        definition ??= new ItemDefinition();
        string target = destination == PetInventoryDestination.Confirmed ? "Confirmed inventory · manual" : "Planned build · " + inventory.Name;
        var value = existing == null ? new PetInventoryItem { ItemKey = definition.Key, Name = definition.Name, Position = PetInventoryEditing.NextPosition(inventory), Slot = definition.Slots.FirstOrDefault() ?? "Storage" } : System.Text.Json.JsonSerializer.Deserialize<PetInventoryItem>(System.Text.Json.JsonSerializer.Serialize(existing))!;
        value.Exaltations = EquipmentPositions.Copy(inventory.SlotConfigurations.FirstOrDefault(s => s.Position == (positionOverride ?? value.Position))?.Effects ?? []);
        if(definition.Key.Length>0&&definition.Key!=value.ItemKey){value.ItemKey=definition.Key;value.Name=definition.Name;value.Rank=0;}
        if (rankOverride.HasValue) value.Rank = rankOverride.Value; if (positionOverride.HasValue) value.Position = positionOverride.Value;
        var position = UI.Input(value.Position.ToString(),65);
        var rank=UI.Select(Enumerable.Range(0,21).Select(n=>n.ToString()).Append(value.Rank.ToString()).Distinct(),value.Rank.ToString(),50);rank.IsEditable=true;rank.IsTextSearchEnabled=false;
        PositionInput = position;
        var slot = UI.Select(definition.Slots.Prepend("Storage").Concat([value.Slot]).Distinct(StringComparer.OrdinalIgnoreCase), value.Slot, 160);
        slot.SelectedItem = slot.Items.Cast<string>().First(s => s.Equals(value.Slot, StringComparison.OrdinalIgnoreCase));
        var heading = UI.Text(definition.Name.Length > 0 ? definition.Name : "Empty position", 17, "Accent", true); heading.ToolTip = "Explicit advanced editing of this saved inventory entry; no game items are changed.";
        rank.ToolTip="Choose or type an item rank from 0 to 20. Recorded higher ranks are retained unchanged.";
        string originalKey=existing?.ItemKey??"";int originalRank=existing?.Rank??0;var originalEffects=EquipmentPositions.Copy(value.Exaltations);
        string Snapshot(string key,int tier,IEnumerable<ExaltationAssignment> configured)=>PetComparisonEngine.Key(new{Item=key,Rank=tier,Effects=configured});
        string original=Snapshot(originalKey,originalRank,originalEffects);
        bool Valid(out int tier,out string issue)
        {
            issue="";if(ItemChoice!=null&&ItemChoice.Text!=(definition.Name.Length>0?definition.Name:"Empty position")){tier=0;issue="Choose a recognized item from the list.";return false;}if(!int.TryParse(rank.Text,out tier)||tier<0||tier>20&&tier!=originalRank){issue="Choose a rank from 0 to 20.";return false;}
            if(definition.Key.Length>0&&definition.Key!=originalKey){issue=PetPositionEditing.Validate(inventory,definition,value.Position,PetEquipmentPlanner.Capacity(controller.Profile.ActiveLoadout.Classes),controller.Profile.ActiveLoadout,controller.Catalog);if(issue.Length>0)return false;}
            issue=PetEffectPolicy.Validate(value.Exaltations);return issue.Length==0||PetComparisonEngine.Key(value.Exaltations)==PetComparisonEngine.Key(originalEffects);
        }
        void UpdateSave(){if(SaveButton==null)return;bool valid=Valid(out int tier,out string issue);SaveButton.IsEnabled=valid&&MetadataReady&&Snapshot(definition.Key,tier,value.Exaltations)!=original;error.Text=valid?"":issue;}
        var effects = UI.Table(("Source", "SourceName", 0, null), ("Effect", "Effect", 160, null), ("State", "State", 80, null), ("Availability", "Status", 140, null)); effects.Height = 100; effects.FontSize = 11;
        void RefreshEffects(){UI.SetRows(effects, value.Exaltations.Select(e => {
            var source = controller.Catalog.Find(e.SourceName);string status = "Inactive · choose an item";
            if (definition.Key.Length > 0) {
                int.TryParse(rank.Text, out int tier);string type = PetEffectPolicy.Kind(e);
                status = source == null ? "Unverified source" : ExaltationPlanner.Validate(new(e.EffectId,source,type,e.Effect,ExaltationPlanner.Unlock(type),false,false),definition,tier,slot.SelectedItem?.ToString()??"Storage",GuidedPetBuilds.Context(controller.Profile.ActiveLoadout,inventory),true,controller.Catalog);
                if (status != "Compatible") status = "Inactive · " + status;
            }
            return new AssignedRow(e,status);
        }).ToList());UpdateSave();} RefreshEffects();
        var effectSearch = UI.Input("", 200);
        var effectType = UI.Select(["All effects", "Proc", "Focus", "Click", "Worn", "Ornamentation", "Unknown"], definition.Stats.GetValueOrDefault("Delay") > 0 ? "Proc" : "All effects", 105);
        var effectOwnership = UI.Select(["All sources", "Owned effects", "Source owned"], "All sources", 120);
        var state = UI.Select(["Planned", "Socketed"], destination == PetInventoryDestination.Confirmed ? "Socketed" : "Planned", 95);
        var options = UI.Table(("Source", "Source", 0, null), ("Effect", "Effect", 140, null), ("Type", "Type", 60, null), ("Unlock Rank", "UnlockRank", 75, null), ("Compatibility", "Compatibility", 130, null)); options.Height = 150; options.FontSize = 11;
        TableLayout.SetKey(options, "pet.editor.exaltation-search"); TableLayout.SetKey(effects, "pet.editor.assignments");
        var replaceEffect = new CheckBox { Content = "Replace existing effect of this type", ToolTip = "Replace the selected type. Catalog defaults are inactive until explicitly selected." };
        effectSearch.ToolTip = "Search by source item or effect. Results start alphabetically; click a heading to sort."; effectType.ToolTip = "Weapons start with Proc effects; choose All effects to browse other types."; effectOwnership.ToolTip = "Ownership is a separate filter. Source ownership alone is not an extracted Exaltation.";
        state.ToolTip = "Socketed records an observed effect. Planned records future intent without changing confirmed equipment.";
        var effectContext = GuidedPetBuilds.Context(controller.Profile.ActiveLoadout, inventory);
        var choices = new List<ExaltationOption>(); bool effectsLoaded = false;
        void FilterEffects()
        {
            string? selected = (options.SelectedItem as EffectChoice)?.Option.Id; int.TryParse(rank.Text, out int tier);
            var rows = choices.Where(o => o.Name.Contains(effectSearch.Text, StringComparison.OrdinalIgnoreCase) && (effectType.SelectedIndex == 0 || o.Type == effectType.SelectedItem?.ToString()) && (effectOwnership.SelectedIndex == 0 || effectOwnership.SelectedIndex == 1 && o.Owned || effectOwnership.SelectedIndex == 2 && o.SourceOwned))
                .OrderBy(o => o.Source.Name, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Effect, StringComparer.OrdinalIgnoreCase).Select(o => new EffectChoice(o, definition.Key.Length == 0 ? "Configured · choose an item to validate" : ExaltationPlanner.Validate(o, definition, tier, slot.SelectedItem?.ToString() ?? "Storage", effectContext, true, controller.Catalog))).ToList();
            UI.SetRows(options, rows); options.SelectedItem = rows.FirstOrDefault(o => o.Option.Id == selected); RefreshEffects();
        }
        effectSearch.TextChanged += (_, _) => FilterEffects(); effectType.SelectionChanged += (_, _) => FilterEffects(); effectOwnership.SelectionChanged += (_, _) => FilterEffects();
        rank.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,new TextChangedEventHandler((_,_)=>{FilterEffects();UpdateSave();}));slot.SelectionChanged+=(_,_)=>FilterEffects();
        var addEffect = UI.Button("Add effect", () =>
        {
            if (options.SelectedItem is not EffectChoice choice) { error.Text = "Select a result from the Exaltation search."; return; }
            if (!int.TryParse(rank.Text, out int selectedRank)) { error.Text = "Enter a valid +rank."; return; }
            if (definition.Key.Length == 0) {
                if (value.Exaltations.Any(e => PetEffectPolicy.Kind(e) == choice.Type) && replaceEffect.IsChecked != true) { error.Text = "This effect slot is occupied. Enable replacement first."; return; }
                value.Exaltations.RemoveAll(e => PetEffectPolicy.Kind(e) == choice.Type);
                value.Exaltations.Add(new() { SocketType = choice.Type, EffectId = choice.Option.Id, SourceName = choice.Source, Effect = choice.Effect, State = "Planned", Target = "Pet" });
                RefreshEffects(); error.Text = "Effect configured; inactive until a compatible item is present."; return;
            }
            if (!ExaltationPlanner.Assign(value, choice.Option, definition, selectedRank, slot.SelectedItem?.ToString() ?? "Storage", effectContext, controller.Catalog, state.SelectedItem?.ToString() ?? "Planned", replaceEffect.IsChecked == true, out string issue)) { error.Text = issue; return; }
            RefreshEffects(); replaceEffect.IsChecked = false;
            error.Text = choice.Option.Source.Name.Equals("Earthshaker", StringComparison.OrdinalIgnoreCase) ? "Socket assignment allowed. Earthquake activation requires level 45; pet AoE-proc evidence conflicts and needs client verification." : "Effect added. Proc activation is checked separately from socket compatibility.";
        });
        var removeEffect = UI.Button("Remove effect", () => { if (effects.SelectedItem is AssignedRow row) { value.Exaltations.Remove(row.Assignment); RefreshEffects();UpdateSave(); } });
        var effectControls = new WrapPanel(); foreach (var child in new UIElement[] { UI.Field("Search Exaltations", effectSearch), UI.Field("Effect type", effectType), UI.Field("Ownership", effectOwnership) }) effectControls.Children.Add(child);
        var effectActions = new WrapPanel(); foreach (var child in new UIElement[] { UI.Field("Record as", state), UI.ActionField(addEffect), UI.ActionField(removeEffect) }) effectActions.Children.Add(child);
        var save = UI.Button("Save", () =>
        {
            if (latest != null && (latest() is not { } current || PetWorkspaceDraft.Key(current) != initialRevision)) { error.Text = "This build changed while the editor was open. Reopen the slot to review current equipment."; return; }
            if (!effectsLoaded && value.Exaltations.Count > 0) { error.Text = "Effect metadata is loading; please wait before saving socketed effects."; return; }
            if(!Valid(out int tier,out string issue)){error.Text=issue;return;}
            value.Rank=tier;
            if(value.ItemKey!=originalKey)value.Id=Guid.NewGuid().ToString("N");
            if(value.ItemKey.Length>0&&PetPositionEditing.Validate(inventory,definition,value.Position,PetEquipmentPlanner.Capacity(controller.Profile.ActiveLoadout.Classes)).Length==0)PetPositionEditing.Assign(inventory,value);
            string conflict = PetEffectPolicy.Validate(value.Exaltations); if (conflict.Length > 0&&PetComparisonEngine.Key(value.Exaltations)!=PetComparisonEngine.Key(originalEffects)) { error.Text = conflict; return; }
            Result=value;DialogResult=true;
        }, true);
        var effectsPanel = UI.Stack(effectControls, options, replaceEffect, effectActions, UI.Label("Assigned effects"), effects);
        var itemChoice = ItemChoice = UI.Select([], "",270);itemChoice.IsEditable=true;itemChoice.IsTextSearchEnabled=true;
        var definitions = controller.Catalog.Items.Where(i => i.Slots.Count > 0).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        itemChoice.ItemsSource = new[] { "Empty position" }.Concat(definitions.Select(i => i.Name)).ToList(); itemChoice.SelectedItem = definition.Name.Length > 0 ? definition.Name : "Empty position";
        itemChoice.ToolTip = "Choose the slot item. Configured effects stay with this position; catalog defaults require explicit selection.";
        itemChoice.SelectionChanged += (_, _) => {
            var selected = definitions.FirstOrDefault(i => i.Name == itemChoice.SelectedItem?.ToString());
            if (selected?.Key == value.ItemKey || selected == null && value.ItemKey.Length == 0) return;
            definition=selected??new ItemDefinition();value.ItemKey=definition.Key;value.Name=definition.Name;rank.Text=definition.Key==originalKey?originalRank.ToString():"0";
            if(definition.Key==originalKey)value.Exaltations=EquipmentPositions.Copy(originalEffects);else foreach(var effect in value.Exaltations)effect.State="Planned";
            heading.Text=definition.Name.Length>0?definition.Name:"Empty position";slot.ItemsSource=definition.Slots.Prepend("Storage").Distinct(StringComparer.OrdinalIgnoreCase).ToList();slot.SelectedItem=value.Position==1?"Primary":value.Position==2&&inventory.DualWield?"Secondary":"Storage";FilterEffects();UpdateSave();
        };
        var inputs=new WrapPanel();inputs.Children.Add(UI.Field("Item",itemChoice));inputs.Children.Add(UI.Field("Rank",rank));
        var disclosure=FocusedLayout.Disclosure("Exaltations",effectsPanel,"Configure effects for this numbered position. Rank and compatibility restrictions remain visible.");
        var content=UI.Stack(heading,inputs,disclosure,error);
        Editor.Children.Add(content); Actions.Children.Add(save); var cancel = UI.Button("Cancel", Close); cancel.ToolTip = "Discard all uncommitted changes and close this editor."; Actions.Children.Add(cancel);
        SaveButton=save;save.IsDefault=true;save.IsEnabled=false;
        double compact=360;bool resizing=false;
        void Fit(bool expanded){if(resizing||!IsLoaded)return;resizing=true;try{if(expanded){compact=Height;content.Measure(new Size(Math.Max(300,ActualWidth-32),double.PositiveInfinity));Height=Math.Min(MaxHeight,Math.Max(compact,content.DesiredSize.Height+110));}else Height=Math.Min(MaxHeight,compact);}finally{resizing=false;}}
        disclosure.Expanded+=(_,_)=>Dispatcher.BeginInvoke(()=>Fit(true));disclosure.Collapsed+=(_,_)=>Dispatcher.BeginInvoke(()=>Fit(false));

        bool closed = false; Closed += (_, _) => closed = true;
        Loaded += async (_, _) =>
        {
            Usability.Tooltips(content);
            var catalog = new Catalog { Items = controller.Catalog.Items.ToList() }; var stock = System.Text.Json.JsonSerializer.Deserialize<InventorySnapshot>(System.Text.Json.JsonSerializer.Serialize(controller.Inventory))!;
            try { choices = await Task.Run(() => ExaltationPlanner.Build(catalog, stock)); if (closed) return; effectsLoaded = MetadataReady = true; FilterEffects();UpdateSave(); }
            catch (Exception e) { if (!closed) error.Text = "Effect metadata could not load. Close and retry. " + e.Message; }
        };
    }
}
