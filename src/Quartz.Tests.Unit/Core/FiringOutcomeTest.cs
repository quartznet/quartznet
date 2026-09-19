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

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// How the run shell classifies a firing, one test per way a firing can end.
/// </summary>
/// <remarks>
/// <para>
/// The outcome is what settles the continuations waiting on a trigger, so getting it wrong is a
/// workflow that runs the wrong branch — and it is not otherwise observable: before continuations
/// there was nothing in the store that could tell a cancelled firing from a successful one.
/// </para>
/// <para>
/// Asserted at the store, because the store is what receives it. Every test here waits on a recorded
/// completion rather than on a length of time.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class FiringOutcomeTest
{
    private const string Group = "firing-outcome";

    /// <summary>
    /// How long a test is willing to wait for the firing to reach the store. Long enough that a loaded
    /// build agent never trips it, and never used as a measurement.
    /// </summary>
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    [Test]
    public async Task AJobThatReturnsSucceeded()
    {
        CompletedFiring completion = await RunOnce<QuietJob>("succeeded");

        completion.Outcome.Should().Be(ExecutionOutcome.Succeeded,
            "the job ran to completion and threw nothing, which is the only thing success can mean");
    }

    [Test]
    public async Task AJobThatThrowsAJobExecutionExceptionFailed()
    {
        CompletedFiring completion = await RunOnce<JobExecutionExceptionJob>("job-exception");

        completion.Outcome.Should().Be(ExecutionOutcome.Failed);
    }

    [Test]
    public async Task AJobThatThrowsAnythingElseFailedToo()
    {
        CompletedFiring completion = await RunOnce<UnhandledExceptionJob>("unhandled");

        completion.Outcome.Should().Be(ExecutionOutcome.Failed,
            "the run shell wraps whatever a job threw, and the trigger is told the same thing either way");
    }

    /// <summary>
    /// A failure the trigger answers with another attempt is still a failure. What says the
    /// occurrence is not finished is the instruction, not the outcome.
    /// </summary>
    [Test]
    public async Task AFailureTheTriggerRetriesStillFailed()
    {
        CompletedFiring completion = await RunOnce<UnhandledExceptionJob>(
            "retrying",
            // Well inside the hour between occurrences: a retry that would land at the next scheduled
            // fire time is dropped and the schedule wins, which is a different branch entirely.
            configureTrigger: builder => builder.WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(1))));

        completion.Outcome.Should().Be(ExecutionOutcome.Failed,
            "the job ran and it threw, which is what the outcome reports — anything else would tell a "
            + "store, or an execution-history listener, that the firing did not happen");

        completion.Instruction.Should().Be(SchedulerInstruction.RetryTrigger,
            "the trigger has attempts left, so it asked for one — and that, not the outcome, is what "
            + "tells a store the occurrence is not finished and its continuations are not to be settled");
    }

    [Test]
    public async Task AnInterruptedJobWasCancelled()
    {
        CompletedFiring completion = await RunOnce<SelfInterruptingJob>("cancelled");

        completion.Outcome.Should().Be(ExecutionOutcome.Cancelled,
            "the firing's token was signalled and the job stopped rather than finished — which, before this, "
            + "was indistinguishable from a job that returned");
    }

    [Test]
    public async Task AVetoedFiringWasVetoed()
    {
        CompletedFiring completion = await RunOnce<QuietJob>(
            "vetoed",
            configureScheduler: (scheduler, key) => scheduler.ListenerManager.AddTriggerListener(new VetoingListener()));

        completion.Outcome.Should().Be(ExecutionOutcome.Vetoed,
            "the job never ran, and a continuation waiting on a veto is waiting on exactly this");
    }

    /// <summary>
    /// A listener that throws before the job runs abandons the firing. The occurrence did not happen,
    /// so it settles nothing.
    /// </summary>
    [Test]
    public async Task AFiringAListenerAbandonedDidNotExecute()
    {
        CompletedFiring completion = await RunOnce<QuietJob>(
            "abandoned",
            configureScheduler: (scheduler, key) => scheduler.ListenerManager.AddTriggerListener(new ThrowingListener()));

        completion.Instruction.Should().Be(SchedulerInstruction.NoInstruction);
        completion.Outcome.Should().Be(ExecutionOutcome.NotExecuted,
            "nothing ran, so nothing can be concluded from it — a trigger waiting on this one keeps waiting "
            + "for a firing that does happen");
    }

    /// <summary>
    /// The same, one step earlier: the job could not be built at all.
    /// </summary>
    [Test]
    public async Task AJobThatCouldNotBeInstantiatedDidNotExecute()
    {
        CompletedFiring completion = await RunOnce<QuietJob>(
            "uninstantiable",
            jobFactory: new ThrowingJobFactory());

        completion.Instruction.Should().Be(SchedulerInstruction.SetAllJobTriggersError,
            "a job that cannot be built is a configuration failure, and the trigger is parked for an operator");
        completion.Outcome.Should().Be(ExecutionOutcome.NotExecuted);
    }

    /// <summary>
    /// Runs one firing of <typeparamref name="TJob" /> and hands back the completion the store was
    /// told about.
    /// </summary>
    private static async Task<CompletedFiring> RunOnce<TJob>(
        string name,
        Action<TriggerBuilder<IJob>> configureTrigger = null,
        Action<IScheduler, JobKey> configureScheduler = null,
        IJobFactory jobFactory = null) where TJob : IJob
    {
        CompletionWatchingJobStore store = null;
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q =>
            {
                q.ConfigureScheduler(options => options.InstanceName = "firing-outcome-" + name);
                q.UseJobStore(provider =>
                {
                    store = new CompletionWatchingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
                    return store;
                });

                if (jobFactory is not null)
                {
                    q.UseJobFactory(jobFactory);
                }
            })
            .BuildScheduler();

        try
        {
            IJobDetail job = JobBuilder.Create<TJob>()
                .WithIdentity(name, Group)
                .Build();

            // Repeating, so that the firing is settled by how it ended rather than by the trigger
            // running out of occurrences — a one-shot trigger is deleted whatever happened to it.
            TriggerBuilder<IJob> builder = TriggerBuilder.Create()
                .WithIdentity(name, Group)
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever());

            configureTrigger?.Invoke(builder);

            await scheduler.ScheduleJob(job, builder.Build());
            configureScheduler?.Invoke(scheduler, job.Key);

            await scheduler.Start();

            // The store records after it has acted, so once this returns the firing is genuinely over.
            await store.Completions.Reaches(1).WaitAsync(observationDeadline);

            return store.Completions.Entries.Should().ContainSingle().Subject;
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    public sealed class QuietJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class JobExecutionExceptionJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new JobExecutionException("the job refused");
        }
    }

    public sealed class UnhandledExceptionJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("something the job did not expect");
        }
    }

    /// <summary>
    /// Interrupts its own firing and then waits on the token, which is the shape of every job that is
    /// stopped rather than finished.
    /// </summary>
    public sealed class SelfInterruptingJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            await context.Scheduler.InterruptFireInstance(context.FireInstanceId, CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class VetoingListener : ITriggerListener
    {
        public string Name => "vetoing";

        public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(true);
        }
    }

    /// <summary>
    /// Throws where the run shell asks whether the firing may go ahead, which abandons it before the
    /// job is reached.
    /// </summary>
    private sealed class ThrowingListener : ITriggerListener
    {
        public string Name => "throwing";

        public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new SchedulerException("the listener refused to let the firing start");
        }
    }

    private sealed class ThrowingJobFactory : IJobFactory
    {
        public ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Unable to resolve service for type 'ISomething'");
        }

        public ValueTask ReturnJob(JobScope jobScope, CancellationToken cancellationToken = default) => default;
    }
}
