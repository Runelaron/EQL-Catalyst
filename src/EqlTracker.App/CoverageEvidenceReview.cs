using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class ReviewedComponents
{
    internal static string Text(IEnumerable<EffectInput> effects) => string.Join("; ",effects.Where(e=>e.Amount.HasValue).Select(e=>e.Family+"="+e.Amount!.Value.ToString("0.###",CultureInfo.InvariantCulture)));
    internal static List<EffectInput> Parse(string text, EffectIdentity identity, string source, bool complete)
    {
        var result=new List<EffectInput>();
        foreach(string part in text.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            var cells=part.Split('=',2,StringSplitOptions.TrimEntries);
            if(cells.Length!=2||cells[0].Length is <1 or >60||!double.TryParse(cells[1],NumberStyles.Float,CultureInfo.InvariantCulture,out double amount)||!double.IsFinite(amount)||Math.Abs(amount)>1000000)throw new InvalidDataException("Use reviewed components as Stat=amount; Stat=amount, or leave unresolved.");
            var channel=cells[0] switch{"HastePercent"=>EffectChannel.OrdinaryHaste,"Overhaste"=>EffectChannel.Overhaste,"Damage"=>EffectChannel.Damage,_=>EffectChannel.Attribute};
            result.Add(new(){Identity=identity,Family=cells[0],Channel=channel,Amount=amount,Quality=EvidenceQuality.UserConfirmed,Source=source});
        }
        if(!complete||result.Count==0)result.Add(new(){Identity=identity,Family="Other unreviewed song components",Quality=EvidenceQuality.Unknown,Source=source}); return result;
    }
}
public sealed partial class CombatGroupPanel
{
    private UIElement BuildCoverageReview(IEnumerable<CoverageObservation> observations)
    {
        var body=new StackPanel();
        foreach(var observation in observations)
        {
            var row=new StackPanel();row.Children.Add(UI.Text($"{observation.At:HH:mm:ss} · {(observation.Caster.Length>0?observation.Caster:"Unattributed")} · {observation.Song.Name} +{observation.Song.UpgradeRank?.ToString()??"?"}\nRecipient: {(observation.Recipient.Length>0?observation.Recipient:"Unknown")} · {(observation.SuccessfulApplication?"Successful application":"Success unknown")}",11));
            if(!observation.SuccessfulApplication||observation.Caster.Length==0||observation.Recipient.Length==0||!observation.Song.UpgradeRank.HasValue){body.Children.Add(row);continue;}
            var previous=controller.IntegratedSnapshot().CoverageRules.FirstOrDefault(r=>r.Song==observation.Song&&r.Caster==observation.Caster&&r.Recipient==observation.Recipient&&r.Zone==controller.Profile.Zone);
            var duration=UI.Input(previous?.DurationSeconds.ToString(CultureInfo.InvariantCulture)??"",70);var instrument=UI.Input(previous?.Instrument.ToString(CultureInfo.InvariantCulture)??"",70);var singing=UI.Input(previous?.Singing.ToString(CultureInfo.InvariantCulture)??"",70);var focus=UI.Input(previous?.Focus.ToString(CultureInfo.InvariantCulture)??"",70);var level=UI.Input(previous?.EffectiveCasterLevel.ToString()??"",70);var aa=UI.Input(previous?.AaContext??"",170);
            var values=UI.Input(ReviewedComponents.Text(previous?.Effects??[]),220);var complete=new CheckBox{Content="All song components reviewed",IsChecked=previous!=null&&previous.Effects.All(e=>e.Amount.HasValue)};var evidence=UI.Input(previous?.Source??"",220);var result=UI.Text("",11,"SecondaryText");
            var inputs=new WrapPanel();foreach(var field in new[]{UI.Field("Measured duration (seconds)",duration),UI.Field("Instrument modifier",instrument),UI.Field("Singing modifier",singing),UI.Field("Focus modifier",focus),UI.Field("Effective caster level",level),UI.Field("Relevant AAs (None if verified)",aa)})inputs.Children.Add(field);
            var review=UI.Stack(inputs,UI.Field("Inspected component magnitudes",values),complete,UI.Field("Evidence / context",evidence),UI.Text("Only successful recorded recipients are eligible. This review applies to the same caster, spell rank, recipient and zone. A changed instrument or AA requires a new review.",11,"SecondaryText"));
            review.Children.Add(UI.Button("Save reviewed coverage",()=>{
                try
                {
                    double Number(TextBox box){if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||!double.IsFinite(n)||n<0||n>10000)throw new InvalidDataException("Enter known nonnegative modifier values.");return n;}
                    double seconds=Number(duration);if(seconds<=0||seconds>1800||!int.TryParse(level.Text,out int casterLevel)||casterLevel is <1 or >100||string.IsNullOrWhiteSpace(aa.Text)||string.IsNullOrWhiteSpace(evidence.Text))throw new InvalidDataException("Supply a supported 1–1,800 second window, known level, AA context and evidence.");
                    var rule=new SongCoverageRule{Song=observation.Song,Caster=observation.Caster,Recipient=observation.Recipient,Zone=controller.Profile.Zone,DurationSeconds=seconds,Instrument=Number(instrument),Singing=Number(singing),Focus=Number(focus),EffectiveCasterLevel=casterLevel,AaContext=aa.Text.Trim(),Source=evidence.Text.Trim(),Effects=ReviewedComponents.Parse(values.Text,observation.Song,evidence.Text,complete.IsChecked==true)};
                    var setup=controller.IntegratedSnapshot();setup.CoverageRules.RemoveAll(r=>r.Song==rule.Song&&r.Caster==rule.Caster&&r.Recipient==rule.Recipient&&r.Zone==rule.Zone);setup.CoverageRules.Add(rule);foreach(var known in setup.Coverage)rule.Apply(known,controller.Profile.Zone);controller.SaveIntegrated(setup);result.Text="Reviewed window saved; expired observations stay expired.";
                }
                catch(InvalidDataException error){result.Text=error.Message;}
            }));review.Children.Add(result);row.Children.Add(FocusedLayout.Disclosure("Review supported window & potency",review,"Optional evidence review; a cast attempt cannot be promoted into coverage"));body.Children.Add(row);
        }
        return body;
    }
}
