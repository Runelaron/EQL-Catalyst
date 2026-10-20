using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class CompactSettingsReview
{
    internal static void Run(string folder,TrackerController controller,QuestWindow tracker)
    {
        if(!controller.Demo)throw new InvalidOperationException("Synthetic review required.");
        Directory.CreateDirectory(Path.Combine(folder,"qa"));var checks=new List<string>();
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        void Flush(Window window){window.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);window.UpdateLayout();}
        void Save(string name,FrameworkElement element,double width,double height,double scale)
        {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(element,width,height,scale)));using var stream=File.Create(Path.Combine(folder,"qa",name+".png"));encoder.Save(stream);}
        var p=controller.Profile;var settings=new SettingsWindow(controller){Left=-15000,Top=-15000,ShowActivated=false,ShowInTaskbar=false};settings.Show();Flush(settings);
        ComboBox Choice(string tag)=>Usability.Tree(settings).OfType<ComboBox>().Single(b=>Equals(b.Tag,tag));
        var damage=Choice("damage-emblem-selector");var tabs=Choice("tab-style-selector");
        var style=Usability.Tree(settings).OfType<ComboBox>().Single(b=>b.Items.Contains("Clean"));
        var theme=Usability.Tree(settings).OfType<ComboBox>().Single(b=>b.Items.Contains("Shadowman"));
        var meter=Usability.Tree(settings).OfType<ComboBox>().Single(b=>b.Items.Contains("Luminous"));
        var damageItems=damage.Items.Cast<DamageAppearanceChoice>().ToArray();var tabItems=tabs.Items.Cast<TabAppearanceChoice>().ToArray();
        Check(damageItems.Length==6&&damageItems.Select(c=>c.Name).SequenceEqual(damageItems.Select(c=>c.Name).Order(StringComparer.OrdinalIgnoreCase)),"Six Damage names are alphabetical without gallery numbers");
        Check(tabItems.Length==14&&tabItems.Select(c=>c.Name).SequenceEqual(tabItems.Select(c=>c.Name).Order(StringComparer.OrdinalIgnoreCase))&&tabItems.Any(c=>c.Name=="Cut Stone - Dwarf"),"Fourteen alphabetical tab choices retain Cut Stone - Dwarf");
        Check(Usability.Tree(settings).OfType<Expander>().All(e=>e.Header?.ToString()?.StartsWith("Damage emblem")!=true),"Old emblem gallery removed");
        foreach(string value in new[]{"Clean","Vibrant"})
        {
            style.SelectedItem=value;Flush(settings);
            foreach(var item in damageItems){damage.SelectedItem=item;Flush(settings);Check(p.DamageEmblem==item.Value&&IconArtwork.Emblem==item.Value,"Live Damage selection: "+value+" / "+item.Name);}
        }
        tracker.Left=tracker.Top=-15000;tracker.ShowActivated=false;tracker.ShowInTaskbar=false;tracker.Show();tracker.ShowView(TrackerTab.Gear);Flush(tracker);
        var trackerTabs=Usability.Tree(tracker).OfType<TabControl>().First();var selectedTab=trackerTabs.SelectedItem;
        foreach(var item in tabItems)
        {
            tabs.SelectedItem=item;Flush(settings);Flush(tracker);
            Check(p.TrackerTabs==item.Value&&trackerTabs.SelectedItem==selectedTab&&Usability.Tree(tracker).OfType<TrackerTabChrome>().Any(c=>c.StyleId==item.Value),"Tab style updates actual Tracker without changing its selected page: "+item.Name);
        }
        tabs.SelectedValue=TrackerTabStyle.Dwarf;damage.SelectedValue=DamageEmblem.Fire;
        foreach(string name in new[]{"Minimal","Frost","Luminous"}){meter.SelectedItem=name;Flush(settings);Check(p.MeterStyle==name&&Usability.Tree(settings).OfType<MeterPreview>().Single().ToolTip!=null,"Meter example follows "+name);}
        meter.SelectedItem="Frost";
        settings.SelectSection(SettingsSection.Combat);Flush(settings);
        var timing=Usability.Tree(settings).OfType<EncounterTimingControls>().Single();
        Check(timing.Cutoff.Items.Cast<string>().SequenceEqual(new[]{"6 seconds","8 seconds","10 seconds","12 seconds","Custom"}),"Idle dropdown exposes approved presets and Custom");
        foreach(int preset in EncounterTimingSettings.Presets){timing.Cutoff.SelectedItem=preset+" seconds";Flush(settings);Check(timing.Custom.Visibility==Visibility.Collapsed&&timing.IdleText==preset.ToString(),"Preset hides custom field without reserving space: "+preset);}
        var custom=timing.Custom.Children.OfType<TextBox>().Single();timing.Cutoff.SelectedItem="Custom";custom.Text="17";Flush(settings);
        Check(timing.TryRead(out int idle,out _)&&idle==17&&timing.Custom.Visibility==Visibility.Visible,"Custom cutoff is visible and reads 17 seconds");
        timing.Cutoff.SelectedItem="8 seconds";timing.Cutoff.SelectedItem="Custom";Check(custom.Text=="17","Custom draft survives preset selection");
        int before=p.EncounterTimeoutSeconds;custom.Text="6.5";Check(!timing.TryRead(out _,out _)&&timing.Error.Visibility==Visibility.Visible&&p.EncounterTimeoutSeconds==before,"Invalid custom cutoff explains rejection without overwriting saved timing");custom.Text="17";
        Check(Usability.Tree(timing).OfType<TextBlock>().Any(t=>t.Text=="Idle cutoff")&&Usability.Tree(timing).OfType<TextBlock>().Any(t=>t.Text=="Resume"),"Timing titles preserve title case");
        var legacy=new EncounterTimingControls(UI.Input("19"),UI.Input("30"),19);Check(Equals(legacy.Cutoff.SelectedItem,"Custom")&&legacy.IdleText=="19","Existing nonpreset timing opens as Custom");
        settings.SelectSection(SettingsSection.Advanced);Flush(settings);
        Check(Usability.Tree(settings).OfType<Expander>().All(e=>e.Header?.ToString() is not "Hotkeys" and not "Display setup · reversible"),"Display setup and Hotkeys are always expanded");
        var shortcuts=Usability.Tree(settings).OfType<ShortcutSettings>().Single();
        Check(Usability.Tree(shortcuts).OfType<Button>().Count(b=>Equals(b.Content,"Change…"))==3,"All three overlay actions expose Change and Reset");
        var registered=new Dictionary<int,GlobalShortcut>();var dispatched=new List<int>();bool fail=false;
        using(var keys=new Native.Hotkeys(dispatched.Add,(id,key)=>{if(fail&&key.VirtualKey==0x4D)return false;registered[id]=key;return true;},id=>registered.Remove(id)))
        {
            var request=OverlayShortcuts.Effective(p);Check(keys.TryApply(request,out _),"Shortcut registration accepts defaults");var original=registered.ToDictionary(k=>k.Key,k=>k.Value);
            request[OverlayShortcutAction.Dps]=new(ShortcutModifiers.Control|ShortcutModifiers.Shift,0x4D);fail=true;
            Check(!keys.TryApply(request,out _)&&registered.Count==3&&original.All(k=>registered.GetValueOrDefault(k.Key)==k.Value),"Unavailable shortcut leaves all previous registrations intact");
            request[OverlayShortcutAction.Objectives]=new(ShortcutModifiers.Control|ShortcutModifiers.Shift,0x4E);
            Check(!keys.TryApply(request,out _)&&registered.Count==3&&original.All(k=>registered.GetValueOrDefault(k.Key)==k.Value),"Failure after a successful provisional registration cleans it up and retains all old keys");
            request[OverlayShortcutAction.Objectives]=OverlayShortcuts.Default(OverlayShortcutAction.Objectives);fail=false;
            Check(keys.TryApply(request,out _)&&registered.Count==3,"Successful shortcut change replaces only obsolete registration");
            (request[OverlayShortcutAction.Dps],request[OverlayShortcutAction.Objectives])=(request[OverlayShortcutAction.Objectives],request[OverlayShortcutAction.Dps]);
            Check(keys.TryApply(request,out _)&&registered.Count==3,"Shortcut swap reuses registered chords without conflicts");
            int trigger=registered.Single(k=>k.Value==request[OverlayShortcutAction.Dps]).Key;keys.Dispatch(trigger);Check(dispatched.SequenceEqual(new[]{2}),"Native registration identifiers dispatch the selected action");
        }
        Check(registered.Count==0,"Disposing shortcuts releases all registrations");
        var change=Usability.Tree(shortcuts).OfType<Button>().First(b=>Equals(b.Content,"Change…"));change.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(ShortcutSettings.IsRecording,"Routed Change action starts shortcut recording");
        var recordingBox=Usability.Tree(shortcuts).OfType<TextBox>().First();
        recordingBox.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(recordingBox),0,System.Windows.Input.Key.Escape){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});
        Check(!ShortcutSettings.IsRecording&&recordingBox.Text==shortcuts.Draft[OverlayShortcutAction.Objectives].Label,"Routed Escape cancels recording and restores the draft label");
        shortcuts.Draft[OverlayShortcutAction.Dps]=new(ShortcutModifiers.Control|ShortcutModifiers.Shift,0x4D);
        var save=Usability.Tree(settings).OfType<Button>().Single(b=>Equals(b.Content,"Save changes"));save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(p.EncounterTimeoutSeconds==17&&OverlayShortcuts.Label(p,OverlayShortcutAction.Dps)=="Ctrl+Shift+M","Save applies timing and shortcut drafts for an offline profile");
        custom.Text="6.5";save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(p.EncounterTimeoutSeconds==17&&settings.SelectedSection==SettingsSection.Combat,"Invalid Save preserves the previous valid cutoff and reveals its explanation");custom.Text="17";
        shortcuts.Draft[OverlayShortcutAction.Dps]=shortcuts.Draft[OverlayShortcutAction.Objectives];save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(OverlayShortcuts.Label(p,OverlayShortcutAction.Dps)=="Ctrl+Shift+M"&&settings.SelectedSection==SettingsSection.Advanced,"Duplicate shortcut Save preserves working profile bindings");
        shortcuts.Draft[OverlayShortcutAction.Dps]=OverlayShortcuts.Default(OverlayShortcutAction.Dps);save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        foreach(string themeName in new[]{"Coldain","Sunstrider","Shadowman"})
        {
            theme.SelectedItem=themeName;style.SelectedItem="Vibrant";Flush(settings);
            foreach(var section in new[]{SettingsSection.Appearance,SettingsSection.Combat,SettingsSection.Advanced})
            {
                settings.SelectSection(section);Flush(settings);
                foreach(double scale in new[]{1d,1.5,2d,3d})Save($"{themeName.ToLowerInvariant()}-{section.ToString().ToLowerInvariant()}-{scale:0.0}x",(FrameworkElement)settings.Content,940,760,scale);
                settings.Width=640;settings.Height=660;Flush(settings);Save($"{themeName.ToLowerInvariant()}-{section.ToString().ToLowerInvariant()}-narrow",(FrameworkElement)settings.Content,640,660,1);
                settings.Width=940;settings.Height=760;Flush(settings);
            }
            settings.SelectSection(SettingsSection.Appearance);Flush(settings);
            foreach(var selector in new[]{damage,tabs})
            {
                selector.IsDropDownOpen=true;Flush(settings);
                var popup=(Popup)selector.Template.FindName("PART_Popup",selector);
                Check(popup.Child is FrameworkElement{ActualWidth:>0},themeName+" visual dropdown has measurable contents");
                var content=(FrameworkElement)popup.Child;Save(themeName.ToLowerInvariant()+"-"+selector.Tag,content,content.ActualWidth,content.ActualHeight,2);selector.IsDropDownOpen=false;
            }
            if(themeName=="Shadowman")
            {
                foreach(string token in new[]{"Surface","InnerSurface","DetailSurface","AlternateRow","ButtonFace"}){var color=UI.Brush(token).Color;Check(color.R==color.G&&color.G==color.B&&color.R<=0x25,"Shadowman uses neutral near-black surfaces: "+token);}
                Check(UI.Brush("Spell").Color!=UI.Brush("Melee").Color&&UI.Brush("Mint").Color.G>UI.Brush("Mint").Color.R,"Shadowman preserves semantic combat colors");
            }
            tracker.ShowView(TrackerTab.Exaltations);Flush(tracker);
            var effect=Usability.Tree(tracker).OfType<DockPanel>().Single(d=>d.Children.OfType<TextBlock>().Any(t=>t.Text=="EFFECT"));
            Check(effect.Margin.Left==0&&effect.Margin.Right==0,themeName+" Effect inspector has no redundant horizontal outer inset");
            foreach(double scale in new[]{1d,1.5,2d,3d})Save(themeName.ToLowerInvariant()+"-exaltation-spacing-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x",(FrameworkElement)tracker.Content,1220,800,scale);
        }
        var inspector=(DockPanel)FocusedLayout.FloatingInspector("Item Details",UI.Text("Synthetic item"));
        Check(inspector.Margin.Left==0&&inspector.Margin.Right==0&&inspector.Children.OfType<Border>().Single().Padding==new Thickness(6),"Floating inspectors remove outer horizontal padding and retain internal padding");
        GearBuildFixtures.Apply(controller);tracker.ShowView(TrackerTab.Gear);Flush(tracker);
        foreach(var view in new[]{TrackerTab.Gear,TrackerTab.Pet,TrackerTab.Items,TrackerTab.Exaltations})
        {
            tracker.ShowView(view);Flush(tracker);
            foreach(double scale in new[]{1d,1.5,2d,3d})Save("tracker-"+view+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x",(FrameworkElement)tracker.Content,1220,800,scale);
        }
        Json.Write(Path.Combine(folder,"compact-settings-checks.json"),new{Passed=checks.Count,Checks=checks,PhysicalKeyboardAndDpiAcceptance="Separate from synthetic WPF checks"});settings.Close();
    }
}
