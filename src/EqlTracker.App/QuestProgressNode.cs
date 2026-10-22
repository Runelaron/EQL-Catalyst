using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class QuestProgressNode : INotifyPropertyChanged
{
    public QuestNode Node { get; private set; }
    public ObservableCollection<QuestProgressNode> Children { get; } = [];
    private readonly TrackerController controller;
    private bool expanded, selected;
    private bool itemMatch;
    public bool IsItemMatch { get => itemMatch; internal set { if(itemMatch==value)return;itemMatch=value;PropertyChanged?.Invoke(this,new(nameof(IsItemMatch))); } }
    public string Title => (Node.Fulfilled ? "✓ " : "") + Node.Name;
    public string Status => Node.Status;
    public bool Fulfilled => Node.Fulfilled;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsExpanded
    {
        get => expanded;
        set
        {
            if (expanded == value) return; expanded = value;
            if (value) { controller.Profile.ExpandedQuestNodes.Add(Node.Id); controller.Profile.CollapsedQuestNodes.Remove(Node.Id); }
            else { controller.Profile.ExpandedQuestNodes.Remove(Node.Id); controller.Profile.CollapsedQuestNodes.Add(Node.Id); }
            controller.Save(); PropertyChanged?.Invoke(this, new(nameof(IsExpanded)));
        }
    }
    public bool IsSelected { get => selected; set { if (selected == value) return; selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    internal void ExpandForBrowse(bool value)
    { if(expanded==value)return;expanded=value;PropertyChanged?.Invoke(this,new(nameof(IsExpanded))); }
    private QuestProgressNode(QuestNode node, TrackerController owner, int depth)
    {
        Node = node; controller = owner;
        expanded = !owner.Profile.CollapsedQuestNodes.Contains(node.Id) && (depth <= 2 || owner.Profile.ExpandedQuestNodes.Contains(node.Id));
        Sync(Children, node.Children, owner, depth + 1);
    }
    public static void Sync(ObservableCollection<QuestProgressNode> target, IEnumerable<QuestNode> source, TrackerController owner, int depth = 0)
    {
        var nodes = source.ToList(); var keys = nodes.Select(n => n.Id).ToHashSet();
        for (int i = target.Count - 1; i >= 0; i--) if (!keys.Contains(target[i].Node.Id)) target.RemoveAt(i);
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i]; var vm = target.FirstOrDefault(x => x.Node.Id == node.Id);
            if (vm == null) { target.Insert(i, new(node, owner, depth)); continue; }
            bool changed = vm.Node.Name != node.Name || vm.Node.Status != node.Status || vm.Node.Detail != node.Detail;
            vm.Node = node; Sync(vm.Children, node.Children, owner, depth + 1);
            if (changed) foreach (string property in new[] { nameof(Node), nameof(Title), nameof(Status), nameof(Fulfilled) }) vm.PropertyChanged?.Invoke(vm, new(property));
            int previous = target.IndexOf(vm); if (previous != i) target.Move(previous, i);
        }
    }
    public IEnumerable<QuestProgressNode> Descendants() => new[] { this }.Concat(Children.SelectMany(x => x.Descendants()));
}
