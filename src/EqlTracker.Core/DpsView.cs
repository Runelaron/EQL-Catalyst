namespace EqlTracker.Core;

public sealed class DpsSelection
{
    public string GroupPeriodId { get; set; } = "";
    public OverlayCombatMode DisplayMode { get; set; }
    public string Actor { get; set; } = "Pet";
    public string Scope { get; set; } = "Fight";
    public string Sort { get; set; } = "Damage";
    public string EncounterId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public List<string> ClassCombinations { get; set; } = [];
    public string Target { get; set; } = "";
    public string Ability { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool CriticalOnly { get; set; }
    public DateTime? From { get; set; }
    public DateTime? Until { get; set; }
    public CombatFilter Filter => new(Actor, Target, Ability, Kind, CriticalOnly, From, Until);
    public DpsSelection Copy() => new() { GroupPeriodId = GroupPeriodId, DisplayMode = DisplayMode, Actor = Actor, Scope = Scope, Sort = Sort, EncounterId = EncounterId, SessionId = SessionId, ClassCombinations = [..ClassCombinations], Target = Target, Ability = Ability, Kind = Kind, CriticalOnly = CriticalOnly, From = From, Until = Until };
    public void ClearFilters() { Target = Ability = Kind = ""; CriticalOnly = false; From = Until = null; ClassCombinations.Clear(); }
    public int FilterCount => (Target.Length > 0 ? 1 : 0) + (Ability.Length > 0 ? 1 : 0) + (Kind.Length > 0 ? 1 : 0) + (CriticalOnly ? 1 : 0) + (From.HasValue ? 1 : 0) + (Until.HasValue ? 1 : 0) + (ClassCombinations.Count > 0 ? 1 : 0);
}

public sealed record DpsViewState(List<Encounter> Fights, CombatFilter Filter, List<DamageRow> Rows,
    double Seconds, long Damage, double PetDps, double PlayerDps, double CombinedDps,
    long UnknownDamage, long MeleeDamage, long SpellDamage, bool DetailComplete, bool InCombat)
{
    public double Dps => Damage / Math.Max(1, Seconds);
    public long MaxHit => Rows.Select(x => x.MaxHit).DefaultIfEmpty(0).Max();
    public bool HasFilters => Filter.Target.Length > 0 || Filter.Ability.Length > 0 || Filter.Kind.Length > 0 || Filter.CriticalOnly || Filter.Until.HasValue;
    public List<string> TargetChoices { get; init; } = [];
    public List<TargetRow> Targets { get; init; } = [];
    public List<PeakHit> Peaks { get; init; } = [];
    public CombatChart Chart { get; init; } = new(default, 1, 1, [], [], []);
    public PlaySession? Session { get; init; }
}

public static class DpsViews
{
    public static DpsViewState Build(IEnumerable<Encounter> history, Encounter? current, DateTime sessionStart, DpsSelection selection, IReadOnlyList<PlaySession>? sessions = null)
    {
        var all = history.Concat(current == null ? [] : [current]).DistinctBy(x => x.Id).OrderByDescending(x => x.Start).ThenByDescending(x => x.End).ToList();
        var session = selection.Scope == "Session" ? sessions?.FirstOrDefault(s => s.Id == selection.SessionId) ?? sessions?.LastOrDefault() : null;
        var fights = selection.Scope switch
        {
            "Session" => all.Where(x => session == null ? x.End >= sessionStart : x.Start >= session.Start && (!session.NextLogin.HasValue || x.Start < session.NextLogin)).Where(x => session == null || selection.ClassCombinations.Count == 0 || selection.ClassCombinations.Contains(SessionEvidence.ClassesFor(session, x))).ToList(),
            "History" => all.Where(x => x.Id == selection.EncounterId).ToList(),
            _ => current != null ? new List<Encounter> { current } : all.Take(1).ToList()
        };
        var filter = selection.Filter;
        if (selection.Scope == "Session") { var start = session?.Start ?? sessionStart; if (!filter.From.HasValue || filter.From < start) filter = filter with { From = start }; }
        fights = fights.Where(x => (!filter.From.HasValue || x.End >= filter.From) && (!filter.Until.HasValue || x.Start <= filter.Until)).ToList();
        double seconds = CombatMetrics.Duration(fights, filter);
        var rows = Sort(DamageHealing.Apply(CombatMetrics.Breakdown(fights, filter), fights, filter), selection.Sort);
        if (sessions != null)
        {
            var kicks = fights.SelectMany(f => f.Events).Where(e => e.Kind == "Melee" && e.Ability.Contains("kick", StringComparison.OrdinalIgnoreCase) && CombatMetrics.Matches(e, filter)).ToLookup(e => e.Ability);
            rows = rows.Select(r => r.Kind == "Melee" && r.Ability.Contains("kick", StringComparison.OrdinalIgnoreCase) ? r with { MonkKick = kicks[r.Ability].Any() && kicks[r.Ability].All(e => e.Owner == "Player" && SessionEvidence.ClassesAt(SessionEvidence.For(sessions, e.Time), e.Time).Split('/').Contains("MNK")) } : r).ToList();
        }
        bool contentFilter = filter.Target.Length > 0 || filter.Ability.Length > 0 || filter.Kind.Length > 0 || filter.CriticalOnly;
        var totals = new Dictionary<string, long> { ["Player"] = 0, ["Pet"] = 0, ["Unknown"] = 0 };
        foreach (var fight in fights)
        {
            bool clipped = filter.From > fight.Start || filter.Until < fight.End;
            if (!fight.DetailAvailable && !contentFilter && !clipped)
            { totals["Player"] += fight.PlayerDamage; totals["Pet"] += fight.PetDamage; totals["Unknown"] += fight.UnknownDamage; continue; }
            foreach (var e in fight.Events)
                if (totals.ContainsKey(e.Owner) && CombatMetrics.Matches(e, filter with { Owner = e.Owner })) totals[e.Owner] += e.Damage;
        }
        long Total(string actor) => actor == "Combined" ? totals["Player"] + totals["Pet"] : totals.GetValueOrDefault(actor);
        double denominator = Math.Max(1, seconds);
        return new(fights, filter, rows, seconds, Total(filter.Owner), Total("Pet") / denominator,
            Total("Player") / denominator, Total("Combined") / denominator, Total("Unknown"),
            rows.Where(x => x.Kind == "Melee").Sum(x => x.Damage), rows.Where(x => x.Kind is "Spell" or "DoT").Sum(x => x.Damage),
            fights.All(x => x.DetailAvailable), current != null && !current.IdleResumable && fights.Any(x => x.Id == current.Id)) { Session = session, TargetChoices = fights.SelectMany(f => f.Events).Where(e => CombatMetrics.Matches(e, filter with { Target = "" })).Select(e => e.Target.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(), Targets = CombatMetrics.Targets(fights, filter), Peaks = CombatMetrics.Peaks(fights, filter), Chart = CombatChart.Build(fights, filter) };
    }
    public static List<DamageRow> Sort(IEnumerable<DamageRow> rows, string sort) => (sort switch
    {
        "Hits" => rows.OrderByDescending(x => x.Hits),
        "Average" => rows.OrderByDescending(x => x.Average),
        "DPS" => rows.OrderByDescending(x => x.Dps),
        _ => rows.OrderByDescending(x => x.Damage)
    }).ThenBy(x => x.Ability, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Kind, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Channel, StringComparer.OrdinalIgnoreCase).ToList();
}
