using Microsoft.Extensions.Time.Testing;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// One arming of a timer: which timer, counted in the order they were created, and what it was armed
/// for.
/// </summary>
public sealed record TimerArming(int Timer, TimeSpan DueTime);

/// <summary>
/// A <see cref="FakeTimeProvider" /> that records every arming of every timer created on it, so a test
/// can wait for something to have parked on the clock instead of guessing when to advance it.
/// </summary>
/// <remarks>
/// <para>
/// An arming is recorded only once the timer is armed. A test that sees the record and advances the
/// clock at once must find a timer to fire; recorded first, an advance landing between the record and
/// the arming would leave the timer due in a future nothing advances to — the flake
/// <c>ClusterManagerTest</c>'s own recorder was fixed for.
/// </para>
/// <para>
/// <see cref="Fire" /> runs a timer's callback without its clock, which is what a timer that comes due
/// just after something else ended the wait it was armed for looks like to whoever was waiting.
/// </para>
/// </remarks>
public sealed class ArmingRecordingTimeProvider : TimeProvider
{
    private readonly FakeTimeProvider clock;
    private readonly Lock gate = new();
    private readonly List<(TimerCallback Callback, object State)> callbacks = [];
    private readonly List<TimerArming> armings = [];
    private readonly List<(Func<TimerArming, bool> Match, int Count, TaskCompletionSource Source)> waiters = [];

    public ArmingRecordingTimeProvider(FakeTimeProvider clock)
    {
        this.clock = clock;
    }

    /// <summary>The clock the timers run on, for the test to advance.</summary>
    public FakeTimeProvider Clock => clock;

    /// <summary>Every arming with a finite due time, oldest first.</summary>
    public IReadOnlyList<TimerArming> Armings
    {
        get
        {
            lock (gate)
            {
                return armings.ToArray();
            }
        }
    }

    /// <summary>
    /// How far what this provider says the time is has been stepped away from the clock its timers run
    /// on: a wall clock stepped forward, as far as whoever reads it can tell, with no timer firing.
    /// </summary>
    public TimeSpan Skew { get; set; }

    /// <summary>
    /// Added to <see cref="Skew" /> by every read of the time: a negative step is a wall clock that keeps
    /// being stepped back between one reading and the next.
    /// </summary>
    public TimeSpan StepPerRead { get; set; }

    /// <summary>
    /// Run once, the next time a timer is armed, after it is armed and before the arming is recorded:
    /// a clock that moves while whoever armed the timer is between two readings of it.
    /// </summary>
    public Action OnNextArming { get; set; }

    public override DateTimeOffset GetUtcNow()
    {
        DateTimeOffset now = clock.GetUtcNow() + Skew;
        Skew += StepPerRead;
        return now;
    }

    public override TimeZoneInfo LocalTimeZone => clock.LocalTimeZone;

    public override long TimestampFrequency => clock.TimestampFrequency;

    public override long GetTimestamp() => clock.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
    {
        ITimer inner = clock.CreateTimer(callback, state, dueTime, period);

        int index;
        lock (gate)
        {
            index = callbacks.Count;
            callbacks.Add((callback, state));
        }

        Record(index, dueTime);
        return new RecordingTimer(inner, this, index);
    }

    /// <summary>
    /// A task that completes once <paramref name="count" /> armings that <paramref name="match" /> have
    /// been recorded, counting the ones already there.
    /// </summary>
    public Task Armed(Func<TimerArming, bool> match, int count = 1)
    {
        TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (armings.Count(match) >= count)
            {
                return Task.CompletedTask;
            }

            waiters.Add((match, count, source));
        }

        return source.Task;
    }

    /// <summary>How many armings that <paramref name="match" /> have been recorded so far.</summary>
    public int CountArmed(Func<TimerArming, bool> match)
    {
        lock (gate)
        {
            return armings.Count(match);
        }
    }

    /// <summary>
    /// Runs the callback of the <paramref name="timer" />th timer created here, as its clock would when
    /// it came due.
    /// </summary>
    public void Fire(int timer)
    {
        (TimerCallback Callback, object State) entry;
        lock (gate)
        {
            entry = callbacks[timer];
        }

        entry.Callback(entry.State);
    }

    private void Record(int timer, TimeSpan dueTime)
    {
        if (dueTime == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        List<TaskCompletionSource> ready = null;
        lock (gate)
        {
            armings.Add(new TimerArming(timer, dueTime));
            for (int i = waiters.Count - 1; i >= 0; i--)
            {
                if (armings.Count(waiters[i].Match) >= waiters[i].Count)
                {
                    ready ??= [];
                    ready.Add(waiters[i].Source);
                    waiters.RemoveAt(i);
                }
            }
        }

        // Completed outside the lock, so a continuation that arms another timer cannot re-enter it.
        foreach (TaskCompletionSource source in ready ?? [])
        {
            source.TrySetResult();
        }
    }

    private sealed class RecordingTimer : ITimer
    {
        private readonly ITimer inner;
        private readonly ArmingRecordingTimeProvider owner;
        private readonly int index;

        public RecordingTimer(ITimer inner, ArmingRecordingTimeProvider owner, int index)
        {
            this.inner = inner;
            this.owner = owner;
            this.index = index;
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            bool changed = inner.Change(dueTime, period);

            Action onArming = owner.OnNextArming;
            if (onArming is not null && dueTime != Timeout.InfiniteTimeSpan)
            {
                owner.OnNextArming = null;
                onArming();
            }

            owner.Record(index, dueTime);
            return changed;
        }

        public void Dispose() => inner.Dispose();

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
