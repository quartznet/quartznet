namespace Quartz.Benchmark.Competitors.Engines;

/// <summary>
/// One scheduler, behind the four things every scenario here asks of one.
/// </summary>
/// <remarks>
/// <para>
/// The interface is deliberately thin. Anything richer would have to be expressible by all three
/// libraries, and the moment a shape has to be emulated on one side the comparison stops being about
/// the libraries and starts being about the emulation. So each implementation uses the API its own
/// documentation teaches — <c>IScheduler.ScheduleJob</c>, <c>ITimeTickerManager.AddAsync</c>,
/// <c>BackgroundJob.Schedule</c> — and the fairness is in the settings and in where the counting
/// happens rather than in a common abstraction.
/// </para>
/// <para>
/// Every implementation is built and torn down per benchmark iteration where a store fills up, which
/// is every throughput scenario: TickerQ keeps completed tickers and LINQ-scans every entry it holds on
/// each poll, and Hangfire's in-memory storage expires finished jobs on a timer rather than at once, so
/// a second iteration against the first one's leftovers would measure the leftovers.
/// </para>
/// </remarks>
internal interface IEngine : IAsyncDisposable
{
    /// <summary>What the row is called in the published table.</summary>
    string Name { get; }

    /// <summary>
    /// Builds the scheduler and starts it processing, with a worker limit of
    /// <paramref name="maxConcurrency" />.
    /// </summary>
    ValueTask Start(int maxConcurrency, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules <paramref name="count" /> one-off executions, all due at <paramref name="dueAt" />,
    /// through whatever batch API the library has.
    /// </summary>
    ValueTask ScheduleOneOff(int count, DateTimeOffset dueAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules <paramref name="count" /> schedules that each recur once a second, indefinitely.
    /// </summary>
    ValueTask ScheduleRecurring(int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Empties the store without stopping the scheduler, so the next measured window starts where the
    /// last one did.
    /// </summary>
    ValueTask Clear(CancellationToken cancellationToken = default);
}
