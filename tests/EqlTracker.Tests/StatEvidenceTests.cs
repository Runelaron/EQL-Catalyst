using EqlTracker.Core;

static class StatEvidenceTests
{
    public static void Register(Action<string,Action> test)
    {
        void Check(bool ok){if(!ok)throw new Exception("Stat evidence assertion failed");}
        string Line(string message,int second=0)=>$"[Thu Oct 15 21:00:{second:00} 2026] {message}";
        bool Read(StatEvidenceState state,string text,int second=0,SpellMessageIndex? spells=null,List<PetIdentity>? pets=null)=>StatEvidenceReader.Observe(state,Line(text,second),"fixture:"+second,"ExampleHero",pets ?? [],spells);
        SpellMessageIndex Messages()=>SpellMessageIndex.Parse(["107^Burnout III^0","421^Skin like Steel^0","422^Other Skin^0"],["#SPELLINDEX^CASTERMETXT^CASTEROTHERTXT^CASTEDMETXT^CASTEDOTHERTXT^SPELLGONE^","107^^^You feel fiery.^ looks fiery.^The fire fades.^","421^^^Your skin turns hard.^'s skin turns hard.^Your skin softens.^","422^^^Your skin turns hard.^^^"]);
        test("Real AA purchase and improvement formats distinguish exact ranks from ownership",()=>{
            var s=new StatEvidenceState();Read(s,"You have improved Combat Fury 4 at a cost of 4 ability points.");Read(s,"You have gained the ability \"Combat Stability\" at a cost of 2 ability points.",1);Read(s,"You have gained an ability point!  You now have 3 ability points.",2);
            Check(s.AAs.Count==2 && s.AAs["combat fury"].Rank==4 && s.AAs["combat stability"].Rank==null && s.AAs["combat stability"].MinimumRank==1);
        });
        test("AA replay is idempotent and older purchases cannot downgrade newer evidence",()=>{
            var s=new StatEvidenceState();Read(s,"You have improved Harm Touch 10 at a cost of 0 ability points.",4);long version=s.Revision;Read(s,"You have improved Harm Touch 9 at a cost of 0 ability points.",1);Read(s,"You have improved Harm Touch 10 at a cost of 0 ability points.",4);
            Check(s.Revision==version && s.AAs["harm touch"].Rank==10);
        });
        test("Only own who output and subsequent level evidence populate character context",()=>{
            var s=new StatEvidenceState();Read(s,"[49 SHD/BRD/MAG] ExampleHero (High Elf)");Read(s,"[50 WAR] Otherplayer (Human)",1);Read(s,"You have gained a level! Welcome to level 50!",3);Read(s,"[48 MAG] ExampleHero (High Elf)",2);
            Check(s.Level==50 && s.Classes.SequenceEqual(new[]{"MAG"}));
        });
        test("Client message mapping retains ambiguity and does not claim active buffs",()=>{
            var s=new StatEvidenceState();Read(s,"Your skin turns hard.",0,Messages());Read(s,"You feel fiery.",1,Messages());
            Check(s.Effects.Count==2 && s.Effects.Any(e=>e.Spells.Count==2) && s.Effects.All(e=>e.State.Contains("unknown")));
        });
        test("Pet effect evidence is resolved at timestamp and excludes other actors",()=>{
            var at=new DateTime(2026,10,15,21,0,0);var pets=new List<PetIdentity>{new(){Name="Cloudling",From=at.AddMinutes(-1),Until=at.AddSeconds(2)},new(){Name="Kobekn",From=at.AddSeconds(2)}};var s=new StatEvidenceState();
            Read(s,"Cloudling looks fiery.",1,Messages(),pets);Read(s,"Cloudling looks fiery.",3,Messages(),pets);Read(s,"Otherpet looks fiery.",4,Messages(),pets);Read(s,"Your pet's Burnout III I spell has worn off.",5,Messages(),pets);
            Check(s.Effects.Count==2 && s.Effects.Any(e=>e.Actor=="Cloudling") && s.Effects.Any(e=>e.Actor=="Kobekn" && e.Spells.Single()=="Burnout III I" && e.State.StartsWith("Wore off")));
        });
        test("Unsupported spell-file layouts and combat/chat records create no stat observations",()=>{
            var map=SpellMessageIndex.Parse(["107^Burnout III^0"],["107^^^You feel fiery.^^^"]);var s=new StatEvidenceState();
            Check(!Read(s,"You feel fiery.",0,map) && !Read(s,"You hit an orc for 10 points of damage.") && !Read(s,"Otherplayer tells you, 'You have improved Combat Fury 4 at a cost of 4 ability points.'"));Check(s.Revision==0);
        });
        test("Prefill preserves manual ranks including deliberately unknown overrides",()=>{
            var s=new StatEvidenceState();Read(s,"You have improved Combat Fury 4 at a cost of 4 ability points.");var rows=new List<GearAaModifier>{new(){Name="Combat Fury",Rank=2}};StatProfilePrefill.MergeAAs(rows,s,false);Check(rows.Single().Rank==2);
            rows[0].Rank=null;rows[0].RankEvidence="Manual override";StatProfilePrefill.MergeAAs(rows,s,false);Check(rows.Single().Rank==null);
        });
        test("Imported rank changes require renewed verification of numeric AA models",()=>{
            var s=new StatEvidenceState();Read(s,"You have improved Combat Fury 4 at a cost of 4 ability points.");var rows=new List<GearAaModifier>{new(){Name="Combat Fury",Rank=3,RankObservedAt=new DateTime(2026,10,14),Kind=AaModifierKind.FlatStat,Confirmed=true,Value=3}};
            StatProfilePrefill.MergeAAs(rows,s,false);Check(rows[0].Rank==4 && !rows[0].Confirmed && rows[0].Value==3);
        });
        test("Pet AA prefill uses only owner AAs relevant to pet effects",()=>{
            var s=new StatEvidenceState();Read(s,"You have improved Combat Fury 4 at a cost of 4 ability points.");Read(s,"You have improved Companion's Fury 2 at a cost of 4 ability points.",1);var rows=GearMechanicsEvidence.RelevantAAs(true);StatProfilePrefill.MergeAAs(rows,s,true);
            Check(rows.Single(a=>a.Name=="Companion's Fury").Rank==2 && rows.All(a=>a.Name!="Combat Fury") && rows.Any(a=>a.Rank==null));
        });
        test("Worn estimates remain distinct from observed totals and exclude storage",()=>{
            var c=new Catalog{Items=[new(){Key="ring",Name="Ring",Stats=new(){["DEX"]=10}},new(){Key="sword",Name="Sword",Stats=new(){["Ratio"]=1,["DEX"]=5}}]};var positions=new List<EquipmentPosition>{new(){Slot="Finger",Item=new(){Name="Ring"}},new(){Slot="Any Slot",Item=new(){Name="Sword"}}};var values=StatProfilePrefill.WornContributions(c,positions);
            Check(values["DEX"]==15 && !values.ContainsKey("Ratio"));
        });
        test("Evidence persistence and partial-line checkpoints resume without replaying combat",()=>{
            string folder=Path.Combine(Path.GetTempPath(),"eql-stat-evidence-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string log=Path.Combine(folder,"log.txt"),db=Path.Combine(folder,"evidence.db");
            try{
                string line=Line("You have improved Soul Abrasion 2 at a cost of 6 ability points.");File.WriteAllText(log,line[..30]);var first=LogTailer.Read(log,null);Check(first.Lines.Count==0);
                File.AppendAllText(log,line[30..]+"\n");var next=LogTailer.Read(log,first.Checkpoint);var s=new StatEvidenceState();foreach(var l in next.Lines)StatEvidenceReader.Observe(s,l.Text,l.Id,"ExampleHero",[],null);
                using(var store=new TrackerStore(db)){store.Set("stats",s);store.Set("offset",next.Checkpoint);}
                using(var store=new TrackerStore(db)){var restored=store.Get<StatEvidenceState>("stats")!;var tail=LogTailer.Read(log,store.Get<FileCheckpoint>("offset"));Check(restored.AAs["soul abrasion"].Rank==2 && tail.Lines.Count==0);}
            }finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
        });
    }
}
