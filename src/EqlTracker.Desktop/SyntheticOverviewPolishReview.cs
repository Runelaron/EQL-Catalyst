using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class TrackerController
{
    internal void SeedOverviewPolishReview()
    {
        if(!Demo)throw new InvalidOperationException();var at=Sessions!.Sessions.Last().Start.AddMinutes(15);
        lock(Sync){Journal(new(){Id="review-coin",At=at,Kind="Coin",Copper=234});Journal(new(){Id="review-sale",At=at,Kind="Merchant",Copper=1000});Journal(PurchaseEvidence.Parse(at,"review:500","You purchased 13 Bottle from Merchant Example for 6 silver 5 copper.")!);}
    }
}
