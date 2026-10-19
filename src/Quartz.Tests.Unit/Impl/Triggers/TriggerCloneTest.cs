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

using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Impl.Triggers;

/// <summary>
/// A clone of every built-in trigger type has a job data map of its own.
/// </summary>
/// <remarks>
/// <see cref="CronTriggerImpl" /> cloned with <c>MemberwiseClone</c> instead of <see cref="TriggerBase.Clone" />,
/// so its clone shared the original's map: <c>RAMJobStore</c> stores and hands out clones, and a write to
/// one reached the trigger it was cloned from.
/// </remarks>
public sealed class TriggerCloneTest
{
    private static IEnumerable<TestCaseData> Triggers()
    {
        yield return new TestCaseData(new SimpleTriggerImpl()).SetArgDisplayNames(nameof(SimpleTriggerImpl));
        yield return new TestCaseData(new CronTriggerImpl { CronExpressionString = "0 0 12 * * ?" }).SetArgDisplayNames(nameof(CronTriggerImpl));
        yield return new TestCaseData(new CalendarIntervalTriggerImpl()).SetArgDisplayNames(nameof(CalendarIntervalTriggerImpl));
        yield return new TestCaseData(new DailyTimeIntervalTriggerImpl()).SetArgDisplayNames(nameof(DailyTimeIntervalTriggerImpl));
        yield return new TestCaseData(new RecurrenceTriggerImpl()).SetArgDisplayNames(nameof(RecurrenceTriggerImpl));
    }

    [TestCaseSource(nameof(Triggers))]
    public void ACloneHasAJobDataMapOfItsOwn(TriggerBase trigger)
    {
        trigger.Key = new TriggerKey("t", "g");
        trigger.JobDataMap["shared"] = "before";

        ITrigger clone = trigger.Clone();
        clone.JobDataMap["shared"] = "written to the clone";
        clone.JobDataMap["added"] = "only on the clone";
        trigger.JobDataMap["original"] = "only on the original";

        clone.JobDataMap.Should().NotBeSameAs(trigger.JobDataMap, "a clone that shared the map would see every write to it");
        trigger.JobDataMap.Should().BeEquivalentTo(new Dictionary<string, object> { ["shared"] = "before", ["original"] = "only on the original" },
            "a write to the clone's map does not reach the trigger it was cloned from");
        clone.JobDataMap.Should().BeEquivalentTo(new Dictionary<string, object> { ["shared"] = "written to the clone", ["added"] = "only on the clone" },
            "and a write to the original's does not reach the clone");
    }
}
