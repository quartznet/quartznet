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

using Microsoft.Extensions.Logging;

using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit;

/// <summary>
/// The XML and JSON misfire instruction names, per family.
/// </summary>
/// <remarks>
/// These used to be resolved by reflecting over <c>MisfireInstruction</c> and all of its nested
/// types at once, which made every family's names resolve for every family — silently, and with a
/// different meaning.
/// </remarks>
public class MisfireInstructionNamesTest
{
    [TestCase("Simple", "SmartPolicy", 0)]
    [TestCase("Simple", "InstructionNotSet", 0)]
    [TestCase("Simple", "IgnoreMisfirePolicy", -1)]
    [TestCase("Simple", "FireNow", 1)]
    [TestCase("Simple", "RescheduleNowWithExistingRepeatCount", 2)]
    [TestCase("Simple", "RescheduleNowWithRemainingRepeatCount", 3)]
    [TestCase("Simple", "RescheduleNextWithRemainingCount", 4)]
    [TestCase("Simple", "RescheduleNextWithExistingCount", 5)]
    [TestCase("Cron", "SmartPolicy", 0)]
    [TestCase("Cron", "IgnoreMisfirePolicy", -1)]
    [TestCase("Cron", "FireOnceNow", 1)]
    [TestCase("Cron", "DoNothing", 2)]
    [TestCase("CalendarInterval", "FireOnceNow", 1)]
    [TestCase("CalendarInterval", "DoNothing", 2)]
    [TestCase("DailyTimeInterval", "FireOnceNow", 1)]
    [TestCase("DailyTimeInterval", "DoNothing", 2)]
    [TestCase("Recurrence", "FireOnceNow", 1)]
    [TestCase("Recurrence", "DoNothing", 2)]
    public void TheDocumentedNamesResolveWithoutComplaint(string familyName, string name, int expected)
    {
        RecordingLoggerProvider provider = new RecordingLoggerProvider();
        TriggerFamily family = Enum.Parse<TriggerFamily>(familyName);

        MisfireInstructionNames.Resolve(family, name, provider.CreateLogger("test")).Should().Be(expected);

        provider.Entries.Should().BeEmpty("a name the family owns is not a legacy spelling");
    }

    [TestCase("Simple", "smartpolicy", 0)]
    [TestCase("Simple", "  FireNow  ", 1)]
    [TestCase("Cron", "donothing", 2)]
    public void NamesAreCaseInsensitiveAndTrimmed(string familyName, string name, int expected)
    {
        TriggerFamily family = Enum.Parse<TriggerFamily>(familyName);

        MisfireInstructionNames.Resolve(family, name).Should().Be(expected);
    }

    [TestCase("Simple", "IgnoreMisfires", -1)]
    [TestCase("Simple", "NextWithExistingCount", 5)]
    [TestCase("Cron", "FireAndProceed", 1)]
    [TestCase("Recurrence", "IgnoreMisfires", -1)]
    public void TheEnumSpellingsResolveToo(string familyName, string name, int expected)
    {
        TriggerFamily family = Enum.Parse<TriggerFamily>(familyName);

        MisfireInstructionNames.Resolve(family, name).Should().Be(expected);
    }

    /// <summary>
    /// The live defect this replaces: a cron trigger configured with a simple trigger's name became
    /// a different policy with nothing said about it. The value is kept - configuration that works
    /// today keeps working - but it is now said out loud.
    /// </summary>
    [Test]
    public void ANameFromAnotherFamilyStillResolvesButIsWarnedAbout()
    {
        RecordingLoggerProvider provider = new RecordingLoggerProvider();

        int value = MisfireInstructionNames.Resolve(TriggerFamily.Cron, "RescheduleNowWithExistingRepeatCount", provider.CreateLogger("test"));

        value.Should().Be((int) CronTriggerMisfireInstruction.DoNothing,
            "the code is what it always was - 2 in both families - and changing it would break stored configuration");

        LogEntry entry = provider.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("RescheduleNowWithExistingRepeatCount")
            .And.Contain("DoNothing", "the warning has to name the policy the value actually selects");
    }

    [Test]
    public void ANameFromAnotherFamilyWithNoCounterpartIsRejected()
    {
        Action act = () => MisfireInstructionNames.Resolve(TriggerFamily.Cron, "RescheduleNowWithRemainingRepeatCount");

        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*RescheduleNowWithRemainingRepeatCount*cron*");
    }

    [Test]
    public void AnUnknownNameNamesTheValidOnes()
    {
        Action act = () => MisfireInstructionNames.Resolve(TriggerFamily.Simple, "FireWhenReady");

        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*FireWhenReady*")
            .WithMessage("*RescheduleNextWithExistingCount*", "an error about a name should list the names that work");
    }
}
