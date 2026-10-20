using System.Threading.Channels;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    private readonly Channel<Action> persistence = Channel.CreateUnbounded<Action>(new() { SingleReader = true, AllowSynchronousContinuations = false });
    private Task? persistenceWorker;
    private bool profileNeedsPersistence = true;
    private string persistedInventoryVersion = "";
    internal Task DrainPersistenceAsync()
    {
        if (Demo) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Sync) QueuePersistence(() => completion.SetResult());
        return completion.Task;
    }
    private void QueuePersistence(Action write)
    {
        if (Demo) return;
        if (!persistence.Writer.TryWrite(write)) throw new InvalidOperationException("Persistence queue has stopped; restart the tracker to recover from the retained log.");
        persistenceWorker ??= Task.Run(async () =>
        {
            await foreach (var action in persistence.Reader.ReadAllAsync())
                for (int attempt = 0; ; attempt++)
                    try { using var measure = UpdateDiagnostics.Measure("Database write"); action(); break; }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    {
                        Status = "Storage retry: " + error.Message;
                        File.AppendAllText(Path.Combine(DataFolder, "errors.log"), DateTime.Now + " Storage retry (checkpoint not advanced): " + error + Environment.NewLine);
                        await Task.Delay(Math.Min(5000, 250 * (attempt + 1)));
                    }
        });
    }
}
