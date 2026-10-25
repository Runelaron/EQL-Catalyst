using EqlTracker.Core;
using EqlTracker.Companion;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    private readonly SemaphoreSlim phoneGate = new(1, 1);
    private CompanionHostSettings phoneSettings = new();
    private CompanionHost? phoneHost;
    private CancellationTokenSource? phoneCancel;
    private Task? phoneProjectionWorker;
    private string phoneStatus = "Disabled";
    private long phoneRevision;
    private string phoneMechanicalKey="";
    private IReadOnlyList<CompanionRow> phoneMechanicalRows=[];
    internal string PhoneStatus => PhoneHost is {Running:false,BoundaryStatus.Length:>0} paused?paused.BoundaryStatus:Volatile.Read(ref phoneStatus);
    internal CompanionHost? PhoneHost => Volatile.Read(ref phoneHost);
    internal CompanionHostSettings PhoneSettings => EquipmentPositions.Copy(phoneSettings);
    internal string SharingContext => PairingAuthority.Hash(Profile.Server.ToUpperInvariant() + "/" + Profile.Character.ToUpperInvariant());
    internal async Task ConfigurePhoneAsync(CompanionHostSettings settings)
    {
        await phoneGate.WaitAsync().ConfigureAwait(false);
        try
        {
            phoneCancel?.Cancel(); if (phoneProjectionWorker != null) try { await phoneProjectionWorker.ConfigureAwait(false); } catch (OperationCanceledException) { }
            if (phoneHost != null) await phoneHost.DisposeAsync().ConfigureAwait(false); phoneHost = null; phoneCancel?.Dispose(); phoneCancel = null;
            phoneSettings = EquipmentPositions.Copy(settings); var frozen = EquipmentPositions.Copy(settings); QueuePersistence(() => store.Set("phone-settings-v1", frozen));
            if (!settings.Enabled) { phoneStatus = "Disabled"; return; }
            phoneStatus = "Starting secure LAN host…";
            var host = await Task.Run(() => new CompanionHost(settings, Path.Combine(Path.GetDirectoryName(DataFolder)!, "companion-private"))).ConfigureAwait(false);
            try { await host.StartAsync(Path.Combine(AppContext.BaseDirectory, "companion-web"), cancel.Token).ConfigureAwait(false); }
            catch { await host.DisposeAsync().ConfigureAwait(false); throw; }
            phoneHost = host; phoneCancel = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token); var token = phoneCancel.Token;
            phoneStatus = "Connected on " + host.Address + " · " + host.DiscoveryStatus;
            phoneProjectionWorker = Task.Run(async () =>
            {
                string prior = "";
                while (!token.IsCancellationRequested)
                {
                    string key;lock(Sync)key=$"{equipmentRevision}|{petRevision}|{statProfileRevision}|{research.Revision}|{standardPetBuffs.Revision}|{integrated.Revision}|{journalRevision}|{Sessions?.Revision}|{Published?.Revision}|{Profile.SelectedLoadout}|{Profile.ActiveLoadout.SelectedPetBuildId}|{PetComparisonEngine.Key(Appearance.PhoneTokens)}|{integrated.Camps.Timer?.Due<=DateTime.Now}|{string.Join(',',integrated.Coverage.Where(c=>c.Fresh(DateTime.Now,Profile.Character)).Select(c=>c.Id))}";
                    if (key != prior) { prior = key; var projection=BuildPhoneProjection(Interlocked.Increment(ref phoneRevision)); host.Publish(projection); }
                    await Task.Delay(500, token).ConfigureAwait(false);
                }
            }, token);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { phoneStatus = "Phone sharing unavailable · " + error.Message; }
        finally { phoneGate.Release(); Updated?.Invoke(); }
    }
    private void StopPhone()
    { Task.Run(async () => { await phoneGate.WaitAsync(); try { phoneCancel?.Cancel(); if (phoneProjectionWorker != null) try { await phoneProjectionWorker; } catch (OperationCanceledException) { } if (phoneHost != null) await phoneHost.DisposeAsync(); phoneHost = null; } finally { phoneGate.Release(); } }).GetAwaiter().GetResult(); }
    internal CompanionSnapshot BuildPhoneProjection(long revision)
    {
        lock (Sync)
        {
            var build = Profile.ActiveLoadout.PetBuilds.FirstOrDefault(b=>b.Id==Profile.ActiveLoadout.SelectedPetBuildId)??PetInventoryEditing.Confirmed(Profile.ActiveLoadout, Catalog);
            string mechanicalKey=$"{equipmentRevision}|{petRevision}|{statProfileRevision}|{research.Revision}|{standardPetBuffs.Revision}|{Profile.SelectedLoadout}|{Profile.ActiveLoadout.SelectedPetBuildId}";
            List<CompanionRow> buildRows;
            if(mechanicalKey!=phoneMechanicalKey)
            {
            var stats = PetDetailsStatistics.Project(build, Profile.ActiveLoadout, Catalog, research);
            var buffView = StandardPetBuffs.Project(stats, build, standardPetBuffs, research, false);var scenario=StandardPetBuffs.Project(stats,build,standardPetBuffs,research,true);
            buildRows = new List<CompanionRow> { new("Selected setup", Profile.ActiveLoadout.Name + " · " + build.PetType, stats.Capture), new("Usual-buff scenario",scenario.Title+" · "+scenario.Conditions,string.Join("\n",scenario.Details)+"\nHistorical messages do not establish a complete current roster. Captured values below retain the desktop’s unchecked scenario default.") };
            var selfPositions=EquipmentPositions.For(Profile.ActiveLoadout,false);buildRows.Add(new("Self setup",Profile.ActiveLoadout.Name+" · "+string.Join("/",Profile.ActiveLoadout.Classes),"Equipment snapshot; complete current buffs and displayed totals are not inferred."));
            foreach(var position in selfPositions.Where(p=>p.Item!=null&&p.Knowledge==EquipmentKnowledge.Occupied))buildRows.Add(new("Self item · "+position.Item!.Name,position.Label,"Recorded equipment rank "+position.Item.Rank+". Selected effects remain distinct from spell buffs.",position.Item.ConfirmedItemKey));
            foreach (var stat in buffView.Values) buildRows.Add(new(stat.Key, stat.Value?.ToString("0.###") ?? "Unknown", buffView.Hints.GetValueOrDefault(stat.Key, "")));
            var contextOwner=GuidedPetBuilds.Context(Profile.ActiveLoadout,build);var positions=GuidedPetBuilds.Positions(build,contextOwner,Catalog);
            var sessionEvaluation=new LoadoutEvaluationSession(Catalog,contextOwner,true,new(),positions,positions,petInventory:build).CompareBuild(positions);
            var compatibility=sessionEvaluation.BuffedCompatibility;
            if(compatibility!=null)
            {
                buildRows.Add(new("Compatibility",compatibility.After.OrdinaryAttackSpeed?.ToString("0.###")??"Displayed ordinary attack speed unknown",string.Join("\n",compatibility.After.Notes)));
                if(!compatibility.CanClaimUpgrade)buildRows.Add(new("Contribution uncertainty","Some mechanics remain unresolved","Unknown effects do not become zero or establish an overall upgrade."));
            }
            foreach(var item in build.Items)buildRows.Add(new("Pet item · "+item.Name,"Rank "+item.Rank,"Recorded in "+build.Name+" · position "+item.Position+". Other stats and effects are retained; preference points do not predict DPS.",item.ItemKey));
            phoneMechanicalKey=mechanicalKey;phoneMechanicalRows=buildRows.ToArray();
            }
            else buildRows=phoneMechanicalRows.ToList();
            var bard = SongCoverage(); foreach (string condition in bard.Conditions) buildRows.Add(new("Song coverage", condition));
            foreach (var song in bard.Coverage) buildRows.Add(new(song.Caster + " · " + song.Song.Name, "Rank " + (song.Song.UpgradeRank?.ToString() ?? "Unknown"), "Confirmed recipient: " + song.Recipient + " · last observed " + song.At.ToString("g")));
            foreach (var suggestion in bard.Suggestions) buildRows.Add(new("Song option · gem " + suggestion.Gem, suggestion.Current.Name + " → " + suggestion.Replacement.Name, suggestion.Explanation));
            var goalHistory = ObjectiveQueue.Build(Profile, Catalog, Objectives, Inventory).Select(g => new CompanionRow(g.Goal.Name, g.Status, g.Destination, g.Goal.Id)).ToList();
            var recap = Recap();var lootHistory=recap?.Loot.Select(loot=>new CompanionRow("Loot · "+loot.Name,loot.Quantity+" acquired · "+loot.Disposition,loot.Purpose+"\n"+string.Join("\n",loot.Receipts.Take(20).Select(r=>$"{r.At:g} · {r.Quantity} × · {r.Disposition} · {r.Zone} · {r.Source}")),loot.Identity)).ToArray()??[];
            var goals=goalHistory.Take(100).Concat(lootHistory.Take(50)).ToList();
            var session = new List<CompanionRow>();
            if (recap != null)
            {
                if(recap.HistoryIncomplete)session.Add(new("Activity history","Partial retained history","Earlier receipts or actions may be missing."));
                session.Add(new("Session", recap.Session.Start.ToString("g"), "Last observed " + recap.Session.LastObserved.ToString("g") + " · " + recap.Session.Evidence));
                session.Add(new("Recorded combat", recap.Damage.ToString("N0") + " damage · " + recap.Encounters + " encounters", "Self " + recap.PlayerDamage.ToString("N0") + " · Pet " + recap.PetDamage.ToString("N0") + ". Damage differences are not causal build evidence."));
                session.Add(new("Recorded income", recap.Coin.HasValue ? recap.Coin.Value.ToString("N0") + " copper" : "Unknown"));
                session.Add(new("Merchant income", recap.MerchantIncome.HasValue ? recap.MerchantIncome.Value.ToString("N0") + " copper" : "Unknown"));
                foreach (var progress in recap.Progress) session.Add(new("Progress", progress.Name)); foreach (var setup in recap.Setup) session.Add(new(setup.Kind, setup.Name, setup.Kind == "Planned setup" ? "Saved in Catalyst only" : "Recorded observation"));
            }
            if (integrated.Camps.Timer is {} timer) session.Add(new("Camp estimate · " + timer.Name, timer.Label(DateTime.Now), "Silent estimate; no automatic rearming"));
            var holdings = otherHoldings.SelectMany(s => s.Items.Select(i => new CompanionRow(s.Character + " · " + i.Name, i.Quantity?.ToString() ?? "Present", i.Location + " · rank " + i.Rank + " · snapshot " + s.CapturedAt.ToString("g"), PairingAuthority.Hash(s.Server.ToUpperInvariant() + "/" + s.Character.ToUpperInvariant())))).Take(500).ToArray();
            CompanionRow Trim(CompanionRow row)=>row with{Label=row.Label.Length>256?row.Label[..256]+"…":row.Label,Value=row.Value.Length>512?row.Value[..512]+"…":row.Value,Detail=row.Detail.Length>2048?row.Detail[..2048]+"…":row.Detail};
            return new(revision, DateTimeOffset.UtcNow, SharingContext, Profile.Character + " · " + Profile.Server+" · "+build.Name+(recap!=null?" · session "+recap.Session.Start.ToString("MM/dd HH:mm"):""), Appearance.PhoneTokens, new("Build & song coverage", buildRows.Take(200).Select(Trim).ToArray()), new("Goals & session loot", goals.Select(Trim).ToArray()), new("Session recap", session.Take(100).Select(Trim).ToArray()), new("Other-character holdings", holdings.Take(200).Select(Trim).ToArray())) {History=new Dictionary<string,IReadOnlyList<CompanionRow>>{["goals"]=goalHistory.Select(Trim).ToArray(),["loot"]=lootHistory.Select(Trim).ToArray(),["session"]=session.Select(Trim).ToArray()}};
        }
    }
    internal IReadOnlyList<(string Id, string Name)> ShareableCharacters()
    { lock (Sync) return otherHoldings.Select(s => (PairingAuthority.Hash(s.Server.ToUpperInvariant() + "/" + s.Character.ToUpperInvariant()), s.Character)).Prepend((SharingContext, Profile.Character)).ToArray(); }
}
