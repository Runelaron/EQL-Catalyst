using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

// A financial receipt is evidence of spending, never an inventory adjustment.
internal static class PurchaseEvidence
{
    internal const string Version="purchase-evidence-v1";
    internal static JournalActivity? Parse(DateTime at,string lineId,string message,string zone="")
    {
        var match=Regex.Match(message,@"^You purchased (?<count>[\d,]+) (?<item>.+?) from (?<merchant>.+?) for\s+(?<money>.+)\.$");
        if(!match.Success||!int.TryParse(match.Groups["count"].Value.Replace(",",""),NumberStyles.None,CultureInfo.InvariantCulture,out int count)||count<=0)return null;
        string money=match.Groups["money"].Value;
        if(!Regex.IsMatch(money,@"\A(?:[\d,]+ (?:platinum|gold|silver|copper)(?: pieces?)?(?:,?\s+(?:and\s+)?)?)+\z",RegexOptions.IgnoreCase)||ActivityJournal.ParseCopper(money) is not {} amount)return null;
        // The offset distinguishes identical transactions in the same second. It
        // also matches across independent live and historical reader generations.
        string offset=lineId[(lineId.LastIndexOf(':')+1)..];
        string id="purchase:"+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(at.ToString("O")+"|"+offset+"|"+message)));
        return new(){Id=id,At=at,Kind="Spent",Copper=amount,Name=match.Groups["item"].Value,Quantity=count,Source="Purchased from "+match.Groups["merchant"].Value+"; logged price is the whole transaction total",Zone=zone};
    }
}
