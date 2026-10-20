namespace EqlTracker.Core;

// Offsets identify records; identical text/timestamps are legitimate separate attacks.
public sealed class RecentIds(int capacity = 16384)
{
    private readonly HashSet<string> ids = [];
    private readonly Queue<string> order = new();
    public bool Accept(string id)
    {
        if (id.Length == 0) return true;
        if (!ids.Add(id)) return false;
        order.Enqueue(id); if (order.Count > capacity) ids.Remove(order.Dequeue()); return true;
    }
}
