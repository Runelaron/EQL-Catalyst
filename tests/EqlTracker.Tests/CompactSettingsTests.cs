using EqlTracker.Core;
internal static class CompactSettingsTests
{
    internal static void Register(Action<string,Action> test)
    {
        void Check(bool value){if(!value)throw new Exception("Settings assertion failed");}
        foreach(int seconds in EncounterTimingSettings.Presets.Append(2).Append(17).Append(120))
            test("Idle cutoff round trip: "+seconds,()=>Check(EncounterTimingSettings.TryRead(seconds.ToString(),"0",out int idle,out int resume,out _)&&idle==seconds&&resume==0));
        foreach(string invalid in new[]{"","1","121","6.5","six","2147483648"})
            test("Invalid idle cutoff rejected: "+invalid,()=>Check(!EncounterTimingSettings.TryRead(invalid,"30",out _,out _,out _)));
        foreach(string invalid in new[]{"-1","301","11","1.5",""})
            test("Invalid resume rejected: "+invalid,()=>Check(!EncounterTimingSettings.TryRead("12",invalid,out _,out _,out _)));
        test("Resume permits cutoff equality and upper boundary",()=>Check(EncounterTimingSettings.TryRead("12","12",out _,out _,out _)&&EncounterTimingSettings.TryRead("120","300",out _,out _,out _)));
        test("Legacy profiles retain default overlay shortcuts without mutation",()=>{var p=new CharacterProfile();var keys=OverlayShortcuts.Effective(p);Check(keys[OverlayShortcutAction.Objectives].Label=="Ctrl+Alt+Q"&&keys[OverlayShortcutAction.Dps].Label=="Ctrl+Alt+D"&&keys[OverlayShortcutAction.ClickThrough].Label=="Ctrl+Alt+T"&&p.OverlayShortcuts.Count==0);});
        test("Shortcut validation rejects duplicates and Windows-reserved F12",()=>{var keys=OverlayShortcuts.Effective(new());keys[OverlayShortcutAction.Dps]=keys[OverlayShortcutAction.Objectives];Check(!OverlayShortcuts.Validate(keys,out _));Check(!OverlayShortcuts.Valid(new(ShortcutModifiers.Control,0x7B)));Check(!OverlayShortcuts.Valid(new(ShortcutModifiers.None,0x41)));Check(OverlayShortcuts.Valid(new(ShortcutModifiers.None,0x70)));});
        test("Custom shortcuts and timing persist additively with stable choices",()=>{var p=new CharacterProfile{EncounterTimeoutSeconds=17,EncounterResumeSeconds=45,DamageEmblem=DamageEmblem.Lightning,TrackerTabs=TrackerTabStyle.Dwarf,Theme="shadowman"};p.OverlayShortcuts[OverlayShortcutAction.Dps]=new(ShortcutModifiers.Control|ShortcutModifiers.Shift,0x4D);string json=System.Text.Json.JsonSerializer.Serialize(p,Json.Options);var read=System.Text.Json.JsonSerializer.Deserialize<CharacterProfile>(json,Json.Options)!;Check(read.EncounterTimeoutSeconds==17&&read.EncounterResumeSeconds==45&&OverlayShortcuts.Label(read,OverlayShortcutAction.Dps)=="Ctrl+Shift+M"&&read.DamageEmblem==DamageEmblem.Lightning&&read.TrackerTabs==TrackerTabStyle.Dwarf&&read.Theme=="shadowman");});
    }
}
