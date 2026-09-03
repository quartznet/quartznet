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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// The one rule about a misfire, on the in-memory store: a trigger is late once its fire time is
/// <em>at or before</em> <c>now - MisfireThreshold</c>, and the threshold instant itself is late.
/// </summary>
/// <remarks>
/// <see cref="StdAdoConstantsTest.TheAcquisitionPredicateShouldBeTheComplementOfTheMisfirePredicate" />
/// pins the ADO store's spelling of the same rule; the two stores have to agree on the instant, or a
/// trigger due exactly then is a misfire on one and an ordinary firing on the other (#3462).
/// </remarks>
[NonParallelizable]
public class RAMJobStoreMisfireThresholdTest
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(1);

    private Func<DateTimeOffset> clock;

    [SetUp]
    public void FreezeTheClock()
    {
        clock = SystemTime.UtcNow;
        SystemTime.UtcNow = () => Now;
    }

    [TearDown]
    public void RestoreTheClock()
    {
        SystemTime.UtcNow = clock;
    }

    [Test]
    public async Task ATriggerDueExactlyAtTheThresholdInstantIsMisfired()
    {
        IOperableTrigger acquired = await AcquireTriggerDueAt(Now - Threshold);

        acquired.GetNextFireTimeUtc().Should().Be(Now,
            "the fire-now misfire policy moved the trigger to the current instant, which is what says the store called it a misfire");
    }

    [Test]
    public async Task ATriggerDueOneTickAfterTheThresholdInstantIsNotMisfired()
    {
        DateTimeOffset due = (Now - Threshold).AddTicks(1);

        IOperableTrigger acquired = await AcquireTriggerDueAt(due);

        acquired.GetNextFireTimeUtc().Should().Be(due,
            "a trigger whose fire time is strictly after now - MisfireThreshold is not late, and its fire time is left where it was");
    }

    private static async Task<IOperableTrigger> AcquireTriggerDueAt(DateTimeOffset due)
    {
        RAMJobStore store = new RAMJobStore { MisfireThreshold = Threshold };
        await store.Initialize(null, new RAMJobStoreTest.SampleSignaler());
        await store.SchedulerStarted();

        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job", "misfire").Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger", "misfire")
            .ForJob(job)
            .StartAt(due)
            .WithSimpleSchedule(schedule => schedule.WithMisfireHandlingInstructionFireNow())
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);

        await store.StoreJobAndTrigger(job, trigger);

        IReadOnlyCollection<IOperableTrigger> acquired = await store.AcquireNextTriggers(Now.AddSeconds(1), 1, TimeSpan.Zero);

        acquired.Should().ContainSingle("the trigger is due either way; what differs is whether its misfire policy was applied on the way");
        return acquired.Single();
    }
}
