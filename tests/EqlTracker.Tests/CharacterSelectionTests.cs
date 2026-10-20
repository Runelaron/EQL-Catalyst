using EqlTracker.Core;

internal static class CharacterSelectionTests
{
    internal static void Register(Action<string,Action> test)
    {
        static void Check(bool condition){if(!condition)throw new Exception("Character discovery regression");}
        static void InFolder(Action<string> run)
        {
            string root=Path.Combine(Path.GetTempPath(),"eql-characters-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{run(root);}finally{if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("eql-characters-"))Directory.Delete(root,true);}
        }
        test("Character registry retains same-name characters on different servers",()=>
        {
            var merged=LauncherPreferences.Merge([new("ExampleHero","First","")],[new("examplehero","first",""),new("ExampleHero","Second","")]);
            Check(merged.Count==2&&merged[0].Server=="First"&&merged[1].Server=="Second");
        });
        test("Character discovery uses owned filenames and ignores other players in who output",()=>InFolder(root=>
        {
            Directory.CreateDirectory(Path.Combine(root,"Logs"));File.WriteAllText(Path.Combine(root,"Logs","eqlog_ExampleHero_First.txt"),"[Tue Oct 20 21:00:00 2026] [50 WAR] AnotherPlayer\n");
            File.WriteAllText(Path.Combine(root,"UI_ExampleHero_Second_LO1.ini"),"");File.WriteAllText(Path.Combine(root,"ExampleMage_First-Inventory.txt"),"");
            var found=LauncherPreferences.FromGame(root);Check(found.Count==3&&found.All(p=>p.Character!="AnotherPlayer")&&found.Count(p=>p.Character=="ExampleHero")==2);
            Check(!Directory.Exists(Path.Combine(root,"ExampleHero_First")));
        }));
        test("Discovered characters save without changing the last opened character or creating profiles",()=>InFolder(root=>
        {
            var prefs=new LauncherPreferences();var current=new LaunchProfile("ExampleHero","First","");prefs.Remember(root,current);
            Check(prefs.Register(root,[current,new("ExampleMage","Second","")]));var read=LauncherPreferences.Read(root);
            Check(read.LastProfile==current&&read.KnownProfiles.Count==2&&read.Version==2&&Directory.GetDirectories(root).Length==0);
            Check(!read.Register(root,read.KnownProfiles));
        }));
        test("Legacy launcher preferences preserve last profile and add a registry on discovery",()=>InFolder(root=>
        {
            File.WriteAllText(Path.Combine(root,"launcher.json"),"{\"Version\":1,\"LastProfile\":{\"Character\":\"ExampleHero\",\"Server\":\"First\",\"GameFolder\":\"\"}}");
            var prefs=LauncherPreferences.Read(root);Check(prefs.KnownProfiles.Count==0&&prefs.LastProfile?.Character=="ExampleHero");
            prefs.Register(root,[prefs.LastProfile!]);Check(LauncherPreferences.Read(root).KnownProfiles.Count==1);
        }));
        test("Discovered new profiles keep unknown character facts and capture opt-ins off",()=>
        {
            var p=LauncherPreferences.NewProfile(new("ExampleHero","First",""));
            Check(p.CurrentClasses.Count==0&&p.ActiveLoadout.Level==0&&p.ActiveLoadout.Race==""&&!p.NativeSync.Periodic&&!p.NativeSync.Login);
        });
    }
}
