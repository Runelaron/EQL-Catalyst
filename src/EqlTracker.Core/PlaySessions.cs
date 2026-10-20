using System.Globalization;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public sealed class PlaySession
{
    public string Id { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime LastObserved { get; set; }
    public DateTime? NextLogin { get; set; }
    public bool Inferred { get; set; }
    public string Evidence { get; set; } = "";
    public List<ClassObservation> Classes { get; set; } = [];
    public List<GroupMembership> Members { get; set; } = [];
}
public sealed record ClassObservation(string Id, DateTime Time, string Combination, string Raw, string Source)
{ public DateTime? Until { get; init; } }
public sealed class SessionEvidence
{
    public List<PlaySession> Sessions { get; set; } = [];
    public FileCheckpoint? Checkpoint { get; set; }
    public long Revision { get; set; }
    public long ContentRevision { get; set; }
    public void Observe(string text, string id)
    {
        int close = text.IndexOf("] ", StringComparison.Ordinal);
        if (close < 0 || !DateTime.TryParseExact(text[1..close], ["ddd MMM dd HH:mm:ss yyyy", "ddd MMM d HH:mm:ss yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var at)) return;
        at = DateTime.SpecifyKind(at, DateTimeKind.Local); string message = text[(close + 2)..];
        bool login = message is "Welcome to EverQuest Legends!" or "Welcome to EverQuest!";
        var current = Sessions.LastOrDefault();
        if (login && Sessions.Any(s => s.Id == id)) return;
        if (current == null || login || current.Id.Split(':')[0] != id.Split(':')[0])
        {
            if (current != null) { current.NextLogin = at; foreach (var m in current.Members.Where(m => m.Until == null)) m.Until = at; }
            current = new() { Id = id, Start = at, LastObserved = at, Inferred = !login, Evidence = login ? "Verified login marker" : "First retained log record; login unavailable" }; Sessions.Add(current); ContentRevision++;
        }
        if (at > current.LastObserved) current.LastObserved = at;
        Revision++;
    }
    public void ObserveDetails(ParsedLine line, string id, string raw)
    {
        var s = Sessions.LastOrDefault(); if (s == null) return;
        if (line.Classes is { Count: > 0 })
        {
            string combo = Normalize(line.Classes);
            if (!s.Classes.Any(c => c.Id == id)) { s.Classes.Add(new(id, line.Time, combo, raw, "Character /who record")); ContentRevision++; }
        }
        if (line.JoinedGroup || line.LeftGroup || line.GroupRosterReset || line.GroupJoined != null || line.AcceptedInviter != null || line.GroupLeft != null) ContentRevision++;
        if (line.JoinedGroup || line.LeftGroup || line.GroupRosterReset) foreach (var m in s.Members.Where(m => m.Until == null)) if (!line.JoinedGroup || m.Source != "Accepted invitation" || Math.Abs((m.From - line.Time).TotalSeconds) > 2) m.Until = line.Time;
        string? member = line.GroupJoined ?? line.AcceptedInviter;
        if (member != null && !s.Members.Any(m => m.Contains(member, line.Time))) s.Members.Add(new() { Name = member, From = line.Time, Source = line.AcceptedInviter != null ? "Accepted invitation" : "Logged join" });
        if (line.GroupLeft is { } left) foreach (var m in s.Members.Where(m => CombatIdentity.Same(m.Name, left) && m.Until == null)) m.Until = line.Time;
    }
    public static string Normalize(IEnumerable<string> classes) => string.Join("/", classes.Select(c => c.ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal));
    public static string ClassesAt(PlaySession? session, DateTime time) => session?.Classes.Where(c => c.Time <= time && (!c.Until.HasValue || time < c.Until)).OrderByDescending(c => c.Source == "Manual correction").ThenByDescending(c => c.Time).FirstOrDefault()?.Combination ?? "Unknown";
    public static string ClassesFor(PlaySession? session, Encounter fight)
    {
        string first = ClassesAt(session, fight.Start);
        return session?.Classes.SelectMany(c => c.Until.HasValue ? new[] { c.Time, c.Until.Value } : new[] { c.Time }).Any(t => t > fight.Start && t <= fight.End && ClassesAt(session, t) != first) == true ? "Mixed" : first;
    }
    public static PlaySession? For(IReadOnlyList<PlaySession> sessions, DateTime at) => sessions.LastOrDefault(s => s.Start <= at && (!s.NextLogin.HasValue || at < s.NextLogin));
}
public sealed record SessionChoice(string Id, string Label) { public override string ToString() => Label; }
public sealed record SessionView(long Revision, IReadOnlyList<PlaySession> Sessions, IReadOnlyList<SessionChoice> Choices) { public long ContentRevision { get; init; } }
public sealed record EncounterHistoryRow(Encounter Fight, long? Total, double? Rate, string Classes)
{
    public string Id => Fight.Id;
    public DateTime Started => Fight.Start;
    public string Targets => Fight.Targets.Length > 0 ? Fight.Targets : "Targets unavailable";
    public string Zone => Fight.Zone;
    public double Duration => Fight.Duration;
    public string Pet => Fight.PetType.Length > 0 ? Fight.PetType : "Unknown";
}
