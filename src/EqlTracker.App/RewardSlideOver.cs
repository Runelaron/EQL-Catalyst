using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

/// <summary>Transient read-only reward facts; both surfaces stay measured in one pane.</summary>
internal sealed class RewardSlideOver : Grid
{
    private readonly TrackerController controller;
    private readonly FrameworkElement walkthrough;
    internal readonly Border Surface;
    internal readonly StackPanel Facts=new();
    internal readonly ComboBox Copies=new(){HorizontalAlignment=HorizontalAlignment.Stretch,DisplayMemberPath="Label"};
    private readonly TranslateTransform front=new();
    private readonly RectangleGeometry underlayClip=new();
    private static readonly DependencyProperty OffsetProperty=DependencyProperty.Register("Offset",typeof(double),typeof(RewardSlideOver),new PropertyMetadata(0d,(d,_)=>((RewardSlideOver)d).ApplyOffset()));
    private double Offset {get=>(double)GetValue(OffsetProperty);set=>SetValue(OffsetProperty,value);}
    private void ApplyOffset(){front.X=Offset;underlayClip.Rect=new Rect(0,0,Math.Clamp(Offset,0,Math.Max(0,ActualWidth)),Math.Max(0,ActualHeight));}
    private readonly TextBlock title=UI.Text("",16,"Accent",true),error=UI.Text("",11,"Muted");
    private readonly ItemHeaderIcons header;
    private readonly ContentControl card=new();
    private readonly StackPanel effects=new();
    private readonly ItemAcquisitionView acquisition=new();
    private readonly Button returnButton,closeButton;
    private string character="",questId="",itemKey="",version="",selectedCopy="catalog",requested="";
    private ToggleButton? origin;
    private bool changing,copyChosen;
    private long animationRevision;
    internal bool IsOpen {get;private set;}
    internal bool IsAnimating=>HasAnimatedProperties;
    internal string ItemKey=>itemKey;
    internal void UpdateOrigin(ToggleButton button)=>origin=button;
    internal double SlideOffset=>Offset;
    internal FrameworkElement Underlay=>walkthrough;
    internal double UnderlayVisibleWidth=>underlayClip.Rect.Width;
    internal event Action? StateChanged;
    internal event Action? Dismissed;
    private sealed record Copy(string Label,InventoryEntry? Entry);
    internal RewardSlideOver(TrackerController controller,FrameworkElement walkthrough)
    {
        this.controller=controller;this.walkthrough=walkthrough;ClipToBounds=true;
        walkthrough.Clip=underlayClip;Children.Add(walkthrough);
        header=new(title);Facts.Children.Add(header);Facts.Children.Add(Copies);Facts.Children.Add(card);Facts.Children.Add(effects);Facts.Children.Add(error);Facts.Children.Add(acquisition);
        returnButton=UI.Button("‹ Back to walkthrough",()=>Dismiss(restoreFocus:true));returnButton.ToolTip="Return to the selected quest step";
        closeButton=UI.Button("×",()=>Dismiss(restoreFocus:true));closeButton.Width=28;closeButton.Height=28;closeButton.Padding=new(0);closeButton.FontSize=14;closeButton.Margin=new(4,0,0,4);closeButton.ToolTip="Close reward details";
        System.Windows.Automation.AutomationProperties.SetName(closeButton,"Close reward details");
        var actions=new DockPanel();DockPanel.SetDock(closeButton,Dock.Right);actions.Children.Add(closeButton);actions.Children.Add(returnButton);
        var root=new DockPanel();DockPanel.SetDock(actions,Dock.Top);root.Children.Add(actions);root.Children.Add(UI.Scroll(Facts));
        Surface=UI.Card(root);Surface.SetResourceReference(Border.BackgroundProperty,"DetailSurface");Surface.RenderTransform=front;Surface.Visibility=Visibility.Collapsed;Children.Add(Surface);
        Copies.ToolTip="Inspect a recorded physical copy, or catalog +0. No equipment or inventory changes.";
        Copies.SelectionChanged+=(_,_)=>{if(changing||Copies.SelectedItem is not Copy copy)return;selectedCopy=PhysicalItemChoices.Identity(copy.Entry);copyChosen=true;version="";Refresh();};
        SizeChanged+=(_,_)=>{if(IsAnimating)Snap(IsOpen);else Offset=IsOpen?0:Math.Max(1,ActualWidth);ApplyOffset();};
        Unloaded+=(_,_)=>{if(!PreviewRenderer.Capturing)Dismiss(true);};
    }
    private string Character=>controller.Profile.Character+"|"+controller.Profile.Server+"|"+controller.Profile.SelectedLoadout;
    internal void Open(QuestDefinition quest,string key,ToggleButton source)
    {
        bool entering=!IsOpen;bool same=questId==quest.Id&&itemKey==key&&character==Character;
        character=Character;questId=quest.Id;itemKey=key;origin=source;
        if(!same){selectedCopy="catalog";copyChosen=false;requested="";version="";}
        if(!same||entering&&!copyChosen){version="";var item=controller.Catalog.FindKey(key);var copy=item==null?null:PhysicalItemChoices.Ordered(controller.Inventory.Evidence(item).Where(e=>!e.IsCollection&&!e.IsAugment&&e.Count>0)).FirstOrDefault();selectedCopy=PhysicalItemChoices.Identity(copy);}
        IsOpen=true;StateChanged?.Invoke();Refresh();
        if(entering){if(Surface.Visibility!=Visibility.Visible){Offset=Math.Max(1,ActualWidth);}Transition(true);}
        Dispatcher.BeginInvoke(()=>{if(IsOpen&&itemKey==key&&IsVisible)returnButton.Focus();},DispatcherPriority.Input);
    }
    internal void Refresh()
    {
        if(!IsOpen)return;
        var quest=controller.Catalog.Quests.FirstOrDefault(q=>q.Id==questId);
        if(character!=Character||quest==null||!QuestPresentation.RewardKeys(quest).Contains(itemKey)){Dismiss(true);return;}
        var item=controller.Catalog.FindKey(itemKey);var inventory=controller.Inventory;
        string key=controller.EquipmentRevision+"|"+controller.CatalogRevision+"|"+inventory.Revision+"|"+item?.Revision+"|"+item?.MetadataVersion+"|"+selectedCopy;
        if(version==key)return;version=key;
        var physical=item==null?[]:PhysicalItemChoices.Ordered(inventory.Evidence(item).Where(e=>!e.IsCollection&&!e.IsAugment&&e.Count>0));
        changing=true;var rows=new[]{new Copy("Catalog +0",null)}.Concat(physical.Select(e=>new Copy(e.Location+" · +"+e.Rank,e))).ToList();Copies.ItemsSource=rows;Copies.SelectedItem=rows.FirstOrDefault(c=>PhysicalItemChoices.Identity(c.Entry)==selectedCopy)??rows[0];changing=false;
        Copies.Visibility=physical.Count>0?Visibility.Visible:Visibility.Collapsed;
        var instance=(Copies.SelectedItem as Copy)?.Entry;selectedCopy=PhysicalItemChoices.Identity(instance);int rank=instance?.Rank??0;
        title.Text=(item?.Name??itemKey)+(rank>0?" +"+rank:"");effects.Children.Clear();
        if(item==null){header.Clear();card.Content=null;error.Text="Item metadata pending. Known reward identity is retained.";acquisition.Visibility=Visibility.Collapsed;}
        else{
            header.Show(item,ItemOwnership.For(item,inventory));
            if(card.Content is ClassicItemCard existing)existing.Update(item,rank,instance,false);else card.Content=new ClassicItemCard(item,rank,instance);
            foreach(var effect in instance?.Augments??[])effects.Children.Add(UI.Text("Recorded effect: "+(effect.EffectText.Length>0?effect.EffectText:effect.Name),11,"Mint"));
            error.Text=item.MetadataComplete?"":"Some item facts are unverified; undocumented values are omitted.";
            acquisition.Visibility=Visibility.Visible;acquisition.Show(item,controller.Catalog,inventory);
        }
        foreach(var button in Facts.Children.OfType<Button>().ToArray())Facts.Children.Remove(button);
        string url=item?.WikiUrl??"";if(url.Length>0)Facts.Children.Add(UI.Button("Open item wiki",()=>UI.OpenUrl(url)));
        if(quest.WikiUrl.Length>0)Facts.Children.Add(UI.Button("Open walkthrough",()=>UI.OpenUrl(quest.WikiUrl)));
        if((item==null||!item.MetadataComplete)&&requested!=itemKey&&!controller.Demo){requested=itemKey;controller.RequestGearMetadata(item?.Name??itemKey);}
        Usability.Tooltips(Surface);
    }
    internal bool Contains(DependencyObject? node)
    {
        for(var current=node;current!=null;current=AncestorOf(current))if(ReferenceEquals(current,Surface))return true;
        return false;
    }
    internal static DependencyObject? AncestorOf(DependencyObject child)=>child is Visual||child is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(child)??LogicalTreeHelper.GetParent(child):LogicalTreeHelper.GetParent(child);
    internal void Dismiss(bool immediate=false,bool restoreFocus=false)
    {
        if(!IsOpen&&Surface.Visibility!=Visibility.Visible)return;
        IsOpen=false;requested="";StateChanged?.Invoke();
        if(immediate)Snap(false);else Transition(false);
        if(restoreFocus&&origin is {IsVisible:true,IsEnabled:true})origin.Focus();
        Dismissed?.Invoke();
    }
    private bool Motion=>controller.Profile.SmoothMeterMotion&&SystemParameters.ClientAreaAnimation;
    private void Transition(bool open)
    {
        long revision=++animationRevision;
        Surface.Visibility=Visibility.Visible;Surface.IsHitTestVisible=open;walkthrough.IsHitTestVisible=!open;
        double width=Math.Max(1,ActualWidth),start=Offset;
        if(!Motion){Snap(open);return;}
        var animation=new DoubleAnimation(start,open?0:width,TimeSpan.FromMilliseconds(180)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.HoldEnd};
        animation.Completed+=(_,_)=>{if(revision==animationRevision)Snap(open);};
        BeginAnimation(OffsetProperty,animation,HandoffBehavior.SnapshotAndReplace);
    }
    private void Snap(bool open)
    {
        animationRevision++;BeginAnimation(OffsetProperty,null);Offset=open?0:Math.Max(1,ActualWidth);
        Surface.IsHitTestVisible=open;Surface.Visibility=open?Visibility.Visible:Visibility.Collapsed;walkthrough.IsHitTestVisible=!open;
    }

}

public sealed partial class QuestWindow
{
    private RewardSlideOver? progressRewardSlide;
    private Grid? progressFooter;
    private string progressRewardQuest="",progressRewardButtons="";
    private readonly Dictionary<string,ToggleButton> rewardActions=[];
    private bool syncingRewardActions;
    private void RefreshProgressRewards(QuestDefinition quest)
    {
        if(progressRewardQuest!=quest.Id){progressRewardSlide?.Dismiss();progressRewardQuest=quest.Id;}
        var keys=QuestPresentation.RewardKeys(quest).ToArray();
        string signature=quest.Id+"|"+string.Join('|',keys.Select(k=>k+":"+Controller.Catalog.FindKey(k)?.Name));
        if(signature!=progressRewardButtons)
        {
            progressRewardButtons=signature;progressReward.Children.Clear();rewardActions.Clear();
            foreach(string key in keys)
            {
                var item=Controller.Catalog.FindKey(key);var action=new ToggleButton{Content=item?.Name??key,Tag=key,FontSize=12,FontWeight=FontWeights.SemiBold,Cursor=Cursors.Hand,HorizontalAlignment=HorizontalAlignment.Left,HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,3),Padding=new(2,2,2,2),ToolTip="Show or hide reward details"};
                // Text-only chrome, with native toggle semantics for keyboard and assistive technology.
                action.Template=(ControlTemplate)XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ToggleButton">
                  <Border x:Name="Face" Background="Transparent" Padding="{TemplateBinding Padding}" BorderThickness="0,0,0,1" BorderBrush="Transparent">
                    <TextBlock x:Name="Name" Text="{TemplateBinding Content}" Foreground="{DynamicResource Accent}" TextWrapping="Wrap"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsChecked" Value="True"><Setter TargetName="Face" Property="Background" Value="{DynamicResource Selection}"/><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Name" Property="TextDecorations" Value="Underline"/></Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource Foreground}"/></Trigger>
                    <Trigger Property="IsPressed" Value="True"><Setter TargetName="Face" Property="Background" Value="{DynamicResource Selection}"/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
                """);
                action.Checked+=(_,_)=>{if(!syncingRewardActions)progressRewardSlide?.Open(quest,key,action);};
                action.Unchecked+=(_,_)=>{if(!syncingRewardActions&&progressRewardSlide?.IsOpen==true&&progressRewardSlide.ItemKey==key)progressRewardSlide.Dismiss();};
                action.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){action.IsChecked=action.IsChecked!=true;e.Handled=true;}};
                System.Windows.Automation.AutomationProperties.SetName(action,"Reward details: "+(item?.Name??key));rewardActions[key]=action;progressReward.Children.Add(action);
            }
            if(keys.Length==0)progressReward.Children.Add(UI.Text("Reward not documented",11,"Muted"));
        }
        if(progressRewardSlide?.IsOpen==true&&rewardActions.TryGetValue(progressRewardSlide.ItemKey,out var origin))progressRewardSlide.UpdateOrigin(origin);
        progressRewardSlide?.Refresh();SyncRewardActions();
    }
    private void SyncRewardActions()
    { syncingRewardActions=true;try{foreach(var pair in rewardActions)pair.Value.IsChecked=progressRewardSlide?.IsOpen==true&&progressRewardSlide.ItemKey==pair.Key;}finally{syncingRewardActions=false;} }
    private void InstallRewardDismissal()
    {
        progressRewardSlide!.StateChanged+=SyncRewardActions;
        AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler((_,e)=>
        {
            if(progressRewardSlide?.IsOpen!=true)return;
            var source=e.OriginalSource as DependencyObject;if(progressRewardSlide.Contains(source))return;
            for(var node=source;node!=null;node=RewardSlideOver.AncestorOf(node))if(node is ToggleButton b&&rewardActions.Values.Contains(b))return;
            progressRewardSlide.Dismiss(); // Do not handle: the original action must still run.
        }),true);
        AddHandler(Keyboard.PreviewKeyDownEvent,new KeyEventHandler((_,e)=>{if(e.Key==Key.Escape&&progressRewardSlide?.IsOpen==true&&!Usability.Tree(progressRewardSlide).OfType<ComboBox>().Any(c=>c.IsDropDownOpen)){progressRewardSlide.Dismiss(restoreFocus:true);e.Handled=true;}}),true);
        tabs.SelectionChanged+=(_,e)=>{if(e.Source==tabs&&SelectedTab!=TrackerTab.Progress)progressRewardSlide?.Dismiss(true);};
        Body.IsVisibleChanged+=(_,_)=>{if(!Body.IsVisible&&!PreviewRenderer.Capturing)progressRewardSlide?.Dismiss(true);};
        progressRewardSlide!.Dismissed+=()=>{
            string? node=progressDetailNodeId;var scroll=UI.FindScroll(howTo.Parent as DependencyObject??howTo);double offset=scroll?.VerticalOffset??0;
            Dispatcher.BeginInvoke(()=>{if(IsLoaded&&SelectedTab==TrackerTab.Progress&&selectedStep?.Id==node&&progressRewardSlide?.IsOpen!=true){ShowProgressDetails();UpdateLayout();scroll?.ScrollToVerticalOffset(offset);}},DispatcherPriority.Loaded);
        };
        Closed+=(_,_)=>progressRewardSlide?.Dismiss(true);
    }
}
