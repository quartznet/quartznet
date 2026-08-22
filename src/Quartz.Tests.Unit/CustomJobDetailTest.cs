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

using System.Collections.Specialized;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit;

/// <summary>
/// An <see cref="IJobDetail" /> of somebody else's making, from end to end (#1143).
/// </summary>
/// <remarks>
/// <para>
/// The interface used to declare <c>GetJobBuilder()</c>, which nobody outside Quartz could implement:
/// <see cref="JobBuilder{TJob}" /> is sealed and builds Quartz's own detail, so an implementation had
/// to hand back a builder that produces somebody else's type. It was not a cosmetic problem —
/// <see cref="RAMJobStore" /> called it to re-store the data of a
/// <see cref="PersistJobDataAfterExecutionAttribute" /> job, so the first completion of such a job
/// silently swapped the caller's detail for Quartz's.
/// </para>
/// <para>
/// The member the store needs is <see cref="IJobDetail.WithJobData" />, and the builder accessor is an
/// extension over the interface's public state. These tests hold both ends of that: a detail of our own
/// survives a firing in the in-memory store, and the extension describes it without needing to know
/// what it is.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public class CustomJobDetailTest
{
    private static TaskCompletionSource<SecondFiring> secondFiring = null!;

    [SetUp]
    public void SetUp()
    {
        secondFiring = new TaskCompletionSource<SecondFiring>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// What the job saw the second time it ran, which is what the store held after the first firing
    /// completed: the detail's type, and the data the first firing left in it.
    /// </summary>
    private sealed record SecondFiring(Type DetailType, string Tenant, int RunsSeen);

    [Test]
    public async Task RamStoreKeepsTheDetailsTypeWhenItRestoresTheDataOfAFinishedJob()
    {
        RAMJobStore store = TestJobStores.Ram();
        TenantJobDetail job = new(new JobKey("job", "custom"), new JobType(typeof(CountingJob)), "acme");
        IOperableTrigger trigger = CreateTrigger("trigger", job.Key);

        await store.ScheduleJob(job, trigger);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = 1,
        });
        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);

        IJobDetail firing = fired.Should().ContainSingle().Which.TriggerFiredBundle!.JobDetail;
        firing.Should().BeOfType<TenantJobDetail>("the store hands the scheduler the detail it was given");

        // What the job would have done: leave something behind in its own data map.
        firing.JobDataMap["runs"] = 1;

        await store.TriggeredJobComplete(acquired[0], firing, SchedulerInstruction.NoInstruction);

        IJobDetail stored = (await store.GetJob(job.Key))!;
        stored.Should().BeOfType<TenantJobDetail>(
            "re-storing job data must ask the detail for a copy of itself rather than rebuild one");
        stored.As<TenantJobDetail>().Tenant.Should().Be("acme", "state only this implementation has survives with it");
        stored.JobDataMap.GetInt("runs").Should().Be(1, "the point of the copy is that it carries the new data");
        stored.Should().NotBeSameAs(firing, "the store hands out copies, never the instance it holds");
    }

    /// <summary>
    /// The same thing through a running scheduler rather than through the store's own contract. The
    /// second firing is what proves the first one landed: its detail is a copy of what the store holds
    /// after completing the first.
    /// </summary>
    [Test]
    public async Task ASchedulerRunsAndReStoresADetailOfOurOwn()
    {
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.instanceName"] = nameof(ASchedulerRunsAndReStoresADetailOfOurOwn),
            ["quartz.scheduler.idleWaitTime"] = "1000",
            ["quartz.threadPool.threadCount"] = "2",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
        };

        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseProperties(properties).BuildScheduler();
        try
        {
            TenantJobDetail job = new(new JobKey("job", "custom"), new JobType(typeof(CountingJob)), "acme");
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger", "custom")
                .ForJob(job.Key)
                .StartNow()
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMilliseconds(200)).RepeatForever())
                .Build();

            await scheduler.ScheduleJob(job, trigger);
            await scheduler.Start();

            SecondFiring observed = await secondFiring.Task.WaitAsync(TimeSpan.FromSeconds(30));

            observed.DetailType.Should().Be<TenantJobDetail>(
                "the scheduler fires with a copy of what the store holds, and the first completion must not have replaced it");
            observed.Tenant.Should().Be("acme");
            observed.RunsSeen.Should().Be(1, "the data the first firing left behind is what the second one starts from");

            IJobDetail stored = (await scheduler.GetJobDetail(job.Key))!;
            stored.Should().BeOfType<TenantJobDetail>("what the scheduler hands back is what the store holds");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    [Test]
    public void GetJobBuilderDescribesADetailItKnowsNothingAbout()
    {
        JobDataMap data = new() { ["greeting"] = "hello" };
        TenantJobDetail job = new(new JobKey("job", "custom"), new JobType(typeof(CountingJob)), "acme", data);

        IJobDetail rebuilt = job.GetJobBuilder().Build();

        rebuilt.Key.Should().Be(job.Key);
        rebuilt.Description.Should().Be(job.Description);
        rebuilt.JobType.Should().Be(job.JobType);
        rebuilt.Durable.Should().Be(job.Durable);
        rebuilt.RequestsRecovery.Should().Be(job.RequestsRecovery);
        rebuilt.ConcurrentExecutionDisallowed.Should().Be(job.ConcurrentExecutionDisallowed);
        rebuilt.PersistJobDataAfterExecution.Should().Be(job.PersistJobDataAfterExecution);
        rebuilt.JobDataMap.GetString("greeting").Should().Be("hello");

        rebuilt.Should().NotBeOfType<TenantJobDetail>(
            "a builder describes a detail, it does not preserve one - WithJobData and Clone are what keep the type");
    }

    /// <summary>
    /// A job store loads details without resolving their job type, so the accessor must not force the
    /// stored name to resolve either. It reads <see cref="IJobDetail.JobType" /> and hands it on as it
    /// is, which is also how the stored spelling survives being read.
    /// </summary>
    [Test]
    public void GetJobBuilderDoesNotResolveTheJobTypeName()
    {
        TenantJobDetail job = new(new JobKey("job", "custom"), new JobType("Library.UnknownType"), "acme");

        IJobDetail rebuilt = job.GetJobBuilder().Build();

        rebuilt.JobType.FullName.Should().Be("Library.UnknownType");
        rebuilt.JobType.TryResolve(out _).Should().BeFalse("nothing along the way may have settled the type");
        rebuilt.Key.Should().Be(job.Key);
    }

    private static IOperableTrigger CreateTrigger(string name, JobKey jobKey)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "custom")
            .ForJob(jobKey)
            .StartAt(DateTimeOffset.UtcNow)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }

    /// <summary>
    /// Counts its firings in its own job data, and reports the second one - by then the store has
    /// completed the first, so the detail the job is handed says what survived that.
    /// </summary>
    public sealed class CountingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            JobDataMap data = context.JobDetail.JobDataMap;
            int runsSeen = data.ContainsKey("runs") ? data.GetInt("runs") : 0;
            data["runs"] = runsSeen + 1;

            if (runsSeen == 1)
            {
                secondFiring.TrySetResult(new SecondFiring(
                    context.JobDetail.GetType(),
                    (context.JobDetail as TenantJobDetail)?.Tenant ?? "",
                    runsSeen));
            }

            return default;
        }
    }

    /// <summary>
    /// An <see cref="IJobDetail" /> written the way an application would write one: immutable, carrying
    /// a field of its own that Quartz knows nothing about, and copying itself for the two members that
    /// hand a detail back.
    /// </summary>
    private sealed class TenantJobDetail : IJobDetail
    {
        public TenantJobDetail(JobKey key, JobType jobType, string tenant, JobDataMap jobDataMap = null)
        {
            Key = key;
            JobType = jobType;
            Tenant = tenant;
            JobDataMap = jobDataMap ?? new JobDataMap();
        }

        /// <summary>The reason for having an implementation of one's own at all.</summary>
        public string Tenant { get; }

        public JobKey Key { get; }

        public string Description => $"jobs for {Tenant}";

        public JobType JobType { get; }

        public JobDataMap JobDataMap { get; }

        public bool Durable => true;

        public bool PersistJobDataAfterExecution => true;

        public bool ConcurrentExecutionDisallowed => true;

        public bool RequestsRecovery => false;

        public IJobDetail WithJobData(JobDataMap jobDataMap) => new TenantJobDetail(Key, JobType, Tenant, jobDataMap);

        // JobDataMap's copy constructor is the public way to copy one; the map itself is mutable, so a
        // clone that shared it would not be one.
        public IJobDetail Clone() => new TenantJobDetail(Key, JobType, Tenant, new JobDataMap(JobDataMap));
    }
}
