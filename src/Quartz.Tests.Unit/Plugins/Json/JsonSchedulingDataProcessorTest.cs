
using System.Collections.Specialized;

using Microsoft.Extensions.Logging.Abstractions;

using Quartz.Impl;
using Quartz.Plugins.Json;

namespace Quartz.Tests.Unit.Plugin.Json;

public class JsonSchedulingDataProcessorTest
{
    private static JsonSchedulingDataProcessor CreateProcessor() =>
        new(NullLogger<JsonSchedulingDataProcessor>.Instance, new SimpleTypeLoader(), TimeProvider.System);

    [Test]
    public void ParsesCronTrigger()
    {
        var json = """
        {
            "Schedule": {
                "Jobs": [{ "Name": "testJob", "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs", "Durable": true }],
                "Triggers": [{ "Name": "cronTrigger", "JobName": "testJob", "Cron": { "Expression": "0/10 * * * * ?" } }]
            }
        }
        """;

        var processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.ParsedJobs.Should().HaveCount(1);
        processor.ParsedTriggers.Should().HaveCount(1);
        processor.ParsedTriggers[0].Should().BeAssignableTo<ICronTrigger>();
    }

    [Test]
    public void ParsesSimpleTrigger()
    {
        var json = """
        {
            "Schedule": {
                "Jobs": [{ "Name": "sJob", "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs", "Durable": true }],
                "Triggers": [{ "Name": "sTrigger", "JobName": "sJob", "Simple": { "RepeatCount": -1, "Interval": "00:00:05" } }]
            }
        }
        """;

        var processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        var trigger = (ISimpleTrigger) processor.ParsedTriggers[0];
        trigger.RepeatCount.Should().Be(-1);
        trigger.RepeatInterval.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void ParsesCalendarIntervalTrigger()
    {
        var json = """
        {
            "Schedule": {
                "Jobs": [{ "Name": "cJob", "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs", "Durable": true }],
                "Triggers": [{ "Name": "cTrigger", "JobName": "cJob", "CalendarInterval": { "RepeatInterval": 2, "RepeatIntervalUnit": "Hour" } }]
            }
        }
        """;

        var processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        var trigger = (ICalendarIntervalTrigger) processor.ParsedTriggers[0];
        trigger.RepeatInterval.Should().Be(2);
        trigger.RepeatIntervalUnit.Should().Be(IntervalUnit.Hour);
    }

    [Test]
    public void ParsesDailyTimeIntervalTrigger()
    {
        var json = """
        {
            "Schedule": {
                "Jobs": [{ "Name": "dJob", "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs", "Durable": true }],
                "Triggers": [{
                    "Name": "dTrigger", "JobName": "dJob",
                    "DailyTimeInterval": {
                        "RepeatInterval": 15, "RepeatIntervalUnit": "Minute",
                        "StartTimeOfDay": "08:00:00", "EndTimeOfDay": "17:00:00",
                        "DaysOfWeek": ["Monday", "Wednesday", "Friday"]
                    }
                }]
            }
        }
        """;

        var processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        var trigger = (IDailyTimeIntervalTrigger) processor.ParsedTriggers[0];
        trigger.RepeatInterval.Should().Be(15);
        trigger.RepeatIntervalUnit.Should().Be(IntervalUnit.Minute);
        trigger.StartTimeOfDay.Should().Be(new TimeOnly(8, 0, 0));
        trigger.EndTimeOfDay.Should().Be(new TimeOnly(17, 0, 0));
        trigger.DaysOfWeek.Should().BeEquivalentTo(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday });
    }

    /// <summary>
    /// A recurrence rule is a schedule a file can declare, and everything the declaration says has to
    /// reach the trigger: the rule, the anchor it repeats from, the misfire instruction and the zone
    /// the rule's days and times are read in.
    /// </summary>
    /// <remarks>
    /// The anchor is the trigger's own <c>StartTime</c> rather than a field of the recurrence section:
    /// a rule repeats from a moment the way <c>DTSTART</c> does, and every other schedule type in this
    /// format already starts from that field.
    /// </remarks>
    [Test]
    public void ParsesRecurrenceTrigger()
    {
        var json = """
        {
            "Schedule": {
                "Jobs": [{ "Name": "rJob", "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs", "Durable": true }],
                "Triggers": [{
                    "Name": "rTrigger", "JobName": "rJob",
                    "StartTime": "2026-01-05T09:00:00Z",
                    "Recurrence": {
                        "Rule": "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO",
                        "TimeZone": "America/New_York",
                        "MisfireInstruction": "DoNothing"
                    }
                }]
            }
        }
        """;

        var processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        var trigger = (IRecurrenceTrigger) processor.ParsedTriggers[0];
        trigger.RecurrenceRule.Should().Be("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO");
        trigger.StartTimeUtc.Should().Be(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
            "every second week is counted from somewhere, and the trigger's start time is where");
        trigger.MisfireInstruction.Should().Be(RecurrenceTriggerMisfireInstruction.DoNothing);
        trigger.TimeZone.Should().Be(TimeZones.FindById("America/New_York"),
            "a rule naming a day names it in some zone, and a file read on a machine in another one has to fire the same");
    }

    [Test]
    public void RejectsAnUnparseableRecurrenceRule()
    {
        var json = """
        {
            "Schedule": {
                "Jobs": [{ "Name": "rJob", "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs", "Durable": true }],
                "Triggers": [{ "Name": "rTrigger", "JobName": "rJob", "Recurrence": { "Rule": "FREQ=FORTNIGHTLY" } }]
            }
        }
        """;

        var processor = CreateProcessor();

        Action act = () => processor.ProcessJsonContent(json);

        act.Should().Throw<SchedulerConfigException>(
                "a rule nobody can parse is a mistake to report while the scheduler is still starting")
            .WithMessage("*FREQ=FORTNIGHTLY*");
    }

    [Test]
    public void ParsesJobDataMap()
    {
        var json = """
        {
            "Schedule": {
                "Jobs": [{ "Name": "dataJob", "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs", "Durable": true, "JobDataMap": { "k1": "v1" } }],
                "Triggers": [{ "Name": "dt", "JobName": "dataJob", "Cron": { "Expression": "0 0 12 * * ?" } }]
            }
        }
        """;

        var processor = CreateProcessor();
        processor.ProcessJsonContent(json);
        processor.ParsedJobs[0].JobDataMap["k1"].Should().Be("v1");
    }

    [Test]
    public void OmittedGroupDefaultsCorrectly()
    {
        var json = """
        {
            "Schedule": {
                "Jobs": [{ "Name": "noGrpJob", "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs", "Durable": true }],
                "Triggers": [{ "Name": "noGrpTrigger", "JobName": "noGrpJob", "Cron": { "Expression": "0 0 12 * * ?" } }]
            }
        }
        """;

        var processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.ParsedJobs[0].Key.Group.Should().Be(JobKey.DefaultGroup);
        processor.ParsedTriggers[0].Key.Group.Should().Be(TriggerKey.DefaultGroup);
    }

    [Test]
    public void EmptyGroupDefaultsCorrectly()
    {
        var json = """
        {
            "Schedule": {
                "Jobs": [{ "Name": "eGrpJob", "Group": "", "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs", "Durable": true }],
                "Triggers": [{ "Name": "eGrpTrigger", "Group": "", "JobName": "eGrpJob", "JobGroup": "", "Cron": { "Expression": "0 0 12 * * ?" } }]
            }
        }
        """;

        var processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.ParsedJobs[0].Key.Group.Should().Be(JobKey.DefaultGroup);
        processor.ParsedTriggers[0].Key.Group.Should().Be(TriggerKey.DefaultGroup);
    }

    [Test]
    public void ParsesProcessingDirectives()
    {
        var json = """{ "ProcessingDirectives": { "OverwriteExistingData": false, "IgnoreDuplicates": true }, "Schedule": {} }""";
        var processor = CreateProcessor();
        processor.ProcessJsonContent(json);
        processor.OverwriteExistingData.Should().BeFalse();
        processor.IgnoreDuplicates.Should().BeTrue();
    }

    [Test]
    public void SecondProcessJsonContent_WithoutDirectives_ResetsScheduleTriggerRelativeFlag()
    {
        var jsonWithDirectives = """
        {
            "ProcessingDirectives": {
                "OverwriteExistingData": false,
                "IgnoreDuplicates": true,
                "ScheduleTriggerRelativeToReplacedTrigger": true
            },
            "Schedule": { "Jobs": [], "Triggers": [] }
        }
        """;

        var jsonWithoutDirectives = """{ "Schedule": { "Jobs": [], "Triggers": [] } }""";

        var processor = CreateProcessor();

        processor.ProcessJsonContent(jsonWithDirectives);
        processor.ScheduleTriggerRelativeToReplacedTrigger.Should().BeTrue();
        processor.OverwriteExistingData.Should().BeFalse();
        processor.IgnoreDuplicates.Should().BeTrue();

        // Second load (hot reload) without directives should reset all flags to defaults
        processor.ProcessJsonContent(jsonWithoutDirectives);
        processor.ScheduleTriggerRelativeToReplacedTrigger.Should().BeFalse();
        processor.OverwriteExistingData.Should().BeTrue();
        processor.IgnoreDuplicates.Should().BeFalse();
    }

    [Test]
    public void ParsesExecutionGroup()
    {
        var json = """
        {
            "Schedule": {
                "Jobs": [{ "Name": "testJob", "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs" }],
                "Triggers": [{ "Name": "testTrigger", "JobName": "testJob", "ExecutionGroup": "batch", "Cron": { "Expression": "0 0 12 * * ?" } }]
            }
        }
        """;

        var processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.ParsedTriggers.Should().HaveCount(1);
        var trigger = (Quartz.Impl.Triggers.TriggerBase) processor.ParsedTriggers[0];
        trigger.ExecutionGroup.Should().Be("batch");
    }

    [Test]
    public void MissingJobName_Throws()
    {
        var json = """{ "Schedule": { "Jobs": [{ "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs" }] } }""";
        var processor = CreateProcessor();
        var act = () => processor.ProcessJsonContent(json);
        act.Should().Throw<SchedulerConfigException>().WithMessage("*missing required 'Name'*");
    }

    [Test]
    public void MissingTriggerScheduleType_Throws()
    {
        var json = """{ "Schedule": { "Triggers": [{ "Name": "t", "JobName": "j" }] } }""";
        var processor = CreateProcessor();
        var act = () => processor.ProcessJsonContent(json);
        act.Should().Throw<SchedulerConfigException>().WithMessage("*must specify exactly one*");
    }

    [Test]
    public void MultipleTriggerScheduleTypes_Throws()
    {
        var json = """
        { "Schedule": { "Triggers": [{
            "Name": "multi", "JobName": "j",
            "Simple": { "RepeatCount": 0, "Interval": "00:00:01" },
            "Cron": { "Expression": "0 0 * * * ?" }
        }] } }
        """;

        var processor = CreateProcessor();
        var act = () => processor.ProcessJsonContent(json);
        act.Should().Throw<SchedulerConfigException>().WithMessage("*multiple schedule types*");
    }

    [Test]
    public void StartTimeAndFuture_MutuallyExclusive()
    {
        var json = """
        { "Schedule": { "Triggers": [{
            "Name": "conflict", "JobName": "j",
            "StartTime": "2024-01-01T00:00:00Z", "StartTimeSecondsInFuture": 30,
            "Cron": { "Expression": "0 0 * * * ?" }
        }] } }
        """;

        var processor = CreateProcessor();
        var act = () => processor.ProcessJsonContent(json);
        act.Should().Throw<SchedulerConfigException>().WithMessage("*mutually exclusive*");
    }

    /// <summary>
    /// The guard lives in <c>XmlSchedulingDataProcessor.AddTriggerToSchedule</c>, the funnel this
    /// processor's own parsing calls, so it is inherited rather than written twice.
    /// </summary>
    [Test]
    public void TwoTriggersWithOneNameAndGroup_Throws()
    {
        string json = """
        {
            "Schedule": {
                "Jobs": [{ "Name": "dupJob", "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs", "Durable": true }],
                "Triggers": [
                    { "Name": "dupTrigger", "JobName": "dupJob", "Cron": { "Expression": "0/10 * * * * ?" } },
                    { "Name": "dupTrigger", "JobName": "dupJob", "Cron": { "Expression": "0 0 12 * * ?" } }
                ]
            }
        }
        """;

        JsonSchedulingDataProcessor processor = CreateProcessor();
        Action act = () => processor.ProcessJsonContent(json);

        act.Should().Throw<SchedulingDataValidationException>(
                "the JSON loader inherits the XML processor's duplicate-key guard, so the flaw cannot be fixed on one side only")
            .WithMessage("*Trigger 'DEFAULT.dupTrigger' is defined more than once in the scheduling data.*");
    }

    [Test]
    public void NullJsonContent_Throws()
    {
        var processor = CreateProcessor();
        var act = () => processor.ProcessJsonContent("null");
        act.Should().Throw<SchedulerConfigException>().WithMessage("*null after deserialization*");
    }

    [Test]
    public void EmptySchedule_ProducesNoJobsOrTriggers()
    {
        var processor = CreateProcessor();
        processor.ProcessJsonContent("""{ "Schedule": { "Jobs": [], "Triggers": [] } }""");
        processor.ParsedJobs.Should().BeEmpty();
        processor.ParsedTriggers.Should().BeEmpty();
    }

    /// <summary>
    /// A schedule file is written by hand, so the reader has always allowed comments, a trailing comma
    /// and whatever casing its author chose. Those three settings moved from a JsonSerializerOptions
    /// instance onto the generated context's <c>[JsonSourceGenerationOptions]</c> when the file stopped
    /// being read by reflection, and this is what says they moved rather than were dropped — a file like
    /// the one below parses with none of them and fails with any one missing.
    /// </summary>
    [Test]
    public void AHandWrittenFileKeepsItsComments_TrailingCommas_AndCasing()
    {
        var json = """
        {
            // the schedule this deployment runs
            "schedule": {
                "jobs": [{ "name": "handWritten", "jobType": "Quartz.Jobs.NativeJob, Quartz.Jobs", "durable": true, }],
                /* one trigger for now */
                "triggers": [{ "name": "handWrittenTrigger", "jobName": "handWritten", "simple": { "repeatCount": 0, "interval": "00:00:30" } }],
            },
        }
        """;

        var processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.ParsedJobs.Should().ContainSingle(
            "camelCased property names are the ones a hand-written file most often uses, and the reader "
            + "has always matched them case-insensitively")
            .Which.Key.Name.Should().Be("handWritten");

        ((ISimpleTrigger) processor.ParsedTriggers[0]).RepeatInterval.Should().Be(TimeSpan.FromSeconds(30),
            "the trigger has to survive the comments and the trailing commas around it, not merely the job");
    }

    /// <summary>
    /// The <c>quartz_jobs.json</c> the quick start prints, read by the reader that reads it. The only
    /// substitution is the job type, which the page names as the reader's own <c>MyApp.HelloJob</c>.
    /// </summary>
    [Test]
    public void TheQuickStartFileIsReadable()
    {
        var json = """
        {
          "Schedule": {
            "Jobs": [
              {
                "Name": "helloJob",
                "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs",
                "Durable": true
              }
            ],
            "Triggers": [
              {
                "Name": "helloTrigger",
                "JobName": "helloJob",
                "Simple": {
                  "RepeatCount": -1,
                  "Interval": "00:00:10"
                }
              }
            ]
          }
        }
        """;

        var processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.ParsedJobs.Should().ContainSingle()
            .Which.Key.Name.Should().Be("helloJob", "a documented file that does not parse is worse than none");

        var trigger = (ISimpleTrigger) processor.ParsedTriggers.Should().ContainSingle().Which;
        trigger.RepeatInterval.Should().Be(TimeSpan.FromSeconds(10));
        trigger.RepeatCount.Should().Be(-1);
        trigger.JobKey.Name.Should().Be("helloJob");
    }

    [Test]
    public async Task DeleteInAllGroups_SkipsProtectedGroups()
    {
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.instanceName"] = "JsonDeleteAllGroups_" + Guid.NewGuid().ToString("N"),
            ["quartz.threadPool.threadCount"] = "1"
        };
        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseProperties(properties).BuildScheduler();
        try
        {
            foreach (string group in new[] { "keep", "drop" })
            {
                JobKey jobKey = new("job1", group);
                await scheduler.ScheduleJob(
                    JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build(),
                    TriggerBuilder.Create().WithIdentity("trigger1", group).ForJob(jobKey).WithCronSchedule("0 0 1 * * ?").Build());
            }

            var processor = CreateProcessor();
            processor.ProtectJobGroup("keep");
            processor.ProtectTriggerGroup("keep");

            string fileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            await File.WriteAllTextAsync(
                fileName,
                """{ "PreProcessingCommands": { "DeleteJobsInGroup": ["*"], "DeleteTriggersInGroup": ["*"] } }""");
            try
            {
                await processor.ProcessJsonFileAndScheduleJobs(fileName, scheduler);
            }
            finally
            {
                File.Delete(fileName);
            }

            List<JobKey> jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
            jobKeys.Should().Equal([new JobKey("job1", "keep")], "only the unprotected group is deleted");

            List<TriggerKey> triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
            triggerKeys.Should().Equal([new TriggerKey("trigger1", "keep")], "the protected trigger group survives too");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }
}
