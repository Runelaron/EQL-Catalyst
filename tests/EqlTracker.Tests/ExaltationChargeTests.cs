using EqlTracker.Core;
internal static class ExaltationChargeTests
{
    internal static void Register(Action<string,Action> test)
    {
        void Check(bool value){if(!value)throw new InvalidOperationException("Charged Exaltation policy");}
        ExaltationOption Option(string effect,string type="Click")=>new("fixture:click:effect",new(){Key="fixture",Name="Fixture",Effects=[effect]},type,effect,2,false,true);
        test("Exaltations hide explicit charged click descriptions by default",()=>{foreach(string text in new[]{"Spirit Tap (Charge Clicky, Casting Time: 0.3)","Restore Sight (Charged Clicky)","Click: Heal (Charges: 10)","Click: Heal (1 charge)","Click: Heal (Charges: 0)"}){var o=Option(text);Check(!ExaltationChargePolicy.Visible(o)&&ExaltationChargePolicy.Visible(o,true));}});
        test("Exaltations retain ordinary unlimited and unknown-charge click effects",()=>{foreach(string text in new[]{"Click: Heal (Casting Time: Instant)","Click: Heal (Charges: Unlimited)","Click: Heal (Unlimited charges)","Click: Heal (Charges: -1)","Click: Heal (Charges: Unknown)","Click: Recharge"})Check(ExaltationChargePolicy.Visible(Option(text)));});
        test("Exaltations charged filtering is per effect rather than per source",()=>{Check(ExaltationChargePolicy.Visible(Option("Focus: Recharge 3 charges","Focus")));var item=new ItemDefinition{Key="fixture",Name="Fixture",Effects=["Tap (Charge Clicky, Casting Time: Instant)","Focus: Improved Healing"]};var options=ExaltationPlanner.Build(new(){Items=[item]},new());Check(options.Count==2&&options.Count(o=>ExaltationChargePolicy.Visible(o))==1&&options.Single(o=>ExaltationChargePolicy.Visible(o)).Type=="Focus");});
        test("Exaltations keep charged ownership identities and saved socket assignments",()=>{var o=Option("Tap (Charge Clicky, Casting Time: Instant)");var catalog=new Catalog{Items=[o.Source]};var stock=new InventorySnapshot{Entries=[new(){Name="Fixture +4",Location="General1",Count=1}]};var rows=ExaltationPlanner.Build(catalog,stock);var build=new PetBuild{Items=[new(){Name="Fixture",ItemKey="fixture",Exaltations=[new(){EffectId=rows.Single().Id,Effect=rows.Single().Effect,State="Socketed"}]}]};string before=PetResearch.Hash(new{catalog,stock,build});Check(rows.Where(o=>ExaltationChargePolicy.Visible(o)).Count()==0&&rows.Single().State.Contains("unverified"));Check(before==PetResearch.Hash(new{catalog,stock,build})&&build.Items.Single().Exaltations.Single().State=="Socketed");});
    }
}
