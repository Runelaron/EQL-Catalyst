namespace EqlTracker.Core;

// Actor-only choice: the secondary readout shares the overlay's time/filter
// context, but never writes its primary actor, scope or Combat Lab selection.
public sealed class DpsDisplayChoice
{
    public string Actor { get; set; } = "Pet";
    public OverlayCombatMode DisplayMode { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Value => DisplayMode == OverlayCombatMode.Group ? "Group" : Actor;

    public void Select(string value)
    {
        if (value is not ("Pet" or "Player" or "Combined" or "Group")) throw new ArgumentException("Unknown DPS display choice.", nameof(value));
        DisplayMode = value == "Group" ? OverlayCombatMode.Group : OverlayCombatMode.Personal;
        if (value != "Group") Actor = value;
    }

    public static DpsDisplayChoice From(DpsSelection selection) => new()
    {
        Actor = selection.Actor is "Pet" or "Player" or "Combined" ? selection.Actor : "Pet",
        DisplayMode = selection.DisplayMode
    };

    public static bool NeedsGroup(CharacterProfile profile) => profile.OverlayMeter?.DisplayMode == OverlayCombatMode.Group || profile.OverlayFooterDisplay?.DisplayMode == OverlayCombatMode.Group;

    public double? Rate(DpsViewState view, GroupProjection? group) => Value switch
    {
        "Group" => group?.Members.Any(m => m.Total.HasValue) == true ? group.Rate : null,
        "Player" => view.PlayerDps,
        "Combined" => view.CombinedDps,
        _ => view.PetDps
    };
}
