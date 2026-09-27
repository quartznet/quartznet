using System.Collections.Concurrent;

using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The listener manager builds its snapshots when the registrations change and hands the same one to
/// every read until the next change (#3865), so a notification costs no copy.
/// </summary>
public sealed class ListenerSnapshotTest
{
    [Test]
    public void AReadIsOneSnapshotUntilTheRegistrationsChange()
    {
        ListenerManagerImpl manager = new();
        manager.AddJobListener(new NamedJobListener("a"));
        manager.AddTriggerListener(new NamedTriggerListener("a"));
        manager.AddSchedulerListener(new NamedSchedulerListener("a"));

        IReadOnlyList<IJobListener> jobListeners = manager.GetJobListeners();
        IReadOnlyList<ITriggerListener> triggerListeners = manager.GetTriggerListeners();
        IReadOnlyList<ISchedulerListener> schedulerListeners = manager.GetSchedulerListeners();

        manager.GetJobListeners().Should().BeSameAs(jobListeners, "nothing changed, so there is nothing to copy");
        manager.GetTriggerListeners().Should().BeSameAs(triggerListeners);
        manager.GetSchedulerListeners().Should().BeSameAs(schedulerListeners);
        manager.GetAttachedJobListeners().Should().BeSameAs(manager.GetAttachedJobListeners());
        manager.GetAttachedTriggerListeners().Should().BeSameAs(manager.GetAttachedTriggerListeners());
        manager.GetSchedulerListenerArray().Should().BeSameAs(manager.GetSchedulerListenerArray());

        manager.AddJobListener(new NamedJobListener("b"));
        manager.AddTriggerListener(new NamedTriggerListener("b"));
        manager.AddSchedulerListener(new NamedSchedulerListener("b"));

        manager.GetJobListeners().Select(x => x.Name).Should().Equal(["a", "b"], "a change publishes a new snapshot");
        manager.GetTriggerListeners().Select(x => x.Name).Should().Equal(["a", "b"]);
        manager.GetSchedulerListeners().Select(x => x.Name).Should().Equal(["a", "b"]);
        jobListeners.Select(x => x.Name).Should().Equal(["a"], "a snapshot taken before a change never sees it");
        triggerListeners.Select(x => x.Name).Should().Equal(["a"]);
        schedulerListeners.Select(x => x.Name).Should().Equal(["a"]);
    }

    [Test]
    public void ASnapshotCannotBeWrittenThrough()
    {
        ListenerManagerImpl manager = new();
        manager.AddJobListener(new NamedJobListener("a"));
        manager.AddTriggerListener(new NamedTriggerListener("a"));
        manager.AddSchedulerListener(new NamedSchedulerListener("a"));

        Action writeJobListener = () => ((IList<IJobListener>) manager.GetJobListeners())[0] = new NamedJobListener("b");
        Action writeTriggerListener = () => ((IList<ITriggerListener>) manager.GetTriggerListeners())[0] = new NamedTriggerListener("b");
        Action writeSchedulerListener = () => ((IList<ISchedulerListener>) manager.GetSchedulerListeners())[0] = new NamedSchedulerListener("b");

        writeJobListener.Should().Throw<NotSupportedException>("every caller shares the snapshot until the next change");
        writeTriggerListener.Should().Throw<NotSupportedException>();
        writeSchedulerListener.Should().Throw<NotSupportedException>();
        manager.GetJobListener("a").Should().NotBeNull();
    }

    [Test]
    public void RemovingTheLastListenerLeavesTheEmptySnapshot()
    {
        ListenerManagerImpl manager = new();
        manager.AddJobListener(new NamedJobListener("a"));
        manager.AddSchedulerListener(new NamedSchedulerListener("a"));

        manager.RemoveJobListener("a").Should().BeTrue();
        manager.RemoveSchedulerListener("a").Should().BeTrue();

        manager.GetJobListeners().Should().BeSameAs(Array.Empty<IJobListener>(), "an empty manager hands out the shared empty list");
        manager.GetAttachedJobListeners().Should().BeEmpty();
        manager.GetSchedulerListeners().Should().BeSameAs(Array.Empty<ISchedulerListener>());
        manager.RemoveJobListener("a").Should().BeFalse("there is nothing left to remove");
    }

    /// <summary>
    /// A reader racing a change holds the registrations from before it or from after it, never a
    /// mixture: every snapshot is internally whole, whichever moment it was read at.
    /// </summary>
    [Test]
    public async Task ReadersRacingChangesSeeWholeSnapshots()
    {
        ListenerManagerImpl manager = new();
        ConcurrentQueue<string> faults = new();
        const int Writers = 4;
        const int Changes = 2_000;
        int writersLeft = Writers;

        Task[] writers = Enumerable.Range(0, Writers)
            .Select(writer => Task.Run(() =>
            {
                for (int i = 0; i < Changes; i++)
                {
                    string name = $"writer-{writer}-{i % 5}";
                    if (i % 2 == 0)
                    {
                        manager.AddJobListener(new NamedJobListener(name));
                        manager.AddTriggerListener(new NamedTriggerListener(name));
                        manager.AddSchedulerListener(new NamedSchedulerListener(name));
                    }
                    else
                    {
                        manager.RemoveJobListener(name);
                        manager.RemoveTriggerListener(name);
                        manager.RemoveSchedulerListener(name);
                    }
                }

                Interlocked.Decrement(ref writersLeft);
            }))
            .ToArray();

        Task[] readers = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() =>
            {
                while (Volatile.Read(ref writersLeft) > 0)
                {
                    CheckWhole(manager.GetAttachedJobListeners().Select(x => (x.Name, x.Listener?.Name)), "attached job listeners", faults);
                    CheckWhole(manager.GetAttachedTriggerListeners().Select(x => (x.Name, x.Listener?.Name)), "attached trigger listeners", faults);
                    CheckWhole(manager.GetJobListeners().Select(x => (x?.Name, x?.Name)), "job listeners", faults);
                    CheckWhole(manager.GetSchedulerListeners().Select(x => (x?.Name, x?.Name)), "scheduler listeners", faults);
                }
            }))
            .ToArray();

        Func<Task> race = () => Task.WhenAll(writers.Concat(readers));
        await race.Should().CompleteWithinAsync(TimeSpan.FromSeconds(60), "the race is a fixed number of changes");

        faults.Should().BeEmpty("a reader only ever holds a snapshot a change published whole");
    }

    /// <summary>
    /// Notifying the scheduler listeners while they are being registered and removed reaches a whole
    /// snapshot of them every time, the internal ones included.
    /// </summary>
    [Test]
    public async Task NotificationsRacingChangesReachEveryListenerOfTheirSnapshot()
    {
        QuartzScheduler scheduler = new(new QuartzSchedulerResources
        {
            Name = "listener-snapshots",
            InstanceId = "listener-snapshots",
            IdleWaitTime = TimeSpan.FromSeconds(1),
            JobStore = TestJobStores.Ram(),
        });
        CountingSchedulerListener resident = new("resident");
        CountingSchedulerListener internalListener = new("internal");
        scheduler.ListenerManager.AddSchedulerListener(resident);
        scheduler.AddInternalSchedulerListener(internalListener);
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job").StoreDurably().Build();
        const int Notifications = 5_000;
        int notifying = 1;

        Task changes = Task.Run(() =>
        {
            for (int i = 0; Volatile.Read(ref notifying) == 1; i++)
            {
                string name = $"transient-{i % 3}";
                scheduler.ListenerManager.AddSchedulerListener(new CountingSchedulerListener(name));
                scheduler.ListenerManager.RemoveSchedulerListener(name);
            }
        });

        Task notifications = Task.Run(async () =>
        {
            for (int i = 0; i < Notifications; i++)
            {
                await scheduler.NotifySchedulerListenersJobAdded(job);
            }

            Volatile.Write(ref notifying, 0);
        });

        Func<Task> race = () => Task.WhenAll(changes, notifications);
        await race.Should().CompleteWithinAsync(TimeSpan.FromSeconds(60), "the race is a fixed number of notifications");

        resident.Calls.Should().Be(Notifications, "a listener registered throughout is in every snapshot");
        internalListener.Calls.Should().Be(Notifications, "and so is an internal one, whatever the registered ones were doing");
    }

    [Test]
    public async Task ANotificationAfterAChangeReachesTheListenersAsTheyNowAre()
    {
        QuartzScheduler scheduler = new(new QuartzSchedulerResources
        {
            Name = "listener-snapshot-refresh",
            InstanceId = "listener-snapshot-refresh",
            IdleWaitTime = TimeSpan.FromSeconds(1),
            JobStore = TestJobStores.Ram(),
        });
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job").StoreDurably().Build();
        CountingSchedulerListener first = new("first");
        CountingSchedulerListener second = new("second");
        CountingSchedulerListener internalListener = new("internal");

        scheduler.ListenerManager.AddSchedulerListener(first);
        await scheduler.NotifySchedulerListenersJobAdded(job);

        scheduler.ListenerManager.AddSchedulerListener(second);
        scheduler.AddInternalSchedulerListener(internalListener);
        await scheduler.NotifySchedulerListenersJobAdded(job);

        scheduler.ListenerManager.RemoveSchedulerListener("first");
        await scheduler.NotifySchedulerListenersJobAdded(job);

        first.Calls.Should().Be(2, "it was registered for the first two notifications");
        second.Calls.Should().Be(2, "it was registered for the last two");
        internalListener.Calls.Should().Be(2, "an internal listener added between notifications is in the next one's list");
    }

    private static void CheckWhole(IEnumerable<(string EntryName, string ListenerName)> snapshot, string what, ConcurrentQueue<string> faults)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach ((string entryName, string listenerName) in snapshot)
        {
            if (entryName is null || listenerName is null)
            {
                faults.Enqueue($"{what}: an empty slot");
            }
            else if (!string.Equals(entryName, listenerName, StringComparison.Ordinal))
            {
                faults.Enqueue($"{what}: '{entryName}' holds the listener '{listenerName}'");
            }
            else if (!seen.Add(entryName))
            {
                faults.Enqueue($"{what}: '{entryName}' twice");
            }
        }
    }

    private sealed class NamedJobListener(string name) : IJobListener
    {
        public string Name { get; } = name;
    }

    private sealed class NamedTriggerListener(string name) : ITriggerListener
    {
        public string Name { get; } = name;
    }

    private sealed class NamedSchedulerListener(string name) : ISchedulerListener
    {
        public string Name { get; } = name;
    }

    private sealed class CountingSchedulerListener(string name) : ISchedulerListener
    {
        private int calls;

        public string Name { get; } = name;

        public int Calls => Volatile.Read(ref calls);

        public ValueTask JobAdded(IScheduler scheduler, IJobDetail jobDetail, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            return default;
        }
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
