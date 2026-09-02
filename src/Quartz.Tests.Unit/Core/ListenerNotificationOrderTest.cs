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

#nullable enable

using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Listeners are notified in the order they were registered — a promise as of 4.0, and the reason the
/// manager holds each family in an <c>OrderedDictionary</c> rather than a <c>Dictionary</c>.
/// </summary>
/// <remarks>
/// <para>
/// It matters to anything built out of several listeners that are not independent: an audit listener
/// that has to see a state a chaining listener sets, a tenant listener that establishes the context the
/// next one reads. Without the promise the answer is "whatever the hash of the names happens to be",
/// which is stable enough to build on by accident and to break on a rename.
/// </para>
/// <para>
/// A plain <c>Dictionary</c> enumerates in insertion order too, right up until an entry is removed, so
/// <see cref="AListenerAddedAfterAnotherWasRemovedGoesLast" /> is the case that tells the two apart. It
/// asks the manager rather than a firing, because the notification loops are a <c>foreach</c> over
/// exactly what it answers with.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ListenerNotificationOrderTest
{
    private const string Group = "listener-order";

    /// <summary>
    /// How long a test is willing to wait for a firing to be reported. Long enough that a loaded build
    /// agent never trips it, and never used as a measurement.
    /// </summary>
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Deliberately not alphabetical, and not the order the listeners were constructed in either, so
    /// that a manager which sorted or hashed its listeners could not produce it by chance.
    /// </summary>
    private static readonly string[] registrationOrder = ["charlie", "alpha", "bravo"];

    [Test]
    public async Task ListenersAreNotifiedInRegistrationOrder()
    {
        CallLog log = new(expectedCompletions: registrationOrder.Length);

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q.ConfigureScheduler(options => options.InstanceName = "listener-order"))
            .BuildScheduler();

        try
        {
            foreach (string name in registrationOrder)
            {
                scheduler.ListenerManager.AddJobListener(new RecordingJobListener(name, log));
                scheduler.ListenerManager.AddTriggerListener(new RecordingTriggerListener(name, log));
                scheduler.ListenerManager.AddSchedulerListener(new RecordingSchedulerListener(name, log));
            }

            IJobDetail job = JobBuilder.Create<NoopJob>().WithIdentity("job", Group).Build();
            ITrigger trigger = TriggerBuilder.Create().WithIdentity("trigger", Group).ForJob(job).StartNow().Build();

            await scheduler.ScheduleJob(job, trigger);
            await scheduler.Start();

            await log.Completed.WaitAsync(observationDeadline);

            log.Names("JobScheduled").Should().Equal(registrationOrder,
                "a scheduler listener registered first hears about a scheduling first");
            log.Names("TriggerFired").Should().Equal(registrationOrder,
                "and so does a trigger listener, on the way in");
            log.Names("JobToBeExecuted").Should().Equal(registrationOrder,
                "and a job listener, which is the order a listener that prepares something for the next one depends on");
            log.Names("JobWasExecuted").Should().Equal(registrationOrder,
                "the way out is the same order rather than the reverse of it: these are notifications, not a stack of scopes");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// Registering again under the same name replaces the listener where it stands, which is what makes
    /// the documented "register it again with the matchers it needs" safe for a listener whose position
    /// in the chain matters.
    /// </summary>
    [Test]
    public void ReregisteringAListenerKeepsItsPlace()
    {
        CallLog log = new(expectedCompletions: 0);
        ListenerManagerImpl manager = new();

        manager.AddJobListener(new RecordingJobListener("alpha", log));
        manager.AddJobListener(new RecordingJobListener("bravo", log));
        manager.AddJobListener(new RecordingJobListener("charlie", log));

        RecordingJobListener replacement = new("bravo", log);
        manager.AddJobListener(replacement);

        manager.GetJobListeners().Select(x => x.Name).Should().Equal(["alpha", "bravo", "charlie"],
            "the second registration replaces the listener without moving it, so a listener that has to hear "
            + "about something else does not silently change when it is heard");
        manager.GetJobListeners()[1].Should().BeSameAs(replacement,
            "it is the replacement that stands there, not the listener it replaced");
    }

    /// <summary>
    /// The case a <c>Dictionary</c> would fail: it fills the hole a removal left, so a listener added
    /// afterwards would be notified in the removed one's place rather than last.
    /// </summary>
    [Test]
    public void AListenerAddedAfterAnotherWasRemovedGoesLast()
    {
        CallLog log = new(expectedCompletions: 0);
        ListenerManagerImpl manager = new();

        manager.AddJobListener(new RecordingJobListener("alpha", log));
        manager.AddJobListener(new RecordingJobListener("bravo", log));
        manager.AddJobListener(new RecordingJobListener("charlie", log));
        manager.RemoveJobListener("alpha");
        manager.AddJobListener(new RecordingJobListener("delta", log));

        manager.GetJobListeners().Select(x => x.Name).Should().Equal(["bravo", "charlie", "delta"],
            "registration order is a running order, not a set of slots: a listener added later is notified later, "
            + "whatever room an earlier removal left");

        manager.AddTriggerListener(new RecordingTriggerListener("alpha", log));
        manager.AddTriggerListener(new RecordingTriggerListener("bravo", log));
        manager.RemoveTriggerListener("alpha");
        manager.AddTriggerListener(new RecordingTriggerListener("charlie", log));

        manager.GetTriggerListeners().Select(x => x.Name).Should().Equal(["bravo", "charlie"],
            "the trigger listeners are held the same way");

        manager.AddSchedulerListener(new RecordingSchedulerListener("alpha", log));
        manager.AddSchedulerListener(new RecordingSchedulerListener("bravo", log));
        manager.RemoveSchedulerListener("alpha");
        manager.AddSchedulerListener(new RecordingSchedulerListener("charlie", log));

        manager.GetSchedulerListeners().Select(x => x.Name).Should().Equal(["bravo", "charlie"],
            "and so are the scheduler listeners");
    }

    /// <summary>
    /// Who was called, in the order they were called, per notification.
    /// </summary>
    private sealed class CallLog
    {
        private readonly Lock gate = new();
        private readonly Dictionary<string, List<string>> calls = [];
        private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int expectedCompletions;

        public CallLog(int expectedCompletions)
        {
            this.expectedCompletions = expectedCompletions;
        }

        public Task Completed => completed.Task;

        public IReadOnlyList<string> Names(string notification)
        {
            lock (gate)
            {
                return calls.TryGetValue(notification, out List<string>? names) ? [.. names] : [];
            }
        }

        public void Record(string notification, string listener)
        {
            bool done;
            lock (gate)
            {
                if (!calls.TryGetValue(notification, out List<string>? names))
                {
                    names = [];
                    calls[notification] = names;
                }

                names.Add(listener);
                done = notification == "JobWasExecuted" && names.Count >= expectedCompletions;
            }

            if (done)
            {
                completed.TrySetResult();
            }
        }
    }

    private sealed class RecordingJobListener : IJobListener
    {
        private readonly CallLog log;

        public RecordingJobListener(string name, CallLog log)
        {
            Name = name;
            this.log = log;
        }

        public string Name { get; }

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            log.Record(nameof(JobToBeExecuted), Name);
            return default;
        }

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            log.Record(nameof(JobWasExecuted), Name);
            return default;
        }
    }

    private sealed class RecordingTriggerListener : ITriggerListener
    {
        private readonly CallLog log;

        public RecordingTriggerListener(string name, CallLog log)
        {
            Name = name;
            this.log = log;
        }

        public string Name { get; }

        public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            log.Record(nameof(TriggerFired), Name);
            return default;
        }
    }

    private sealed class RecordingSchedulerListener : ISchedulerListener
    {
        private readonly CallLog log;

        public RecordingSchedulerListener(string name, CallLog log)
        {
            Name = name;
            this.log = log;
        }

        public string Name { get; }

        public ValueTask JobScheduled(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
        {
            log.Record(nameof(JobScheduled), Name);
            return default;
        }
    }

    public sealed class NoopJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
