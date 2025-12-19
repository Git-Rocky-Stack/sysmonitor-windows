using Microsoft.UI.Dispatching;

namespace SysMonitor.App.Helpers;

public static class DispatcherQueueExtensions
{
    /// <summary>
    /// Enqueues a task to run on the dispatcher queue and returns a Task that completes when the action is executed.
    /// </summary>
    public static Task EnqueueAsync(this DispatcherQueue dispatcherQueue, Action callback)
    {
        var tcs = new TaskCompletionSource();

        if (!dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                callback();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }))
        {
            tcs.SetException(new InvalidOperationException("Failed to enqueue to dispatcher"));
        }

        return tcs.Task;
    }

    /// <summary>
    /// Enqueues a task to run on the dispatcher queue with priority.
    /// </summary>
    public static Task EnqueueAsync(this DispatcherQueue dispatcherQueue, DispatcherQueuePriority priority, Action callback)
    {
        var tcs = new TaskCompletionSource();

        if (!dispatcherQueue.TryEnqueue(priority, () =>
        {
            try
            {
                callback();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }))
        {
            tcs.SetException(new InvalidOperationException("Failed to enqueue to dispatcher"));
        }

        return tcs.Task;
    }
}