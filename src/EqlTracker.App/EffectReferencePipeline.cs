using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using EqlTracker.Core;

namespace EqlTracker.App;
public sealed class EffectReferenceProgress
{
    public string CatalogKey { get; set; }="";
    public int Next { get; set; }
    public Dictionary<string,DateTime> Failed { get; set; }=[];
}
public sealed partial class TrackerController
{
    private Task? effectReferenceWorker;
    private readonly ConcurrentQueue<(string Name,bool Exact)> referencePriority=new();
    private readonly ConcurrentDictionary<string,byte> referenceQueued=new();
    private readonly ConcurrentDictionary<string,byte> combatReferenceInterests=new();
    private EffectReferenceProgress effectReferenceProgress=new();
    private Dictionary<string,string> referenceAliases=[];
    public long ReferenceRevision=>Interlocked.Read(ref referenceRevision);
    internal string EffectReferenceVersion(string effect)
    {
        string key=EffectReferences.Key(effect);
        lock(Sync)return spellReferences.TryGetValue(key,out var r)?$"{r.Revision}:{r.ParserVersion}:{r.FetchedAt.Ticks}":effectReferenceProgress.Failed.TryGetValue(key,out var at)?"failed:"+at.Ticks:"pending";
    }
    public EffectReferenceSnapshot EffectReference(string effect,ItemDefinition source)
    {
        string name=EffectReferences.Name(effect),key=EffectReferences.Key(name);SpellReference? reference;bool failed;
        lock(Sync){spellReferences.TryGetValue(key,out reference);failed=effectReferenceProgress.Failed.ContainsKey(key);}
        QueueReference(name,true);
        return EffectReferences.Build(effect,source,reference,failed||Demo);
    }
    private void QueueReference(string ability,bool exact=false)
    {
        string name=EffectReferences.Name(ability.Replace(" · Lifetap","")),key=exact?EffectReferences.Key(name):CombatInspection.IdentityKey(name);
        if(!exact&&combatReferenceInterests.TryAdd(key,0))Interlocked.Increment(ref referenceRevision);
        lock(Sync)
        {
            if(Demo||cancel.IsCancellationRequested||name.Length==0||spellReferences.TryGetValue(key,out var reference)&&reference.ParserVersion>=EffectReferences.ParserVersion&&reference.FetchedAt>DateTime.UtcNow.AddDays(-1)||effectReferenceProgress.Failed.TryGetValue(key,out var failed)&&failed>DateTime.UtcNow.AddMinutes(-5))return;
        }
        if(referenceQueued.TryAdd((exact?"effect:":"combat:")+key,0))referencePriority.Enqueue((name,exact));
    }
    private async Task EffectReferenceLoop()
    {
        using var client=new WikiClient();long catalogSeen=-1;string[] names=[];int next=0;DateTime completed=DateTime.MinValue,nextCatalogScan=DateTime.MinValue;
        try
        {
            while(!cancel.IsCancellationRequested)
            {
                var equipment=EquipmentView;
                if(equipment!=null&&catalogSeen!=CatalogRevision&&DateTime.UtcNow>=nextCatalogScan)
                {
                    catalogSeen=CatalogRevision;nextCatalogScan=DateTime.UtcNow.AddMinutes(1);
                    // Build a resumable catalog once per catalog revision, outside ingestion locks.
                    var owned=equipment.Inventory.Entries.Select(e=>Identity.Key(e.Name)).ToHashSet();
                    names=equipment.Catalog.Items.OrderByDescending(i=>owned.Contains(Identity.Key(i.Name))).ThenBy(i=>i.Key,StringComparer.Ordinal).SelectMany(i=>i.Effects).Select(EffectReferences.Name).Where(n=>n.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\n',names))));
                    lock(Sync){if(effectReferenceProgress.CatalogKey!=key){effectReferenceProgress.CatalogKey=key;effectReferenceProgress.Next=0;}next=Math.Min(effectReferenceProgress.Next,names.Length);}
                }
                string name;bool exact=true,background=false;
                if(!referencePriority.TryDequeue(out var request))
                {
                    if(next>=names.Length){if(names.Length>0&&DateTime.UtcNow-completed>TimeSpan.FromMinutes(5)){next=0;completed=DateTime.UtcNow;}else{await Task.Delay(1000,cancel.Token);continue;}}
                    name=names[next++];background=true;
                }
                else{name=request.Name;exact=request.Exact;}
                string lookup=exact?EffectReferences.Key(name):CombatInspection.IdentityKey(name);bool ready;
                lock(Sync)ready=spellReferences.TryGetValue(lookup,out var cached)&&cached.ParserVersion>=EffectReferences.ParserVersion&&cached.FetchedAt>DateTime.UtcNow.AddDays(-1)||effectReferenceProgress.Failed.TryGetValue(lookup,out var last)&&last>DateTime.UtcNow.AddMinutes(-5);
                try
                {
                    if(!ready)
                    {
                        SpellReference? reference=null;
                        try{reference=await client.SpellReferenceAsync(name,cancel.Token,exact);}
                        catch(Exception error)when(error is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException){if(cancel.IsCancellationRequested)break;}
                        lock(Sync)
                        {
                            if(reference!=null){spellReferences[lookup]=reference;spellReferences[EffectReferences.Key(reference.Name)]=reference;referenceAliases[lookup]=EffectReferences.Key(reference.Name);effectReferenceProgress.Failed.Remove(lookup);if(combatReferenceInterests.ContainsKey(CombatInspection.IdentityKey(name)))Interlocked.Increment(ref referenceRevision);var records=spellReferences.Values.Distinct().ToArray();var aliases=new Dictionary<string,string>(referenceAliases);QueuePersistence(()=>{store.Set("spellReferences",records);store.Set("referenceAliases",aliases);});}
                            else{effectReferenceProgress.Failed[lookup]=DateTime.UtcNow;if(combatReferenceInterests.ContainsKey(CombatInspection.IdentityKey(name)))Interlocked.Increment(ref referenceRevision);}
                        }
                        Updated?.Invoke();
                    }
                }
                finally
                {
                    referenceQueued.TryRemove((exact?"effect:":"combat:")+lookup,out _);
                    lock(Sync)
                    {
                        if(background)effectReferenceProgress.Next=next;
                        if(!ready||next%20==0){var progress=EquipmentPositions.Copy(effectReferenceProgress);QueuePersistence(()=>store.Set("effectReferenceProgress",progress));}
                    }
                }
                await Task.Delay(ready?25:100,cancel.Token);
            }
        }
        catch(OperationCanceledException){}
        catch(Exception error)when(error is not OutOfMemoryException){if(!cancel.IsCancellationRequested)File.AppendAllText(Path.Combine(DataFolder,"errors.log"),DateTime.Now+" Effect references: "+error+Environment.NewLine);}
    }
}
