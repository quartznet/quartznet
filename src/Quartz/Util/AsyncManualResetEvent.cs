using System.Diagnostics;

// Adapted from: https://github.com/StephenCleary/AsyncEx/blob/0361015459938f2eb8f3c1ad1021d19ee01c93a4/src/Nito.AsyncEx.Coordination/AsyncManualResetEvent.cs
// Original idea by Stephen Toub: http://blogs.msdn.com/b/pfxteam/archive/2012/02/11/10266920.aspx

namespace Quartz.Util;

/// <summary>
/// An async-compatible manual-reset event.
/// </summary>
[DebuggerDisplay("Id = {Id}, IsSet = {GetStateForDebugger}")]
[DebuggerTypeProxy(typeof(DebugView))]
public sealed class AsyncManualResetEvent
{
    /// <summary>
    /// The object used for synchronization.
    /// </summary>
    private readonly object mutex;

    /// <summary>
    /// The current state of the event.
    /// </summary>
    private TaskCompletionSource<object?> tcs;

    [DebuggerNonUserCode]
    private bool GetStateForDebugger => tcs.Task.IsCompleted;

    /// <summary>
    /// Creates an async-compatible manual-reset event.
    /// </summary>
    /// <param name="set">Whether the manual-reset event is initially set or unset.</param>
    public AsyncManualResetEvent(bool set)
    {
        mutex = new object();
        tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (set)
        {
            tcs.TrySetResult(null);
        }
    }

    /// <summary>
    /// Creates an async-compatible manual-reset event that is initially unset.
    /// </summary>
    public AsyncManualResetEvent()
        : this(false)
    {
    }

    /// <summary>
    /// Gets a semi-unique identifier for this asynchronous manual-reset event.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Whether this event is currently set. This member is seldom used; code using this member has a high possibility of race conditions.
    /// </summary>
    public bool IsSet
    {
        get
        {
            lock (mutex)
            {
                return tcs.Task.IsCompleted;
            }
        }
    }

    /// <summary>
    /// Asynchronously waits for this event to be set.
    /// </summary>
    public Task WaitAsync()
    {
        lock (mutex)
        {
            return tcs.Task;
        }
    }

    /// <summary>
    /// Asynchronously waits for this event to be set or for the wait to be canceled.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token used to cancel the wait. If this token is already canceled, this method will first check whether the event is set.</param>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        Task waitTask = WaitAsync();
        if (waitTask.IsCompleted)
        {
            return waitTask;
        }

        return waitTask.WaitAsync(Timeout.InfiniteTimeSpan, TimeProvider.System, cancellationToken);
    }

    /// <summary>
    /// Synchronously waits for this event to be set. This method may block the calling thread.
    /// </summary>
    public void Wait()
    {
        WaitAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Synchronously waits for this event to be set. This method may block the calling thread.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token used to cancel the wait. If this token is already canceled, this method will first check whether the event is set.</param>
    public void Wait(CancellationToken cancellationToken)
    {
        Task waitTask = WaitAsync(CancellationToken.None);
        if (waitTask.IsCompleted)
        {
            return;
        }

        waitTask.Wait(cancellationToken);
    }

    /// <summary>
    /// Sets the event, atomically completing every task returned by <see cref="AsyncManualResetEvent.WaitAsync()"/>. If the event is already set, this method does nothing.
    /// </summary>
    public void Set()
    {
        lock (mutex)
        {
            tcs.TrySetResult(null);
        }
    }

    /// <summary>
    /// Resets the event. If the event is already reset, this method does nothing.
    /// </summary>
    public void Reset()
    {
        lock (mutex)
        {
            if (tcs.Task.IsCompleted)
            {
                tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    // ReSharper disable UnusedMember.Local
    [DebuggerNonUserCode]
    private sealed class DebugView(AsyncManualResetEvent mre)
    {
        public Guid Id => mre.Id;

        public bool IsSet => mre.GetStateForDebugger;

        public Task CurrentTask => mre.tcs.Task;
    }
    // ReSharper restore UnusedMember.Local
}