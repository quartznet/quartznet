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

using System.Text;

using FakeItEasy;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Quartz.Impl;
using Quartz.Xml;

namespace Quartz.Tests.Unit.Xml;

/// <summary>
/// Covers the XML to object model to scheduling path of <see cref="XMLSchedulingDataProcessor" />.
/// </summary>
/// <remarks>
/// The processor's only other coverage is database-backed and therefore Docker-bound, which leaves
/// the parsing itself — element names, the trigger type each element selects, optional elements and
/// their defaults, and schema validation — without a fast test loop. Everything here runs against a
/// fake scheduler or no scheduler at all, so the XML contract can be exercised on its own.
/// </remarks>
public class XMLSchedulingDataProcessorTest
{
    private const string JobType = "Quartz.Jobs.NoOpJob, Quartz.Jobs";

    /// <summary>
    /// Wraps a document body in the root element, so that each test shows only the markup it is about.
    /// </summary>
    private static string Document(string body)
    {
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <job-scheduling-data xmlns="http://quartznet.sourceforge.net/JobSchedulingData" version="2.0">
                {body}
                </job-scheduling-data>
                """;
    }

    /// <summary>
    /// A single job that triggers can point at, so tests about triggers do not have to repeat it.
    /// </summary>
    private const string Job = $"""
                                <job>
                                  <name>job1</name>
                                  <group>group1</group>
                                  <job-type>{JobType}</job-type>
                                </job>
                                """;

    private static TestProcessor CreateProcessor(TimeProvider timeProvider = null)
    {
        return new TestProcessor(timeProvider ?? TimeProvider.System);
    }

    private static async ValueTask<TestProcessor> Process(string xml, TimeProvider timeProvider = null)
    {
        TestProcessor processor = CreateProcessor(timeProvider);
        await processor.ProcessStream(ToStream(xml), null);
        return processor;
    }

    private static Stream ToStream(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    private static FakeTimeProvider CreateTimeProvider()
    {
        return new FakeTimeProvider(new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero));
    }

    private static IScheduler CreateFakeScheduler()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.GetJobDetail(A<JobKey>._, A<CancellationToken>._)).Returns(new ValueTask<IJobDetail>());
        A.CallTo(() => scheduler.GetTrigger(A<TriggerKey>._, A<CancellationToken>._)).Returns(new ValueTask<ITrigger>());
        A.CallTo(() => scheduler.QueryJobs(A<JobQuery>._, A<CancellationToken>._))
            .Returns(new ValueTask<PagedResult<JobHeader>>(new PagedResult<JobHeader>([], false, 0)));
        A.CallTo(() => scheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new ValueTask<PagedResult<TriggerHeader>>(new PagedResult<TriggerHeader>([], false, 0)));
        return scheduler;
    }

    /// <summary>
    /// Exposes the loaded jobs and triggers, which the processor itself only offers to subclasses.
    /// </summary>
    private sealed class TestProcessor : XMLSchedulingDataProcessor
    {
        public TestProcessor(TimeProvider timeProvider)
            : base(NullLogger<XMLSchedulingDataProcessor>.Instance, new SimpleTypeLoadHelper(), timeProvider)
        {
        }

        public List<IJobDetail> Jobs => LoadedJobs;

        public List<ITrigger> Triggers => LoadedTriggers;

        public IJobDetail SingleJob => Jobs.Should().ContainSingle().Subject;

        public ITrigger SingleTrigger => Triggers.Should().ContainSingle().Subject;
    }

    // ---------------------------------------------------------------------------------------------
    // Trigger types: the element name under <trigger> is what picks the schedule
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task SimpleTriggerIsReadWithEveryProperty()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              {Job}
              <trigger>
                <simple>
                  <name>triggerName</name>
                  <group>triggerGroup</group>
                  <description>triggerDescription</description>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  <priority>7</priority>
                  <calendar-name>calendarName</calendar-name>
                  <job-data-map>
                    <entry>
                      <key>triggerKey</key>
                      <value>triggerValue</value>
                    </entry>
                  </job-data-map>
                  <start-time>2020-01-01T10:00:00Z</start-time>
                  <end-time>2030-01-01T10:00:00Z</end-time>
                  <misfire-instruction>FireNow</misfire-instruction>
                  <repeat-count>5</repeat-count>
                  <repeat-interval>3000</repeat-interval>
                </simple>
              </trigger>
            </schedule>
            """));

        ITrigger trigger = processor.SingleTrigger;
        trigger.Should().BeAssignableTo<ISimpleTrigger>();
        trigger.Key.Should().Be(new TriggerKey("triggerName", "triggerGroup"));
        trigger.JobKey.Should().Be(new JobKey("job1", "group1"));
        trigger.Description.Should().Be("triggerDescription");
        trigger.Priority.Should().Be(7);
        trigger.CalendarName.Should().Be("calendarName");
        trigger.StartTimeUtc.Should().Be(new DateTimeOffset(2020, 1, 1, 10, 0, 0, TimeSpan.Zero));
        trigger.EndTimeUtc.Should().Be(new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero));
        trigger.MisfireInstruction.Should().Be(MisfireInstruction.SimpleTrigger.FireNow);
        trigger.JobDataMap.GetString("triggerKey").Should().Be("triggerValue");

        ISimpleTrigger simple = (ISimpleTrigger) trigger;
        simple.RepeatCount.Should().Be(5);
        simple.RepeatInterval.Should().Be(TimeSpan.FromMilliseconds(3000));
    }

    [Test]
    public async Task CronTriggerIsReadWithEveryProperty()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              {Job}
              <trigger>
                <cron>
                  <name>triggerName</name>
                  <group>triggerGroup</group>
                  <description>triggerDescription</description>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  <priority>3</priority>
                  <calendar-name>calendarName</calendar-name>
                  <start-time>2020-01-01T10:00:00Z</start-time>
                  <end-time>2030-01-01T10:00:00Z</end-time>
                  <misfire-instruction>DoNothing</misfire-instruction>
                  <cron-expression>0 0/5 * * * ?</cron-expression>
                  <time-zone>UTC</time-zone>
                </cron>
              </trigger>
            </schedule>
            """));

        ITrigger trigger = processor.SingleTrigger;
        trigger.Should().BeAssignableTo<ICronTrigger>();
        trigger.Key.Should().Be(new TriggerKey("triggerName", "triggerGroup"));
        trigger.Priority.Should().Be(3);
        trigger.MisfireInstruction.Should().Be(MisfireInstruction.CronTrigger.DoNothing);

        ICronTrigger cron = (ICronTrigger) trigger;
        cron.CronExpressionString.Should().Be("0 0/5 * * * ?");
        cron.TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    [Test]
    public async Task CalendarIntervalTriggerIsReadWithEveryProperty()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              {Job}
              <trigger>
                <calendar-interval>
                  <name>triggerName</name>
                  <group>triggerGroup</group>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  <start-time>2020-01-01T10:00:00Z</start-time>
                  <misfire-instruction>DoNothing</misfire-instruction>
                  <repeat-interval>2</repeat-interval>
                  <repeat-interval-unit>Week</repeat-interval-unit>
                </calendar-interval>
              </trigger>
            </schedule>
            """));

        ITrigger trigger = processor.SingleTrigger;
        trigger.Should().BeAssignableTo<ICalendarIntervalTrigger>();
        trigger.MisfireInstruction.Should().Be(MisfireInstruction.CalendarIntervalTrigger.DoNothing);

        ICalendarIntervalTrigger calendarInterval = (ICalendarIntervalTrigger) trigger;
        calendarInterval.RepeatInterval.Should().Be(2);
        calendarInterval.RepeatIntervalUnit.Should().Be(IntervalUnit.Week);
    }

    [Test]
    public async Task TriggerElementNameSelectsTheScheduleType()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              {Job}
              <trigger>
                <simple>
                  <name>simpleTrigger</name>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  <repeat-count>1</repeat-count>
                  <repeat-interval>1000</repeat-interval>
                </simple>
              </trigger>
              <trigger>
                <cron>
                  <name>cronTrigger</name>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  <cron-expression>0 0 12 * * ?</cron-expression>
                </cron>
              </trigger>
              <trigger>
                <calendar-interval>
                  <name>calendarIntervalTrigger</name>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  <repeat-interval>1</repeat-interval>
                  <repeat-interval-unit>Day</repeat-interval-unit>
                </calendar-interval>
              </trigger>
            </schedule>
            """));

        processor.Triggers.Should().SatisfyRespectively(
            first => first.Should().BeAssignableTo<ISimpleTrigger>(),
            second => second.Should().BeAssignableTo<ICronTrigger>(),
            third => third.Should().BeAssignableTo<ICalendarIntervalTrigger>());

        processor.Triggers.Select(x => x.Key.Name).Should()
            .Equal("simpleTrigger", "cronTrigger", "calendarIntervalTrigger");
    }

    [TestCase("SmartPolicy", MisfireInstruction.SmartPolicy)]
    [TestCase("IgnoreMisfirePolicy", MisfireInstruction.IgnoreMisfirePolicy)]
    [TestCase("FireNow", MisfireInstruction.SimpleTrigger.FireNow)]
    [TestCase("RescheduleNowWithExistingRepeatCount", MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount)]
    [TestCase("RescheduleNowWithRemainingRepeatCount", MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount)]
    [TestCase("RescheduleNextWithExistingCount", MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount)]
    [TestCase("RescheduleNextWithRemainingCount", MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount)]
    public async Task SimpleTriggerMisfireInstructionIsReadByName(string name, int expected)
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              {Job}
              <trigger>
                <simple>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  <misfire-instruction>{name}</misfire-instruction>
                  <repeat-count>1</repeat-count>
                  <repeat-interval>1000</repeat-interval>
                </simple>
              </trigger>
            </schedule>
            """));

        processor.SingleTrigger.MisfireInstruction.Should().Be(expected);
    }

    [TestCase("SmartPolicy", MisfireInstruction.SmartPolicy)]
    [TestCase("IgnoreMisfirePolicy", MisfireInstruction.IgnoreMisfirePolicy)]
    [TestCase("FireOnceNow", MisfireInstruction.CronTrigger.FireOnceNow)]
    [TestCase("DoNothing", MisfireInstruction.CronTrigger.DoNothing)]
    public async Task CronTriggerMisfireInstructionIsReadByName(string name, int expected)
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              {Job}
              <trigger>
                <cron>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  <misfire-instruction>{name}</misfire-instruction>
                  <cron-expression>0 0 12 * * ?</cron-expression>
                </cron>
              </trigger>
            </schedule>
            """));

        processor.SingleTrigger.MisfireInstruction.Should().Be(expected);
    }

    [TestCase("SmartPolicy", MisfireInstruction.SmartPolicy)]
    [TestCase("IgnoreMisfirePolicy", MisfireInstruction.IgnoreMisfirePolicy)]
    [TestCase("FireOnceNow", MisfireInstruction.CalendarIntervalTrigger.FireOnceNow)]
    [TestCase("DoNothing", MisfireInstruction.CalendarIntervalTrigger.DoNothing)]
    public async Task CalendarIntervalTriggerMisfireInstructionIsReadByName(string name, int expected)
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              {Job}
              <trigger>
                <calendar-interval>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  <misfire-instruction>{name}</misfire-instruction>
                  <repeat-interval>1</repeat-interval>
                  <repeat-interval-unit>Day</repeat-interval-unit>
                </calendar-interval>
              </trigger>
            </schedule>
            """));

        processor.SingleTrigger.MisfireInstruction.Should().Be(expected);
    }

    [TestCase("Second", IntervalUnit.Second)]
    [TestCase("Minute", IntervalUnit.Minute)]
    [TestCase("Hour", IntervalUnit.Hour)]
    [TestCase("Day", IntervalUnit.Day)]
    [TestCase("Week", IntervalUnit.Week)]
    [TestCase("Month", IntervalUnit.Month)]
    [TestCase("Year", IntervalUnit.Year)]
    public async Task CalendarIntervalUnitIsReadByName(string name, IntervalUnit expected)
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              {Job}
              <trigger>
                <calendar-interval>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  <repeat-interval>1</repeat-interval>
                  <repeat-interval-unit>{name}</repeat-interval-unit>
                </calendar-interval>
              </trigger>
            </schedule>
            """));

        ((ICalendarIntervalTrigger) processor.SingleTrigger).RepeatIntervalUnit.Should().Be(expected);
    }

    // ---------------------------------------------------------------------------------------------
    // Start and end times: the start-time / start-time-seconds-in-future choice, and the
    // "specified" flag that makes end-time optional even though it is a non-nullable DateTime
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task StartTimeIsReadAsAnInstant()
    {
        TestProcessor processor = await Process(SimpleTriggerWith("<start-time>2020-06-28T18:15:00Z</start-time>"));

        processor.SingleTrigger.StartTimeUtc.Should()
            .Be(new DateTimeOffset(2020, 6, 28, 18, 15, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task StartTimeWithAnOffsetKeepsTheSameInstant()
    {
        TestProcessor processor = await Process(SimpleTriggerWith("<start-time>2020-06-28T18:15:00+02:00</start-time>"));

        processor.SingleTrigger.StartTimeUtc.Should()
            .Be(new DateTimeOffset(2020, 6, 28, 16, 15, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task StartTimeToleratesSurroundingWhitespace()
    {
        TestProcessor processor = await Process(SimpleTriggerWith("""
            <start-time>
              2020-06-28T18:15:00Z
            </start-time>
            """));

        processor.SingleTrigger.StartTimeUtc.Should()
            .Be(new DateTimeOffset(2020, 6, 28, 18, 15, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task StartTimeSecondsInFutureIsRelativeToTheTimeProvider()
    {
        FakeTimeProvider timeProvider = CreateTimeProvider();

        TestProcessor processor = await Process(
            SimpleTriggerWith("<start-time-seconds-in-future>90</start-time-seconds-in-future>"),
            timeProvider);

        processor.SingleTrigger.StartTimeUtc.Should().Be(timeProvider.GetUtcNow().AddSeconds(90));
    }

    [Test]
    public async Task TriggerWithoutAStartTimeStartsNow()
    {
        FakeTimeProvider timeProvider = CreateTimeProvider();

        TestProcessor processor = await Process(SimpleTriggerWith(""), timeProvider);

        processor.SingleTrigger.StartTimeUtc.Should().Be(timeProvider.GetUtcNow());
    }

    [Test]
    public async Task EndTimeIsOptional()
    {
        TestProcessor processor = await Process(SimpleTriggerWith("<start-time>2020-06-28T18:15:00Z</start-time>"));

        processor.SingleTrigger.EndTimeUtc.Should().BeNull(
            "end-time is a non-nullable DateTime in the object model, so only its 'specified' flag distinguishes "
            + "an absent element from the default value");
    }

    [Test]
    public async Task EndTimeIsReadWhenPresent()
    {
        TestProcessor processor = await Process(SimpleTriggerWith("""
            <start-time>2020-06-28T18:15:00Z</start-time>
            <end-time>2021-06-28T18:15:00Z</end-time>
            """));

        processor.SingleTrigger.EndTimeUtc.Should()
            .Be(new DateTimeOffset(2021, 6, 28, 18, 15, 0, TimeSpan.Zero));
    }

    private static string SimpleTriggerWith(string timeElements)
    {
        return Document($"""
            <schedule>
              {Job}
              <trigger>
                <simple>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  {timeElements}
                  <repeat-count>1</repeat-count>
                  <repeat-interval>1000</repeat-interval>
                </simple>
              </trigger>
            </schedule>
            """);
    }

    // ---------------------------------------------------------------------------------------------
    // Defaults for omitted optional elements
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task OmittedTriggerElementsFallBackToTheirDefaults()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              {Job}
              <trigger>
                <cron>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <cron-expression>0 0 12 * * ?</cron-expression>
                </cron>
              </trigger>
            </schedule>
            """));

        ITrigger trigger = processor.SingleTrigger;
        trigger.Key.Group.Should().Be(SchedulerConstants.DefaultGroup);
        trigger.JobKey.Group.Should().Be(SchedulerConstants.DefaultGroup);
        trigger.Description.Should().BeNull();
        trigger.CalendarName.Should().BeNull();
        trigger.EndTimeUtc.Should().BeNull();
        trigger.Priority.Should().Be(TriggerConstants.DefaultPriority);
        trigger.MisfireInstruction.Should().Be(MisfireInstruction.SmartPolicy);
        ((ICronTrigger) trigger).TimeZone.Should().Be(TimeZoneInfo.Local);
    }

    [Test]
    public async Task SimpleTriggerWithoutRepeatElementsDoesNotRepeat()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              {Job}
              <trigger>
                <simple>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                </simple>
              </trigger>
            </schedule>
            """));

        ISimpleTrigger trigger = (ISimpleTrigger) processor.SingleTrigger;
        trigger.RepeatCount.Should().Be(0);
        trigger.RepeatInterval.Should().Be(TimeSpan.Zero);
    }

    [Test]
    public async Task NegativeRepeatCountMeansRepeatForever()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              {Job}
              <trigger>
                <simple>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  <repeat-count>-1</repeat-count>
                  <repeat-interval>10000</repeat-interval>
                </simple>
              </trigger>
            </schedule>
            """));

        ISimpleTrigger trigger = (ISimpleTrigger) processor.SingleTrigger;
        trigger.RepeatCount.Should().Be(-1);
        trigger.RepeatInterval.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task OmittedJobElementsFallBackToTheirDefaults()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              <job>
                <name>job1</name>
                <job-type>{JobType}</job-type>
              </job>
            </schedule>
            """));

        IJobDetail job = processor.SingleJob;
        job.Key.Should().Be(new JobKey("job1", SchedulerConstants.DefaultGroup));
        job.Description.Should().BeNull();
        job.Durable.Should().BeFalse();
        job.RequestsRecovery.Should().BeFalse();
        job.JobDataMap.Should().BeEmpty();
    }

    [Test]
    public async Task JobIsReadWithEveryProperty()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              <job>
                <name>job1</name>
                <group>group1</group>
                <description>jobDescription</description>
                <job-type>{JobType}</job-type>
                <durable>true</durable>
                <recover>true</recover>
                <job-data-map>
                  <entry>
                    <key>key0</key>
                    <value>value0</value>
                  </entry>
                  <entry>
                    <key>key1</key>
                    <value>value1</value>
                  </entry>
                </job-data-map>
              </job>
            </schedule>
            """));

        IJobDetail job = processor.SingleJob;
        job.Key.Should().Be(new JobKey("job1", "group1"));
        job.Description.Should().Be("jobDescription");
        job.JobType.Type.Should().Be(typeof(Quartz.Jobs.NoOpJob));
        job.Durable.Should().BeTrue();
        job.RequestsRecovery.Should().BeTrue();
        job.JobDataMap.GetString("key0").Should().Be("value0");
        job.JobDataMap.GetString("key1").Should().Be("value1");
    }

    [Test]
    public async Task JobTypeNameIsTrimmed()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              <job>
                <name>job1</name>
                <job-type>
                  {JobType}
                </job-type>
              </job>
            </schedule>
            """));

        processor.SingleJob.JobType.Type.Should().Be(typeof(Quartz.Jobs.NoOpJob));
    }

    [Test]
    public async Task EmptyJobDataMapValueBecomesNull()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              <job>
                <name>job1</name>
                <job-type>{JobType}</job-type>
                <job-data-map>
                  <entry>
                    <key>empty</key>
                    <value></value>
                  </entry>
                </job-data-map>
              </job>
            </schedule>
            """));

        processor.SingleJob.JobDataMap.Should().ContainKey("empty").WhoseValue.Should().BeNull();
    }

    [Test]
    public async Task TriggerJobDataMapIsRead()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              {Job}
              <trigger>
                <cron>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  <job-data-map>
                    <entry>
                      <key>triggerKey</key>
                      <value>triggerValue</value>
                    </entry>
                  </job-data-map>
                  <cron-expression>0 0 12 * * ?</cron-expression>
                </cron>
              </trigger>
            </schedule>
            """));

        processor.SingleTrigger.JobDataMap.GetString("triggerKey").Should().Be("triggerValue");
    }

    // ---------------------------------------------------------------------------------------------
    // Processing directives
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task DirectivesFallBackToTheirDefaultsWhenTheElementIsAbsent()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              <job>
                <name>job1</name>
                <job-type>{JobType}</job-type>
              </job>
            </schedule>
            """));

        processor.OverwriteExistingData.Should().BeTrue();
        processor.IgnoreDuplicates.Should().BeFalse();
        processor.ScheduleTriggerRelativeToReplacedTrigger.Should().BeFalse();
    }

    [Test]
    public async Task DirectivesFallBackToTheirDefaultsWhenTheElementIsEmpty()
    {
        TestProcessor processor = await Process(Document($"""
            <processing-directives></processing-directives>
            <schedule>
              <job>
                <name>job1</name>
                <job-type>{JobType}</job-type>
              </job>
            </schedule>
            """));

        processor.OverwriteExistingData.Should().BeTrue(
            "overwrite-existing-data defaults to true, and an empty processing-directives element must not "
            + "silently turn it off");
        processor.IgnoreDuplicates.Should().BeFalse();
        processor.ScheduleTriggerRelativeToReplacedTrigger.Should().BeFalse();
    }

    [Test]
    public async Task DirectivesAreReadWhenPresent()
    {
        TestProcessor processor = await Process(Document($"""
            <processing-directives>
              <overwrite-existing-data>false</overwrite-existing-data>
              <ignore-duplicates>true</ignore-duplicates>
              <schedule-trigger-relative-to-replaced-trigger>true</schedule-trigger-relative-to-replaced-trigger>
            </processing-directives>
            <schedule>
              <job>
                <name>job1</name>
                <job-type>{JobType}</job-type>
              </job>
            </schedule>
            """));

        processor.OverwriteExistingData.Should().BeFalse();
        processor.IgnoreDuplicates.Should().BeTrue();
        processor.ScheduleTriggerRelativeToReplacedTrigger.Should().BeTrue();
    }

    [Test]
    public async Task AnOmittedDirectiveKeepsItsDefaultWhenAnotherIsGiven()
    {
        TestProcessor processor = await Process(Document($"""
            <processing-directives>
              <ignore-duplicates>true</ignore-duplicates>
            </processing-directives>
            <schedule>
              <job>
                <name>job1</name>
                <job-type>{JobType}</job-type>
              </job>
            </schedule>
            """));

        processor.OverwriteExistingData.Should().BeTrue();
        processor.IgnoreDuplicates.Should().BeTrue();
    }

    [Test]
    public async Task ProcessingResetsDirectivesBetweenDocuments()
    {
        TestProcessor processor = CreateProcessor();

        await processor.ProcessStream(ToStream(Document($"""
            <processing-directives>
              <overwrite-existing-data>false</overwrite-existing-data>
              <ignore-duplicates>true</ignore-duplicates>
            </processing-directives>
            <schedule>
              <job>
                <name>job1</name>
                <job-type>{JobType}</job-type>
              </job>
            </schedule>
            """)), null);

        processor.OverwriteExistingData.Should().BeFalse();

        await processor.ProcessStream(ToStream(Document($"""
            <schedule>
              <job>
                <name>job2</name>
                <job-type>{JobType}</job-type>
              </job>
            </schedule>
            """)), null);

        processor.OverwriteExistingData.Should().BeTrue("each document starts from the defaults");
        processor.IgnoreDuplicates.Should().BeFalse();
        processor.Jobs.Should().ContainSingle().Which.Key.Name.Should().Be("job2",
            "the previous document's jobs must not leak into the next one");
    }

    // ---------------------------------------------------------------------------------------------
    // Pre-processing commands
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task DeleteJobCommandDeletesThatJob()
    {
        IScheduler scheduler = CreateFakeScheduler();
        TestProcessor processor = CreateProcessor();

        await processor.ProcessStreamAndScheduleJobs(ToStream(Document("""
            <pre-processing-commands>
              <delete-job>
                <name>doomedJob</name>
                <group>doomedGroup</group>
              </delete-job>
            </pre-processing-commands>
            """)), scheduler);

        A.CallTo(() => scheduler.DeleteJob(new JobKey("doomedJob", "doomedGroup"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task DeleteJobCommandWithoutAGroupUsesTheDefaultGroup()
    {
        IScheduler scheduler = CreateFakeScheduler();
        TestProcessor processor = CreateProcessor();

        await processor.ProcessStreamAndScheduleJobs(ToStream(Document("""
            <pre-processing-commands>
              <delete-job>
                <name>doomedJob</name>
              </delete-job>
            </pre-processing-commands>
            """)), scheduler);

        A.CallTo(() => scheduler.DeleteJob(new JobKey("doomedJob", SchedulerConstants.DefaultGroup), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task DeleteTriggerCommandUnschedulesThatTrigger()
    {
        IScheduler scheduler = CreateFakeScheduler();
        TestProcessor processor = CreateProcessor();

        await processor.ProcessStreamAndScheduleJobs(ToStream(Document("""
            <pre-processing-commands>
              <delete-trigger>
                <name>doomedTrigger</name>
                <group>doomedGroup</group>
              </delete-trigger>
            </pre-processing-commands>
            """)), scheduler);

        A.CallTo(() => scheduler.UnscheduleJob(new TriggerKey("doomedTrigger", "doomedGroup"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task DeleteJobsInGroupCommandDeletesEveryJobInThatGroup()
    {
        IScheduler scheduler = CreateFakeScheduler();
        A.CallTo(() => scheduler.QueryJobs(A<JobQuery>.That.Matches(q => q.Group != null), A<CancellationToken>._))
            .Returns(new ValueTask<PagedResult<JobHeader>>(new PagedResult<JobHeader>(
                [JobHeaderFor("job1", "doomedGroup"), JobHeaderFor("job2", "doomedGroup")], false)));

        TestProcessor processor = CreateProcessor();

        await processor.ProcessStreamAndScheduleJobs(ToStream(Document("""
            <pre-processing-commands>
              <delete-jobs-in-group>doomedGroup</delete-jobs-in-group>
            </pre-processing-commands>
            """)), scheduler);

        A.CallTo(() => scheduler.DeleteJob(new JobKey("job1", "doomedGroup"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => scheduler.DeleteJob(new JobKey("job2", "doomedGroup"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task DeleteJobsInGroupWildcardDeletesEveryJob()
    {
        IScheduler scheduler = CreateFakeScheduler();
        A.CallTo(() => scheduler.QueryJobs(A<JobQuery>._, A<CancellationToken>._))
            .Returns(new ValueTask<PagedResult<JobHeader>>(new PagedResult<JobHeader>(
                [JobHeaderFor("job1", "groupA"), JobHeaderFor("job2", "groupB")], false)));

        TestProcessor processor = CreateProcessor();

        await processor.ProcessStreamAndScheduleJobs(ToStream(Document("""
            <pre-processing-commands>
              <delete-jobs-in-group>*</delete-jobs-in-group>
            </pre-processing-commands>
            """)), scheduler);

        A.CallTo(() => scheduler.DeleteJob(new JobKey("job1", "groupA"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => scheduler.DeleteJob(new JobKey("job2", "groupB"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task DeleteTriggersInGroupCommandUnschedulesEveryTriggerInThatGroup()
    {
        IScheduler scheduler = CreateFakeScheduler();
        A.CallTo(() => scheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new ValueTask<PagedResult<TriggerHeader>>(new PagedResult<TriggerHeader>(
                [TriggerHeaderFor("trigger1", "doomedGroup")], false)));

        TestProcessor processor = CreateProcessor();

        await processor.ProcessStreamAndScheduleJobs(ToStream(Document("""
            <pre-processing-commands>
              <delete-triggers-in-group>doomedGroup</delete-triggers-in-group>
            </pre-processing-commands>
            """)), scheduler);

        A.CallTo(() => scheduler.UnscheduleJob(new TriggerKey("trigger1", "doomedGroup"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task GroupsRegisteredAsNeverDeleteSurviveTheDeleteCommands()
    {
        IScheduler scheduler = CreateFakeScheduler();
        TestProcessor processor = CreateProcessor();
        processor.AddJobGroupToNeverDelete("protectedGroup");
        processor.AddTriggerGroupToNeverDelete("protectedGroup");

        await processor.ProcessStreamAndScheduleJobs(ToStream(Document("""
            <pre-processing-commands>
              <delete-job>
                <name>job1</name>
                <group>protectedGroup</group>
              </delete-job>
              <delete-trigger>
                <name>trigger1</name>
                <group>protectedGroup</group>
              </delete-trigger>
            </pre-processing-commands>
            """)), scheduler);

        A.CallTo(() => scheduler.DeleteJob(A<JobKey>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => scheduler.UnscheduleJob(A<TriggerKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task DeleteJobCommandWithoutANameIsRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream(Document("""
            <pre-processing-commands>
              <delete-job>
                <name></name>
              </delete-job>
            </pre-processing-commands>
            """)), null);

        await act.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*delete-job*");
    }

    [Test]
    public async Task DeleteTriggerCommandWithoutANameIsRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream(Document("""
            <pre-processing-commands>
              <delete-trigger>
                <name></name>
              </delete-trigger>
            </pre-processing-commands>
            """)), null);

        await act.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*delete-trigger*");
    }

    [Test]
    public async Task MultiplePreProcessingCommandElementsAreCombined()
    {
        IScheduler scheduler = CreateFakeScheduler();
        TestProcessor processor = CreateProcessor();

        await processor.ProcessStreamAndScheduleJobs(ToStream(Document("""
            <pre-processing-commands>
              <delete-job>
                <name>job1</name>
              </delete-job>
            </pre-processing-commands>
            <pre-processing-commands>
              <delete-job>
                <name>job2</name>
              </delete-job>
            </pre-processing-commands>
            """)), scheduler);

        A.CallTo(() => scheduler.DeleteJob(new JobKey("job1"), A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => scheduler.DeleteJob(new JobKey("job2"), A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    // ---------------------------------------------------------------------------------------------
    // Document structure
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task MultipleScheduleElementsAreCombined()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              <job>
                <name>job1</name>
                <job-type>{JobType}</job-type>
                <durable>true</durable>
              </job>
            </schedule>
            <schedule>
              <job>
                <name>job2</name>
                <job-type>{JobType}</job-type>
                <durable>true</durable>
              </job>
              <trigger>
                <cron>
                  <name>trigger2</name>
                  <job-name>job2</job-name>
                  <cron-expression>0 0 12 * * ?</cron-expression>
                </cron>
              </trigger>
            </schedule>
            """));

        processor.Jobs.Select(x => x.Key.Name).Should().Equal("job1", "job2");
        processor.Triggers.Should().ContainSingle().Which.Key.Name.Should().Be("trigger2");
    }

    [Test]
    public async Task JobsAndTriggersMayBeInterleavedWithinOneSchedule()
    {
        TestProcessor processor = await Process(Document($"""
            <schedule>
              <job>
                <name>job1</name>
                <job-type>{JobType}</job-type>
                <durable>true</durable>
              </job>
              <trigger>
                <cron>
                  <name>trigger1</name>
                  <job-name>job1</job-name>
                  <cron-expression>0 0 12 * * ?</cron-expression>
                </cron>
              </trigger>
              <job>
                <name>job2</name>
                <job-type>{JobType}</job-type>
                <durable>true</durable>
              </job>
              <trigger>
                <cron>
                  <name>trigger2</name>
                  <job-name>job2</job-name>
                  <cron-expression>0 0 12 * * ?</cron-expression>
                </cron>
              </trigger>
            </schedule>
            """));

        processor.Jobs.Select(x => x.Key.Name).Should().Equal("job1", "job2");
        processor.Triggers.Select(x => x.Key.Name).Should().Equal("trigger1", "trigger2");
    }

    [Test]
    public async Task XmlCommentsAreIgnored()
    {
        TestProcessor processor = await Process(Document($"""
            <!-- a leading comment -->
            <schedule>
              <job>
                <!-- the job's name -->
                <name>job1</name>
                <job-type>{JobType}</job-type>
              </job>
            </schedule>
            """));

        processor.SingleJob.Key.Name.Should().Be("job1");
    }

    // ---------------------------------------------------------------------------------------------
    // Schema validation
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task UnknownElementIsRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream(Document($"""
            <schedule>
              <job>
                <name>job1</name>
                <job-type>{JobType}</job-type>
                <not-a-real-element>x</not-a-real-element>
              </job>
            </schedule>
            """)), null);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Test]
    public async Task MissingRequiredElementIsRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream(Document("""
            <schedule>
              <job>
                <name>job1</name>
              </job>
            </schedule>
            """)), null);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.ValidationExceptions.Should().NotBeEmpty();
    }

    [Test]
    public async Task ElementsOutOfSchemaOrderAreRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream(Document($"""
            <schedule>
              <job>
                <job-type>{JobType}</job-type>
                <name>job1</name>
              </job>
            </schedule>
            """)), null);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Test]
    public async Task UnknownMisfireInstructionIsRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream(Document($"""
            <schedule>
              {Job}
              <trigger>
                <cron>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <misfire-instruction>ThereIsNoSuchInstruction</misfire-instruction>
                  <cron-expression>0 0 12 * * ?</cron-expression>
                </cron>
              </trigger>
            </schedule>
            """)), null);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Test]
    public async Task UnknownIntervalUnitIsRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream(Document($"""
            <schedule>
              {Job}
              <trigger>
                <calendar-interval>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <repeat-interval>1</repeat-interval>
                  <repeat-interval-unit>Fortnight</repeat-interval-unit>
                </calendar-interval>
              </trigger>
            </schedule>
            """)), null);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Test]
    public async Task NegativeRepeatIntervalIsRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream(Document($"""
            <schedule>
              {Job}
              <trigger>
                <simple>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <repeat-count>1</repeat-count>
                  <repeat-interval>-1000</repeat-interval>
                </simple>
              </trigger>
            </schedule>
            """)), null);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Test]
    public async Task ValidationReportsEveryErrorInTheDocument()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream(Document("""
            <schedule>
              <job>
                <name>job1</name>
              </job>
              <job>
                <name>job2</name>
              </job>
            </schedule>
            """)), null);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.ValidationExceptions.Should().HaveCountGreaterThanOrEqualTo(2,
                "every schema error is collected before any of them is thrown");
    }

    [Test]
    public async Task MalformedXmlIsRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream("""
            <?xml version="1.0" encoding="UTF-8"?>
            <job-scheduling-data xmlns="http://quartznet.sourceforge.net/JobSchedulingData" version="2.0">
              <schedule>
            """), null);

        (await act.Should().ThrowAsync<System.Xml.XmlException>(
                "a document that is not well formed never reaches schema validation, so it surfaces from "
                + "the reader rather than as a ValidationException"))
            .WithMessage("*not closed*");
    }

    [Test]
    public async Task InvalidCronExpressionIsRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream(Document($"""
            <schedule>
              {Job}
              <trigger>
                <cron>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <cron-expression>0 * * * *</cron-expression>
                </cron>
              </trigger>
            </schedule>
            """)), null);

        await act.Should().ThrowAsync<FormatException>();
    }

    [Test]
    public async Task UnknownJobTypeIsRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream(Document("""
            <schedule>
              <job>
                <name>job1</name>
                <job-type>Wrong.NameSpace.NoOpJob, Quartz</job-type>
              </job>
            </schedule>
            """)), null);

        await act.Should().ThrowAsync<TypeLoadException>();
    }

    [Test]
    public async Task TriggerWithoutAnyScheduleElementIsRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream(Document($"""
            <schedule>
              {Job}
              <trigger>
              </trigger>
            </schedule>
            """)), null);

        await act.Should().ThrowAsync<ValidationException>(
            "the schema's choice requires exactly one of simple, cron or calendar-interval");
    }

    [Test]
    public async Task BothStartTimeFormsTogetherAreRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream(Document($"""
            <schedule>
              {Job}
              <trigger>
                <simple>
                  <name>triggerName</name>
                  <job-name>job1</job-name>
                  <start-time>2020-06-28T18:15:00Z</start-time>
                  <start-time-seconds-in-future>90</start-time-seconds-in-future>
                </simple>
              </trigger>
            </schedule>
            """)), null);

        await act.Should().ThrowAsync<ValidationException>(
            "start-time and start-time-seconds-in-future are alternatives, not a pair");
    }

    [Test]
    public async Task DocumentWithoutTheQuartzNamespaceIsRejected()
    {
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStream(ToStream($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <job-scheduling-data version="2.0">
              <schedule>
                <job>
                  <name>job1</name>
                  <job-type>{JobType}</job-type>
                </job>
              </schedule>
            </job-scheduling-data>
            """), null);

        (await act.Should().ThrowAsync<SchedulerConfigException>(
                "every element is bound to the JobSchedulingData namespace, and a document that leaves the "
                + "namespace out is not silently read as an empty one"))
            .WithMessage("*http://quartznet.sourceforge.net/JobSchedulingData*");
    }

    [Test]
    public async Task DocumentDeclaringTheSchemaInstanceNamespaceIsAccepted()
    {
        TestProcessor processor = await Process($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <job-scheduling-data xmlns="http://quartznet.sourceforge.net/JobSchedulingData"
                                 xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                 version="2.0">
              <schedule>
                <job>
                  <name>job1</name>
                  <job-type>{JobType}</job-type>
                </job>
              </schedule>
            </job-scheduling-data>
            """);

        processor.SingleJob.Key.Name.Should().Be("job1");
    }

    [Test]
    public async Task DocumentWithNoScheduleAtAllIsAccepted()
    {
        TestProcessor processor = await Process(Document("<schedule></schedule>"));

        processor.Jobs.Should().BeEmpty();
        processor.Triggers.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------
    // Scheduling the parsed model
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task ParsedJobsAndTriggersAreHandedToTheScheduler()
    {
        IScheduler scheduler = CreateFakeScheduler();
        TestProcessor processor = CreateProcessor();

        await processor.ProcessStreamAndScheduleJobs(ToStream(Document($"""
            <schedule>
              {Job}
              <trigger>
                <cron>
                  <name>trigger1</name>
                  <job-name>job1</job-name>
                  <job-group>group1</job-group>
                  <cron-expression>0 0 12 * * ?</cron-expression>
                </cron>
              </trigger>
            </schedule>
            """)), scheduler);

        A.CallTo(() => scheduler.ScheduleJob(
                A<IJobDetail>.That.Matches(j => j.Key == new JobKey("job1", "group1")),
                A<ITrigger>.That.Matches(t => t.Key == new TriggerKey("trigger1", SchedulerConstants.DefaultGroup)),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task DurableJobWithoutTriggersIsAdded()
    {
        IScheduler scheduler = CreateFakeScheduler();
        TestProcessor processor = CreateProcessor();

        await processor.ProcessStreamAndScheduleJobs(ToStream(Document($"""
            <schedule>
              <job>
                <name>job1</name>
                <job-type>{JobType}</job-type>
                <durable>true</durable>
              </job>
            </schedule>
            """)), scheduler);

        A.CallTo(() => scheduler.AddJob(
                A<IJobDetail>.That.Matches(j => j.Key == new JobKey("job1")),
                A<AddJobOptions>.That.Matches(o => o.Replace && !o.StoreNonDurableWhileAwaitingScheduling),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task NonDurableJobWithoutTriggersIsRejected()
    {
        IScheduler scheduler = CreateFakeScheduler();
        TestProcessor processor = CreateProcessor();

        Func<Task> act = async () => await processor.ProcessStreamAndScheduleJobs(ToStream(Document($"""
            <schedule>
              <job>
                <name>job1</name>
                <job-type>{JobType}</job-type>
              </job>
            </schedule>
            """)), scheduler);

        await act.Should().ThrowAsync<SchedulerException>()
            .WithMessage("*must be durable*");
    }

    private static JobHeader JobHeaderFor(string name, string group)
    {
        return new JobHeader(new JobKey(name, group), null, JobType, true, false, false, false);
    }

    private static TriggerHeader TriggerHeaderFor(string name, string group)
    {
        return new TriggerHeader(
            new TriggerKey(name, group),
            new JobKey(name, group),
            null,
            "CRON",
            TriggerState.Normal,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            TriggerConstants.DefaultPriority,
            null);
    }
}
