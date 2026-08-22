using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;

using Quartz.Listeners;
using Quartz.Plugins.Interrupt;
using Quartz.Util;

namespace Quartz.Tests.Unit.Plugin.Interrupt;

/// <summary>
/// Regression tests for https://github.com/quartznet/quartznet/issues/3248 - the interrupt monitor
/// must only ever cancel the execution it was created for.
/// </summary>
[NonParallelizable]
public class JobInterruptMonitorPluginTest
{
    private static readonly TimeSpan waitTimeout = TimeSpan.FromSeconds(10);

    private static readonly ConcurrentDictionary<string, bool> interruptedByTrigger = new();
    private static SemaphoreSlim started;
    private static SemaphoreSlim done;
    private static SemaphoreSlim gate;
    private static int executionCount;

    [SetUp]
    public void SetUp()
    {
        interruptedByTrigger.Clear();
        started = new SemaphoreSlim(0);
        done = new SemaphoreSlim(0);
        gate = new SemaphoreSlim(0);
        executionCount = 0;
    }

    [TearDown]
    public void TearDown()
    {
        started.Dispose();
        done.Dispose();
        gate.Dispose();
    }

    [Test]
    public async Task ShouldOnlyInterruptTheExecutionThatExceededItsMaxRunTime()
    {
        IScheduler scheduler = await CreateScheduler("PerInstance", defaultMaxRunTimeMilliseconds: 60_000);
        try
        {
            IJobDetail job = CreateAutoInterruptableJob();

            ITrigger shortTrigger = TriggerBuilder.Create()
                .WithIdentity("t-short")
                .ForJob(job)
                .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, "500")
                .StartNow()
                .Build();

            ITrigger longTrigger = TriggerBuilder.Create()
                .WithIdentity("t-long")
                .ForJob(job)
                .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, "60000")
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, new[] { shortTrigger, longTrigger });

            (await started.WaitAsync(waitTimeout)).Should().BeTrue("first execution should start");
            (await started.WaitAsync(waitTimeout)).Should().BeTrue("second execution should start");

            (await done.WaitAsync(waitTimeout)).Should().BeTrue("the short-limit execution should be interrupted by its monitor");
            interruptedByTrigger.Should().ContainKey("t-short",
                "the execution that exceeded its max run time is the one that should have been interrupted");
            interruptedByTrigger["t-short"].Should().BeTrue("t-short exceeded its 500 ms max run time");
            interruptedByTrigger.Should().NotContainKey("t-long",
                "t-short's monitor must not cancel a concurrent execution that has not exceeded its own limit");

            gate.Release();

            (await done.WaitAsync(waitTimeout)).Should().BeTrue("the long-limit execution should complete once the gate opens");
            interruptedByTrigger["t-long"].Should().BeFalse("t-long had a 60 second limit and must complete uninterrupted");
        }
        finally
        {
            gate.Release(10); // unblock any execution still gated so shutdown does not wait on it
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    [Test]
    public async Task ShouldCancelMonitorOfVetoedExecution()
    {
        IScheduler scheduler = await CreateScheduler("Veto", defaultMaxRunTimeMilliseconds: 500);
        try
        {
            VetoingTriggerListener vetoListener = new VetoingTriggerListener("t-veto");
            scheduler.ListenerManager.AddTriggerListener(vetoListener);

            IJobDetail job = CreateAutoInterruptableJob();

            // vetoed fire uses the plugin default of 500 ms; the healthy fire allows 60 seconds
            ITrigger vetoedTrigger = TriggerBuilder.Create()
                .WithIdentity("t-veto")
                .ForJob(job)
                .StartNow()
                .Build();

            ITrigger healthyTrigger = TriggerBuilder.Create()
                .WithIdentity("t-run")
                .ForJob(job)
                .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, "60000")
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, new[] { vetoedTrigger, healthyTrigger });

            Task vetoTask = vetoListener.Vetoed.Task;
            (await Task.WhenAny(vetoTask, Task.Delay(waitTimeout))).Should().Be(vetoTask, "the t-veto fire should be vetoed");
            (await started.WaitAsync(waitTimeout)).Should().BeTrue("the healthy execution should start");

            // keep the healthy execution running well past the vetoed fire's would-be 500 ms deadline;
            // before the fix the vetoed fire's leaked monitor would cancel it here
            await Task.Delay(2000);

            gate.Release();

            (await done.WaitAsync(waitTimeout)).Should().BeTrue("the healthy execution should complete once the gate opens");
            executionCount.Should().Be(1, "the vetoed fire must never execute");
            interruptedByTrigger["t-run"].Should().BeFalse("a vetoed fire's monitor must not interrupt a later healthy execution");
        }
        finally
        {
            gate.Release(10); // unblock any execution still gated so shutdown does not wait on it
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    [TestCase(500)]
    [TestCase(500L)]
    [TestCase("500")]
    public async Task ShouldHonourMaxRunTimeStoredAsNumberOrString(object maxRunTime)
    {
        // the plugin default is deliberately long: only a MaxRunTime the plugin actually read can interrupt in time
        IScheduler scheduler = await CreateScheduler("MaxRunTimeAs" + maxRunTime.GetType().Name, defaultMaxRunTimeMilliseconds: 60_000);
        try
        {
            IJobDetail job = CreateAutoInterruptableJob();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("t-typed")
                .ForJob(job)
                .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, maxRunTime)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);

            (await started.WaitAsync(waitTimeout)).Should().BeTrue("the execution should start");
            (await done.WaitAsync(waitTimeout)).Should().BeTrue(
                $"a MaxRunTime of 500 ms stored as {maxRunTime.GetType().Name} should override the plugin's 60 second default");

            interruptedByTrigger["t-typed"].Should().BeTrue(
                $"the execution exceeded the 500 ms it was allowed by a {maxRunTime.GetType().Name} MaxRunTime");
        }
        finally
        {
            gate.Release(10); // unblock any execution still gated so shutdown does not wait on it
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    [Test]
    public async Task ShouldUsePluginDefaultWhenMaxRunTimeIsAbsent()
    {
        IScheduler scheduler = await CreateScheduler("NoMaxRunTime", defaultMaxRunTimeMilliseconds: 500);
        try
        {
            IJobDetail job = CreateAutoInterruptableJob();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("t-default")
                .ForJob(job)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);

            (await started.WaitAsync(waitTimeout)).Should().BeTrue("the execution should start");
            (await done.WaitAsync(waitTimeout)).Should().BeTrue("the plugin's 500 ms default should interrupt a fire that carries no MaxRunTime");

            interruptedByTrigger["t-default"].Should().BeTrue("a fire with no MaxRunTime of its own is bounded by the plugin default");
        }
        finally
        {
            gate.Release(10); // unblock any execution still gated so shutdown does not wait on it
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    private static async Task<IScheduler> CreateScheduler(string name, int defaultMaxRunTimeMilliseconds)
    {
        NameValueCollection config = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "JobInterruptMonitorPluginTest_" + name,
            ["quartz.scheduler.instanceId"] = "AUTO",
            ["quartz.threadPool.threadCount"] = "3",
            ["quartz.plugin.jobInterruptor.type"] = typeof(JobInterruptMonitorPlugin).AssemblyQualifiedNameWithoutVersion(),
            ["quartz.plugin.jobInterruptor.defaultMaxRunTime"] = defaultMaxRunTimeMilliseconds.ToString(CultureInfo.InvariantCulture)
        };

        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseProperties(config).BuildScheduler();
        await scheduler.Start();
        return scheduler;
    }

    private static IJobDetail CreateAutoInterruptableJob()
    {
        JobDataMap jobDataMap = new JobDataMap();
        jobDataMap.PutAsString(JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable, true);

        return JobBuilder.Create<GatedJob>()
            .WithIdentity("gated-job")
            .UsingJobData(jobDataMap)
            .Build();
    }

    private sealed class GatedJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref executionCount);
            bool wasInterrupted = false;
            started.Release();

            try
            {
                await gate.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                wasInterrupted = true;
            }

            interruptedByTrigger[context.Trigger.Key.Name] = wasInterrupted;
            done.Release();
        }
    }

    private sealed class VetoingTriggerListener : ITriggerListener
    {
        private readonly string triggerNameToVeto;

        public VetoingTriggerListener(string triggerNameToVeto)
        {
            this.triggerNameToVeto = triggerNameToVeto;
        }

        public TaskCompletionSource<bool> Vetoed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "veto-listener";

        public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (trigger.Key.Name == triggerNameToVeto)
            {
                Vetoed.TrySetResult(true);
                return new ValueTask<bool>(true);
            }

            return new ValueTask<bool>(false);
        }
    }
}
