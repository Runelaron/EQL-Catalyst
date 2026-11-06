using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

internal sealed class BardResearchState
{
    public string Version {get;set;}=BardRotations.Version;
    public Dictionary<string,BardKnownSong> Known {get;set;}=[];
    public List<BardRotationSet> Common {get;set;}=[];
    public BardRotationSet? Current {get;set;}
    public List<BardPulse> Pending {get;set;}=[];
    public ConnectedSpellState Correlation {get;set;}=new();
    public string Segment {get;set;}="";
    public List<string> RecentIds {get;set;}=[];
    public List<BardRotationSet> Saved {get;set;}=[];
    public List<BardSongPlan> Plans {get;set;}=[];
    public BardComparisonPreferences Preferences {get;set;}=new();
    public List<BardModifierCapture> Modifiers {get;set;}=[];
    public string FileEvidence {get;set;}="Saved EQ sets unavailable";
}
internal sealed class BardComparisonPreferences
{
    public bool EffectiveStrength {get;set;}=true;
    public string SelectedSet {get;set;}="detected";
}
internal sealed record BardSongPlan(string Set,string Before,string After,int? Rank,DateTime At,int? Gem);
internal sealed class BardKnownSong
{
    public BardSong Song {get;set;}=new();
    public DateTime First {get;set;}
    public DateTime Last {get;set;}
    public string Evidence {get;set;}="";
}
internal sealed class BardRotationSet
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public string Origin {get;set;}="";
    public List<BardSong> Songs {get;set;}=[];
    public int GemCount {get;set;}
    public DateTime First {get;set;}
    public DateTime Last {get;set;}
    public double ObservedSeconds {get;set;}
    public int Cycles {get;set;}
    public int Episodes {get;set;}
    public List<string> Sessions {get;set;}=[];
    public string Evidence {get;set;}="";
    public string LastCycle {get;set;}="";
}
internal sealed record BardPulse(string Id,DateTime At,BardSong Song,string Session,double? DurationSeconds);
internal static class BardRotations
{
    internal const string Version="bard-rotations-v2-catalog-ready";
    internal static bool MetadataReady(ConnectedSpellIndex index)=>index.BardSongs.Count>0;
    internal static string Key(EffectIdentity song)=>song.Name.Trim().ToUpperInvariant();
    internal static string VersionKey(EffectIdentity song)=>Key(song)+"|"+song.UpgradeRank;
    internal static string Fingerprint(IEnumerable<BardSong> songs)=>PetComparisonEngine.Key(songs.Select(s=>new{Key=Key(s.Identity),s.Identity.UpgradeRank}).OrderBy(s=>s.Key).ToArray());
    internal static string EvidenceId(DateTime at,string lineId,string message)=>"bard:"+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(at.ToString("O")+"|"+lineId[(lineId.LastIndexOf(':')+1)..]+"|"+message)));
    internal static bool Learn(BardResearchState state,BardSong song,DateTime at,string evidence)
    {
        string key=VersionKey(song.Identity);if(state.Known.TryGetValue(key,out var known)){if(at>known.Last)known.Last=at;if(at<known.First)known.First=at;return false;}
        var copy=EquipmentPositions.Copy(song);copy.Available=true;state.Known[key]=new(){Song=copy,First=at,Last=at,Evidence=evidence};return true;
    }
    internal static bool Memorized(BardResearchState state,string message,DateTime at,ConnectedSpellIndex index)
    {
        const string prefix="You have finished memorizing ";if(!message.StartsWith(prefix,StringComparison.Ordinal))return false;
        var identity=ObservedIdentity(index,message[prefix.Length..].TrimEnd('.'));var song=Song(index,identity);return song!=null&&Learn(state,song,at,"Successful memorization · "+message);
    }
    internal static ConnectedSpellIdentity ObservedIdentity(ConnectedSpellIndex index,string raw)
    {
        var identity=index.Resolve(raw);
        // A bare logged name often omits the upgrade suffix. Catalog base rank
        // is not a recorded +0; an explicit parsed suffix (including +0) is.
        return identity.Definition!=null&&identity.Rank is null or 0?identity with{Rank=null}:identity;
    }
    internal static BardSong? Song(ConnectedSpellIndex index,ConnectedSpellIdentity identity)
    {
        var definition=identity.Definition??index.BardSongs.FirstOrDefault(s=>s.Name==identity.Canonical);
        if(definition?.Levels?.ContainsKey("BRD")!=true)return null;
        return new(){Identity=new(definition.Id?.ToString(CultureInfo.InvariantCulture)??identity.Canonical,identity.Canonical,identity.Rank),Mana=definition.Mana,CooldownMs=definition.RecastMs,RequiredLevel=definition.Levels["BRD"],NonTargetedArea=definition.Target is 4 or 6 or 39 or 40 or 41 or 43?true:definition.Target is 1 or 5 or 8 or 14?false:null,Source=definition.Source+" · Self eligibility: user-confirmed; wiki's narrower area-only wording retained as disagreement"};
    }
    internal static void Boundary(BardResearchState state,string segment)
    {if(state.Segment==segment)return;state.Segment=segment;state.Pending.Clear();}
    internal static bool Observe(BardResearchState state,BardPulse pulse)
    {
        if(state.RecentIds.Contains(pulse.Id))return false;state.RecentIds.Add(pulse.Id);if(state.RecentIds.Count>4000)state.RecentIds.RemoveRange(0,1000);
        Learn(state,pulse.Song,pulse.At,"Successful self-attributed song application · "+pulse.Id);
        if(pulse.Song.AuraEligible!=true)return false;
        // One pulse received by multiple recipients is not multiple repetitions.
        if(state.Pending.Any(p=>p.At==pulse.At&&Key(p.Song.Identity)==Key(pulse.Song.Identity)))return false;
        if(state.Pending.LastOrDefault() is {} last&&pulse.At<last.At)return false;
        state.Pending.Add(pulse);if(state.Pending.Count>45)state.Pending.RemoveAt(0);
        for(int n=Math.Min(5,state.Pending.Count/3);n>=1;n--)
        {
            var tail=state.Pending.TakeLast(n*3).ToArray();var blocks=Enumerable.Range(0,3).Select(i=>tail.Skip(i*n).Take(n).ToArray()).ToArray();
            string[] signature=blocks[0].Select(p=>VersionKey(p.Song.Identity)).Order().ToArray();
            if(signature.Distinct().Count()!=n||blocks.Skip(1).Any(b=>!b.Select(p=>VersionKey(p.Song.Identity)).Order().SequenceEqual(signature)))continue;
            // Stable boundary counts once per complete cycle, not once per song.
            if(VersionKey(pulse.Song.Identity)!=signature[0])continue;
            double a=(blocks[1][0].At-blocks[0][0].At).TotalSeconds,b=(blocks[2][0].At-blocks[1][0].At).TotalSeconds;
            if(a<=0||b<=0||Math.Max(a,b)>Math.Min(a,b)*2)continue;
            string key=Fingerprint(blocks[^1].Select(p=>p.Song));string cycle=blocks[^1][^1].Id;
            if(state.Current is {} current&&current.Songs.Count>n&&blocks[^1].All(p=>current.Songs.Any(s=>Key(s.Identity)==Key(p.Song.Identity))))continue;
            var set=state.Common.FirstOrDefault(s=>s.Id==key);bool created=set==null;
            if(set==null){set=new(){Id=key,Name=string.Join(" / ",blocks[^1].Select(p=>p.Song.Identity.Name)),Origin="Repeated observations",Songs=blocks[^1].Select(p=>{var s=EquipmentPositions.Copy(p.Song);s.Gem=0;return s;}).OrderBy(s=>Key(s.Identity)).ToList(),First=tail[0].At,Last=tail[0].At};state.Common.Add(set);}
            if(set.LastCycle==cycle)return false;
            bool changed=state.Current?.Id!=key;if(changed)set.Episodes++;
            if(!set.Sessions.Contains(pulse.Session))set.Sessions.Add(pulse.Session);
            double? duration=blocks[^1].Select(p=>p.DurationSeconds).All(d=>d is >0)?blocks[^1].Min(p=>p.DurationSeconds):null;
            // Credit only supported intervals; silence and unknown duration add no uptime.
            int addedCycles=created?3:1;double seconds=duration.HasValue?(created?Math.Min(a,duration.Value)+Math.Min(b,duration.Value):Math.Min(b,duration.Value)):0;
            set.ObservedSeconds+=seconds;set.Cycles+=addedCycles;set.Last=pulse.At;set.LastCycle=cycle;
            set.Evidence="At least three complete recurring cycles. Gem order and Aura activation are not inferred. Duration includes only bounded, supported pulse intervals.";
            state.Current=EquipmentPositions.Copy(set);return true;
        }
        return false;
    }
    internal static IReadOnlyList<BardSong> Select(BardRotationSet set,int? capacity)
    {
        int limit=capacity is >=1 and <=5?capacity.Value:5;
        if(set.Songs.Any(s=>s.Gem>0))
        {
            var selected=new List<BardSong>();foreach(var song in set.Songs.OrderByDescending(s=>s.Gem))
            {if(selected.Count==limit)break;if(song.AuraEligible==null)return [];if(song.AuraEligible==true)selected.Add(song);}return selected;
        }
        return set.Songs.Where(s=>s.AuraEligible==true).Take(limit).ToArray();
    }
    internal static List<BardRotationSet> SavedLoadouts(string text,ConnectedSpellIndex index,string evidence)
    {
        if(text.Length>2*1024*1024)throw new InvalidDataException("Character settings exceed the read-only limit");
        bool inSection=false;var sets=new Dictionary<int,(Dictionary<string,string> Fields,Dictionary<int,int> Slots)>();
        foreach(string line in text.Split('\n'))
        {
            string row=line.Trim();if(row.StartsWith('[')){inSection=row.Equals("[SpellLoadouts]",StringComparison.OrdinalIgnoreCase);continue;}if(!inSection)continue;
            var match=Regex.Match(row,@"^SpellLoadout(?<set>\d+)\.(?<field>inuse|name|slot(?<slot>\d+))=(?<value>.*)$",RegexOptions.IgnoreCase);if(!match.Success)continue;
            int number=int.Parse(match.Groups["set"].Value,CultureInfo.InvariantCulture);if(number is <0 or >1000)continue;if(!sets.TryGetValue(number,out var set)){set=(new(StringComparer.OrdinalIgnoreCase),[]);sets[number]=set;}
            string field=match.Groups["field"].Value,value=match.Groups["value"].Value;if(match.Groups["slot"].Success&&int.TryParse(match.Groups["slot"].Value,out int slot)&&slot is >=1 and <=16&&int.TryParse(value,out int id))set.Slots[slot]=id;else set.Fields[field]=value;
        }
        var result=new List<BardRotationSet>();
        foreach(var (number,set) in sets.Where(s=>s.Value.Fields.GetValueOrDefault("inuse")=="1"))
        {
            var songs=new List<BardSong>();foreach(var (gem,id) in set.Slots.Where(s=>s.Value>0))
            {
                var definition=index.FindId(id);if(definition==null){songs.Add(new(){Identity=new(id.ToString(),"Unknown spell #"+id,null),Gem=gem,Source="Saved spell ID has no validated metadata"});continue;}if(definition.Levels?.ContainsKey("BRD")!=true)continue;var song=Song(index,index.Resolve(definition.Name));if(song!=null){song.Gem=gem;songs.Add(song);}
            }
            if(songs.Count>0)result.Add(new(){Id="eq:"+number,Name=set.Fields.GetValueOrDefault("name","Saved set "+number),Origin="Saved in EQ · current use unconfirmed",Songs=songs,GemCount=set.Slots.Keys.DefaultIfEmpty(0).Max(),Evidence=evidence+" · inuse means saved, not active"});
        }
        return result;
    }
}
