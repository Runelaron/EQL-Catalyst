using EqlTracker.Core;
using EqlTracker.Companion;
using System.Security.Cryptography;

internal static class Beta18Tests
{
    internal static void Register(Action<string, Action> test)
    {
        void Check(bool value) { if (!value) throw new Exception("Beta 1.8 preservation assertion failed"); }
        test("Beta18 protected Linux payload authenticates key, directory and ciphertext",()=>
        {
            byte[] key=RandomNumberGenerator.GetBytes(32),plain=System.Text.Encoding.UTF8.GetBytes("synthetic-pairing-secret");string path=Path.Combine(Path.GetTempPath(),"catalyst-secret-fixture");
            var encrypted=LocalSecretProtection.Encrypt(path,plain,key.ToArray());Check(!encrypted.SequenceEqual(plain));Check(LocalSecretProtection.Decrypt(path,encrypted,key.ToArray()).SequenceEqual(plain));
            bool altered=false,moved=false,wrongKey=false;
            try{var bad=encrypted.ToArray();bad[^1]^=1;LocalSecretProtection.Decrypt(path,bad,key.ToArray());}catch(CryptographicException){altered=true;}
            try{LocalSecretProtection.Decrypt(path+"-other",encrypted,key.ToArray());}catch(CryptographicException){moved=true;}
            try{LocalSecretProtection.Decrypt(path,encrypted,RandomNumberGenerator.GetBytes(32));}catch(CryptographicException){wrongKey=true;}
            Check(altered&&moved&&wrongKey);
        });
        test("Beta18 structured location is ground acquisition, not a duplicate NPC",()=>
        {
            var item=new ItemDefinition{Key="hilt",Name="Hilt",Sources=[new(){Zone="Lake Rathetear",Mob="Under the aviak tower",Method="Ground spawn",Coordinates="+2513, +920"},new(){Zone="Lake Rathetear",Mob="{{Loc|Lake Rathetear|2513, 920|(+2513, +920)}}",Method="Drop"}]};
            var source=ProgressPresentation.SourcesFor(["hilt"],new(){Items=[item]});Check(source.Count==1&&source[0].Method=="Ground spawn"&&source[0].Additional.Count(x=>x.StartsWith("Coordinates:"))==1&&source[0].Additional.Contains("Under the aviak tower"));Check(source[0].Additional.All(x=>!x.Contains("{{Loc")));
            Check(AcquisitionPresentation.Method(new(){Mob="a golem",Method="Drop"})=="Drop");
        });
        test("Beta18 patch facts retain official sources without inferred coefficients", () =>
        {
            Check(PatchUpdates.Facts.All(f => f.Source.StartsWith("https://www.everquestlegends.com/", StringComparison.Ordinal)));
            Check(PatchUpdates.Facts.Any(f => f.Subject.Contains("Harm Touch") && f.Change.Contains("does not prove")));
            var catalog = new Catalog(); PatchUpdates.Apply(catalog); string before = PetResearch.Hash(catalog); PatchUpdates.Apply(catalog); Check(before == PetResearch.Hash(catalog));
        });
        test("Beta18 reviewed access supports non-raid instances and keeps stable checklist IDs", () =>
        {
            var catalog = Json.Read<Catalog>(Path.Combine(AppContext.BaseDirectory, "data", "catalog.json"));
            var quest = catalog.Quests.Single(q => q.Name == "Hate, Betrayal, and Trust");
            Check(quest.Steps.Any(s => s.Instructions.Contains("non-raid personal instance", StringComparison.OrdinalIgnoreCase)));
            Check(quest.Steps.Any(s => s.Instructions.Contains("level 46", StringComparison.OrdinalIgnoreCase)));
            Check(quest.Steps.Select(s => s.Id).Distinct().Count() == quest.Steps.Count);
            Check(catalog.Quests.Single(q => q.Name == "The Betrayer").Steps.Any(s => s.Instructions.Contains("600, 201, 15")));
        });
        test("Beta18 game path resolution does not write or invent files", () =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "catalyst-path-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "logs")); File.WriteAllText(Path.Combine(folder, "logs", "eqlog_Example_example.txt"), "retained");
                var resolved = DesktopPaths.Resolve(folder, "logs", "eqlog_Example_example.txt"); Check(File.ReadAllText(resolved) == "retained");
                var missing = DesktopPaths.Resolve(folder, "missing.txt"); Check(!File.Exists(missing));
                if (!OperatingSystem.IsWindows()) Check(DesktopPaths.Resolve(folder, "Logs", "EQLOG_EXAMPLE_EXAMPLE.TXT") == resolved);
                Check(File.ReadAllText(resolved) == "retained");
            }
            finally { Directory.Delete(folder, true); }
        });
        test("Beta18 Linux log identity survives append and detects same-prefix replacement",()=>
        {
            if(!OperatingSystem.IsLinux())return;string folder=Path.Combine(Path.GetTempPath(),"catalyst-log-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            try{string path=Path.Combine(folder,"eqlog.txt");string prefix=new string('a',300)+"\n";File.WriteAllText(path,prefix);var first=LogTailer.Read(path,null);File.AppendAllText(path,"second\n");var next=LogTailer.Read(path,first.Checkpoint);Check(!next.Reset&&next.Checkpoint.Generation==first.Checkpoint.Generation&&next.Lines.Single().Text=="second");File.Move(path,path+".old");File.WriteAllText(path,prefix+"new\n");var replacement=LogTailer.Read(path,next.Checkpoint);Check(replacement.Reset&&replacement.Checkpoint.Generation!=next.Checkpoint.Generation);}
            finally{Directory.Delete(folder,true);}
        });
        test("Beta18 XDG preference requires an absolute path", () =>
        {
            if (!OperatingSystem.IsLinux()) return; string? prior = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            try { Environment.SetEnvironmentVariable("XDG_DATA_HOME", "relative"); Check(Path.IsPathRooted(DesktopPaths.DataRoot())); Environment.SetEnvironmentVariable("XDG_DATA_HOME", "/tmp/catalyst-data-fixture"); Check(DesktopPaths.DataRoot() == "/tmp/catalyst-data-fixture/eql-catalyst"); }
            finally { Environment.SetEnvironmentVariable("XDG_DATA_HOME", prior); }
        });
    }
}
