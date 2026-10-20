using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

internal abstract record AppearanceChoice(string Name) { public abstract object Id { get; } }
internal sealed record DamageAppearanceChoice(DamageEmblem Value,string Title) : AppearanceChoice(Title) { public override object Id=>Value; }
internal sealed record TabAppearanceChoice(TrackerTabStyle Value,string Title) : AppearanceChoice(Title) { public override object Id=>Value; }

internal sealed class AppearanceChoiceGlyph : Decorator
{
    public static readonly DependencyProperty ChoiceProperty=DependencyProperty.Register(nameof(Choice),typeof(AppearanceChoice),typeof(AppearanceChoiceGlyph),new PropertyMetadata(null,Changed));
    public AppearanceChoice? Choice { get=>(AppearanceChoice?)GetValue(ChoiceProperty);set=>SetValue(ChoiceProperty,value); }
    private static void Changed(DependencyObject sender,DependencyPropertyChangedEventArgs args)
    {
        var glyph=(AppearanceChoiceGlyph)sender;
        glyph.Child=args.NewValue switch {
            DamageAppearanceChoice choice=>new DamageEmblemPreview { Emblem=choice.Value,Width=24,Height=24,HorizontalAlignment=HorizontalAlignment.Center },
            TabAppearanceChoice choice=>new Viewbox { Stretch=Stretch.Uniform,Child=new TrackerTabChrome("Tab",choice.Value,true){Width=110,Height=44} },
            _=>null
        };
    }
}

internal sealed class TabStyleSample : Viewbox
{
    internal TrackerTabStyle StyleId { get;private set; }
    internal TabStyleSample() { Width=210;Height=48;Stretch=Stretch.Uniform;HorizontalAlignment=HorizontalAlignment.Left;IsHitTestVisible=false; }
    internal void Set(TrackerTabStyle style)
    {
        StyleId=style;
        Child=UI.Row(new TrackerTabChrome("Gear",style,true){Width=104,Height=44},new TrackerTabChrome("Pet",style,false){Width=104,Height=44});
    }
}

public sealed partial class SettingsWindow
{
    private readonly ComboBox damageChoice=new(){Width=210,Height=28,Tag="damage-emblem-selector"};
    private readonly ComboBox tabChoice=new(){Width=220,Height=28,Tag="tab-style-selector"};
    private readonly DamageEmblemPreview selectedDamagePreview=new(){Width=64,Height=64,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,3,0,0)};
    private readonly TabStyleSample selectedTabPreview=new();
    private static void ConfigureVisualChoice(ComboBox box,IEnumerable<AppearanceChoice> choices,object selected,string help)
    {
        box.ItemsSource=choices.OrderBy(c=>c.Name,StringComparer.OrdinalIgnoreCase).ToArray();
        box.SelectedValuePath=nameof(AppearanceChoice.Id);box.SelectedValue=selected;box.ToolTip=help;
        TextSearch.SetTextPath(box,nameof(AppearanceChoice.Name));
        var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
        var glyph=new FrameworkElementFactory(typeof(AppearanceChoiceGlyph));glyph.SetBinding(AppearanceChoiceGlyph.ChoiceProperty,new Binding());
        glyph.SetValue(FrameworkElement.WidthProperty,60d);glyph.SetValue(FrameworkElement.HeightProperty,24d);glyph.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,6,0));row.AppendChild(glyph);
        var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding(nameof(AppearanceChoice.Name)));name.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(name);
        box.ItemTemplate=new DataTemplate{VisualTree=row};
        var itemStyle=new Style(typeof(ComboBoxItem));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(6,3,6,3)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));itemStyle.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("Foreground")));
        var surface=new FrameworkElementFactory(typeof(Border));surface.SetBinding(Border.BackgroundProperty,new Binding("Background"){RelativeSource=RelativeSource.TemplatedParent});surface.SetBinding(Border.PaddingProperty,new Binding("Padding"){RelativeSource=RelativeSource.TemplatedParent});
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.ContentSourceProperty,"Content");surface.AppendChild(presenter);
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(ComboBoxItem)){VisualTree=surface}));
        foreach(var property in new[]{ComboBoxItem.IsHighlightedProperty,ComboBoxItem.IsSelectedProperty})
        {var trigger=new Trigger{Property=property,Value=true};trigger.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("Selection")));itemStyle.Triggers.Add(trigger);}
        box.ItemContainerStyle=itemStyle;
    }
    private UIElement BuildVisualAppearanceControls()
    {
        ConfigureVisualChoice(damageChoice,IconArtwork.DamageChoices.Select(c=>new DamageAppearanceChoice(c.Id,c.Name)),Controller.Profile.DamageEmblem,"Damage-mode artwork. Choose by name and image; applies immediately in Clean and Vibrant. Independent of Self, Physical attacks and Total.");
        ConfigureVisualChoice(tabChoice,TrackerPresentation.Tabs.Select(c=>new TabAppearanceChoice(c.Id,c.Name)),Controller.Profile.TrackerTabs,"Tracker tab shape, saved per character. The preview uses the same tab renderer as the open Tracker.");
        damageChoice.SelectionChanged+=(_,_)=>{if(damageChoice.SelectedItem is DamageAppearanceChoice choice){Controller.Profile.DamageEmblem=choice.Value;ApplyAppearance();appearanceSave.Stop();Controller.Save();}};
        tabChoice.SelectionChanged+=(_,_)=>{if(tabChoice.SelectedItem is TabAppearanceChoice choice){Controller.Profile.TrackerTabs=choice.Value;ApplyAppearance();appearanceSave.Stop();Controller.Save();}};
        preview.Width=215;preview.Margin=new(0,3,0,0);
        selectedDamagePreview.ToolTip="Selected Damage emblem · 64 DIP · follows Icon style";
        selectedTabPreview.ToolTip="Selected tab style · selected Gear and unselected Pet";
        var damage=UI.Stack(Field("Damage emblem",damageChoice,damageChoice.ToolTip.ToString()!),selectedDamagePreview);
        var tabs=UI.Stack(Field("Tab style",tabChoice,tabChoice.ToolTip.ToString()!),selectedTabPreview);
        var meter=UI.Stack(Field("Meter style",meterStyle,"Minimal, Frost or Luminous presentation. The example below follows the selected style, opacity and Smooth motion."),preview);
        foreach(var group in new[]{damage,tabs,meter}){group.Margin=new(0,0,12,8);group.VerticalAlignment=VerticalAlignment.Top;}
        RefreshIconPresentation();return Flow(damage,tabs,meter);
    }
}
