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

using System.Collections.Concurrent;

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Impl;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A job whose code is a delegate: how it is registered, stored and fired, and what it composes with.
/// </summary>
/// <remarks>
/// How each parameter is bound, and which handlers are refused, is <c>DelegateJobBindingTest</c>'s; this
/// is the same handlers run by a scheduler.
/// </remarks>
public sealed class DelegateJobTest
{
    private static readonly TimeSpan deadline = TimeSpan.FromSeconds(30);

    [Test]
    public async Task ScheduleJobRunsTheHandlerWithItsServicesTheFiringAndItsToken()
    {
        Firings firings = new();
        TaskCompletionSource<(IJobExecutionContext Context, bool FiringsToken)> ran = NewSignal<(IJobExecutionContext, bool)>();

        await RunUntil(ran.Task, services => services.AddSingleton(firings), q =>
            q.ScheduleJob("cleanup", (Firings recorder, ILogger<DelegateJobTest> log, IJobExecutionContext context, CancellationToken token) =>
            {
                log.LogInformation("cleaning up");
                recorder.Add(context.JobDetail.Key.Name);
                ran.TrySetResult((context, token == context.CancellationToken));
            }, trigger => trigger.StartNow()));

        (IJobExecutionContext context, bool firingsToken) = await ran.Task;

        firings.Names.Should().Equal(["cleanup"], "the handler was resolved its service and ran once");
        firingsToken.Should().BeTrue("the token parameter is the firing's own");
        context.JobDetail.JobType.Type.Should().Be(typeof(DelegateJob),
            "every delegate job is stored as the one type, which resolves on every node");
        context.JobDetail.Key.Should().Be(new JobKey("cleanup"), "the job takes its trigger's identity, which is the name");
        context.Trigger.Key.Should().Be(new TriggerKey("cleanup"));
        context.JobDetail.Description.Should().Be("Delegate job 'cleanup'",
            "the type names every delegate job alike, so the description is what tells the dashboard which one this is");
        context.JobDetail.Durable.Should().BeFalse("ScheduleJob's job lives as long as its trigger, as with ScheduleJob<T>");
    }

    [Test]
    public async Task AddJobIsDurableAndFiresThroughTriggerJobWithTheDataItIsGiven()
    {
        TaskCompletionSource<IJobExecutionContext> ran = NewSignal<IJobExecutionContext>();

        ServiceCollection services = new();
        services.AddQuartz(q => q.AddJob("report", (IJobExecutionContext context) =>
        {
            ran.TrySetResult(context);
            return Task.CompletedTask;
        }));

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = provider.GetRequiredService<IScheduler>();
        try
        {
            await scheduler.Start();

            IJobDetail? stored = await scheduler.GetJobDetail(new JobKey("report"));
            stored.Should().NotBeNull("AddJob stores the job with no trigger, so it has to be durable");
            stored!.Durable.Should().BeTrue();

            await scheduler.TriggerJob(new JobKey("report"), new JobDataMap { ["month"] = "2026-09" });
            (await WithDeadline(ran.Task)).MergedJobDataMap.GetString("month").Should().Be("2026-09",
                "a one-off is the named delegate job fired with data of its own");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    [Test]
    public async Task AJobGivenAnIdentityOfItsOwnIsFoundUnderIt()
    {
        TaskCompletionSource<IJobExecutionContext> ran = NewSignal<IJobExecutionContext>();

        await RunUntil(ran.Task, configureQuartz: q =>
        {
            q.AddJob("cleanup", (IJobExecutionContext context) => { ran.TrySetResult(context); }, job => job
                .WithIdentity("cleanup", "maintenance")
                .WithDescription("Purges expired sessions"));

            q.AddTrigger(trigger => trigger.ForJob("cleanup", "maintenance").StartNow());
        });

        IJobExecutionContext context = await ran.Task;
        context.JobDetail.Key.Should().Be(new JobKey("cleanup", "maintenance"),
            "the handler is bound under the key the job was built with, not the name it was added under");
        context.JobDetail.Description.Should().Be("Purges expired sessions", "a description the caller gave is kept");
    }

    [Test]
    public async Task ScheduleJobsJobTakesTheIdentityItsTriggerWasGiven()
    {
        TaskCompletionSource<IJobExecutionContext> ran = NewSignal<IJobExecutionContext>();

        await RunUntil(ran.Task, configureQuartz: q => q.ScheduleJob(
            "cleanup",
            (IJobExecutionContext context) => { ran.TrySetResult(context); },
            trigger => trigger.WithIdentity("hourly", "maintenance").StartNow()));

        IJobExecutionContext context = await ran.Task;
        context.JobDetail.Key.Should().Be(new JobKey("hourly", "maintenance"), "as with ScheduleJob<T>, the job takes the trigger's identity");
        context.JobDetail.Description.Should().Be("Delegate job 'hourly'");
    }

    [Test]
    public async Task ScheduleJobRefusesATriggerPointedAtAnotherJob()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.ScheduleJob("cleanup", () => { }, trigger => trigger.ForJob("elsewhere").StartNow()));

        await using ServiceProvider provider = services.BuildServiceProvider();

        Func<Task> act = async () => await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await act.Should().ThrowAsync<InvalidOperationException>(
            "the call adds the job its trigger fires, so a trigger aimed elsewhere would leave the handler unrun")
            .WithMessage("*'DEFAULT.elsewhere'*ForJob*");
    }

    [Test]
    public async Task TheServiceProviderShapesAreHandedTheSchedulersViewOfTheContainer()
    {
        TaskCompletionSource<IJobExecutionContext> ran = NewSignal<IJobExecutionContext>();

        await RunUntil(ran.Task, services => services.AddSingleton(new ReportSettings("0/1 * * * * ?", "Monthly report")), q =>
        {
            q.AddJob("report", (IJobExecutionContext context) => { ran.TrySetResult(context); }, (provider, job) =>
                job.WithDescription(provider.GetRequiredService<ReportSettings>().Description));

            q.ScheduleJob("tick", () => { }, (provider, trigger) =>
                trigger.WithCronSchedule(provider.GetRequiredService<ReportSettings>().Cron));

            q.AddTrigger(trigger => trigger.ForJob("report").StartNow());
        });

        (await ran.Task).JobDetail.Description.Should().Be("Monthly report",
            "the configuration callback was handed the container, which is what the twin exists for");
    }

    [Test]
    public async Task AServiceTheScopeCannotGiveFailsTheFiringAsAJobExecutionException()
    {
        RecordingJobListener listener = new();

        await RunUntil(listener.Executed, configureQuartz: q =>
        {
            q.AddJobListener(listener);
            q.ScheduleJob("needs-a-service", (ReportSettings settings) => { }, trigger => trigger.StartNow());
        });

        JobExecutionException? failure = (await listener.Executed).Exception;
        failure.Should().NotBeNull("a missing service is the job's failure, which the run shell reports like any other");
        Chain(failure!).Should().Contain(exception => exception is InvalidOperationException && exception.Message.Contains(nameof(ReportSettings)),
            "the container's own explanation of what it could not resolve is kept inside the report");
    }

    [Test]
    public async Task AKeyNobodyRegisteredFailsTheFiringNamingTheKeyAndTheScheduler()
    {
        RecordingJobListener listener = new();

        await RunUntil(listener.Executed, configureQuartz: q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "node-without-the-handler");
            q.AddJobListener(listener);

            // A delegate job this node holds, so the registry exists and the missing key is the only thing
            // wrong with the one below.
            q.AddJob("registered", () => { });

            // What another node's registration, an older deployment or the HTTP API leaves in the store.
            q.AddJob(typeof(DelegateJob), job => job.WithIdentity("unregistered", "reports").StoreDurably());
            q.AddTrigger(trigger => trigger.ForJob("unregistered", "reports").StartNow());
        });

        JobExecutionException? failure = (await listener.Executed).Exception;
        failure.Should().NotBeNull("the type resolves on every node, so the missing handler is found where it runs");
        failure!.Message.Should().Contain("'reports.unregistered'").And.Contain("'node-without-the-handler'",
            "the message names what an operator has to register, and where");
    }

    [Test]
    public async Task ANodeWithNoDelegateJobsAtAllStillReportsTheMissingHandler()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns("bare");
        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        A.CallTo(() => context.Scheduler).Returns(scheduler);
        A.CallTo(() => context.JobDetail).Returns(JobBuilder.Create<DelegateJob>().WithIdentity("orphan").Build());

        DelegateJob job = new(new ServiceCollection().BuildServiceProvider());

        Func<Task> act = async () => await job.Execute(context);

        await act.Should().ThrowAsync<JobExecutionException>(
            "a container with no registry at all can still build the job out of the store, and says what is missing")
            .WithMessage("*'DEFAULT.orphan'*'bare'*");
    }

    [Test]
    public async Task NamedSchedulersKeepTheirOwnHandlersForOneKey()
    {
        TaskCompletionSource<string> alpha = NewSignal<string>();
        TaskCompletionSource<string> beta = NewSignal<string>();

        ServiceCollection services = new();
        services.AddQuartz("alpha", q => q.ScheduleJob("sync", (IJobExecutionContext context) => { alpha.TrySetResult("alpha:" + context.Scheduler.SchedulerName); }, trigger => trigger.StartNow()));
        services.AddQuartz("beta", q => q.ScheduleJob("sync", (IJobExecutionContext context) => { beta.TrySetResult("beta:" + context.Scheduler.SchedulerName); }, trigger => trigger.StartNow()));

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler first = await provider.GetRequiredKeyedService<ISchedulerFactory>("alpha").GetScheduler();
        IScheduler second = await provider.GetRequiredKeyedService<ISchedulerFactory>("beta").GetScheduler();
        try
        {
            await first.Start();
            await second.Start();

            (await WithDeadline(alpha.Task)).Should().Be("alpha:alpha");
            (await WithDeadline(beta.Task)).Should().Be("beta:beta",
                "the same key on two schedulers is two jobs, each running the handler its own scheduler registered");
        }
        finally
        {
            await first.Shutdown(waitForJobsToComplete: true);
            await second.Shutdown(waitForJobsToComplete: true);
        }
    }

    [Test]
    public async Task TheDefaultSchedulerFindsItsHandlersUnderTheNameItRunsAs()
    {
        TaskCompletionSource<string> ran = NewSignal<string>();

        await RunUntil(ran.Task, configureQuartz: q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "renamed");
            q.ScheduleJob("ping", (IJobExecutionContext context) => { ran.TrySetResult(context.Scheduler.SchedulerName); }, trigger => trigger.StartNow());
        });

        (await ran.Task).Should().Be("renamed",
            "the default scheduler is registered under no name but runs under its InstanceName, which is what a firing reads");
    }

    [Test]
    public async Task TwoHandlersUnderOneKeyOnOneSchedulerAreRefused()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.AddJob("cleanup", () => { });
            q.AddJob("cleanup", () => { });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        Func<Task> act = async () => await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await act.Should().ThrowAsync<InvalidOperationException>("a firing could not say which of the two handlers it meant")
            .WithMessage("*'DEFAULT.cleanup'*");
    }

    [Test]
    public void AHandlerThatCouldNeverRunIsRefusedWhereItIsRegistered()
    {
        ServiceCollection services = new();

        Action act = () => services.AddQuartz(q => q.ScheduleJob("answer", () => Task.FromResult(42), trigger => trigger.StartNow()));

        act.Should().Throw<ArgumentException>("the shape is wrong before anything has run, so it is reported at the call that wrote it")
            .WithParameterName("handler")
            .WithMessage("*Task<Int32>*");
    }

    [Test]
    public void ANameIsRequired()
    {
        ServiceCollection services = new();

        Action addJob = () => services.AddQuartz(q => q.AddJob(" ", () => { }));
        Action scheduleJob = () => services.AddQuartz(q => q.ScheduleJob("", () => { }, trigger => trigger.StartNow()));

        addJob.Should().Throw<ArgumentException>("the name is the job's key, and a key is what a firing finds its handler by");
        scheduleJob.Should().Throw<ArgumentException>();
    }

    [Test]
    public async Task AHandlerTakingASchedulersOwnPartIsRefusedAtStartup()
    {
        ServiceCollection services = new();
        services.AddQuartz("reports", q => q.ScheduleJob("rebuild", (ISchedulerFactory factory) => { }, trigger => trigger.StartNow()));

        await using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>().Get("reports");

        act.Should().Throw<OptionsValidationException>(
            "the handler's services come from the scope the container built the job in, where a scheduler's own "
            + "parts are the default scheduler's - the rule a registered job's constructor is held to")
            .WithMessage("*Delegate job 'rebuild'*scheduler 'reports'*ISchedulerFactory factory*context.Scheduler*");
    }

    [Test]
    public async Task ADelegateJobWithoutTheAttributeTakesTheSchedulersDefaultTimeout()
    {
        RecordingJobListener listener = new();
        TaskCompletionSource<bool> cancelled = NewSignal<bool>();

        await RunUntil(listener.Executed, configureQuartz: q =>
        {
            q.AddJobTimeout(TimeSpan.FromMilliseconds(200));
            q.AddJobListener(listener);
            q.ScheduleJob("slow", async (CancellationToken token) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                finally
                {
                    cancelled.TrySetResult(token.IsCancellationRequested);
                }
            }, trigger => trigger.StartNow());
        });

        (await WithDeadline(cancelled.Task)).Should().BeTrue("the budget is spent by cancelling the token the handler was handed");
        JobExecutionException? failure = (await listener.Executed).Exception;
        failure.Should().NotBeNull("DelegateJob carries no [JobTimeout], so the scheduler-wide default is what bounds it");
        failure!.Message.Should().Contain("timed out");
    }

    [Test]
    public async Task ARetryPolicyOnTheTriggerRetriesAFailedHandler()
    {
        int attempts = 0;
        TaskCompletionSource<int> succeeded = NewSignal<int>();

        await RunUntil(succeeded.Task, configureQuartz: q => q.ScheduleJob("flaky", () =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("the first attempt fails");
            }

            succeeded.TrySetResult(attempts);
        }, trigger => trigger.StartNow().WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMilliseconds(100)))));

        (await succeeded.Task).Should().Be(2, "the trigger's retry policy treats a handler's failure as any job's");
    }

    [TestCase("ram")]
    [TestCase("sqlite")]
    public async Task ADelegateJobFlaggedNonConcurrentNeverOverlapsItself(string storeKind)
    {
        using SqliteTestDatabase database = new("delegate-non-concurrent");
        ConcurrencyProbe probe = new(expectedRuns: 2);

        await RunUntil(probe.Finished, configureQuartz: q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = $"delegate-non-concurrent-{storeKind}";
                options.IdleWaitTime = TimeSpan.FromSeconds(1);

                // A batch wide enough to take both triggers at once, so only the job's own flag keeps them apart.
                options.MaxBatchSize = 5;
                options.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.FromSeconds(1);
            });

            if (storeKind == "sqlite")
            {
                q.UsePersistentStore(store =>
                {
                    store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                    store.ProvisionSchema();
                });
            }

            q.AddJob("exclusive", probe.Run, job => job.DisallowConcurrentExecution());

            DateTimeOffset together = DateTimeOffset.UtcNow.AddSeconds(1);
            foreach (string trigger in (string[]) ["first", "second"])
            {
                q.AddTrigger(t => t.WithIdentity(trigger).ForJob("exclusive").StartAt(together));
            }
        });

        probe.Runs.Should().Be(2, "both triggers fire; the flag only keeps them from running at the same time");
        probe.MostAtOnce.Should().Be(1,
            "DelegateJob is one type for every delegate job and carries no attribute, so the builder's flag is the "
            + "only thing that can say this job must not overlap itself");
    }

    [Test]
    public async Task ATenantsDelegateJobRunsBesideTheApplicationsOwn()
    {
        TaskCompletionSource<string> application = NewSignal<string>();
        TaskCompletionSource<string> tenant = NewSignal<string>();

        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton(new ReportSettings("unused", "the application's"));
        services.AddQuartz("main", q => q.ScheduleJob("ping", () => { application.TrySetResult("main"); }, trigger => trigger.StartNow()));

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler main = await provider.GetRequiredKeyedService<ISchedulerFactory>("main").GetScheduler();
        try
        {
            await main.Start();
            (await WithDeadline(application.Task)).Should().Be("main");

            await provider.GetRequiredService<ISchedulerRuntime>().Add("acme", q => q.ScheduleJob(
                "ping",
                (ReportSettings settings, IJobExecutionContext context) => { tenant.TrySetResult($"{context.Scheduler.SchedulerName}:{settings.Description}"); },
                trigger => trigger.StartNow()));

            (await WithDeadline(tenant.Task)).Should().Be("acme:the application's",
                "the tenant finds its own handler, and its services from the application the way a tenant's class job does");
        }
        finally
        {
            await provider.GetRequiredService<ISchedulerRuntime>().Remove("acme", waitForJobsToComplete: true);
            await main.Shutdown(waitForJobsToComplete: true);
        }
    }

    [Test]
    public async Task TheStandaloneBuilderRunsADelegateJob()
    {
        TaskCompletionSource<bool> ran = NewSignal<bool>();

        IScheduler scheduler = await QuartzSchedulerBuilder.Create(q =>
                q.ScheduleJob("standalone", () => { ran.TrySetResult(true); }, trigger => trigger.StartNow()))
            .BuildScheduler();
        try
        {
            await scheduler.Start();
            (await WithDeadline(ran.Task)).Should().BeTrue("the standalone builder is the same builder, delegate jobs included");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    private static async Task RunUntil(
        Task signal,
        Action<IServiceCollection>? configureServices = null,
        Action<IQuartzBuilder>? configureQuartz = null)
    {
        ServiceCollection services = new();
        configureServices?.Invoke(services);
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.IdleWaitTime = TimeSpan.FromSeconds(1));
            configureQuartz?.Invoke(q);
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = provider.GetRequiredService<IScheduler>();
        try
        {
            await scheduler.Start();
            await WithDeadline(signal);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    private static async Task WithDeadline(Task task)
    {
        Task finished = await Task.WhenAny(task, Task.Delay(deadline));
        finished.Should().BeSameAs(task, "the scheduler should have fired the job well within the deadline");
        await task;
    }

    private static async Task<T> WithDeadline<T>(Task<T> task)
    {
        await WithDeadline((Task) task);
        return await task;
    }

    private static TaskCompletionSource<T> NewSignal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    public sealed record ReportSettings(string Cron, string Description);

    public sealed class Firings
    {
        private readonly ConcurrentQueue<string> names = new();

        public List<string> Names => [.. names];

        public void Add(string name) => names.Enqueue(name);
    }

    /// <summary>
    /// Counts how many firings of one job are running at once.
    /// </summary>
    private sealed class ConcurrencyProbe
    {
        private readonly int expectedRuns;
        private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int running;
        private int mostAtOnce;
        private int runs;

        public ConcurrencyProbe(int expectedRuns)
        {
            this.expectedRuns = expectedRuns;
        }

        public Task Finished => finished.Task;

        public int MostAtOnce => Volatile.Read(ref mostAtOnce);

        public int Runs => Volatile.Read(ref runs);

        public async Task Run(CancellationToken cancellationToken)
        {
            int now = Interlocked.Increment(ref running);
            InterlockedMax(ref mostAtOnce, now);
            try
            {
                // Long enough that a second firing let through alongside this one would overlap it.
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref running);
                if (Interlocked.Increment(ref runs) == expectedRuns)
                {
                    finished.TrySetResult();
                }
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current = Volatile.Read(ref target);
            while (value > current)
            {
                int seen = Interlocked.CompareExchange(ref target, value, current);
                if (seen == current)
                {
                    return;
                }

                current = seen;
            }
        }
    }

    private sealed class RecordingJobListener : IJobListener
    {
        private readonly TaskCompletionSource<(IJobExecutionContext Context, JobExecutionException? Exception)> executed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "delegate-job-recorder";

        public Task<(IJobExecutionContext Context, JobExecutionException? Exception)> Executed => executed.Task;

        public ValueTask JobWasExecuted(
            IJobExecutionContext context,
            JobExecutionException? jobException,
            CancellationToken cancellationToken = default)
        {
            executed.TrySetResult((context, jobException));
            return default;
        }
    }
}
