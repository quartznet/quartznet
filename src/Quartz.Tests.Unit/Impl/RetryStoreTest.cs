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

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What the in-memory store does with a retry: the round trip from the completion that schedules one
/// to the acquisition that picks it up, which is where the attempt has to survive.
/// </summary>
/// <remarks>
/// Driven against the store directly rather than through a scheduler, so a failure says which of the
/// two halves lost the value.
/// </remarks>
[TestFixture]
public class RetryStoreTest
{
    private RAMJobStore store;
    private IJobExecutionContext context;

    [SetUp]
    public void SetUp()
    {
        store = TestJobStores.Ram();
        context = A.Fake<IJobExecutionContext>();
    }

    private static JobExecutionException Failure() => new JobExecutionException(new InvalidOperationException("boom"));

    private static IJobDetail Job() => JobBuilder.Create<RetryStoreJob>().WithIdentity("job", "jobs").Build();

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    private sealed class RetryStoreJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private static IOperableTrigger Trigger(RetryPolicy policy)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger", "retries")
            .ForJob("job", "jobs")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .StartAt(DateTimeOffset.UtcNow.AddMilliseconds(-10))
            .WithRetryPolicy(policy)
            .Build();

        // The scheduler does this when it schedules a job; a test driving the store does it itself, or
        // the trigger has no fire time and nothing acquires it.
        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    private async Task<IOperableTrigger> Fire()
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddHours(2),
            MaxCount = 1,
        });

        acquired.Should().ContainSingle("the trigger under test is the only one due");

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);
        results.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();

        return acquired[0];
    }

    [Test]
    public async Task TheAttemptSurvivesTheCompletionAndComesBackOnTheNextAcquisition()
    {
        IJobDetail job = Job();
        IOperableTrigger trigger = Trigger(RetryPolicy.Fixed(3, TimeSpan.FromMilliseconds(1)));
        await store.ScheduleJob(job, trigger);

        IOperableTrigger firing = await Fire();
        firing.RetryAttempt.Should().Be(0, "the first firing is the scheduled occurrence");

        SchedulerInstruction instruction = firing.ExecutionComplete(context, Failure());
        instruction.Should().Be(SchedulerInstruction.RetryTrigger);

        await store.TriggeredJobComplete(firing, job, instruction);

        (await store.GetTrigger(trigger.Key))!.RetryAttempt.Should().Be(1,
            "the store writes the attempt it was handed, or a retry restarts from the first wait every time");

        IOperableTrigger retryFiring = await Fire();
        retryFiring.RetryAttempt.Should().Be(1,
            "the firing the scheduler is handed carries the attempt, which is what the execution context reports");
    }

    [Test]
    public async Task ASuccessfulRetryPutsTheAttemptBackInTheStore()
    {
        IJobDetail job = Job();
        IOperableTrigger trigger = Trigger(RetryPolicy.Fixed(3, TimeSpan.FromMilliseconds(1)));
        await store.ScheduleJob(job, trigger);

        IOperableTrigger firing = await Fire();
        await store.TriggeredJobComplete(firing, job, firing.ExecutionComplete(context, Failure()));

        IOperableTrigger retryFiring = await Fire();
        SchedulerInstruction instruction = retryFiring.ExecutionComplete(context, result: null);
        instruction.Should().Be(SchedulerInstruction.NoInstruction);

        await store.TriggeredJobComplete(retryFiring, job, instruction);

        (await store.GetTrigger(trigger.Key))!.RetryAttempt.Should().Be(0,
            "the occurrence is done with, so the next failure starts from the first wait again");
    }

    [Test]
    public async Task ARetryFireDoesNotBurnARepeatCount()
    {
        IJobDetail job = Job();
        IOperableTrigger trigger = Trigger(RetryPolicy.Fixed(3, TimeSpan.FromMilliseconds(1)));
        await store.ScheduleJob(job, trigger);

        IOperableTrigger firing = await Fire();
        int afterFirstFire = ((ISimpleTrigger) (await store.GetTrigger(trigger.Key))!).TimesTriggered;

        await store.TriggeredJobComplete(firing, job, firing.ExecutionComplete(context, Failure()));
        await Fire();

        ((ISimpleTrigger) (await store.GetTrigger(trigger.Key))!).TimesTriggered.Should().Be(afterFirstFire,
            "a retry is a second go at an occurrence that has already been counted");
    }
}
