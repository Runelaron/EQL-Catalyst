using EqlTracker.Core;

namespace EqlTracker.App;

public sealed record ExportEvidence(string Id, DateTime Time, string File);
public sealed partial class TrackerController
{
    public SyncRequest SyncRequest { get; private set; } = new();
    public DateTime? WorldEvidenceAt { get; private set; }
    public long WorldSession { get; private set; }
    private ExportEvidence? latestExport;
    public string GameLogPath => string.IsNullOrWhiteSpace(Profile.GameFolder) ? "" : Path.Combine(Profile.GameFolder, "Logs", $"eqlog_{Profile.Character}_{Profile.Server}.txt");
    public string InventoryPath => string.IsNullOrWhiteSpace(Profile.GameFolder) ? "" : Path.Combine(Profile.GameFolder, $"{Profile.Character}_{Profile.Server}-Inventory.txt");
    public void BeginSync()
    {
        lock (Sync)
        {
            if (SyncRequest.Active) return;
            var file = new FileInfo(GameLogPath);
            if (!file.Exists || checkpoint == null) throw new InvalidOperationException("Wait for the character log reader before testing Sync.");
            SyncRequest = new() { State = "Requested", RequestedAt = DateTime.Now, BaselineLength = file.Length, Generation = checkpoint.Generation, InventoryWriteUtc = File.Exists(InventoryPath) ? File.GetLastWriteTimeUtc(InventoryPath) : DateTime.MinValue, Message = "Awaiting fresh logging, class and export acknowledgements…" };
        }
    }
    public void SyncFailed(string message) { lock (Sync) { SyncRequest.State = "Failed"; SyncRequest.Message = message; Profile.NativeSync.Verified = false; Save(); } }
    public void CheckSync()
    {
        lock (Sync)
        {
            var request = SyncRequest; if (!request.Active) return;
            if (request.Outcome(DateTime.Now) == "Complete")
            { request.State = "Complete"; request.Message = "Verified logging, classes and fresh inventory."; Profile.NativeSync.Verified = true; Profile.NativeSync.LastSuccess = DateTime.Now; Save(); }
            else if ((DateTime.Now - request.RequestedAt).TotalSeconds >= 15)
            { request.State = request.Who || request.Export || request.Logging ? "Partial" : "Failed"; request.Message = "Sync was not verified. Missing: " + string.Join(", ", new[] { request.Logging ? "" : "logging", request.Who ? "" : "classes", request.Export && request.Imported ? "" : "fresh complete inventory" }.Where(x => x.Length > 0)) + ". Test the linked native shortcut again."; Profile.NativeSync.Verified = false; Save(); }
            else request.State = "Awaiting evidence";
        }
    }
    private void ObserveSync(ParsedLine parsed, string id, bool historical)
    {
        if (parsed.Time >= DateTime.Now.AddMinutes(-2) && (parsed.Zone != null || parsed.RosterObserved || parsed.Combat?.Owner == "Player")) { if (WorldEvidenceAt == null) WorldSession++; WorldEvidenceAt = parsed.Time; }
        if (!historical && parsed.Boundary && parsed.Zone == null) WorldEvidenceAt = null;
        if (parsed.InventoryExport?.Equals(Path.GetFileName(InventoryPath), StringComparison.OrdinalIgnoreCase) == true)
        { latestExport = new(id, parsed.Time, parsed.InventoryExport); var exportEvidence = latestExport; QueuePersistence(() => store.Set("inventoryExportEvidence", exportEvidence)); }
        var request = SyncRequest;
        if (!request.Active || historical || !LogPosition.After(id, request.Generation + ":" + (request.BaselineLength - 1))) return;
        if (parsed.LoggingEnabled) request.Logging = true;
        if (parsed.RosterObserved) request.Who = true;
        if (parsed.InventoryExport?.Equals(Path.GetFileName(InventoryPath), StringComparison.OrdinalIgnoreCase) == true) request.Export = true;
    }
}
