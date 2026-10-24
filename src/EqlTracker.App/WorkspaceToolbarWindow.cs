using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class WorkspaceToolbarWindow : FrostWindow
{
    internal WorkspaceToolbarState State {get;}
    internal readonly StackPanel WorkspaceButtons=new(){Orientation=Orientation.Horizontal};
    internal readonly Dictionary<string,ToggleButton> Buttons=[];
    internal readonly Button SettingsButton;
    private readonly Func<string,FrostWindow> getPanel;
    private readonly Dictionary<string,FrostWindow> panels=[];
    private readonly DispatcherTimer settle=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private bool placing,ready,restoringVisibility,exitSaved;
    private FrostWindow? presented;
    private string presentedId="";
    private string activateWhenShown="";
    internal bool MotionEnabled=>Controller.Profile.SmoothMeterMotion&&SystemParameters.ClientAreaAnimation;
    internal bool IsTransitioning=>presented?.WorkspaceMotion?.Running==true;
    private string reorder="";private Point reorderStart;private bool reordered;private string[]? reorderOrder;
    private Vector? attachedDragOffset;
    internal FrostWindow? ActivePanel=>panels.GetValueOrDefault(State.Active);
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint handle,out NativeRect rect);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(nint handle,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",SetLastError=true)]private static extern nint BeginDeferWindowPos(int count);
    [DllImport("user32.dll",SetLastError=true)]private static extern nint DeferWindowPos(nint batch,nint handle,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool EndDeferWindowPos(nint batch);

    internal WorkspaceToolbarWindow(TrackerController controller,Func<string,FrostWindow> getPanel,Action settings,WorkspaceToolbarState? initial=null):base(controller,"Workspace toolbar","",370,28,surface:WindowSurface.Theme)
    {
        this.getPanel=getPanel;State=initial??controller.ReadWorkspaceToolbar();State.Normalize();
        MinWidth=220;MinHeight=MaxHeight=Height=28;Width=double.IsFinite(State.Placement.Width)?Math.Max(220,State.Placement.Width):370;
        Frame.Padding=new(2,0,2,0);Header.Margin=new(0);Heading.Visibility=Visibility.Collapsed;Body.Visibility=Visibility.Collapsed;HeaderActions.Children.Clear();Header.Children.Clear();
        var logo=new Image{Source=CatalystBranding.Image("gold-elements"),Width=22,Height=22,Margin=new(0,0,5,0),ToolTip="EQL Catalyst · drag the bar; double-click empty space to toggle the last workspace"};
        AutomationProperties.SetName(logo,"EQL Catalyst");var content=new DockPanel{LastChildFill=true};
        var fixedActions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(fixedActions,Dock.Right);content.Children.Add(fixedActions);
        SettingsButton=UI.Button("",settings);SettingsButton.Content=new ToolbarActionArtwork(false){Width=18,Height=18};SettingsButton.Width=SettingsButton.Height=24;SettingsButton.Padding=new(0);SettingsButton.Margin=new(4,0,0,0);SettingsButton.ToolTip="Settings";AutomationProperties.SetName(SettingsButton,"Settings");fixedActions.Children.Add(SettingsButton);
        var update=QuestWindow.UpdateReadyButton();update.Width=update.Height=24;update.Margin=new(2,0,0,0);fixedActions.Children.Add(update);update.IsVisibleChanged+=(_,_)=>{if(ready){MeasureMinimum();ArrangePanel();QueueSave();}};
        var grip=OuterResize!.Grip;if(grip.Parent is Panel resizeHost)resizeHost.Children.Remove(grip);grip.Width=14;grip.Height=24;grip.Margin=new(2,0,4,0);grip.VerticalAlignment=VerticalAlignment.Center;grip.ToolTip="Resize toolbar width";fixedActions.Children.Add(grip);
        var hide=UI.Button("",()=>TryHide());hide.Content=new ToolbarActionArtwork(true){Width=16,Height=16};hide.Tag="toolbar-close";AutomationProperties.SetName(hide,"Hide toolbar");hide.Width=hide.Height=24;hide.Padding=new(0);hide.Margin=new(0);hide.ToolTip="Hide toolbar and workspace · reopen from the tray";fixedActions.Children.Add(hide);
        foreach(var action in new[]{SettingsButton,update,hide})action.Cursor=Cursors.Hand;
        SettingsButton.Template=FlatActionTemplate();hide.Template=FlatActionTemplate();update.Template=FlatActionTemplate();
        DockPanel.SetDock(logo,Dock.Left);content.Children.Add(logo);DockPanel.SetDock(WorkspaceButtons,Dock.Left);content.Children.Add(WorkspaceButtons);content.Children.Add(new Border{Background=Brushes.Transparent,MinWidth=12});
        Header.Children.Add(content);Header.Cursor=Cursors.SizeAll;
        foreach(string id in new[]{"Tracker","CombatLab"})
        {
            var label=UI.Text(id=="Tracker"?"Tracker":"Combat Lab",11,"Foreground",true);var glyph=new ToolbarGlyph(id){Width=16,Height=16,Margin=new(0,0,4,0)};
            label.Margin=new(0);
            var button=new ToggleButton{Content=UI.Row(glyph,label),Cursor=Cursors.Hand,Height=24,Padding=new(5,0,5,0),Margin=new(0,0,3,0),ToolTip="Open / close "+label.Text,Tag=id,VerticalContentAlignment=VerticalAlignment.Center};
            button.Style=ToolbarButtonStyle();AutomationProperties.SetName(button,label.Text);button.Click+=(_,_)=>{if(reordered){reordered=false;UpdateButtons();return;}Open(id,true);};
            var menu=new ContextMenu();foreach(var (text,direction) in new[]{("Move left",-1),("Move right",1)}){var item=new MenuItem{Header=text};item.Click+=(_,_)=>Move(id,direction);menu.Items.Add(item);}button.ContextMenu=menu;
            button.PreviewKeyDown+=(_,e)=>{if(Keyboard.Modifiers==ModifierKeys.Alt&&e.Key is Key.Left or Key.Right){Move(id,e.Key==Key.Left?-1:1);e.Handled=true;}};
            button.PreviewMouseLeftButtonDown+=(_,e)=>BeginReorder(id,e.GetPosition(this));
            button.PreviewMouseMove+=(_,e)=>
            {
                if(reorder!=id||e.LeftButton!=MouseButtonState.Pressed)return;e.Handled=ContinueReorder(e.GetPosition(this));
            };
            button.PreviewMouseLeftButtonUp+=(_,e)=>{reorder="";reorderOrder=null;if(reordered){button.ReleaseMouseCapture();e.Handled=true;UpdateButtons();Dispatcher.BeginInvoke(()=>reordered=false,DispatcherPriority.Input);}};
            button.LostMouseCapture+=(_,_)=>Dispatcher.BeginInvoke(()=>{if(reorder==id&&!button.IsMouseCaptured)CancelReorder();},DispatcherPriority.Input);
            Buttons[id]=button;
        }
        OrderButtons();OuterResize!.HorizontalOnly=true;OuterResize.Grip.Cursor=Cursors.SizeWE;ResizeCommitted=MoveCommitted=()=>{attachedDragOffset=null;RecoverBar();SaveState();};
        IsLocked=State.Locked;CanHide=()=>CanLeaveActive();HideRequested=()=>HideAll(true);
        var barMenu=new ContextMenu();var locked=new MenuItem{Header="Lock toolbar",IsCheckable=true,IsChecked=IsLocked};locked.Click+=(_,_)=>{IsLocked=locked.IsChecked;SaveState();};barMenu.Items.Add(locked);Header.ContextMenu=barMenu;
        Header.AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler((_,e)=>
        {
            if(e.ChangedButton!=MouseButton.Left||e.ClickCount!=2||Interactive(e.OriginalSource as DependencyObject))return;
            CancelInteraction();Open(State.Last,true);e.Handled=true;
        }),true);
        PreviewKeyDown+=(_,e)=>{if(!e.Handled&&e.Key==Key.Escape&&reorder.Length>0){CancelReorder();e.Handled=true;}};
        InteractionStarting=()=>{SnapTransitions(true);attachedDragOffset=presented?.IsVisible==true?Native.WindowTopLeft(presented)-Native.WindowTopLeft(this):null;};
        PhysicalMoveRequested=MoveAttached;
        LocationChanged+=(_,_)=>{if(!placing&&ready){SnapTransitions(true);ArrangePanel();QueueSave();}};
        SizeChanged+=(_,_)=>{if(!placing&&ready){SnapTransitions(true);ArrangePanel();QueueSave();}};
        DpiChanged+=(_,_)=>Dispatcher.BeginInvoke(()=>{if(Header.IsMouseCaptured)return;SnapTransitions(true);ArrangePanel();SaveState();},DispatcherPriority.Loaded);
        StateChanged+=(_,_)=>
        {
            if(WindowState==WindowState.Maximized){WindowState=WindowState.Normal;return;}
            if(WindowState==WindowState.Minimized)SnapTransitions(false);else if(IsVisible&&State.Active.Length>0)ShowActive(false);
        };
        IsVisibleChanged+=(_,_)=>
        {
            if(!ready||restoringVisibility)return;
            if(!IsVisible)SnapTransitions(false);else if(WindowState==WindowState.Normal)ShowActive(false);
        };
        Loaded+=(_,_)=>{if(ready)return;MeasureMinimum();RestorePosition();ready=true;RequestedVisible=true;UpdateButtons();if(State.Active.Length>0)ShowActive();SaveState();};
        System.ComponentModel.PropertyChangedEventHandler preferences=(_,e)=>{if(e.PropertyName==nameof(SystemParameters.ClientAreaAnimation)&&!MotionEnabled)SnapTransitions(true);};
        SystemParameters.StaticPropertyChanged+=preferences;
        Closed+=(_,_)=>{settle.Stop();SnapTransitions(false);SystemParameters.StaticPropertyChanged-=preferences;};settle.Tick+=(_,_)=>{settle.Stop();SaveState();};
    }
    private bool Interactive(DependencyObject? node)
    {while(node!=null&&node!=Header){if(node is ButtonBase or Thumb or ComboBox or System.Windows.Controls.Primitives.TextBoxBase or MenuItem)return true;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);}return false;}
    private void MeasureMinimum()
    {
        Header.Measure(new Size(double.PositiveInfinity,28));MinWidth=Math.Max(220,Header.DesiredSize.Width+Frame.Padding.Left+Frame.Padding.Right+2);if(Width<MinWidth)Width=MinWidth;
    }
    private void QueueSave(){settle.Stop();settle.Start();}
    internal void BeginReorder(string id,Point point){reorder=id;reorderStart=point;reordered=false;reorderOrder=State.Order.ToArray();}
    private void CancelReorder()
    {
        if(reorder.Length==0)return;var button=Buttons[reorder];reorder="";
        if(reorderOrder!=null){State.Order=reorderOrder.ToList();OrderButtons();SaveState();}reorderOrder=null;
        button.ReleaseMouseCapture();reordered=true;UpdateButtons();Dispatcher.BeginInvoke(()=>reordered=false,DispatcherPriority.Input);
    }
    internal bool ContinueReorder(Point point)
    {
        if(reorder.Length==0)return false;
        if(!reordered&&Math.Abs(point.X-reorderStart.X)<SystemParameters.MinimumHorizontalDragDistance)return false;
        reordered=true;var other=Buttons.First(p=>p.Key!=reorder).Value;var bounds=other.TransformToAncestor(this).TransformBounds(new Rect(other.RenderSize));
        int direction=point.X<bounds.Left+bounds.Width/2?-1:1,target=State.Order.IndexOf(reorder)+direction;
        if(target>=0&&target<State.Order.Count&&bounds.Contains(point))Move(reorder,direction);return true;
    }
    private void RestorePosition()
    {
        var saved=State.Placement;var displays=PlanningWindowPlacement.AvailableDisplays(this);
        if(saved.ScreenLeft==null&&Controller.Profile.Panels.TryGetValue("Quest & Gear Tracker",out var legacy)){saved=EquipmentPositions.Copy(legacy);saved.Width=Width;saved.Height=28;}
        var bounds=PlanningWindowPlacement.Resolve(saved,Width,28,MinWidth,28,displays);MoveNative(this,bounds.Left,bounds.Top);Width=Math.Max(MinWidth,bounds.Width);RecoverBar();
    }
    private void RecoverBar()
    {
        if(!GetWindowRect(new WindowInteropHelper(this).Handle,out var bar))return;
        var display=PlanningWindowPlacement.AvailableDisplays(this).OrderBy(d=>Math.Pow(Math.Max(Math.Max(d.Work.Left-bar.Left,0),bar.Left-d.Work.Right),2)+Math.Pow(Math.Max(Math.Max(d.Work.Top-bar.Top,0),bar.Top-d.Work.Bottom),2)).First();
        double width=Math.Max(MinWidth,Math.Min(Width,display.Work.Width/display.DpiX));
        double x=Math.Clamp((double)bar.Left,display.Work.Left,Math.Max(display.Work.Left,display.Work.Right-width*display.DpiX));double y=Math.Clamp((double)bar.Top,display.Work.Top,Math.Max(display.Work.Top,display.Work.Bottom-28*display.DpiY));
        placing=true;try{Width=width;MoveNative(this,x,y);}finally{placing=false;}if(ready)ArrangePanel();
    }
    private static void MoveNative(Window window,double x,double y)=>SetWindowPos(new WindowInteropHelper(window).EnsureHandle(),0,(int)Math.Round(x),(int)Math.Round(y),0,0,0x0001|0x0004|0x0010);
    internal void MoveAttached(Point proposed)
    {
        var handle=new WindowInteropHelper(this).Handle;if(handle==0||!GetWindowRect(handle,out var bar))return;
        var panel=presented?.IsVisible==true?presented:null;
        if(panel==null){placing=true;try{Native.MovePhysical(this,proposed);}finally{placing=false;}QueueSave();return;}
        var panelHandle=new WindowInteropHelper(panel).Handle;if(!GetWindowRect(panelHandle,out var pr))return;
        var offset=attachedDragOffset??(Native.WindowTopLeft(panel)-Native.WindowTopLeft(this));
        // Keep a common physical edge, including while WPF changes DPI. No WPF
        // Left/Top writes or follower callbacks can expose an intermediate gap.
        double dy=offset.Y<0?-(pr.Bottom-pr.Top):bar.Bottom-bar.Top;
        double minX=Math.Min(0,offset.X),maxX=Math.Max(bar.Right-bar.Left,offset.X+pr.Right-pr.Left);
        double minY=Math.Min(0,dy),maxY=Math.Max(bar.Bottom-bar.Top,dy+pr.Bottom-pr.Top);
        var displays=PlanningWindowPlacement.AvailableDisplays(this);
        var d=displays.OrderBy(d=>Math.Pow(Math.Max(Math.Max(d.Work.Left-proposed.X,0),proposed.X-d.Work.Right),2)+Math.Pow(Math.Max(Math.Max(d.Work.Top-proposed.Y,0),proposed.Y-d.Work.Bottom),2)).First();
        double x=Math.Clamp(proposed.X,d.Work.Left-minX,Math.Max(d.Work.Left-minX,d.Work.Right-maxX));
        double y=Math.Clamp(proposed.Y,d.Work.Top-minY,Math.Max(d.Work.Top-minY,d.Work.Bottom-maxY));
        placing=true;
        try
        {
            var batch=BeginDeferWindowPos(2);if(batch==0)return;
            batch=DeferWindowPos(batch,handle,0,(int)Math.Round(x),(int)Math.Round(y),0,0,0x0015);if(batch==0)return;
            batch=DeferWindowPos(batch,panelHandle,0,(int)Math.Round(x+offset.X),(int)Math.Round(y+dy),0,0,0x0015);if(batch==0)return;
            if(!EndDeferWindowPos(batch))return;
        }
        finally{placing=false;}
        QueueSave();
    }
    internal bool Open(string id,bool toggle=false)
    {
        if(!WorkspaceToolbarLayout.Known(id))return false;
        if(State.Active==id&&toggle)return ClosePanel();
        if(State.Active!=id&&!CanLeaveActive()){UpdateButtons();return false;}
        _=Panel(id); // Explicit open callers may populate/focus the persistent window immediately.
        State.Active=State.Last=id;State.Visible=true;RequestedVisible=true;WindowState=WindowState.Normal;
        if(!IsVisible)Show();ShowActive();UpdateButtons();SaveState();return true;
    }
    private FrostWindow Panel(string id)
    {
        if(panels.TryGetValue(id,out var existing))return existing;
        new WindowInteropHelper(this).EnsureHandle();
        var panel=getPanel(id);panels[id]=panel;panel.AttachPresentation(id=="Tracker"?"Tracker":"Combat Lab");panel.Owner=this;panel.WindowStartupLocation=WindowStartupLocation.Manual;
        if(panel is QuestWindow tracker)tracker.AttachToToolbar();
        panel.CollapseRequested=()=>{ClosePanel();};panel.HideRequested=()=>{if(State.Active==id){State.Active="";SnapTransitions(false);UpdateButtons();SaveState();}};
        panel.InteractionStarting=()=>SnapTransitions(true);
        panel.ResizeCommitted=()=>{ArrangePanel();SaveState();};
        panel.SizeChanged+=(_,_)=>{if(!placing&&panel.IsVisible&&presented==panel){SnapTransitions(true);ArrangePanel(true);}};
        // Windows may restore every previously visible owned HWND after its owner.
        // Such restoration is presentation recovery, not a new workspace request.
        panel.IsVisibleChanged+=(_,_)=>
        {
            if(!panel.IsVisible)return;
            Dispatcher.BeginInvoke(()=>
            {
                if(panel.IsVisible&&(presented!=panel||!IsVisible||WindowState!=WindowState.Normal))
                {panel.WorkspaceMotion!.Snap(false);panel.RequestedVisible=false;panel.Hide();}
            },DispatcherPriority.Loaded);
        };
        new WindowInteropHelper(panel).EnsureHandle();return panel;
    }
    private bool CanLeaveActive()=>(presented??ActivePanel)?.CanHide?.Invoke()!=false;
    internal bool ClosePanel()
    {
        if(!CanLeaveActive()){UpdateButtons();return false;}
        State.Active="";activateWhenShown="";ShowActive();UpdateButtons();SaveState();return true;
    }
    internal bool TryHide(){if(!CanLeaveActive())return false;HideAll(true);return true;}
    private void HideAll(bool save)
    {
        CancelReorder();SnapTransitions(false);RequestedVisible=false;State.Visible=false;restoringVisibility=true;Hide();restoringVisibility=false;if(save)SaveState();
    }
    private void ShowActive(bool animate=true)
    {
        if(!ready||!IsVisible||WindowState!=WindowState.Normal)return;
        if(presented!=null&&presentedId!=State.Active)
        {
            var outgoing=presented;outgoing.CancelInteraction();
            outgoing.WorkspaceMotion!.To(false,animate&&MotionEnabled,()=>
            {
                if(presented!=outgoing||presentedId==State.Active)return;
                outgoing.RequestedVisible=false;outgoing.Hide();outgoing.WorkspaceSurface.Detach();presented=null;presentedId="";WorkspaceSurface.Detach();ShowActive(animate);
            });return;
        }
        if(State.Active.Length==0){WorkspaceSurface.Detach();return;}
        var panel=Panel(State.Active);bool fresh=presented!=panel;
        presented=panel;presentedId=State.Active;
        if(fresh)panel.WorkspaceMotion!.Snap(false);
        ArrangePanel();panel.RequestedVisible=true;
        if(fresh){if(panel is QuestWindow tracker)tracker.Refresh();else if(panel is DpsWindow combat)combat.Refresh();}
        if(!panel.IsVisible)panel.Show();ArrangePanel();
        panel.WorkspaceMotion!.To(true,animate&&MotionEnabled,()=>
        {if(activateWhenShown==presentedId&&State.Active==presentedId&&panel.IsVisible){activateWhenShown="";panel.Activate();}});
    }
    internal void FocusWhenShown(string id)
    {if(presentedId==id&&presented?.IsVisible==true&&!IsTransitioning)presented.Activate();else activateWhenShown=id;}
    internal void ApplyMotionPreference(){if(!MotionEnabled&&IsTransitioning)SnapTransitions(true);}
    internal void SnapTransitions(bool show)
    {
        if(show&&!IsTransitioning&&presentedId==State.Active&&(presented==null||presented.IsVisible))return;
        bool canShow=show&&ready&&IsVisible&&WindowState==WindowState.Normal;
        foreach(var (id,panel) in panels)
        {
            bool keep=canShow&&id==State.Active;panel.WorkspaceMotion?.Snap(keep);
            if(!keep){panel.WorkspaceSurface.Detach();panel.CancelInteraction();panel.RequestedVisible=false;panel.Hide();}
        }
        if(!canShow||presentedId!=State.Active){presented=null;presentedId="";WorkspaceSurface.Detach();}
        if(show)ShowActive(false);
    }
    internal void ArrangePanel(bool currentSize=false)
    {
        if(placing||!ready||presented is not {} panel||WindowState!=WindowState.Normal)return;
        var handle=new WindowInteropHelper(this).Handle;if(handle==0||!GetWindowRect(handle,out var bar))return;
        var displays=PlanningWindowPlacement.AvailableDisplays(this);var display=displays.OrderBy(d=>Math.Pow(Math.Max(Math.Max(d.Work.Left-bar.Left,0),bar.Left-d.Work.Right),2)+Math.Pow(Math.Max(Math.Max(d.Work.Top-bar.Top,0),bar.Top-d.Work.Bottom),2)).First();
        var size=currentSize||panel.OuterResize?.IsDragging==true?new Size(panel.Width,panel.Height):panel.Placement?.ExpandedSize??new Size(panel.Width,panel.Height);
        var bounds=WorkspaceToolbarLayout.Attach(bar.Left,bar.Top,bar.Bottom-bar.Top,size.Width,size.Height,panel.MinWidth,panel.MinHeight,new(display.Work.Left,display.Work.Top,display.Work.Width,display.Work.Height,display.DpiX,display.DpiY));
        placing=true;
        try
        {
            if(Math.Abs(bounds.BarTop-bar.Top)>.5)MoveNative(this,bar.Left,bounds.BarTop);
            panel.MinWidth=Math.Min(640,display.Work.Width/display.DpiX);panel.MinHeight=Math.Min(420,Math.Max(1,(display.Work.Height-(bar.Bottom-bar.Top))/display.DpiY));
            panel.Width=bounds.Width;panel.Height=bounds.Height;MoveNative(panel,bounds.Left,bounds.Top);
            double barY=bounds.BarTop;bool above=bounds.Top<barY;
            var dpi=VisualTreeHelper.GetDpi(panel);double left=Math.Max(bar.Left,bounds.Left),right=Math.Min(bar.Right,bounds.Left+bounds.Width*display.DpiX);
            WorkspaceSurface.Join(above,(left-bar.Left)/display.DpiX,(right-bar.Left)/display.DpiX);
            panel.WorkspaceSurface.Join(!above,(left-bounds.Left)/dpi.DpiScaleX,(right-bounds.Left)/dpi.DpiScaleX);
            panel.WorkspaceMotion!.Above=above;
        }
        finally{placing=false;}
    }
    internal void Move(string id,int direction){if(WorkspaceToolbarLayout.Move(State,id,direction)){OrderButtons();UpdateButtons();SaveState();}}
    private void OrderButtons()
    {
        foreach(string id in State.Order)if(!WorkspaceButtons.Children.Contains(Buttons[id]))WorkspaceButtons.Children.Add(Buttons[id]);
        if(!Equals(WorkspaceButtons.Children[0],Buttons[State.Order[0]]))
        {var move=Buttons[State.Order[0]];if(move.IsMouseCaptured){var other=Buttons[State.Order[1]];WorkspaceButtons.Children.Remove(other);WorkspaceButtons.Children.Add(other);}else{WorkspaceButtons.Children.Remove(move);WorkspaceButtons.Children.Insert(0,move);}}
    }
    private static Style ToolbarButtonStyle()
    {
        var style=new Style(typeof(ToggleButton));style.Setters.Add(new Setter(ForegroundProperty,UI.Brush("Foreground")));
        style.Setters.Add(new Setter(TemplateProperty,(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ToggleButton"><Border x:Name="Face" CornerRadius="2" BorderThickness="1" BorderBrush="Transparent" Background="{DynamicResource InnerSurface}" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Face" Property="Background" Value="{DynamicResource Selection}"/></Trigger><Trigger Property="IsChecked" Value="True"><Setter TargetName="Face" Property="Background" Value="{DynamicResource Selection}"/><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Gold}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate>
        """)));return style;
    }
    private static ControlTemplate FlatActionTemplate()=>(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
    <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button"><Border x:Name="Face" Background="{DynamicResource InnerSurface}" BorderBrush="Transparent" BorderThickness="1" CornerRadius="2" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Face" Property="Background" Value="{DynamicResource Selection}"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="Face" Property="Background" Value="{DynamicResource Selection}"/><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate>
    """);
    private void UpdateButtons(){foreach(var (id,button) in Buttons){button.IsChecked=State.Active==id;button.ToolTip=(button.IsChecked==true?"Close ":"Open ")+(id=="Tracker"?"Tracker":"Combat Lab")+" · drag to reorder; Alt+Left/Right moves this button";}}
    internal void SaveState()
    {
        if(!ready||placing||WindowState!=WindowState.Normal||App.Exiting&&!exitSaved)return;
        settle.Stop();GetWindowRect(new WindowInteropHelper(this).Handle,out var rect);var dpi=VisualTreeHelper.GetDpi(this);
        var display=PlanningWindowPlacement.AvailableDisplays(this).OrderBy(d=>Math.Abs(d.Work.Left-rect.Left)+Math.Abs(d.Work.Top-rect.Top)).First();
        State.Placement.Width=ActualWidth;State.Placement.Height=28;State.Placement.ScreenLeft=rect.Left;State.Placement.ScreenTop=rect.Top;State.Placement.MonitorDevice=display.Id;State.Placement.MonitorLeft=display.Work.Left;State.Placement.MonitorTop=display.Work.Top;State.Placement.DpiX=dpi.DpiScaleX;State.Placement.DpiY=dpi.DpiScaleY;State.Locked=IsLocked;
        Controller.SaveWorkspaceToolbar(State);
    }
    internal void SaveBeforeExit(){exitSaved=true;SaveState();}
}

internal sealed class ToolbarGlyph(string id):FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth/16,ActualHeight/16));var pen=new Pen(UI.Brush("Melee"),1.3){LineJoin=PenLineJoin.Round};
        dc.DrawGeometry(null,pen,Geometry.Parse(id=="Tracker"?"M2,1L14,1L14,15L2,15Z M4,5L6,7L9,3 M4,11L6,13L9,9 M10,6L12,6 M10,12L12,12":"M2,14L2,10L5,10L5,14Z M7,14L7,6L10,6L10,14Z M12,14L12,2L15,2L15,14Z"));dc.Pop();
    }
}
