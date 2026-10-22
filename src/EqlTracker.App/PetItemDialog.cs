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
    public bool ReplaceOccupied => replace.IsChecked == true;
    internal Button SaveButton { get; private set; } = null!;
    internal ComboBox ItemChoice { get; private set; } = null!;
    internal bool MetadataReady { get; private set; }
    internal TextBlock ErrorText => error;
    internal TextBox PositionInput { get; private set; } = null!;
    private readonly CheckBox replace = new() { Content = "Replace occupied position" };
    private readonly TextBlock error = UI.Text("", 11, "Violet");
    public PetItemDialog(TrackerController controller, ItemDefinition? definition, PetBuild inventory, PetInventoryDestination destination, PetInventoryItem? existing = null, int? rankOverride = null, int? positionOverride = null, Func<PetBuild?>? latest = null) : base("Edit pet slot", 650, 620)
    {
        PetSlotPlanning.Migrate(inventory);
        string initialRevision = System.Text.Json.JsonSerializer.Serialize(inventory);
        string editingId = existing?.Id ?? "";
        definition ??= new ItemDefinition();
        string target = destination == PetInventoryDestination.Confirmed ? "Confirmed inventory · manual" : "Planned build · " + inventory.Name;
        Heading.Children.Add(UI.Text(target, 11, "Mint"));
        var value = existing == null ? new PetInventoryItem { ItemKey = definition.Key, Name = definition.Name, Position = PetInventoryEditing.NextPosition(inventory), Slot = definition.Slots.FirstOrDefault() ?? "Storage" } : System.Text.Json.JsonSerializer.Deserialize<PetInventoryItem>(System.Text.Json.JsonSerializer.Serialize(existing))!;
        value.Exaltations = EquipmentPositions.Copy(inventory.SlotConfigurations.FirstOrDefault(s => s.Position == (positionOverride ?? value.Position))?.Effects ?? []);
        if (rankOverride.HasValue) value.Rank = rankOverride.Value; if (positionOverride.HasValue) value.Position = positionOverride.Value;
        var verified = new CheckBox { Content = "Verified equipped slot in game", IsChecked = value.EquipmentVerified };
        verified.Visibility = destination == PetInventoryDestination.Confirmed ? Visibility.Visible : Visibility.Collapsed;
        verified.ToolTip = "Inventory presence and actual equipped-slot verification are separate. Check only after inspecting the pet's equipment in game.";
        var position = UI.Input(value.Position.ToString(), 65); var rank = UI.Input(value.Rank.ToString(), 65);
        PositionInput = position;
        var slot = UI.Select(definition.Slots.Prepend("Storage").Concat([value.Slot]).Distinct(StringComparer.OrdinalIgnoreCase), value.Slot, 160);
        slot.SelectedItem = slot.Items.Cast<string>().First(s => s.Equals(value.Slot, StringComparison.OrdinalIgnoreCase));
        var heading = UI.Text(definition.Name.Length > 0 ? definition.Name : "Empty position", 17, "Accent", true); heading.ToolTip = "Explicit advanced editing of this saved inventory entry; no game items are changed.";
        var summary = UI.Text(destination == PetInventoryDestination.Confirmed ? "Save your observed inventory details." : "Changes apply only to this plan.", 12, "Mint");
        var inputs = new WrapPanel(); foreach (var field in new UIElement[] { UI.Field("Position", position), UI.Field("Intended slot", slot), UI.Field("+rank", rank) }) inputs.Children.Add(field);
        position.ToolTip = "Numbered storage position. Occupied positions are preserved unless replacement is explicitly enabled."; slot.ToolTip = "Intended wearable slot, or Storage to retain an unequipped item. Item class and slot restrictions remain visible in the list."; rank.ToolTip = "Item upgrade rank, 0–20. This is separate from the pet's level."; replace.ToolTip = "Explicitly replace an item at the chosen position. Leave off to prevent accidental overwrites.";
        var effects = UI.Table(("Source", "SourceName", 0, null), ("Effect", "Effect", 160, null), ("State", "State", 80, null), ("Availability", "Status", 140, null)); effects.Height = 100; effects.FontSize = 11;
        void RefreshEffects() => UI.SetRows(effects, value.Exaltations.Select(e => {
            var source = controller.Catalog.Find(e.SourceName);string status = "Inactive · choose an item";
            if (definition.Key.Length > 0) {
                int.TryParse(rank.Text, out int tier);string type = ExaltationPlanner.EffectType(e.Effect);
                status = source == null ? "Unverified source" : ExaltationPlanner.Validate(new(e.EffectId,source,type,e.Effect,ExaltationPlanner.Unlock(type),false,false),definition,tier,slot.SelectedItem?.ToString()??"Storage",GuidedPetBuilds.Context(controller.Profile.ActiveLoadout,inventory),true,controller.Catalog);
                if (status != "Compatible") status = "Inactive · " + status;
            }
            return new AssignedRow(e,status);
        }).ToList()); RefreshEffects();
        var effectSearch = UI.Input("", 200);
        var effectType = UI.Select(["All effects", "Proc", "Focus", "Click", "Worn", "Ornamentation", "Unknown"], definition.Stats.GetValueOrDefault("Delay") > 0 ? "Proc" : "All effects", 105);
        var effectOwnership = UI.Select(["All sources", "Owned effects", "Source owned"], "All sources", 120);
        var state = UI.Select(["Planned", "Socketed"], destination == PetInventoryDestination.Confirmed ? "Socketed" : "Planned", 95);
        var options = UI.Table(("Source", "Source", 0, null), ("Effect", "Effect", 140, null), ("Type", "Type", 60, null), ("Unlock Rank", "UnlockRank", 75, null), ("Compatibility", "Compatibility", 130, null)); options.Height = 150; options.FontSize = 11;
        TableLayout.SetKey(options, "pet.editor.exaltation-search"); TableLayout.SetKey(effects, "pet.editor.assignments");
        var replaceEffect = new CheckBox { Content = "Replace existing effect of this type", ToolTip = "Explicitly replace the socketed/planned effect occupying the same effect-type slot. Intrinsic item effects are separate." };
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
        effectSearch.TextChanged += (_, _) => FilterEffects(); effectType.SelectionChanged += (_, _) => FilterEffects(); effectOwnership.SelectionChanged += (_, _) => FilterEffects(); rank.TextChanged += (_, _) => FilterEffects(); slot.SelectionChanged += (_, _) => FilterEffects();
        var addEffect = UI.Button("Add effect", () =>
        {
            if (options.SelectedItem is not EffectChoice choice) { error.Text = "Select a result from the Exaltation search."; return; }
            if (!int.TryParse(rank.Text, out int selectedRank)) { error.Text = "Enter a valid +rank."; return; }
            if (definition.Key.Length == 0) {
                if (value.Exaltations.Any(e => ExaltationPlanner.EffectType(e.Effect) == choice.Type) && replaceEffect.IsChecked != true) { error.Text = "This effect slot is occupied. Enable replacement first."; return; }
                value.Exaltations.RemoveAll(e => ExaltationPlanner.EffectType(e.Effect) == choice.Type);
                value.Exaltations.Add(new() { EffectId = choice.Option.Id, SourceName = choice.Source, Effect = choice.Effect, State = "Planned", Target = "Pet" });
                RefreshEffects(); error.Text = "Effect configured; inactive until a compatible item is present."; return;
            }
            if (!ExaltationPlanner.Assign(value, choice.Option, definition, selectedRank, slot.SelectedItem?.ToString() ?? "Storage", effectContext, controller.Catalog, state.SelectedItem?.ToString() ?? "Planned", replaceEffect.IsChecked == true, out string issue)) { error.Text = issue; return; }
            RefreshEffects(); replaceEffect.IsChecked = false;
            error.Text = choice.Option.Source.Name.Equals("Earthshaker", StringComparison.OrdinalIgnoreCase) ? "Socket assignment allowed. Earthquake activation requires level 45; pet AoE-proc evidence conflicts and needs client verification." : "Effect added. Proc activation is checked separately from socket compatibility.";
        });
        var removeEffect = UI.Button("Remove effect", () => { if (effects.SelectedItem is AssignedRow row) { value.Exaltations.Remove(row.Assignment); RefreshEffects(); } });
        var effectControls = new WrapPanel(); foreach (var child in new UIElement[] { UI.Field("Search Exaltations", effectSearch), UI.Field("Effect type", effectType), UI.Field("Ownership", effectOwnership) }) effectControls.Children.Add(child);
        var effectActions = new WrapPanel(); foreach (var child in new UIElement[] { UI.Field("Record as", state), UI.ActionField(addEffect), UI.ActionField(removeEffect) }) effectActions.Children.Add(child);
        var save = UI.Button(destination == PetInventoryDestination.Confirmed ? "Save manual confirmation" : "Save planned item", () =>
        {
            if (latest != null && (latest() is not { } current || System.Text.Json.JsonSerializer.Serialize(current) != initialRevision)) { error.Text = "This build changed while the editor was open. Reopen the slot to review current equipment."; return; }
            if (!effectsLoaded && value.Exaltations.Count > 0) { error.Text = "Effect metadata is loading; please wait before saving socketed effects."; return; }
            if (!int.TryParse(position.Text, out int pos) || pos < 1 || !int.TryParse(rank.Text, out int tier) || tier is < 0 or > 20) { error.Text = "Use a positive position and +rank 0–20."; return; }
            if (inventory.Items.Any(i => i.Position == pos && i.Id != editingId) && !ReplaceOccupied) { error.Text = "That position is occupied. Choose another position or explicitly enable replacement."; return; }
            value.Position = pos; value.Rank = tier; value.Slot = slot.SelectedItem?.ToString() ?? "Storage";
            value.EquipmentVerified = destination == PetInventoryDestination.Confirmed && verified.IsChecked == true; value.ConfirmedAt = destination == PetInventoryDestination.Confirmed ? DateTime.Now : null; Result = value; DialogResult = true;
        }, true);
        var effectsPanel = UI.Stack(effectControls, options, replaceEffect, effectActions, UI.Label("Assigned effects"), effects);
        var itemChoice = ItemChoice = UI.Select([], "", 360); itemChoice.IsEditable = true; itemChoice.IsTextSearchEnabled = true;
        var definitions = controller.Catalog.Items.Where(i => i.Slots.Count > 0).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        itemChoice.ItemsSource = new[] { "Empty position" }.Concat(definitions.Select(i => i.Name)).ToList(); itemChoice.SelectedItem = definition.Name.Length > 0 ? definition.Name : "Empty position";
        itemChoice.ToolTip = "Choose the item recorded in this position. Configured slot effects are retained; intrinsic effects follow the item.";
        itemChoice.SelectionChanged += (_, _) => {
            var selected = definitions.FirstOrDefault(i => i.Name == itemChoice.SelectedItem?.ToString());
            if (selected?.Key == value.ItemKey || selected == null && value.ItemKey.Length == 0) return;
            definition = selected ?? new ItemDefinition(); value.Id = Guid.NewGuid().ToString("N"); value.ItemKey = definition.Key; value.Name = definition.Name; value.Rank = 0; rank.Text = "0"; value.TransferredOutEffects = []; foreach (var effect in value.Exaltations) effect.State = "Planned"; verified.IsChecked = false;
            heading.Text = definition.Name.Length > 0 ? definition.Name : "Empty position"; slot.ItemsSource = definition.Slots.Prepend("Storage").Distinct(StringComparer.OrdinalIgnoreCase).ToList(); slot.SelectedItem = "Storage"; FilterEffects();
        };
        var content = UI.Stack(heading, summary, UI.Field("Item", itemChoice), inputs, replace, verified, FocusedLayout.Disclosure("Exaltations", effectsPanel, "Review or edit socketed and planned effects. Rank restrictions are checked before saving."), error);
        Editor.Children.Add(content); Actions.Children.Add(save); var cancel = UI.Button("Cancel", Close); cancel.ToolTip = "Discard all uncommitted changes and close this editor."; Actions.Children.Add(cancel);
        SaveButton = save; save.IsDefault = true;

        bool closed = false; Closed += (_, _) => closed = true;
        Loaded += async (_, _) =>
        {
            Usability.Tooltips(content);
            var catalog = new Catalog { Items = controller.Catalog.Items.ToList() }; var stock = System.Text.Json.JsonSerializer.Deserialize<InventorySnapshot>(System.Text.Json.JsonSerializer.Serialize(controller.Inventory))!;
            try { choices = await Task.Run(() => ExaltationPlanner.Build(catalog, stock)); if (closed) return; effectsLoaded = MetadataReady = true; FilterEffects(); }
            catch (Exception e) { if (!closed) error.Text = "Effect metadata could not load. Close and retry. " + e.Message; }
        };
    }
}
