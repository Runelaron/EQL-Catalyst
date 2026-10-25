using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class CompatibilityRuleEditor : Expander
{
    internal CompatibilityRuleEditor(TrackerController controller,bool pet)
    {
        SetResourceReference(StyleProperty,typeof(Expander));Header="Reviewed stacking evidence";IsExpanded=false;ToolTip="Optional context-scoped observations; client slot matches are never promoted into server rules";
        Expanded+=(_,_)=>{if(Content!=null)return;Build();};
        void Build()
        {
            var setup=controller.IntegratedSnapshot();var body=new StackPanel();
            var spells=controller.StandardPetBuffSnapshot().Buffs.Select(b=>new EffectIdentity(b.Spell,b.Spell,b.EffectiveRank)).Concat(setup.Bard.Songs.Select(s=>s.Identity)).Concat(setup.Bard.Available.Select(s=>s.Identity)).Concat(setup.Coverage.Select(c=>c.Song)).Distinct().ToArray();
            var a=new ComboBox{ItemsSource=spells,DisplayMemberPath="Name",MinWidth=140,MaxWidth=250};var b=new ComboBox{ItemsSource=spells,DisplayMemberPath="Name",MinWidth=140,MaxWidth=250};
            var family=UI.Input("",150);var kind=UI.Select(Enum.GetNames<EffectRuleKind>(),"Strongest",170);var winner=UI.Select(["First spell","Second spell"],"First spell",120);var source=UI.Input("",260);var result=UI.Text("",11,"SecondaryText");
            body.Children.Add(UI.Text("Record a reviewed observation for this server, recipient and setup. A whole-spell block removes every component; strongest contribution leaves the other spell’s components available.",11,"SecondaryText"));
            body.Children.Add(UI.Field("First exact spell & reviewed rank",a));body.Children.Add(UI.Field("Second exact spell & reviewed rank",b));body.Children.Add(UI.Field("Rule",kind));body.Children.Add(UI.Field("Effect family (blank for whole spell)",family));body.Children.Add(UI.Field("Whole-spell winner",winner));body.Children.Add(UI.Field("Reviewed evidence and version/context",source));
            var save=WorkspaceForms.Control(UI.Button("Save reviewed rule",()=>{
                if(a.SelectedItem is not EffectIdentity first||b.SelectedItem is not EffectIdentity second||first.UpgradeRank==null||second.UpgradeRank==null||string.IsNullOrWhiteSpace(source.Text)||!Enum.TryParse<EffectRuleKind>(kind.SelectedItem?.ToString(),out var ruleKind)){result.Text="Choose exact spells with known ranks and provide reviewed evidence.";return;}
                if(first.SpellId==second.SpellId&&(first.UpgradeRank!=second.UpgradeRank||ruleKind is EffectRuleKind.BlocksWholeSpell or EffectRuleKind.ReplacesWholeSpell)){result.Text="Same-identity evidence supports component rules at the same rank. Whole-spell exclusions need distinct exact identities.";return;}
                if(ruleKind is EffectRuleKind.Sum or EffectRuleKind.Strongest&&string.IsNullOrWhiteSpace(family.Text)){result.Text="Specify the reviewed effect family, such as HastePercent or STR.";return;}
                string fingerprint=pet?EquipmentPositions.Fingerprint(GuidedPetBuilds.Positions(PetInventoryEditing.Confirmed(controller.Profile.ActiveLoadout,controller.Catalog),controller.Profile.ActiveLoadout,controller.Catalog)):controller.BardContextFingerprint();
                var rule=new EffectRule{Id=Guid.NewGuid().ToString("N"),Version="user-review-v1",Source=source.Text.Trim(),Server=controller.Profile.Server,RecipientKind=pet?"SummonedPet":"Self",PetLevel=pet?controller.Profile.ActiveLoadout.PetLevel:null,ContextFingerprint=fingerprint,Kind=ruleKind,Family=family.Text.Trim(),SpellIds=[first.SpellId,second.SpellId],Ranks=new(){[first.SpellId]=first.UpgradeRank,[second.SpellId]=second.UpgradeRank},Winner=winner.SelectedIndex==0?first.SpellId:second.SpellId,Quality=EvidenceQuality.UserConfirmed};
                if(pet&&!rule.PetLevel.HasValue){result.Text="Confirm the actual pet level before recording a scoped rule.";return;}
                var edited=controller.IntegratedSnapshot();edited.Rules.Add(rule);controller.SaveIntegrated(edited);Content=null;Build();
            }));body.Children.Add(save);body.Children.Add(result);
            save.IsEnabled=false;
            void Validate(){bool exact=a.SelectedItem is EffectIdentity x&&b.SelectedItem is EffectIdentity y&&x.UpgradeRank.HasValue&&y.UpgradeRank.HasValue;bool familyRequired=kind.SelectedItem?.ToString() is "Sum" or "Strongest";save.IsEnabled=exact&&source.Text.Trim().Length>0&&(!familyRequired||family.Text.Trim().Length>0);}
            a.SelectionChanged+=(_,_)=>Validate();b.SelectionChanged+=(_,_)=>Validate();kind.SelectionChanged+=(_,_)=>Validate();family.TextChanged+=(_,_)=>Validate();source.TextChanged+=(_,_)=>Validate();
            foreach(var rule in setup.Rules)
            {
                var known=rule;body.Children.Add(UI.Text(rule.Kind+" · "+rule.Family+" · "+string.Join(" / ",rule.SpellIds)+"\n"+rule.Source+" · "+rule.Version,11,"SecondaryText"));body.Children.Add(UI.Button("Remove reviewed rule",()=>{var edited=controller.IntegratedSnapshot();edited.Rules.RemoveAll(r=>r.Id==known.Id);controller.SaveIntegrated(edited);Content=null;Build();}));
            }
            foreach(var input in Usability.Tree(body).OfType<Control>().Where(c=>c is ComboBox or TextBox or Button))WorkspaceForms.Control(input);
            Content=body;
        }
    }
}
