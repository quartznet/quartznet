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

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// <see cref="IJobListener.JobProgressChanged" />, end to end on both shipped stores: what a listener
/// hears of a running job's <see cref="IJobExecutionContext.ReportProgress" />, when, and what a listener
/// that throws costs.
/// </summary>
/// <remarks>
/// The scripts report back to back, which is far inside the one-second interval, so what the listeners
/// hear is decided by the coalescing and not by how fast the machine is: the first report goes at once,
/// everything after it inside the interval collapses into the last, and the last is announced when the
/// job returns. The interval itself, on a fake clock, is <c>FireProgressWriterTest</c>'s.
/// </remarks>
[NonParallelizable]
public sealed class JobProgressListenerTest
{
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    private SqliteTestDatabase? database;
    private FakeLoggerProvider logs = null!;

    public enum Store
    {
        InMemory,
        Sqlite
    }

    [SetUp]
    public void SetUp()
    {
        logs = new FakeLoggerProvider();
        ScriptedJob.Script = null;
    }

    [TearDown]
    public void TearDown()
    {
        ScriptedJob.Script = null;
        logs.Dispose();
        database?.Dispose();
        database = null;
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task AListenerHearsTheChangesCoalescedAndTheLastBeforeJobWasExecuted(Store store)
    {
        RecordingListener listener = new("recording");

        ScriptedJob.Script = (context, _) =>
        {
            context.ReportProgress(10, "starting");
            context.ReportProgress(20);
            context.ReportProgress(30, "most of the way");
            context.ReportProgress(40, "done");
            return default;
        };

        await Run(store, scheduler => scheduler.ListenerManager.AddJobListener(listener), listener);

        listener.Heard.Should().Equal(["10 starting", "40 done", "executed"],
            "the first report is announced at once, the three after it inside the interval collapse into the last, "
            + "and that one is announced when the job returns - before JobWasExecuted, never after it");
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task AValueTheListenersHaveHeardIsNotAnnouncedAgain(Store store)
    {
        RecordingListener listener = new("recording");

        ScriptedJob.Script = async (context, cancellationToken) =>
        {
            context.ReportProgress(50, "halfway");
            await listener.HeardProgress.Task.WaitAsync(waitLimit, cancellationToken);
            context.ReportProgress(50, "halfway");
            context.ReportProgress(50, "halfway");
        };

        await Run(store, scheduler => scheduler.ListenerManager.AddJobListener(listener), listener);

        listener.Heard.Should().Equal(["50 halfway", "executed"],
            "the job said the same thing again, which is not a change, so it is neither announced nor re-announced on return");
    }

    [Test]
    public async Task AListenerThatThrowsCostsTheJobNothing()
    {
        RecordingListener listener = new("recording");
        int? storedWhileRunning = null;

        ScriptedJob.Script = async (context, cancellationToken) =>
        {
            context.ReportProgress(10, "a");
            await listener.HeardProgress.Task.WaitAsync(waitLimit, cancellationToken);

            PagedResult<FireInstance> running = await context.Scheduler.QueryFireInstances(new FireInstanceQuery(), cancellationToken);
            storedWhileRunning = running.Items.Single(x => x.FireInstanceId == context.FireInstanceId).Progress;

            context.ReportProgress(20, "b");
        };

        await Run(Store.InMemory, scheduler =>
        {
            scheduler.ListenerManager.AddJobListener(new ThrowingListener());
            scheduler.ListenerManager.AddJobListener(listener);
        }, listener);

        listener.Heard.Should().Equal(["10 a", "20 b", "executed"],
            "a listener before it that threw does not keep the next one from hearing");
        listener.JobException.Should().BeNull("the job did not fail because a listener of its progress did");
        listener.Outcome.Should().Be(ExecutionOutcome.Succeeded);
        storedWhileRunning.Should().Be(10, "the store is written before any listener is told, so a throwing one cannot cost the write");

        List<FakeLogRecord> failures = logs.Collector.GetSnapshot().Where(x => x.Id.Id == 1061).ToList();
        failures.Should().HaveCount(2, "each announcement the listener threw on is logged");
        failures.Should().AllSatisfy(record =>
        {
            record.Level.Should().Be(LogLevel.Warning);
            record.Message.Should().Contain(nameof(ThrowingListener)).And.Contain("progress.scripted");
            record.Exception.Should().BeOfType<InvalidOperationException>();
        });
    }

    [Test]
    public async Task OnlyAListenerWhoseMatchersMatchTheJobHearsIt()
    {
        RecordingListener matching = new("matching");
        RecordingListener elsewhere = new("elsewhere");

        ScriptedJob.Script = (context, _) =>
        {
            context.ReportProgress(70, "seventy");
            return default;
        };

        await Run(Store.InMemory, scheduler =>
        {
            scheduler.ListenerManager.AddJobListener(elsewhere, GroupMatcher<JobKey>.GroupEquals("elsewhere"));
            scheduler.ListenerManager.AddJobListener(matching, GroupMatcher<JobKey>.GroupEquals("progress"));
        }, matching);

        matching.Heard.Should().Equal(["70 seventy", "executed"]);
        elsewhere.Heard.Should().BeEmpty("its matcher selects another group, as it does for every other job-listener event");
    }

    [Test]
    public async Task AListenerThatLeavesItToTheDefaultHearsNothingAndCostsNothing()
    {
        RecordingListener listener = new("recording");
        CompletionOnlyListener completionOnly = new();

        ScriptedJob.Script = (context, _) =>
        {
            context.ReportProgress(10);
            context.ReportProgress(90);
            return default;
        };

        await Run(Store.InMemory, scheduler =>
        {
            scheduler.ListenerManager.AddJobListener(completionOnly);
            scheduler.ListenerManager.AddJobListener(listener);
        }, listener);

        completionOnly.Executed.Should().BeTrue("a listener written before 4.4 is told what it always was");
        listener.Heard.Should().Equal(["10 ", "90 ", "executed"]);
    }

    [Test]
    public async Task TheDefaultJobProgressChangedDoesNothing()
    {
        IJobListener listener = A.Fake<IJobListener>(options => options.CallsBaseMethods());

        Func<Task> hear = async () => await listener.JobProgressChanged(
            A.Fake<IJobExecutionContext>(),
            new FireInstanceProgress { Percent = 50, Message = "halfway" });

        await hear.Should().NotThrowAsync("a listener written before 4.4 compiles unchanged, and the default it gets does nothing");
    }

    private async Task Run(Store store, Action<IScheduler> register, RecordingListener waitFor)
    {
        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "progress-listener-" + Guid.NewGuid().ToString("N");
                options.InstanceId = "one";
            });

            if (store == Store.Sqlite)
            {
                database = new SqliteTestDatabase("progress-listener");
                string connectionString = database.ConnectionString;
                q.UsePersistentStore(persistent =>
                {
                    persistent.UseSqlite(SqliteFactory.Instance, connectionString);
                    persistent.ProvisionSchema();
                });
            }
            else
            {
                q.UseInMemoryStore();
            }
        });

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            register(scheduler);

            JobKey key = new("scripted", "progress");
            await scheduler.AddJob(JobBuilder.Create<ScriptedJob>().WithIdentity(key).StoreDurably().Build());
            await scheduler.Start();
            await scheduler.TriggerJob(key);

            await waitFor.Executed.Task.WaitAsync(waitLimit);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    public sealed class ScriptedJob : IJob
    {
        internal static Func<IJobExecutionContext, CancellationToken, ValueTask>? Script { get; set; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Script!(context, cancellationToken);
        }
    }

    private sealed class RecordingListener : IJobListener
    {
        private readonly List<string> heard = [];

        public RecordingListener(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public TaskCompletionSource HeardProgress { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Executed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public JobExecutionException? JobException { get; private set; }

        public ExecutionOutcome? Outcome { get; private set; }

        public List<string> Heard
        {
            get
            {
                lock (heard)
                {
                    return [.. heard];
                }
            }
        }

        public ValueTask JobProgressChanged(IJobExecutionContext context, FireInstanceProgress progress, CancellationToken cancellationToken = default)
        {
            lock (heard)
            {
                heard.Add($"{progress.Percent} {progress.Message}");
            }

            HeardProgress.TrySetResult();
            return default;
        }

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            lock (heard)
            {
                heard.Add("executed");
            }

            JobException = jobException;
            Outcome = context.Outcome;
            Executed.TrySetResult();
            return default;
        }
    }

    private sealed class ThrowingListener : IJobListener
    {
        public ValueTask JobProgressChanged(IJobExecutionContext context, FireInstanceProgress progress, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("this listener cannot cope with progress");
        }
    }

    private sealed class CompletionOnlyListener : IJobListener
    {
        public bool Executed { get; private set; }

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            Executed = true;
            return default;
        }
    }
}
