namespace EqlTracker.Core;

public static class MeterSelectionRepair
{
    public static bool ClearIncomingSelfTarget(DpsSelection selection, string character)
    {
        if (!CombatIdentity.Same(selection.Target, "you") && !CombatIdentity.Same(selection.Target, character)) return false;
        selection.Target = ""; return true;
    }
}
