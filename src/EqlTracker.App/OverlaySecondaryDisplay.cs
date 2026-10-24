using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class OverlayWindow
{
    private readonly DisplayCadence secondaryCadence = new();

    private void RefreshSecondaryDisplay(DpsViewState view)
    {
        var choice = Controller.Profile.OverlayFooterDisplay!;
        var group = Controller.Published?.OverlayGroup;
        if (choice.DisplayMode == OverlayCombatMode.Group && group == null && Controller.Demo)
            group = GroupProjection.Build(view, null, Controller.Profile, Controller.GroupPeriods, "demo-secondary-group");
        double? rate = choice.Rate(view, group);
        string context = choice.Value + "|" + MeterVisualContext.Key(Controller.Profile.OverlayMeter!, view, Controller.SessionStart) + "|" + (choice.DisplayMode == OverlayCombatMode.Group ? group?.Key ?? "pending" : "personal");
        bool due = secondaryCadence.Due(DisplayCadence.Context(view, context));
        if (!rate.HasValue) secondaryDps.Text = "—";
        else if (due) secondaryDps.SetNumber(rate.Value, false, true);
        string help = choice.DisplayMode == OverlayCombatMode.Group
            ? group == null ? "Group snapshot updating" : !rate.HasValue ? "Group member totals unavailable\n" + group.Coverage : $"{rate:N1} DPS · {group.Total:N0} observed damage · {group.Seconds:N0}s combat\n{group.Coverage}"
            : $"{rate:N1} DPS · {view.Seconds:N0}s combat\nPet {view.PetDps:N1} · Self {view.PlayerDps:N1} · Combined {view.CombinedDps:N1} DPS\n{(view.DetailComplete ? "Retained event details available" : "Summary records included; details incomplete")}\n" + DisplayCadence.DpsHelp;
        if(choice.Value=="Pet") help+="\n"+PetIdentityResolver.SessionEvidence(Controller.Profile,view.Fights);
        FooterDisplay(choice.Value, help);
    }
}
