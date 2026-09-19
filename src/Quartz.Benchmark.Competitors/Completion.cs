using System.Diagnostics;
using System.Globalization;

namespace Quartz.Benchmark.Competitors;

/// <summary>
/// The one thing every arm of this harness counts: a job that actually ran.
/// </summary>
/// <remarks>
/// <para>
/// Counting inside the executing job is what makes the three libraries comparable at all. A scheduler
/// can be made to look arbitrarily fast by measuring the call that hands it work — that is what the
/// published third-party comparison this harness answers does — so nothing here reads a queue depth, a
/// status column or a manager's return value. The counter is incremented by the job body, on every
/// side, and a benchmark invocation is <em>waiting for the next N of those to happen</em>.
/// </para>
/// <para>
/// This is <c>Quartz.Benchmark</c>'s <c>FireThroughput</c> pattern generalised: an
/// <see cref="Interlocked" /> counter, an absolute target published before it is checked, a
/// <see cref="ManualResetEventSlim" /> to wait on rather than a spin — a spin would take a core away
/// from the pool whose throughput is the measurement — and a timeout that turns a hang into an
/// exception, because a benchmark that never finishes is indistinguishable from a slow one and the
/// smoke run has nobody watching it.
/// </para>
/// <para>
/// The counter is process-wide and static because it has to be: Hangfire runs a job through an
/// expression tree over a static method and TickerQ through a source-generated delegate that news up
/// the declaring class, so neither job body can be handed an instance to report to. One engine runs at
/// a time in a BenchmarkDotNet process, so there is nothing to share it with.
/// </para>
/// </remarks>
internal static class Completion
{
    /// <summary>
    /// How long a wait may go without completing before it is called a broken harness rather than a
    /// slow engine. Generous enough for two thousand firings against a real PostgreSQL container.
    /// </summary>
    private static readonly TimeSpan timeout = TimeSpan.FromMinutes(5);

    private static long completed;

    /// <summary>The wait in progress, published for the job to see. Null between invocations.</summary>
    private static volatile Waiter? pending;

    private static long lastEntryTimestamp;

    /// <summary>
    /// Whether <see cref="Record" /> stamps <see cref="Stopwatch.GetTimestamp" /> on the way past.
    /// </summary>
    /// <remarks>
    /// Only the latency scenario wants it. A timestamp read is tens of nanoseconds against a firing of
    /// a few microseconds, which is inside the noise of one measurement and not of twenty thousand, so
    /// the throughput scenarios leave it off rather than pay it twenty thousand times an invocation.
    /// </remarks>
    public static bool CaptureEntryTimestamp { get; set; }

    /// <summary>
    /// <see cref="Stopwatch.GetTimestamp" /> as read by the most recent job body, at its first
    /// instruction.
    /// </summary>
    public static long LastEntryTimestamp => Interlocked.Read(ref lastEntryTimestamp);

    /// <summary>How many jobs have run since the last <see cref="Arm" />.</summary>
    public static long Completed => Interlocked.Read(ref completed);

    /// <summary>
    /// Zeroes the counter and publishes the absolute target the next <see cref="Await" /> waits for.
    /// </summary>
    /// <remarks>
    /// Called before the work is scheduled, never after, so that a job which runs while the scheduling
    /// loop is still going is counted rather than lost. The counter is zeroed rather than read because
    /// the engine is rebuilt before every armed window, so there is nothing left over to carry.
    /// </remarks>
    public static void Arm(int count)
    {
        Interlocked.Exchange(ref completed, 0);
        pending = new Waiter(count);
    }

    /// <summary>Blocks until the armed target has been reached, and is the whole body of a benchmark.</summary>
    public static void Await()
    {
        Waiter waiter = pending
            ?? throw new InvalidOperationException("Await without Arm: the target has to be published before the work is scheduled.");

        if (Interlocked.Read(ref completed) >= waiter.Target)
        {
            waiter.Done.Set();
        }

        bool finished = waiter.Done.Wait(timeout);

        if (!finished)
        {
            // Read only on the way out: everything above is inside the measured window, and this is a
            // diagnostic for the case where there is no measurement.
            long seen = Interlocked.Read(ref completed);
            pending = null;

            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"Waited {timeout} for {waiter.Target} executions and saw {seen}. The engine stopped running jobs, which is a broken harness rather than a slow one."));
        }
    }

    /// <summary>Records that a job ran, and releases the wait it completes.</summary>
    public static void Record()
    {
        if (CaptureEntryTimestamp)
        {
            Interlocked.Exchange(ref lastEntryTimestamp, Stopwatch.GetTimestamp());
        }

        long count = Interlocked.Increment(ref completed);
        Waiter? waiter = pending;
        if (waiter is not null && count >= waiter.Target)
        {
            waiter.Done.Set();
        }
    }

    /// <summary>Drops any armed target, so a torn-down engine's stragglers cannot set an event.</summary>
    public static void Disarm()
    {
        pending = null;
    }

    /// <summary>One invocation's wait: the absolute execution count it is waiting for.</summary>
    private sealed class Waiter(long target)
    {
        public long Target { get; } = target;

        public ManualResetEventSlim Done { get; } = new(false);
    }
}
