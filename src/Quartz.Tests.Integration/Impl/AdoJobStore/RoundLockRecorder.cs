using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

using Quartz.Diagnostics;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Counts the firing rounds every node in this process runs, by the lock each said it ran under
/// (#4067): the <c>quartz.jobstore.lock</c> attribute on <c>quartz.trigger.acquisition.duration</c>,
/// and the reason for a locked one.
/// </summary>
public sealed class RoundLockRecorder : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly ConcurrentDictionary<string, int> reasons = new(StringComparer.Ordinal);
    private int lockFree;
    private int locked;
    private int untagged;

    public RoundLockRecorder()
    {
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == QuartzInstrumentation.MeterName
                && instrument.Name == QuartzInstrumentation.Instruments.TriggerAcquisitionDuration)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>(OnRound);
        listener.Start();
    }

    public int LockFree => Volatile.Read(ref lockFree);

    public int Locked => Volatile.Read(ref locked);

    /// <summary>How many rounds took the lock for <paramref name="reason" />, as the reason tag spells it.</summary>
    public int LockedFor(string reason) => reasons.TryGetValue(reason, out int count) ? count : 0;

    public string Describe()
    {
        string byReason = reasons.Count == 0
            ? ""
            : " (" + string.Join(", ", reasons.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}")) + ")";
        return $"none={LockFree}, TRIGGER_ACCESS={Locked}{byReason}, untagged={Volatile.Read(ref untagged)}";
    }

    public void Dispose() => listener.Dispose();

    private void OnRound(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object>> tags, object state)
    {
        string lockName = null;
        string reason = null;
        foreach (KeyValuePair<string, object> tag in tags)
        {
            if (tag.Key == ActivityTags.JobStoreLock)
            {
                lockName = tag.Value as string;
            }
            else if (tag.Key == ActivityTags.JobStoreLockReason)
            {
                reason = tag.Value as string;
            }
        }

        switch (lockName)
        {
            case "none":
                Interlocked.Increment(ref lockFree);
                break;
            case "TRIGGER_ACCESS":
                Interlocked.Increment(ref locked);
                reasons.AddOrUpdate(reason ?? "unknown", 1, static (_, count) => count + 1);
                break;
            default:
                Interlocked.Increment(ref untagged);
                break;
        }
    }
}
