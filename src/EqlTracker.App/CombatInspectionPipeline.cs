using System.Text.Json;
using System.Net.Http;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    private readonly Dictionary<string, AbilityOutcome> abilityOutcomes = [];
    private readonly List<AbilityOutcome> pendingAbilityOutcomes = [];
    private readonly Dictionary<string, SpellReference> spellReferences = [];
    private long outcomeRevision, referenceRevision, inspectionIdentityRevision;
    private FileCheckpoint? outcomeCheckpoint;
    private DateTime? outcomeFirst, outcomeLast;
    private bool outcomesCaughtUp;
    private Task? inspectionWorker;
    private string inspectionRequested = "";
    private CombatInspectionSnapshot? inspection;
    public string OutcomeCoverage => outcomesCaughtUp ? "retained log checked" : "log scan incomplete";
    private void InitializeInspection()
    {
        foreach (var o in store.LoadAbilityOutcomes().Where(o => o.Time >= DateTime.Now.AddDays(-30))) abilityOutcomes[o.Id] = o;
        foreach (var r in store.Get<List<SpellReference>>("spellReferences") ?? []) { spellReferences[EffectReferences.Key(r.Name)] = r;spellReferences.TryAdd(CombatInspection.IdentityKey(r.Name),r); }
        string bundled = Path.Combine(AppContext.BaseDirectory, "data", "spell-references.json");
        if (File.Exists(bundled)) foreach (var r in Json.Read<List<SpellReference>>(bundled)) if (!spellReferences.TryGetValue(CombatInspection.IdentityKey(r.Name), out var cached) || cached.Revision < r.Revision) spellReferences[EffectReferences.Key(r.Name)] = r;
        effectReferenceProgress=store.Get<EffectReferenceProgress>("effectReferenceProgress")??new();
        referenceAliases=store.Get<Dictionary<string,string>>("referenceAliases")??[];
        foreach(var alias in referenceAliases)if(spellReferences.TryGetValue(alias.Value,out var canonical))spellReferences[alias.Key]=canonical;
        string fixtures=Path.Combine(AppContext.BaseDirectory,"data","effect-reference-fixtures.json");
        if(File.Exists(fixtures))foreach(var fixture in Json.Read<List<EffectReferenceFixture>>(fixtures))
        {
            string key=EffectReferences.Key(fixture.Name);
            if(SpellReference.Parse(fixture.Name,fixture.Text,fixture.Revision) is {} parsed&&(!spellReferences.TryGetValue(key,out var prior)||prior.Revision<fixture.Revision||prior.Revision==fixture.Revision&&prior.ParserVersion<parsed.ParserVersion))spellReferences[key]=parsed with {FetchedAt=fixture.VerifiedAt};
        }
        outcomeCheckpoint = store.Get<FileCheckpoint>("abilityOutcomeCheckpoint"); outcomeFirst = store.Get<DateTime?>("abilityOutcomeFirst"); outcomeLast = store.Get<DateTime?>("abilityOutcomeLast");
    }
    private void ObserveAbilityOutcome(AbilityOutcome? value)
    {
        if (value == null || value.Time < DateTime.Now.AddDays(-30) || !abilityOutcomes.TryAdd(value.Id, value)) return;
        outcomeRevision++; pendingAbilityOutcomes.Add(value);
    }
    private void FlushAbilityOutcomes() { if (pendingAbilityOutcomes.Count == 0) return; var values = pendingAbilityOutcomes.ToArray(); pendingAbilityOutcomes.Clear(); QueuePersistence(() => store.SaveAbilityOutcomes(values)); }
    private void BackfillAbilityOutcomes()
    {
        var source = checkpoint; if (source == null || outcomesCaughtUp) return;
        if (outcomeCheckpoint?.Generation != source.Generation) { outcomeCheckpoint = new() { Path = source.Path, Generation = source.Generation, CreationTicks = source.CreationTicks, PrefixHash = source.PrefixHash, PrefixLength = source.PrefixLength }; outcomeFirst = outcomeLast = null; }
        var batch = LogTailer.Read(source.Path, outcomeCheckpoint, 512 * 1024); if (batch.Reset) { outcomeCheckpoint = null; return; }
        var parser = new LogParser(Profile.Character); var parsed = batch.Lines.Select(l => parser.Parse(l.Text, l.Id)).Where(l => l != null).ToArray();
        var retained = parsed.Select(l => l!.AbilityOutcome).Where(o => o != null && o.Time >= DateTime.Now.AddDays(-30)).Cast<AbilityOutcome>().ToArray();
        lock (Sync)
        {
            foreach (var o in retained) if (abilityOutcomes.TryAdd(o.Id, o)) outcomeRevision++;
            foreach (var p in parsed) { if (outcomeFirst == null || p!.Time < outcomeFirst) outcomeFirst = p!.Time; if (outcomeLast == null || p!.Time > outcomeLast) outcomeLast = p!.Time; }
            if (outcomesCaughtUp != batch.CaughtUp) outcomeRevision++;
            outcomesCaughtUp = batch.CaughtUp; outcomeCheckpoint = batch.Checkpoint;
            var saved = EquipmentPositions.Copy(batch.Checkpoint); var first = outcomeFirst; var last = outcomeLast;
            QueuePersistence(() => store.SaveAbilityOutcomes(retained, saved, first, last));
        }
    }
    public void RequestSpellReference(string ability) => QueueReference(ability);
    public CombatInspectionSnapshot? InspectionView(bool healingMode, bool groupView = false)
    {
        var damage = DpsView(); var healing = healingMode ? HealingView() : null;
        string key = $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(damage)}:{(healing == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(healing))}:{Volatile.Read(ref outcomeRevision)}:{Volatile.Read(ref referenceRevision)}:{healingMode}:{groupView}:{CombatRevision}:{Volatile.Read(ref inspectionIdentityRevision)}:{Volatile.Read(ref groupRevision)}";
        var ready = Volatile.Read(ref inspection); if (!Demo && ready?.Key == key) return ready;
        if (inspectionWorker is { IsCompleted: false } || inspectionRequested == key && !Demo) return ready?.Key == key ? ready : null;
        inspectionRequested = key;
        CombatInspectionSnapshot Build()
        {
            CharacterProfile frozen; AbilityOutcome[] outcomes; Dictionary<string, SpellReference> references; string coverage;
            lock (Sync)
            {
                frozen = new() { Character = Profile.Character, Meter = EquipmentPositions.Copy(Profile.Meter), Healing = EquipmentPositions.Copy(Profile.Healing), GroupMembers = Profile.GroupMembers.Select(EquipmentPositions.Copy).ToList(), PetIdentities = Profile.PetIdentities.Select(EquipmentPositions.Copy).ToList() };
                if (damage.Session is { } savedSession) frozen.GroupMembers = savedSession.Members.Concat(frozen.GroupMembers.Where(m => m.Source == "Manual")).Select(EquipmentPositions.Copy).ToList();
                outcomes = abilityOutcomes.Values.ToArray(); references = new(spellReferences);
                DateTime? last = LastLogActivity > outcomeLast ? LastLogActivity : outcomeLast;
                coverage = outcomesCaughtUp && outcomeFirst.HasValue && last.HasValue && damage.Fights.All(f => f.Start >= outcomeFirst && f.End <= last) ? "selected interval checked in retained log" : "selected interval has incomplete log coverage";
                if (Demo) coverage = "illustrative diagnostic fixture";
            }
            using var timing = UpdateDiagnostics.Measure("Ability + group inspection snapshot");
            var value = CombatInspection.Build(damage, healing, frozen, outcomes, references, key, healingMode, coverage, false, !groupView, cancel.Token);
            if (!groupView) return value;
            var projection = GroupProjection.Build(damage, healing, frozen, GroupPeriods, key, cancel.Token);
            return value with { Members = projection.Members, Coverage = projection.Coverage };
        }
        if (Demo) { ready = Build(); Volatile.Write(ref inspection, ready); return ready; }
        inspectionWorker = Task.Run(() => { try { var value = Build(); if (!cancel.IsCancellationRequested) { Volatile.Write(ref inspection, value); Updated?.Invoke(); } } catch (OperationCanceledException) { } }, cancel.Token);
        return ready?.Key == key ? ready : null;
    }
    internal async Task ValidateAbilityOutcomesAsync(string folder)
    {
        var before = Combat.Encounters.Select(f => (f.Id, f.Start, f.End, f.Damage)).ToArray();
        for (int i = 0; i < 10000 && !outcomesCaughtUp; i++) { BackfillAbilityOutcomes(); await Task.Delay(1); }
        await DrainPersistenceAsync();
        var validationParser = new LogParser(Profile.Character);
        var expected = checkpoint == null ? [] : File.ReadLines(checkpoint.Path).Select((s, i) => validationParser.Parse(s, "validation:" + i)?.AbilityOutcome).Where(o => o != null && o.Time >= DateTime.Now.AddDays(-30)).ToArray();
        bool unchanged = before.SequenceEqual(Combat.Encounters.Select(f => (f.Id, f.Start, f.End, f.Damage)));
        if (!unchanged || abilityOutcomes.Count != expected.Length) throw new InvalidDataException("Ability outcome backfill reconciliation failed.");
        Json.Write(Path.Combine(folder, "ability-outcomes-report.json"), new { Passed = true, Imported = abilityOutcomes.Count, Expected = expected.Length, DamageAndTimingUnchanged = unchanged, outcomesCaughtUp });
    }
}
