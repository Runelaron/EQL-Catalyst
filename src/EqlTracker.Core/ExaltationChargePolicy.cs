using System.Text.RegularExpressions;
namespace EqlTracker.Core;

internal static class ExaltationChargePolicy
{
    internal const string Limitation="Charged click effect; conversion to an Exaltation is unverified. Showing it does not establish extraction or socket eligibility.";
    internal static bool IsChargedClick(ExaltationOption option)
    {
        string text=option.Effect;
        bool chargedKind=Regex.IsMatch(text,@"\bcharg(?:e|ed)[ -]+clicky\b",RegexOptions.IgnoreCase);
        if(option.Type!="Click"&&!chargedKind)return false;
        bool finite=Regex.IsMatch(text,@"\b(?:charges?\s*[:=]?\s*\d+\b|\d+\s+charges?\b)",RegexOptions.IgnoreCase);
        bool unlimited=Regex.IsMatch(text,@"\b(?:unlimited|infinite)\s+charges?\b|\bcharges?\s*[:=]?\s*(?:unlimited|infinite|-1)\b",RegexOptions.IgnoreCase);
        return finite||chargedKind&&!unlimited;
    }
    internal static bool Visible(ExaltationOption option,bool includeChargedClicks=false)=>includeChargedClicks||!IsChargedClick(option);
}
