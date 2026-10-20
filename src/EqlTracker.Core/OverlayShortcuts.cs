namespace EqlTracker.Core;

public enum OverlayShortcutAction { Objectives=1,Dps=2,ClickThrough=3 }
[Flags] public enum ShortcutModifiers { None=0,Alt=1,Control=2,Shift=4,Windows=8 }
public sealed record GlobalShortcut(ShortcutModifiers Modifiers,int VirtualKey)
{
    public string Label=>string.Join("+",new[]{Modifiers.HasFlag(ShortcutModifiers.Control)?"Ctrl":null,Modifiers.HasFlag(ShortcutModifiers.Alt)?"Alt":null,Modifiers.HasFlag(ShortcutModifiers.Shift)?"Shift":null,Modifiers.HasFlag(ShortcutModifiers.Windows)?"Win":null,OverlayShortcuts.KeyName(VirtualKey)}.Where(s=>s!=null));
}
internal static class OverlayShortcuts
{
    internal static GlobalShortcut Default(OverlayShortcutAction action)=>new(ShortcutModifiers.Control|ShortcutModifiers.Alt,action switch{OverlayShortcutAction.Objectives=>0x51,OverlayShortcutAction.Dps=>0x44,_=>0x54});
    internal static string KeyName(int key)=>key switch{>=0x41 and <=0x5A or >=0x30 and <=0x39=>((char)key).ToString(),>=0x70 and <=0x87=>"F"+(key-0x6F),0x20=>"Space",0x21=>"Page Up",0x22=>"Page Down",0x23=>"End",0x24=>"Home",0x25=>"Left",0x26=>"Up",0x27=>"Right",0x28=>"Down",0x2D=>"Insert",0x2E=>"Delete",_=>"Unknown"};
    internal static bool Valid(GlobalShortcut shortcut)=>shortcut.VirtualKey!=0x7B&&KeyName(shortcut.VirtualKey)!="Unknown"&&(shortcut.Modifiers&~(ShortcutModifiers)15)==0&&(shortcut.Modifiers!=ShortcutModifiers.None||shortcut.VirtualKey is >=0x70 and <=0x87);
    internal static Dictionary<OverlayShortcutAction,GlobalShortcut> Effective(CharacterProfile profile)=>Enum.GetValues<OverlayShortcutAction>().ToDictionary(a=>a,a=>profile.OverlayShortcuts.TryGetValue(a,out var value)&&value!=null&&Valid(value)?value:Default(a));
    internal static bool Validate(IReadOnlyDictionary<OverlayShortcutAction,GlobalShortcut> shortcuts,out string error)
    {
        if(Enum.GetValues<OverlayShortcutAction>().Any(a=>!shortcuts.TryGetValue(a,out var value)||value==null||!Valid(value))){error="Use Ctrl, Alt, Shift or Win with a letter, number or navigation key, or a function key. Windows reserves F12.";return false;}
        if(shortcuts.Values.Distinct().Count()!=3){error="Each overlay action needs a different shortcut.";return false;}
        error="";return true;
    }
    internal static string Label(CharacterProfile profile,OverlayShortcutAction action)=>Effective(profile)[action].Label;
}
