using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public partial class App
{
    private IReadOnlyList<LaunchProfile> characterChoices = [];
    private readonly List<FileSystemWatcher> characterWatchers = [];
    private readonly DispatcherTimer characterDiscoveryDelay = new() { Interval=TimeSpan.FromSeconds(1.5) };
    private Task? characterDiscovery;
    private List<LaunchProfile>? savedCharacterProfiles;
    private bool characterSwitchAudit;
    internal event Action? CharactersChanged;
    internal IReadOnlyList<LaunchProfile> CharacterChoices => characterChoices;
    private LaunchProfile ActiveCharacter => new(Controller.Profile.Character,Controller.Profile.Server,Controller.Profile.GameFolder);
    internal async Task RefreshCharacters()
    {
        if(Exiting)return;
        if(Controller.Demo)
        {
            characterChoices=LauncherPreferences.Merge([ActiveCharacter],[new("ExampleMage","example",""),new("ExampleHero","second-server","")]);CharactersChanged?.Invoke();return;
        }
        if(characterDiscovery is { IsCompleted:false }){await characterDiscovery;return;}
        async Task Discover()
        {
            try
            {
                var current=ActiveCharacter;string root=DataRoot;
                var result=await Task.Run(()=>
                {
                    var preferences=LauncherPreferences.Read(root);
                    savedCharacterProfiles ??= LauncherPreferences.Existing(root);
                    var known=LauncherPreferences.Merge([current],LauncherPreferences.Merge(savedCharacterProfiles,preferences.KnownProfiles));
                    var detected=new List<LaunchProfile>();
                    foreach(var folder in known.Select(p=>p.GameFolder).Where(p=>p.Length>0).Distinct(StringComparer.OrdinalIgnoreCase))
                        try{detected.AddRange(LauncherPreferences.FromGame(folder));}catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException){ /* Keep cached identities when a folder is offline. */ }
                    var merged=LauncherPreferences.Merge(known,detected);preferences.Register(root,merged);return merged;
                });
                if(Exiting)return;
                characterChoices=result;CharactersChanged?.Invoke();WatchCharacterFolders();
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {if(!Exiting){characterChoices=LauncherPreferences.Merge([ActiveCharacter],characterChoices);CharactersChanged?.Invoke();}}
        }
        characterDiscovery=Discover();await characterDiscovery;
    }
    private void WatchCharacterFolders()
    {
        var watched=characterWatchers.Select(w=>w.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(string folder in characterChoices.Select(p=>p.GameFolder).Where(p=>p.Length>0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if(watched.Contains(folder)||!Directory.Exists(folder))continue;
            try
            {
                var watcher=new FileSystemWatcher(folder){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName,EnableRaisingEvents=true};
                void Changed(object? sender,FileSystemEventArgs e)
                {
                    string name=Path.GetFileName(e.FullPath);
                    if(!(name.StartsWith("eqlog_",StringComparison.OrdinalIgnoreCase)||name.StartsWith("UI_",StringComparison.OrdinalIgnoreCase)||name.EndsWith("-Inventory.txt",StringComparison.OrdinalIgnoreCase)))return;
                    Dispatcher.BeginInvoke(()=>{if(Exiting)return;characterDiscoveryDelay.Stop();characterDiscoveryDelay.Start();});
                }
                watcher.Created+=Changed;watcher.Renamed+=(s,e)=>Changed(s,e);characterWatchers.Add(watcher);
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException){ /* Retry on the next explicit discovery. */ }
        }
    }
    private void StartCharacterDiscovery()
    {
        characterDiscoveryDelay.Tick+=async(_,_)=>{characterDiscoveryDelay.Stop();await RefreshCharacters();};_ = RefreshCharacters();
    }
    internal bool SwitchCharacter(LaunchProfile target, TrackerTab tab)
    {
        if(Exiting||Controller.Demo||LauncherPreferences.SameIdentity(ActiveCharacter,target))return false;
        if(!target.Valid||!characterChoices.Any(p=>p==target))return false;
        if(!CanChangeEquipmentContext())return false;
        try
        {
            if(Mutex.TryOpenExisting("Local\\EQLTracker-"+target.Character.ToLowerInvariant()+"-"+target.Server.ToLowerInvariant(),out var running))
            {running.Dispose();MessageBox.Show("That character is already open. Use its system-tray icon; your current character remains open.","EQL Catalyst");return false;}
            if(File.Exists(Path.Combine(target.DataFolder(DataRoot),"tracker.db"))&&!LauncherPreferences.Existing(DataRoot).Any(p=>LauncherPreferences.SameIdentity(p,target)))
                throw new IOException("The selected profile could not be verified. Its data was not changed.");
            string process=Environment.ProcessPath??throw new IOException("Application executable is unavailable.");
            var start=new ProcessStartInfo(process){UseShellExecute=false,WorkingDirectory=AppContext.BaseDirectory,CreateNoWindow=true};
            if(Path.GetFileNameWithoutExtension(process).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach(string argument in new[]{"--character",target.Character,"--server",target.Server,"--game-folder",target.GameFolder,"--data-root",DataRoot,"--wait-for-parent",Environment.ProcessId.ToString(),"--open-tracker","--tracker-tab",tab.ToString()})start.ArgumentList.Add(argument);
            if(characterSwitchAudit)start.ArgumentList.Add("--launch-audit");
            Controller.Save();
            using var launched=Process.Start(start)??throw new IOException("Could not start the selected character.");
            ExitTracker();return true;
        }
        catch(Exception e)when(e is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {MessageBox.Show("Unable to switch characters. Your current profile remains open.\n\n"+e.Message,"EQL Catalyst");return false;}
    }
    private void StopCharacterDiscovery()
    {characterDiscoveryDelay.Stop();foreach(var watcher in characterWatchers)watcher.Dispose();characterWatchers.Clear();}
}

public sealed partial class QuestWindow
{
    private ComboBox? characterSelector;
    private bool loadingCharacters;
    private void InstallCharacterSelector()
    {
        var app=(App)Application.Current;
        characterSelector=new(){Width=215,Height=28,Margin=new(8,5,0,0),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Right};
        string help="Switch the whole app to another character and server. Ordinary builds save automatically; unsaved Theorycraft work is protected. Characters are discovered from local game filenames, not other players in /who.";
        System.Windows.Automation.AutomationProperties.SetName(characterSelector,"Character and server");
        void Update()
        {
            loadingCharacters=true;
            var current=new LaunchProfile(Controller.Profile.Character,Controller.Profile.Server,Controller.Profile.GameFolder);
            var choices=LauncherPreferences.Merge([current],app.CharacterChoices);
            characterSelector.ItemsSource=choices;characterSelector.SelectedItem=choices.First(p=>LauncherPreferences.SameIdentity(p,current));characterSelector.ToolTip=current+"\n"+help;loadingCharacters=false;
        }
        void Adapt(){bool compact=ActualWidth<800;TrackerTabChrome.SetCompactHeaders(tabs,compact);characterSelector.Width=compact?130:215;}
        SizeChanged+=(_,_)=>Adapt();Loaded+=(_,_)=>Adapt();
        characterSelector.DropDownOpened+=async(_,_)=>await app.RefreshCharacters();
        characterSelector.SelectionChanged+=(_,_)=>{if(loadingCharacters||characterSelector.SelectedItem is not LaunchProfile choice)return;if(!app.SwitchCharacter(choice,SelectedTab))Update();};
        app.CharactersChanged+=Update;Closed+=(_,_)=>app.CharactersChanged-=Update;
        TrackerTabChrome.SetHeaderAccessory(tabs,characterSelector);Update();Adapt();
    }
}
