#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

using System.Diagnostics.Metrics;
using System.Globalization;
using System.Reflection;

using Npgsql;

namespace Quartz.Tests.Integration.MixedVersionNode;

/// <summary>
/// One clustered scheduler on the shared database, and the commands the test drives it with.
/// </summary>
/// <remarks>
/// The scheduler is built with the shipped defaults for everything but what makes it a cluster node on
/// that database: pool, batch size, idle wait, misfire threshold and check-in are what each version
/// ships. With <c>--history</c> it also keeps its execution history there, bounded so that nothing the
/// run writes is swept before the test reads it. <c>--lock-free false</c> turns 4.6's lock-free firing
/// rounds off on the working tree; a released node keeps its shipped default.
/// </remarks>
internal sealed class Node
{
    /// <summary>The group every job the commands create is stored in.</summary>
    private const string JobGroup = "gate";

    /// <summary>How many history rows a node keeps per scheduler when it keeps any.</summary>
    private const int HistoryBound = 1_000_000;

    private readonly IScheduler scheduler;
    private readonly RoundLockCounter rounds;

    private Node(IScheduler scheduler, RoundLockCounter rounds)
    {
        this.scheduler = scheduler;
        this.rounds = rounds;
    }

#if QUARTZ_WORKING_TREE
    private const bool WorkingTree = true;
#else
    private const bool WorkingTree = false;
#endif

    private static string QuartzVersion
    {
        get
        {
            Assembly quartz = typeof(IScheduler).Assembly;
            return quartz.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? quartz.GetName().Version?.ToString()
                   ?? "unknown";
        }
    }

    public static async Task<Node> Create(NodeOptions options)
    {
        Runs.Configure(options);

        // Listening before the scheduler exists, so that every round it ever runs is counted.
        RoundLockCounter rounds = new();

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q =>
            {
                q.ConfigureScheduler(o =>
                {
                    o.InstanceName = options.SchedulerName;
                    o.InstanceId = options.InstanceId;
                });

                q.UsePersistentStore(store =>
                {
                    store.ConfigureStore(o => o.TablePrefix = options.TablePrefix);
                    store.UsePostgres(NpgsqlFactory.Instance, options.ConnectionString);
                    store.UseSystemTextJsonSerializer();
                    store.UseClustering();

                    if (options.History)
                    {
                        store.UseExecutionHistory();
                    }

#if QUARTZ_WORKING_TREE
                    if (!options.LockFreeRounds)
                    {
                        store.ConfigureStore(o => o.LockFreeFiringRounds = false);
                    }
#endif
                });

                if (options.History)
                {
                    // Well above the run's few thousand rows: the default bound of 2,000 per scheduler would
                    // have one node's sweep trim rows the test counts.
                    q.Services.AddQuartzExecutionHistory(o => o.MaxEntriesPerScheduler = HistoryBound);
                }
            })
            .BuildScheduler()
            .ConfigureAwait(false);

        return new Node(scheduler, rounds);
    }

    public List<KeyValuePair<string, string>> Describe() =>
    [
        new("instance", scheduler.SchedulerInstanceId),
        new("quartz", QuartzVersion),
        new("build", WorkingTree ? "working-tree" : "released")
    ];

    /// <summary>
    /// Runs commands until told to stop, or until standard input closes.
    /// </summary>
    public async Task<int> Serve(TextReader input)
    {
        while (true)
        {
            string? line = await input.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                // The test has gone. Stop the way a killed process would, without waiting for jobs.
                await scheduler.Shutdown(waitForJobsToComplete: false).ConfigureAwait(false);
                return 0;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            Command command;
            try
            {
                command = Protocol.Parse(line);
                Protocol.Reply(Protocol.Ok, await Execute(command).ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                Protocol.Reply(Protocol.Error, exception);
                continue;
            }

            if (command.Verb == "shutdown")
            {
                return 0;
            }
        }
    }

    private async Task<List<KeyValuePair<string, string>>> Execute(Command command)
    {
        switch (command.Verb)
        {
            case "start":
                await scheduler.Start().ConfigureAwait(false);
                return [];

            case "schedule-one-offs":
                return await ScheduleOneOffs(command).ConfigureAwait(false);

            case "schedule-serial":
                return await ScheduleSerial(command).ConfigureAwait(false);

            case "schedule-chain":
                return await ScheduleChain(command).ConfigureAwait(false);

            case "schedule":
                return await Schedule(command).ConfigureAwait(false);

            case "update-trigger":
                bool updated = await scheduler.UpdateTriggerDetails(
                    TriggerKeyOf(command),
                    new TriggerDetailsUpdate()
                        .WithDescription(command.Text("description"))
                        .WithPriority(command.Number("priority"))).ConfigureAwait(false);
                return [new("updated", updated.ToString())];

            case "pause-trigger":
                return [new("paused", (await PauseTrigger(command).ConfigureAwait(false)).ToString())];

            case "resume-trigger":
                return [new("resumed", (await scheduler.ResumeTrigger(TriggerKeyOf(command)).ConfigureAwait(false)).ToString())];

            case "pause-trigger-group":
                return [new("groups", string.Join(",", await PauseTriggerGroup(command).ConfigureAwait(false)))];

            case "pause-job-group":
                return [new("groups", string.Join(",", await PauseJobGroup(command).ConfigureAwait(false)))];

            case "trigger-pause":
                return Describe(await scheduler.GetTriggerPause(TriggerKeyOf(command)).ConfigureAwait(false));

            case "trigger-group-pause":
                return Describe(await scheduler.GetTriggerGroupPause(command.Text("group")).ConfigureAwait(false));

            case "job-group-pause":
                return Describe(await scheduler.GetJobGroupPause(command.Text("group")).ConfigureAwait(false));

            case "cluster-nodes":
                List<ClusterNode> nodes = await scheduler.QueryClusterNodes().ConfigureAwait(false);
                return [new("nodes", string.Join(",", nodes.Select(x => x.InstanceId + ":" + x.State).Order(StringComparer.Ordinal)))];

            case "round-locks":
                return rounds.Describe();

            case "shutdown":
                await scheduler.Shutdown(waitForJobsToComplete: command.Flag("wait")).ConfigureAwait(false);
                return [];

            default:
                throw new ArgumentException($"'{command.Verb}' is not a command this node ({QuartzVersion}) knows.");
        }
    }

    /// <summary>
    /// One-off triggers of one concurrent job, all due at one instant: <c>one-off.one-off-{i}</c>.
    /// </summary>
    private async Task<List<KeyValuePair<string, string>>> ScheduleOneOffs(Command command)
    {
        int from = command.Number("from");
        int count = command.Number("count");
        DateTimeOffset due = command.Instant("due");

        IJobDetail job = Job("one-off", new JobKey("one-off", JobGroup));
        ITrigger[] triggers = new ITrigger[count];
        for (int i = 0; i < count; i++)
        {
            triggers[i] = TriggerBuilder.Create()
                .WithIdentity("one-off-" + (from + i).ToString(CultureInfo.InvariantCulture), "one-off")
                .ForJob(job)
                .StartAt(due)
                .Build();
        }

        await scheduler.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [job] = triggers }, ScheduleJobOptions.Replacing).ConfigureAwait(false);
        return [new("scheduled", count.ToString(CultureInfo.InvariantCulture))];
    }

    /// <summary>
    /// Recurring triggers of the one serial job, all starting at one instant: <c>serial.serial-{i}</c>.
    /// </summary>
    private async Task<List<KeyValuePair<string, string>>> ScheduleSerial(Command command)
    {
        int from = command.Number("from");
        int count = command.Number("count");
        DateTimeOffset start = command.Instant("start");
        TimeSpan interval = TimeSpan.FromMilliseconds(command.Number("intervalMs"));

        IJobDetail job = Job("serial", new JobKey("serial", JobGroup));
        ITrigger[] triggers = new ITrigger[count];
        for (int i = 0; i < count; i++)
        {
            triggers[i] = TriggerBuilder.Create()
                .WithIdentity("serial-" + (from + i).ToString(CultureInfo.InvariantCulture), "serial")
                .ForJob(job)
                .StartAt(start)
                .WithSchedule(SimpleScheduleBuilder.Create().WithInterval(interval).RepeatForever())
                .Build();
        }

        await scheduler.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [job] = triggers }, ScheduleJobOptions.Replacing).ConfigureAwait(false);
        return [new("scheduled", count.ToString(CultureInfo.InvariantCulture))];
    }

    /// <summary>
    /// Pairs of a one-off parent pinned to one node and a continuation of it pinned to the other:
    /// <c>chain.parent-{i}</c> and <c>chain.continuation-{i}</c>.
    /// </summary>
    /// <remarks>
    /// The continuation starts in the past, so that once its parent's completion releases it, it is due
    /// at once and only the release was holding it back.
    /// </remarks>
    private async Task<List<KeyValuePair<string, string>>> ScheduleChain(Command command)
    {
        int from = command.Number("from");
        int count = command.Number("count");
        DateTimeOffset due = command.Instant("due");
        PreferredNode parentNode = PreferredNode.For(command.Text("parentNode"));
        PreferredNode childNode = PreferredNode.For(command.Text("childNode"));

        IJobDetail parentJob = Job("chain", new JobKey("parent", JobGroup));
        IJobDetail continuationJob = Job("chain", new JobKey("continuation", JobGroup));

        ITrigger[] parents = new ITrigger[count];
        ITrigger[] continuations = new ITrigger[count];
        for (int i = 0; i < count; i++)
        {
            string index = (from + i).ToString(CultureInfo.InvariantCulture);
            TriggerKey parentKey = new("parent-" + index, "chain");

            parents[i] = TriggerBuilder.Create()
                .WithIdentity(parentKey)
                .ForJob(parentJob)
                .StartAt(due)
                .WithPreferredNode(parentNode)
                .Build();

            continuations[i] = TriggerBuilder.Create()
                .WithIdentity("continuation-" + index, "chain")
                .ForJob(continuationJob)
                .StartAt(TimeProvider.System.GetUtcNow().AddSeconds(-5))
                .StartAfter(parentKey, ContinuationCondition.OnSuccess)
                .WithPreferredNode(childNode)
                .Build();
        }

        // Parents first: a continuation is refused unless the trigger it waits for exists.
        await scheduler.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [parentJob] = parents }, ScheduleJobOptions.Replacing).ConfigureAwait(false);
        await scheduler.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [continuationJob] = continuations }, ScheduleJobOptions.Replacing).ConfigureAwait(false);
        return [new("scheduled", count.ToString(CultureInfo.InvariantCulture))];
    }

    /// <summary>
    /// One simple trigger every <c>intervalMs=</c>, repeating forever unless <c>repeat=</c> says how often
    /// or <c>end=</c> when to stop, optionally pinned to a node, given an overlap policy and a retry policy,
    /// and measuring its interval from completion (<c>fromCompletion=</c>, the working tree only).
    /// </summary>
    private async Task<List<KeyValuePair<string, string>>> Schedule(Command command)
    {
        JobKey jobKey = new(command.Text("job"), command.OptionalText("jobGroup") ?? JobGroup);
        await scheduler.AddJob(Job(command.Text("type"), jobKey), AddJobOptions.Replacing).ConfigureAwait(false);

        SimpleScheduleBuilder schedule = SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromMilliseconds(command.Number("intervalMs")));
        schedule = command.OptionalText("repeat") is { } repeat
            ? schedule.WithRepeatCount(int.Parse(repeat, CultureInfo.InvariantCulture))
            : schedule.RepeatForever();

        // An interval measured from the previous run's completion (#4140), which only the working tree has.
        if (command.OptionalText("fromCompletion") is not null)
        {
#if QUARTZ_WORKING_TREE
            schedule = schedule.MeasureIntervalFromCompletion(command.Flag("fromCompletion"));
#else
            throw new ArgumentException($"Quartz {QuartzVersion} cannot measure an interval from completion; only the working tree is sent fromCompletion=.");
#endif
        }

        TriggerBuilder<IJob> trigger = TriggerBuilder.Create()
            .WithIdentity(TriggerKeyOf(command))
            .ForJob(jobKey)
            .StartAt(command.Instant("start"))
            .WithSchedule(schedule);

        if (command.OptionalText("end") is not null)
        {
            trigger = trigger.EndAt(command.Instant("end"));
        }

        if (command.OptionalText("pin") is { } pin)
        {
            trigger = trigger.WithPreferredNode(PreferredNode.For(pin));
        }

        if (command.OptionalText("policy") is { } policy)
        {
            trigger = trigger.WithOverlapPolicy(Enum.Parse<OverlapPolicy>(policy));
        }

        // The stored form, parsed by whichever Quartz this node runs.
        if (command.OptionalText("retry") is { } retry)
        {
            trigger = trigger.WithRetryPolicy(RetryPolicy.Parse(retry));
        }

        DateTimeOffset first = await scheduler.ScheduleJob(trigger.Build()).ConfigureAwait(false);
        return [new("first", first.UtcTicks.ToString(CultureInfo.InvariantCulture))];
    }

    private async Task<bool> PauseTrigger(Command command)
    {
        if (command.OptionalText("reason") is null)
        {
            return await scheduler.PauseTrigger(TriggerKeyOf(command)).ConfigureAwait(false);
        }

        return await scheduler.PauseTriggerWith(TriggerKeyOf(command), Details(command)).ConfigureAwait(false);
    }

    private async Task<List<string>> PauseTriggerGroup(Command command)
    {
        GroupMatcher<TriggerKey> group = GroupMatcher<TriggerKey>.GroupEquals(command.Text("group"));
        if (command.OptionalText("reason") is null)
        {
            return await scheduler.PauseTriggerGroups(group).ConfigureAwait(false);
        }

        return await scheduler.PauseTriggerGroupsWith(group, Details(command)).ConfigureAwait(false);
    }

    private async Task<List<string>> PauseJobGroup(Command command)
    {
        GroupMatcher<JobKey> group = GroupMatcher<JobKey>.GroupEquals(command.Text("group"));
        if (command.OptionalText("reason") is null)
        {
            return await scheduler.PauseJobGroups(group).ConfigureAwait(false);
        }

        return await scheduler.PauseJobGroupsWith(group, Details(command)).ConfigureAwait(false);
    }

    private static PauseDetails Details(Command command) => new()
    {
        Reason = command.Text("reason"),
        RequestedBy = command.Text("by")
    };

    private static List<KeyValuePair<string, string>> Describe(PauseInfo? pause) => pause is null
        ? [new("paused", "False")]
        :
        [
            new("paused", "True"),
            new("reason", pause.Reason ?? ""),
            new("by", pause.RequestedBy ?? ""),
            new("at", pause.PausedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture))
        ];

    private static TriggerKey TriggerKeyOf(Command command) => new(command.Text("name"), command.Text("group"));

    private static IJobDetail Job(string type, JobKey key) => type switch
    {
        "one-off" => JobBuilder.Create<OneOffJob>().WithIdentity(key).StoreDurably().Build(),
        "serial" => JobBuilder.Create<SerialJob>().WithIdentity(key).StoreDurably().Build(),
        "chain" => JobBuilder.Create<ChainJob>().WithIdentity(key).StoreDurably().Build(),
        "state" => JobBuilder.Create<StateJob>().WithIdentity(key).StoreDurably().Build(),
        "progress" => JobBuilder.Create<ProgressJob>().WithIdentity(key).StoreDurably().Build(),
        _ => throw new ArgumentException($"'{type}' is not a job type this node has.")
    };

    /// <summary>
    /// Counts this node's firing rounds by the lock each said it ran under (#4067): the
    /// <c>quartz.jobstore.lock</c> attribute on <c>quartz.trigger.acquisition.duration</c>, which a 4.6 node
    /// tags <c>none</c> or <c>TRIGGER_ACCESS</c> and an older node does not tag at all.
    /// </summary>
    /// <remarks>
    /// Spelled with the instrument and attribute names rather than the constants, so that the same source
    /// compiles against a released Quartz that may not have the constants.
    /// </remarks>
    private sealed class RoundLockCounter
    {
        private readonly MeterListener listener = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> reasons = new(StringComparer.Ordinal);
        private int lockFree;
        private int locked;
        private int untagged;

        public RoundLockCounter()
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "Quartz" && instrument.Name == "quartz.trigger.acquisition.duration")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<double>(OnRound);
            listener.Start();
        }

        public List<KeyValuePair<string, string>> Describe() =>
        [
            new("lockfree", Volatile.Read(ref lockFree).ToString(CultureInfo.InvariantCulture)),
            new("locked", Volatile.Read(ref locked).ToString(CultureInfo.InvariantCulture)),
            new("untagged", Volatile.Read(ref untagged).ToString(CultureInfo.InvariantCulture)),
            new("reasons", string.Join(",", reasons.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + x.Value.ToString(CultureInfo.InvariantCulture))))
        ];

        private void OnRound(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        {
            string? lockName = null;
            string? reason = null;
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == "quartz.jobstore.lock")
                {
                    lockName = tag.Value as string;
                }
                else if (tag.Key == "quartz.jobstore.lock.reason")
                {
                    reason = tag.Value as string;
                }
            }

            if (lockName is null)
            {
                Interlocked.Increment(ref untagged);
            }
            else if (lockName == "none")
            {
                Interlocked.Increment(ref lockFree);
            }
            else
            {
                Interlocked.Increment(ref locked);
                reasons.AddOrUpdate(reason ?? "unknown", 1, static (_, count) => count + 1);
            }
        }
    }
}
