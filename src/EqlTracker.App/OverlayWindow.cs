using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class OverlayWindow : FrostWindow
{
    internal static Border CompactObjectiveCard(ObjectiveRow item,bool fallback)
    {var card=UI.Card(UI.Text(CompactObjective.Text(item),12,"Foreground",true),new(0,0,0,3));card.Padding=new(4,2,4,2);card.ToolTip=CompactObjective.Tooltip(item,fallback);Usability.Tooltips(card);return card;}
    public bool ClickThrough => settings.ClickThrough;
    public bool IsDps { get; }
    private readonly StackPanel objectives = new();
    private readonly TextBlock footer = UI.Text("", 11, "Muted"), alert = UI.Text("", 11, "Mint");
    private readonly TextBlock split = UI.Text("", 12), legend = UI.Text("", 11, "Muted");
    private readonly DamageRowsView damageRows = new();
    private readonly DataGrid groupRows = UI.Table(("Member","Name",130,null),("Damage","Total",70,"N0"),("DPS","Rate",60,"N1"),("Share","Share",55,"0.#'%'"));
    private readonly Grid splitBar = new() { Height = 4, Margin = new(0, 2, 0, 3) };
    private readonly Dictionary<string, Button> actors = [], scopes = [], sorts = [];
    private readonly OverlaySettings settings;
    private readonly Button lockButton, clearFilters, expandButton;
    private OverlayFilterWindow? filterWindow;private ThemedUtilityWindow? helpWindow;
    private readonly StackPanel fullSummary = new(), miniSummary = new();
    private readonly AnimatedNumber miniDps = new() { Format = "N1", Suffix = " dps", FontSize = 14, FontWeight = FontWeights.SemiBold };
    private readonly AnimatedNumber secondaryDps = new() { Format = "N1", FontSize = 12 };
    private readonly ActorBadge displayBadge = new(){Width=13,Height=13};
    private readonly DockPanel paired = new() { LastChildFill = true };
    private readonly TextBlock miniDuration = UI.Text("", 11, "Muted");
    private readonly ActorBadge badge = new();
    private readonly MeterOverview overview = new();
    private readonly Dictionary<string, MenuItem> styleChoices = [];
    private MenuItem? motionChoice;
    private string visualContext = "";
    private string fullHeading = "";
    private readonly DropShadowEffect glow = new() { BlurRadius = 9, ShadowDepth = 0, Opacity = .28 };
    private readonly Grid miniChoices = new();
    private readonly StackPanel tinyScopes = UI.Row();
    private readonly ComboBox miniActor, miniScope;
    private bool updatingChoices;
    private string objectiveSignature = "";
    public OverlayWindow(TrackerController controller, bool dps) : base(controller, dps ? "Pet damage" : "Zone Objectives", "", dps ? controller.Profile.Dps.Width : controller.Profile.Objectives.Width, dps ? controller.Profile.Dps.Height : controller.Profile.Objectives.Height, true)
    {
        IsDps = dps; settings = dps ? controller.Profile.Dps : controller.Profile.Objectives;
        MinWidth = 128; MinHeight = dps ? 72 : 90; RequestedVisible = true;
        HideRequested = () => RequestedVisible = false;
        Left = settings.Left; Top = settings.Top;
        var expand = expandButton = HeaderButton("↗", "Open detailed tracker", () => { if (dps) ((App)Application.Current).OpenDps(); else ((App)Application.Current).OpenQuests("Current zone"); });
        lockButton = HeaderButton("◇", "Lock position and size", () => { settings.Locked = !settings.Locked; ApplySettings(); Controller.Save(); });
        HeaderActions.Children.Insert(0, expand); HeaderActions.Children.Insert(1, lockButton);
        miniActor = UI.Select(["Pet", "Self", "All", "Group"], ActorLabel(controller.Profile.OverlayMeter!.Actor), double.NaN);
        miniScope = UI.Select(["Fight", "Sess."], controller.Profile.OverlayMeter!.Scope == "Session" ? "Sess." : "Fight", double.NaN);
        clearFilters = UI.Button("Filters…", OpenFilters);
        clearFilters.FontSize = 11; clearFilters.Visibility = Visibility.Visible;
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        if (dps)
        {
            BuildTitleDisplaySelector();
            var controls = new DockPanel { LastChildFill = true, Margin = new(0, 0, 0, 4) };
            var time = UI.Row(); foreach (var name in new[] { "Fight", "Session" }) { var b = Choice(name, () => { Controller.Profile.OverlayMeter!.Scope = name; Changed(); }); scopes[name] = b; time.Children.Add(b); }
            DockPanel.SetDock(time, Dock.Right); controls.Children.Add(time);
            var actor = UI.Row(); foreach (var name in new[] { "Pet", "Player", "Combined", "Group" }) { var b = Choice(name == "Player" ? "Self" : name, () => SelectDisplay(name)); actors[name] = b; actor.Children.Add(b); } controls.Children.Add(actor);
            var sortRow = new WrapPanel { Margin = new(0, 0, 0, 3) }; sortRow.Children.Add(UI.Text("sort: ", 11, "Muted"));
            foreach (var (key, label) in new[] { ("Damage", "total"), ("Hits", "hits"), ("Average", "avg"), ("DPS", "dps") }) { var b = Choice(label, () => { Controller.Profile.OverlayMeter!.Sort = key; Changed(); }); b.FontSize = 11; sorts[key] = b; sortRow.Children.Add(b); }
            sortRow.Children.Add(clearFilters);
            splitBar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); splitBar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var melee = new Border(); melee.SetResourceReference(Border.BackgroundProperty, "Melee"); var spell = new Border(); spell.SetResourceReference(Border.BackgroundProperty, "Spell"); Grid.SetColumn(spell, 1); splitBar.Children.Add(melee); splitBar.Children.Add(spell);
            foreach (var child in new UIElement[] { controls, split, splitBar, legend, sortRow }) fullSummary.Children.Add(child);
            miniChoices.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); miniChoices.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            foreach (var combo in new[] { miniActor, miniScope }) { combo.MinHeight = combo.Height = 20; combo.Width = combo == miniActor ? 66 : 72; combo.Padding = new(3, 0, 3, 0); combo.FontSize = 11; combo.Margin = new(0, 0, 2, 2); }
            miniActor.ToolTip = "Damage actor"; miniScope.ToolTip = "Fight or session";
            Grid.SetColumn(miniScope, 1); miniChoices.Children.Add(miniActor); miniChoices.Children.Add(miniScope);
            miniActor.SelectionChanged += (_, _) => { if (!updatingChoices)SelectDisplay(miniActor.SelectedItem?.ToString() switch {"Self"=>"Player","All"=>"Combined","Group"=>"Group",_=>"Pet"}); };
            miniScope.SelectionChanged += (_, _) => { if (!updatingChoices) { Controller.Profile.OverlayMeter!.Scope = miniScope.SelectedItem?.ToString() == "Sess." ? "Session" : "Fight"; Changed(); } };
            var miniValue = new DockPanel(); DockPanel.SetDock(miniDuration, Dock.Right); miniValue.Children.Add(miniDuration); miniValue.Children.Add(miniDps);
            foreach (var scope in new[] { "Fight", "Session" }) { var button = Choice(scope.ToUpperInvariant(), () => { Controller.Profile.OverlayMeter!.Scope = scope; Changed(); }); button.FontSize = 10; button.Padding = new(4, 0, 4, 0); button.ToolTip = scope == "Fight" ? "Current or newest fight" : "All combat since tracker launch"; tinyScopes.Children.Add(button); }
            miniSummary.Children.Add(miniChoices); miniSummary.Children.Add(miniValue); miniSummary.Children.Add(tinyScopes);
            root.Children.Add(UI.Stack(fullSummary, miniSummary)); Grid.SetRow(damageRows, 1); root.Children.Add(damageRows); Grid.SetRow(groupRows,1);root.Children.Add(groupRows);groupRows.Visibility=Visibility.Collapsed;GroupMemberPresentation.Configure(groupRows,true);
            var displayMenu=new ContextMenu();FillDisplayChoices(displayMenu,secondary:true);OverlayMenuDensity.Attach(displayMenu,Controller.Profile);
            var flip=OverlayMenus.Button(displayMenu,"Choose secondary Pet, Self, Combined or Group DPS",timedDismissal:true);flip.Content=displayBadge;flip.Width=20;flip.Height=17;flip.Tag="overlay-display-selector";
            DockPanel.SetDock(flip, Dock.Right); paired.Children.Add(flip);
            secondaryDps.FontSize=11;secondaryDps.Margin=new(0);paired.Children.Add(secondaryDps);
            var menu = new ContextMenu();
            void Entry(object label, Action action) { var item = new MenuItem { Header = label.ToString()!.StartsWith("Click-through") ? OverlayMenus.ClickThroughHeader() : label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
            var displayChoices=new MenuItem{Header="Display",ToolTip="Choose whose outgoing DPS is shown. Group combines characters with verified pets and stays separate from personal totals."};FillDisplayChoices(displayChoices);menu.Items.Add(displayChoices);
            foreach (var scopeName in new[] { "Fight", "Session" }) OverlayMenus.Choice(menu, scopeName + " scope", () => Controller.Profile.OverlayMeter!.Scope == scopeName, () => { Controller.Profile.OverlayMeter!.Scope = scopeName; Changed(); });
            menu.Items.Add(new Separator());
            Entry("Combat Lab", () => ((App)Application.Current).OpenDps());
            Entry("Objective panel",()=>((App)Application.Current).OpenOverlay(false));
            Entry("Quest & Gear Tracker",()=>((App)Application.Current).OpenQuests());
            Entry("Settings",()=>((App)Application.Current).OpenSettings());
            menu.Items.Add(new Separator());
            Entry("Lock / unlock", () => lockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            Entry(OverlayMenus.ClickThroughHeader(), () => { settings.ClickThrough = !settings.ClickThrough; ApplySettings(); Controller.Save(); });
            menu.Items.Add(new Separator());
            var sorting = new MenuItem { Header = "Sort by" }; menu.Items.Add(sorting);
            foreach (var sortName in new[] { "Damage", "Hits", "Average", "DPS" }) OverlayMenus.Choice(sorting, sortName, () => Controller.Profile.OverlayMeter!.Sort == sortName, () => { Controller.Profile.OverlayMeter!.Sort = sortName; Changed(); });
            Entry("Filters…", OpenFilters);
            var appearance = new MenuItem { Header = "Appearance" }; menu.Items.Add(appearance);
            foreach (string style in new[] { "Minimal", "Frost", "Luminous" }) styleChoices[style] = OverlayMenus.Choice(appearance, style, () => Controller.Profile.MeterStyle == style, () => { Controller.Profile.MeterStyle = style; Changed(); });
            motionChoice = OverlayMenus.Choice(appearance, "Smooth motion", () => Controller.Profile.SmoothMeterMotion, () => { Controller.Profile.SmoothMeterMotion = !Controller.Profile.SmoothMeterMotion; Changed(); });
            var linked = new MenuItem { Header = "Show healing beside damage", IsCheckable = true, IsChecked = controller.Profile.ShowLinkedHealing };
            linked.Click += (_, _) => { Controller.Profile.ShowLinkedHealing = linked.IsChecked; Controller.Save(); Refresh(); }; appearance.Items.Add(linked); appearance.SubmenuOpened += (_, _) => linked.IsChecked = Controller.Profile.ShowLinkedHealing;
            appearance.Items.Add(new Separator());
            var sizes = new MenuItem { Header = "Window size" }; appearance.Items.Add(sizes);
            foreach (var (label, width, height) in new[] { ("Minimum · 128 × 72", 128d, 72d), ("Hotbar · 190 × 88", 190d, 88d), ("Summary · 190 × 112", 190d, 112d), ("Compact · 240 × 160", 240d, 160d), ("Full · 440 × 410", 440d, 410d) })
                OverlayMenus.Choice(sizes, label, () => Math.Abs(Width - width) < 1 && Math.Abs(Height - height) < 1, () => SetMeterSize(width, height));
            OverlayMenuDensity.Attach(menu,Controller.Profile); OverlayMenus.AutoDismiss(menu, true); HeadingTitle.ContextMenu = menu;
            var options = OverlayMenus.Button(menu, "Meter size, actor, sorting and appearance", timedDismissal: true); HeaderActions.Children.Insert(0, options);
        }
        else
        {
            var scroll = UI.Scroll(objectives); Grid.SetRow(scroll, 1); root.Children.Add(scroll);
            var menu = new ContextMenu();
            void Entry(string label,Action action) { var item=new MenuItem { Header=label.StartsWith("Click-through")?OverlayMenus.ClickThroughHeader():label };item.Click+=(_,_)=>action();menu.Items.Add(item); }
            Entry("Combat Lab",()=>((App)Application.Current).OpenDps());
            Entry("Damage tracker",()=>((App)Application.Current).OpenOverlay(true));
            Entry("Quest & Gear Tracker",()=>((App)Application.Current).OpenQuests());
            Entry("Settings",()=>((App)Application.Current).OpenSettings());
            menu.Items.Add(new Separator());
            Entry("Lock / unlock",()=>lockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            Entry("Click-through",()=>{settings.ClickThrough=!settings.ClickThrough;ApplySettings();Controller.Save();});
            menu.Items.Add(new Separator());Entry("Help",OpenHelp);
            OverlayMenuDensity.Attach(menu,Controller.Profile); OverlayMenus.AutoDismiss(menu, true); HeadingTitle.ContextMenu = menu; HeaderActions.Children.Insert(0, OverlayMenus.Button(menu, "Window options", timedDismissal: true));
        }
        overview.Visibility = dps ? Visibility.Visible : Visibility.Collapsed;
        paired.Visibility = dps ? Visibility.Visible : Visibility.Collapsed;
        var bottom = UI.Stack(paired, overview, alert, footer); Grid.SetRow(bottom, 2); root.Children.Add(bottom); Body.Children.Add(root);
        SourceInitialized += (_, _) => Native.SetOverlay(this, settings.ClickThrough);
        LocationChanged += (_, _) => SaveBounds(); SizeChanged += (_, _) => { UpdateDensity(); SaveBounds(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) Native.SetOverlay(this, settings.ClickThrough); };
        groupRows.SizeChanged+=(_,_)=>{groupRows.Columns[1].Visibility=groupRows.ActualWidth<260?Visibility.Collapsed:Visibility.Visible;groupRows.Columns[3].Visibility=groupRows.ActualWidth<330?Visibility.Collapsed:Visibility.Visible;};
        ApplySettings(); Refresh();
    }
    internal void OpenFilters()
    {
        if(!IsDps)return;
        if(filterWindow==null) { filterWindow=new(Controller,this);filterWindow.Closed+=(_,_)=>filterWindow=null; }
        filterWindow.Show();filterWindow.Activate();
    }
    internal void OpenHelp()
    {var window=GetHelpWindow();window.Show();window.Activate();}
    private ThemedUtilityWindow GetHelpWindow()
    {
        if(helpWindow==null) {
            helpWindow=new("Objectives help",this,320,190);
            helpWindow.Body.Children.Add(UI.Text("Goals for your current zone, using your tracked quests and inventory evidence.",12,"SecondaryText"));
            helpWindow.Body.Children.Add(UI.Text("Fresh exports confirm inventory and bank items. When no local goals remain, the panel follows your configured queue or suggestions.",12,"SecondaryText"));
            helpWindow.Actions.Children.Add(UI.Button("Close",()=>helpWindow?.Close()));helpWindow.Closed+=(_,_)=>helpWindow=null;
        }
        return helpWindow;
    }
    private static Button HeaderButton(string text, string tooltip, Action action)
    { var b = UI.Button(text, action); b.ToolTip = tooltip; b.Width = b.Height = 24; b.Padding = new(0); b.Margin = new(0, 0, 2, 0); return b; }
    private static Button Choice(string text, Action action)
    { var b = UI.Button(text, action); b.FontSize = 12; b.Padding = new(6, 2, 6, 2); return b; }
    private static void Mark(Dictionary<string, Button> buttons, string selected)
    { foreach (var (key, button) in buttons) { button.SetResourceReference(Control.ForegroundProperty, key == selected ? "Mint" : "Muted"); button.SetResourceReference(Control.BorderBrushProperty, key == selected ? "Accent" : "Edge"); } }
    private void Changed() { Controller.Save(); Refresh(); }
    private void SelectDisplay(string value,bool secondary=false)
    { if(secondary)Controller.Profile.OverlayFooterDisplay!.Select(value);else { var p=Controller.Profile.OverlayMeter!;p.DisplayMode=value=="Group"?OverlayCombatMode.Group:OverlayCombatMode.Personal;if(value!="Group")p.Actor=value; }Changed(); }
    private string SelectedDisplay(bool secondary) => secondary ? Controller.Profile.OverlayFooterDisplay!.Value : Controller.Profile.OverlayMeter!.DisplayMode==OverlayCombatMode.Group?"Group":Controller.Profile.OverlayMeter.Actor;
    private void FillDisplayChoices(ItemsControl parent,bool secondary=false)
    {
        foreach(string value in new[]{"Pet","Player","Combined","Group"})
        {
            var glyph=new ActorBadge{Width=16,Height=16};
            var item=OverlayMenus.Choice(parent,value=="Player"?"Self":value,()=>SelectedDisplay(secondary)==value,()=>SelectDisplay(value,secondary));item.Icon=glyph;
            void Update(){string help=value switch {"Pet"=>"DPS from timestamp-verified owned pets.","Player"=>"DPS from your character only.","Combined"=>"Total DPS from Self and the verified owned pet; group members are excluded.",_=>"DPS from known group/raid characters and verified pets. Missing log coverage remains unknown."};string element=DisplayPetElement();glyph.Set(value,value=="Pet"&&PetElementDetector.HasFamilyIcon(element),help,element);item.ToolTip=help+" Uses this tracker's independent scope and filters.";}
            Update();if(parent is ContextMenu menu)menu.Opened+=(_,_)=>Update();else if(parent is MenuItem group)group.SubmenuOpened+=(_,_)=>Update();
        }
    }
    private string DisplayPetElement()
    { var view=Controller.Published?.OverlayCombat??lastMeterView; return PetIdentityResolver.DisplayFamily(Controller.Profile,view?.Fights??[],Controller.Profile.OverlayMeter!.Scope=="History"); }
    private void FooterDisplay(string actor,string help)
    {
        string element=DisplayPetElement();displayBadge.Set(actor,actor=="Pet"&&PetElementDetector.HasFamilyIcon(element),"Display · "+(actor=="Player"?"Self":actor),element);
        paired.ToolTip=help+"\nSecondary readout · independent of the title selector; uses this panel's scope and filters.";secondaryDps.ToolTip=paired.ToolTip;
        if(displayBadge.Parent is Button button) { button.ToolTip="Secondary display: "+(actor=="Player"?"Self":actor)+". Choose Pet, Self, Combined or Group.\n"+paired.ToolTip;System.Windows.Automation.AutomationProperties.SetName(button,"Secondary DPS display: "+(actor=="Player"?"Self":actor)); }
    }
    private GroupProjection? lastGroup;
    private void RefreshGroup(DpsViewState view,DpsSelection selection)
    {
        var projection=Controller.Published?.OverlayGroup;
        if(projection==null&&Controller.Demo)projection=GroupProjection.Build(view,null,Controller.Profile,Controller.GroupPeriods,"demo-group");
        badge.Set("Group",false,"Known group/raid characters and verified pets");HeadingTitle.Text="Group / Raid damage";fullHeading=HeadingTitle.Text;HeadingTitle.ToolTip=projection?.Coverage??"Group snapshot updating";
        groupRows.Visibility=Visibility.Visible;damageRows.Visibility=Visibility.Collapsed;paired.Visibility=Visibility.Visible;overview.Visibility=Visibility.Collapsed;
        clearFilters.Content=selection.FilterCount>0?$"Filters… ({selection.FilterCount})":"Filters…";
        Subtitle.Text=$"{selection.Scope} · {view.Seconds:N0}s · {projection?.Members.Count??0} known members";Subtitle.ToolTip=projection?.Coverage;
        updatingChoices=true;miniActor.SelectedItem="Group";miniScope.SelectedItem=selection.Scope=="Session"?"Sess.":"Fight";updatingChoices=false;
        Mark(actors,"Group");Mark(scopes,selection.Scope);Mark(sorts,selection.Sort);
        bool due=dpsCadence.Due(DisplayCadence.Context(view,"group|"+MeterVisualContext.Key(selection,view,Controller.SessionStart)));
        if(projection!=null&&!ReferenceEquals(lastGroup,projection)&&(due||lastGroup==null||lastGroup.Members.Count!=projection.Members.Count))
        {
            string? id=(groupRows.SelectedItem as GroupMemberRow)?.Id;UI.SetRows(groupRows,projection.Members.ToList());if(id!=null)groupRows.SelectedItem=projection.Members.FirstOrDefault(m=>m.Id==id);
            miniDps.SetNumber(projection.Rate,false,true);if(!projection.Members.Any(m=>m.Total.HasValue))miniDps.Text="—";lastGroup=projection;
        }
        if(projection==null)miniDps.Text="—";
        miniDuration.Text=$"{view.Seconds:N0}s";split.Text=projection==null?"Updating group…":!projection.Members.Any(m=>m.Total.HasValue)?"Member totals unavailable":$"{projection.Total:N0} observed damage · {projection.Rate:N1} DPS";
        splitBar.Visibility=Visibility.Collapsed;legend.Text="Characters include verified owned pets";legend.ToolTip=projection?.Coverage;
        alert.Text=projection==null?"Updating group…":projection.Members.Any(m=>m.Total.HasValue)?"":"No qualifying member damage observed";
        footer.Text=projection?.Members.Any(m=>!m.Total.HasValue)==true?"Some member totals unavailable":"";footer.ToolTip=projection?.Coverage;footer.Visibility=footer.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        miniSummary.ToolTip=split.Text+"\n"+projection?.Coverage;lastMeterView=null;
    }
    private static string ActorLabel(string actor) => actor switch { "Player" => "Self", "Combined" => "All", _ => "Pet" };
    public void SetMeterSize(double width, double height)
    {
        if (!IsDps) return;
        settings.Locked = false; ApplySettings(); Width = Math.Max(MinWidth, width); Height = Math.Max(MinHeight, height); UpdateDensity(); Controller.Save();
    }
    private void UpdateDensity()
    {
        if (IsCollapsed) { SizeTitleDisplaySelector(true); Subtitle.Visibility = Visibility.Collapsed; return; }
        double width = ActualWidth > 0 ? ActualWidth : Width, height = ActualHeight > 0 ? ActualHeight : Height;
        bool dense = width < 360 || height < 240, tiny = height < 120;
        Frame.Padding = new(dense ? 3 : 8); Header.Margin = new(0, 0, 0, dense ? 1 : 4);
        HeadingTitle.FontSize = dense ? 11 : 14; HeadingTitle.Margin = new(0, 0, 0, dense ? 0 : 4);
        HeaderIcon.Width = HeaderIcon.Height = dense ? 12 : 20; HeaderIcon.Margin = new(0, 1, dense ? 3 : 6, 0);
        foreach (var button in HeaderActions.Children.OfType<Button>()) button.Width = button.Height = dense ? 17 : 24;
        expandButton.Visibility = lockButton.Visibility = width < 200 ? Visibility.Collapsed : Visibility.Visible;
        if (IsDps)
        {
            fullSummary.Visibility = dense ? Visibility.Collapsed : Visibility.Visible; miniSummary.Visibility = dense ? Visibility.Visible : Visibility.Collapsed;
            miniChoices.Visibility = tiny || width < 152 ? Visibility.Collapsed : Visibility.Visible; tinyScopes.Visibility = tiny && height >= 92 ? Visibility.Visible : Visibility.Collapsed;
            bool group=Controller.Profile.OverlayMeter!.DisplayMode==OverlayCombatMode.Group;groupRows.Visibility=group&&height>=180?Visibility.Visible:Visibility.Collapsed;paired.Visibility=Visibility.Visible;
            damageRows.Visibility = group||height < 180 ? Visibility.Collapsed : Visibility.Visible; damageRows.Compact = dense;
            Subtitle.Visibility = dense ? Visibility.Collapsed : Visibility.Visible;
            if (dense) HeadingTitle.Text = Controller.Profile.OverlayMeter!.DisplayMode==OverlayCombatMode.Group?"Group":Controller.Profile.OverlayMeter!.Actor switch { "Player" => "Self", "Combined" => "Total", _ => "Pet" };
            else if (fullHeading.Length > 0) HeadingTitle.Text = fullHeading;
            footer.Visibility = footer.Text.Length == 0 || dense && height < 200 ? Visibility.Collapsed : Visibility.Visible;
            overview.Visibility = !group&&height >= 128 ? Visibility.Visible : Visibility.Collapsed;
            alert.Visibility = dense ? Visibility.Collapsed : alert.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            HeaderIcon.Visibility = Visibility.Visible; SizeTitleDisplaySelector(dense);
            Frame.SetResourceReference(Border.BorderBrushProperty, Controller.Profile.MeterStyle == "Minimal" ? "Edge" : "Accent");
            glow.Color = UI.Brush("Accent").Color; Frame.Effect = Controller.Profile.MeterStyle == "Luminous" ? glow : null;
        }
        else { HeadingTitle.Text = ExpansionRules.ShortZone(Controller.Profile.Zone, width) + " Obj"; HeadingTitle.ToolTip = Controller.Profile.Zone + " objectives"; Subtitle.Visibility = height < 160 ? Visibility.Collapsed : Visibility.Visible; footer.Visibility = Visibility.Visible; footer.FontSize = width < 180 ? 9 : 11; alert.Visibility = height < 200 ? Visibility.Collapsed : alert.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }
    }
    private void SaveBounds()
    {
        if (!IsLoaded || Controller.Demo || WindowState != WindowState.Normal) return;
        settings.Left = Left; settings.Top = Top; if (!IsCollapsed) { settings.Width = ActualWidth; settings.Height = ActualHeight; }
    }
    private readonly DisplayCadence dpsCadence = new();
    private DpsViewState? lastMeterView;
    private string lastOverlayState = "";
    private double lastRolling;
    public void Refresh()
    {
        if (Monitor.IsEntered(Controller.Sync)) { Dispatcher.BeginInvoke(Refresh); return; }
        using (UpdateDiagnostics.Measure("OverlayWindow refresh"))
        {
            OverlayMenuDensity.Update(Controller.Profile);
            if (IsDps)
            {
                RefreshTitleDisplaySelector();
                var view = Controller.OverlayDpsView(); var p = Controller.Profile.OverlayMeter!;
                RefreshSecondaryDisplay(view);
                if (p.DisplayMode==OverlayCombatMode.Group) { RefreshGroup(view,p); return; }
                groupRows.Visibility=Visibility.Collapsed;damageRows.Visibility=Visibility.Visible;overview.Visibility=Visibility.Visible;paired.Visibility=Visibility.Visible;
                bool updateDps = dpsCadence.Due(DisplayCadence.Context(view, MeterVisualContext.Key(Controller.Profile.OverlayMeter!, view, Controller.SessionStart)));
                double rolling = Controller.Published?.OverlayRollingDps ?? Controller.Combat.RollingDps(view.Filter, DateTime.Now);
                string state = $"{p.DisplayMode}|{p.Actor}|{p.Scope}|{p.FilterCount}|{Controller.Profile.MeterStyle}|{Controller.Profile.SmoothMeterMotion}|{Controller.Profile.ShowLinkedHealing}|{ActualWidth}|{ActualHeight}|{Controller.QuestRevision}|{Controller.Profile.EffectivePetElement}|{Controller.PetResearchRevision}";
                if ((!Controller.Demo || Controller.Published!=null) && ReferenceEquals(lastMeterView, view) && lastOverlayState == state)
                {
                    if (lastRolling != rolling) overview.Set(view.Damage, view.MaxHit, rolling, view.DetailComplete, Controller.Profile.SmoothMeterMotion, false, ActualWidth < 360 || ActualHeight < 240);
                    lastRolling = rolling; return;
                }
                lastMeterView = view; lastRolling = rolling; lastOverlayState = state;
                string nextContext = MeterVisualContext.Key(Controller.Profile.OverlayMeter!, view, Controller.SessionStart); bool snap = nextContext != visualContext; visualContext = nextContext;
                bool smooth = Controller.Profile.SmoothMeterMotion && !Controller.Demo;
                Mark(actors, p.Actor); Mark(scopes, p.Scope); Mark(sorts, p.Sort);
                updatingChoices = true; miniActor.SelectedItem = ActorLabel(p.Actor); miniScope.SelectedItem = p.Scope == "Session" ? "Sess." : "Fight"; updatingChoices = false;
                var historicalFight = p.Scope == "History" ? view.Fights.FirstOrDefault() : null;
                var identity = historicalFight != null ? PetIdentityResolver.At(Controller.Profile,historicalFight.Start,historicalFight.PetName) : Controller.Profile.PetIdentities.OrderByDescending(x => x.From).FirstOrDefault();
                string petName = historicalFight?.PetName ?? identity?.Name ?? Controller.Profile.ActiveLoadout.PetName;
                string petEvidence = identity != null ? identity.Confirmed ? "Identity manually confirmed" : "Name detected from pet messages" : petName.Length > 0 ? "Name from loadout tag; ownership not currently observed" : "Waiting for a pet ownership message";
                if(p.Scope=="Session") petEvidence+="\n"+PetIdentityResolver.SessionEvidence(Controller.Profile,view.Fights);
                string element = DisplayPetElement();
                badge.Set(p.Actor, identity != null, p.Actor == "Combined" ? ActorBadge.TotalTooltip : $"{(petName.Length > 0 ? petName : "Unidentified pet")}\n{petEvidence}\n{element} · {(Controller.Profile.PetElementOverride.Length > 0 ? "Manual override" : identity?.ElementConfidence ?? "Unknown")}\n{identity?.ElementEvidence}", element);
                HeadingTitle.Text = p.Actor switch { "Pet" => "Pet damage — " + (petName.Length > 0 ? petName : "not identified"), "Player" => "Self damage — " + Controller.Profile.Character, "Unknown" => "Unattributed damage", _ => "Combined damage" };
                fullHeading = HeadingTitle.Text;
                HeadingTitle.ToolTip = HeadingTitle.Text;
                string zone = p.Scope == "Session" ? Controller.Profile.Zone : view.Fights.FirstOrDefault()?.Zone ?? Controller.Profile.Zone;
                Subtitle.Visibility = Visibility.Visible; Subtitle.Text = $"{zone} · {view.Seconds:N0}s · {(p.Scope == "Session" ? "Session" : p.Scope == "History" ? "Saved fight" : view.InCombat ? "Fighting" : "Last fight")} · {view.Dps:N1} dps"; Subtitle.ToolTip = Subtitle.Text; if(p.FilterCount>0){Subtitle.Text+=$" · Filtered ({p.FilterCount})";}
                if (updateDps) miniDps.SetNumber(view.Dps, false, true); miniDps.ToolTip = DisplayCadence.DpsHelp; miniDuration.Text = $"{(view.InCombat ? "● " : "")}{view.Seconds:N0}s";
                miniDuration.SetResourceReference(TextBlock.ForegroundProperty, view.InCombat ? "Mint" : "Muted");
                if (updateDps) split.Text = $"Pet {view.PetDps:N1} · Self {view.PlayerDps:N1} · Combined {view.CombinedDps:N1} dps";
                long detailed = view.MeleeDamage + view.SpellDamage;
                splitBar.ColumnDefinitions[0].Width = new(detailed > 0 ? view.MeleeDamage : 1, GridUnitType.Star); splitBar.ColumnDefinitions[1].Width = new(detailed > 0 ? view.SpellDamage : 0, GridUnitType.Star);
                splitBar.Visibility = detailed > 0 ? Visibility.Visible : Visibility.Collapsed;
                legend.Text = detailed > 0 ? $"Melee {view.MeleeDamage * 100d / detailed:N0}% · Spells {view.SpellDamage * 100d / detailed:N0}% · {view.Damage:N0} damage" : $"{view.Damage:N0} damage · max {(view.DetailComplete ? view.MaxHit.ToString("N0") : "unknown")}";
                clearFilters.Visibility = Visibility.Visible; clearFilters.Content=p.FilterCount>0?$"Filters… ({p.FilterCount})":"Filters…";
                damageRows.SetRows(Controller.Profile.ShowLinkedHealing ? view.Rows : view.Rows.Select(r => r with { LinkedHealing = null }).ToList(), nextContext, smooth);
                double live = rolling;
                overview.Set(view.Damage, view.MaxHit, live, view.DetailComplete, smooth, snap, ActualWidth < 360 || ActualHeight < 240);
                footer.Text = (p.Scope == "Session" ? $"{view.Fights.Count} encounters" : "") + (view.UnknownDamage > 0 ? $" · unattributed {view.UnknownDamage:N0}" : "");
                footer.Visibility = footer.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var (style, item) in styleChoices) item.IsChecked = Controller.Profile.MeterStyle == style;
                if (motionChoice != null) motionChoice.IsChecked = Controller.Profile.SmoothMeterMotion;
                miniSummary.ToolTip = $"{HeadingTitle.ToolTip}\n{Subtitle.Text}\n{split.Text}\n{legend.Text}\n{footer.Text}";
                foreach (var button in tinyScopes.Children.OfType<Button>()) { bool selected = button.Content.ToString() == p.Scope.ToUpperInvariant(); button.SetResourceReference(Control.ForegroundProperty, selected ? "Mint" : "Muted"); button.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "Edge"); }
                alert.Text = view.Fights.Count == 0 ? "Waiting for combat in this scope." : !view.DetailComplete ? "Summary records included · breakdown incomplete." : view.Rows.Count == 0 ? "No damage matches these filters." : "";
            }
            else
            {
                string state = $"{Controller.QuestRevision}|{Controller.Profile.ObjectiveMode}|{Controller.Profile.Zone}|{Controller.Inventory.UpdatedAt.Ticks}|{ActualWidth}|{ActualHeight}|{Controller.Alert}";
                if (!Controller.Demo && lastOverlayState == state) return; lastOverlayState = state;
                HeadingTitle.Text = Controller.Profile.Zone + " objectives"; Subtitle.Text = string.Join(" / ", Controller.Profile.EnabledClasses); Subtitle.Visibility = Visibility.Visible;
                var local = Controller.Objectives.Where(x => x.Remaining > 0 && QuestPlanner.InZone(x, Controller.Profile.Zone, ExpansionRules.Current(Controller.Profile))).ToList();
                var shown = local.Count > 0 ? local : ObjectiveQueue.Fallback(Controller.Profile, Controller.Catalog, Controller.Objectives, Controller.Inventory);
                bool fallback = local.Count == 0 && shown.Count > 0;
                var signature = Controller.Profile.ObjectiveMode + fallback + string.Join('|', shown.Select(x => $"{x.Class}:{x.Item}:{x.Mob}:{x.Remaining}:{x.Zone}"));
                if (signature != objectiveSignature || objectives.Children.Count == 0)
                {
                    objectiveSignature = signature; objectives.Children.Clear();
                    if (fallback) objectives.Children.Add(UI.Text(Controller.Profile.ObjectiveMode == "My queue" ? "Next goals · My queue" : "Next goals · Suggestions", 10, "Mint"));
                    foreach (var item in shown)
                    { objectives.Children.Add(CompactObjectiveCard(item,fallback)); }
                    if (shown.Count == 0) objectives.Children.Add(UI.Text(Controller.Profile.ObjectiveMode == "My queue" ? "No goals in Zone or Queue" : "No goals in zone", 12, "Muted"));
                }
                footer.Text = $"{local.Count} Obj" + ((ActualWidth > 0 ? ActualWidth : Width) < 180 ? "" : Controller.Inventory.UpdatedAt == default ? " · Not updated" : " · Updated " + Controller.Inventory.UpdatedAt.ToString("MMM/dd", System.Globalization.CultureInfo.InvariantCulture));
                footer.ToolTip = $"Last confirmed export: {Controller.Inventory.CapturedAt:MMM dd HH:mm:ss}\nLatest log estimate: {Controller.Inventory.LastObservedChangeAt:MMM dd HH:mm:ss}\nLogs estimate changes; exports confirm bank, trades and equipment.";
                alert.Text = Controller.Alert;
            }
            alert.Visibility = alert.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateDensity();
        }
    }
    public void ApplySettings()
    {
        Opacity = Math.Clamp(settings.Opacity, .3, 1); IsLocked = settings.Locked; ResizeMode = IsLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        lockButton.Content = IsLocked ? "◆" : "◇"; lockButton.ToolTip = IsLocked ? "Unlock position and size" : "Lock position and size";
        ShowInteractionState(settings.ClickThrough);
        Native.SetOverlay(this, settings.ClickThrough);
    }
}
