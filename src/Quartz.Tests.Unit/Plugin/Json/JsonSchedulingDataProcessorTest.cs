using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using FakeItEasy;
using FluentAssertions;

using NUnit.Framework;

using Quartz.Impl.Triggers;
using Quartz.Plugin.Json;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Plugin.Json;

public class JsonSchedulingDataProcessorTest
{
    private JsonSchedulingDataProcessor CreateProcessor()
    {
        return new JsonSchedulingDataProcessor(new SimpleTypeLoadHelper());
    }

    [Test]
    public void ParsesCronTrigger()
    {
        string json = @"{
            ""Schedule"": {
                ""Jobs"": [{
                    ""Name"": ""testJob"",
                    ""JobType"": ""Quartz.Job.NativeJob, Quartz.Jobs"",
                    ""Durable"": true
                }],
                ""Triggers"": [{
                    ""Name"": ""cronTrigger"",
                    ""JobName"": ""testJob"",
                    ""Cron"": {
                        ""Expression"": ""0/10 * * * * ?"",
                        ""TimeZone"": ""UTC""
                    }
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.ParsedJobs.Should().HaveCount(1);
        processor.ParsedJobs[0].Key.Name.Should().Be("testJob");
        processor.ParsedTriggers.Should().HaveCount(1);
        processor.ParsedTriggers[0].Key.Name.Should().Be("cronTrigger");
        processor.ParsedTriggers[0].Should().BeAssignableTo<ICronTrigger>();
    }

    [Test]
    public void ParsesSimpleTrigger()
    {
        string json = @"{
            ""Schedule"": {
                ""Jobs"": [{
                    ""Name"": ""simpleJob"",
                    ""JobType"": ""Quartz.Job.NativeJob, Quartz.Jobs"",
                    ""Durable"": true
                }],
                ""Triggers"": [{
                    ""Name"": ""simpleTrigger"",
                    ""JobName"": ""simpleJob"",
                    ""Simple"": {
                        ""RepeatCount"": -1,
                        ""Interval"": ""00:00:05""
                    }
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        ISimpleTrigger trigger = (ISimpleTrigger) processor.ParsedTriggers[0];
        trigger.RepeatCount.Should().Be(-1);
        trigger.RepeatInterval.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void ParsesCalendarIntervalTrigger()
    {
        string json = @"{
            ""Schedule"": {
                ""Jobs"": [{
                    ""Name"": ""calJob"",
                    ""JobType"": ""Quartz.Job.NativeJob, Quartz.Jobs"",
                    ""Durable"": true
                }],
                ""Triggers"": [{
                    ""Name"": ""calTrigger"",
                    ""JobName"": ""calJob"",
                    ""CalendarInterval"": {
                        ""RepeatInterval"": 2,
                        ""RepeatIntervalUnit"": ""Hour""
                    }
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        ICalendarIntervalTrigger trigger = (ICalendarIntervalTrigger) processor.ParsedTriggers[0];
        trigger.RepeatInterval.Should().Be(2);
        trigger.RepeatIntervalUnit.Should().Be(IntervalUnit.Hour);
    }

    [Test]
    public void ParsesDailyTimeIntervalTrigger()
    {
        string json = @"{
            ""Schedule"": {
                ""Jobs"": [{
                    ""Name"": ""dailyJob"",
                    ""JobType"": ""Quartz.Job.NativeJob, Quartz.Jobs"",
                    ""Durable"": true
                }],
                ""Triggers"": [{
                    ""Name"": ""dailyTrigger"",
                    ""JobName"": ""dailyJob"",
                    ""DailyTimeInterval"": {
                        ""RepeatInterval"": 15,
                        ""RepeatIntervalUnit"": ""Minute"",
                        ""StartTimeOfDay"": ""08:00:00"",
                        ""EndTimeOfDay"": ""17:00:00"",
                        ""DaysOfWeek"": [""Monday"", ""Wednesday"", ""Friday""]
                    }
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        IDailyTimeIntervalTrigger trigger = (IDailyTimeIntervalTrigger) processor.ParsedTriggers[0];
        trigger.RepeatInterval.Should().Be(15);
        trigger.RepeatIntervalUnit.Should().Be(IntervalUnit.Minute);
        trigger.StartTimeOfDay.Should().Be(new TimeOfDay(8, 0, 0));
        trigger.EndTimeOfDay.Should().Be(new TimeOfDay(17, 0, 0));
        trigger.DaysOfWeek.Should().BeEquivalentTo(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday });
    }

    [Test]
    public void ParsesJobDataMap()
    {
        string json = @"{
            ""Schedule"": {
                ""Jobs"": [{
                    ""Name"": ""dataJob"",
                    ""JobType"": ""Quartz.Job.NativeJob, Quartz.Jobs"",
                    ""Durable"": true,
                    ""JobDataMap"": {
                        ""key1"": ""value1"",
                        ""key2"": ""value2""
                    }
                }],
                ""Triggers"": [{
                    ""Name"": ""dataTrigger"",
                    ""JobName"": ""dataJob"",
                    ""Cron"": { ""Expression"": ""0 0 12 * * ?"" }
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.ParsedJobs[0].JobDataMap["key1"].Should().Be("value1");
        processor.ParsedJobs[0].JobDataMap["key2"].Should().Be("value2");
    }

    [Test]
    public void ParsesJobProperties()
    {
        string json = @"{
            ""Schedule"": {
                ""Jobs"": [{
                    ""Name"": ""fullJob"",
                    ""Group"": ""myGroup"",
                    ""JobType"": ""Quartz.Job.NativeJob, Quartz.Jobs"",
                    ""Description"": ""A test job"",
                    ""Durable"": true,
                    ""Recover"": true
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        IJobDetail job = processor.ParsedJobs[0];
        job.Key.Name.Should().Be("fullJob");
        job.Key.Group.Should().Be("myGroup");
        job.Description.Should().Be("A test job");
        job.Durable.Should().BeTrue();
        job.RequestsRecovery.Should().BeTrue();
    }

    [Test]
    public void OmittedGroupDefaultsCorrectly()
    {
        string json = @"{
            ""Schedule"": {
                ""Jobs"": [{
                    ""Name"": ""noGroupJob"",
                    ""JobType"": ""Quartz.Job.NativeJob, Quartz.Jobs"",
                    ""Durable"": true
                }],
                ""Triggers"": [{
                    ""Name"": ""noGroupTrigger"",
                    ""JobName"": ""noGroupJob"",
                    ""Cron"": { ""Expression"": ""0 0 12 * * ?"" }
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.ParsedJobs[0].Key.Group.Should().Be("DEFAULT");
        processor.ParsedTriggers[0].Key.Group.Should().Be("DEFAULT");
    }

    [Test]
    public void EmptyGroupDefaultsCorrectly()
    {
        string json = @"{
            ""Schedule"": {
                ""Jobs"": [{
                    ""Name"": ""emptyGroupJob"",
                    ""Group"": """",
                    ""JobType"": ""Quartz.Job.NativeJob, Quartz.Jobs"",
                    ""Durable"": true
                }],
                ""Triggers"": [{
                    ""Name"": ""emptyGroupTrigger"",
                    ""Group"": """",
                    ""JobName"": ""emptyGroupJob"",
                    ""JobGroup"": """",
                    ""Cron"": { ""Expression"": ""0 0 12 * * ?"" }
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.ParsedJobs[0].Key.Group.Should().Be("DEFAULT");
        processor.ParsedTriggers[0].Key.Group.Should().Be("DEFAULT");
        processor.ParsedTriggers[0].JobKey.Group.Should().Be("DEFAULT");
    }

    [Test]
    public void ParsesProcessingDirectives()
    {
        string json = @"{
            ""ProcessingDirectives"": {
                ""OverWriteExistingData"": false,
                ""IgnoreDuplicates"": true,
                ""ScheduleTriggerRelativeToReplacedTrigger"": true
            },
            ""Schedule"": { ""Jobs"": [], ""Triggers"": [] }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.OverWriteExistingData.Should().BeFalse();
        processor.IgnoreDuplicates.Should().BeTrue();
        processor.ScheduleTriggerRelativeToReplacedTrigger.Should().BeTrue();
    }

    [Test]
    public void SecondProcessJsonContent_WithoutDirectives_ResetsScheduleTriggerRelativeFlag()
    {
        string jsonWithDirectives = @"{
            ""ProcessingDirectives"": {
                ""OverWriteExistingData"": false,
                ""IgnoreDuplicates"": true,
                ""ScheduleTriggerRelativeToReplacedTrigger"": true
            },
            ""Schedule"": { ""Jobs"": [], ""Triggers"": [] }
        }";

        string jsonWithoutDirectives = @"{
            ""Schedule"": { ""Jobs"": [], ""Triggers"": [] }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();

        // First load sets the flags
        processor.ProcessJsonContent(jsonWithDirectives);
        processor.ScheduleTriggerRelativeToReplacedTrigger.Should().BeTrue();
        processor.OverWriteExistingData.Should().BeFalse();
        processor.IgnoreDuplicates.Should().BeTrue();

        // Second load (hot reload) without directives should reset all flags to defaults
        processor.ProcessJsonContent(jsonWithoutDirectives);
        processor.ScheduleTriggerRelativeToReplacedTrigger.Should().BeFalse();
        processor.OverWriteExistingData.Should().BeTrue();
        processor.IgnoreDuplicates.Should().BeFalse();
    }

    [Test]
    public void ParsesExecutionGroup()
    {
        string json = @"{
            ""Schedule"": {
                ""Jobs"": [{
                    ""Name"": ""testJob"",
                    ""JobType"": ""Quartz.Job.NativeJob, Quartz.Jobs""
                }],
                ""Triggers"": [{
                    ""Name"": ""testTrigger"",
                    ""JobName"": ""testJob"",
                    ""ExecutionGroup"": ""batch"",
                    ""Cron"": { ""Expression"": ""0 0 12 * * ?"" }
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.ParsedTriggers.Should().HaveCount(1);
        Quartz.Impl.Triggers.AbstractTrigger trigger = (Quartz.Impl.Triggers.AbstractTrigger) processor.ParsedTriggers[0];
        trigger.ExecutionGroup.Should().Be("batch");
    }

    [Test]
    public void MissingJobName_Throws()
    {
        string json = @"{
            ""Schedule"": {
                ""Jobs"": [{
                    ""JobType"": ""Quartz.Job.NativeJob, Quartz.Jobs""
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();

        Action act = () => processor.ProcessJsonContent(json);
        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*missing required 'Name'*");
    }

    [Test]
    public void MissingJobType_Throws()
    {
        string json = @"{
            ""Schedule"": {
                ""Jobs"": [{
                    ""Name"": ""incomplete""
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();

        Action act = () => processor.ProcessJsonContent(json);
        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*missing required 'JobType'*");
    }

    [Test]
    public void MissingTriggerScheduleType_Throws()
    {
        string json = @"{
            ""Schedule"": {
                ""Triggers"": [{
                    ""Name"": ""noSchedule"",
                    ""JobName"": ""someJob""
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();

        Action act = () => processor.ProcessJsonContent(json);
        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*must specify exactly one schedule type*");
    }

    [Test]
    public void MultipleTriggerScheduleTypes_Throws()
    {
        string json = @"{
            ""Schedule"": {
                ""Triggers"": [{
                    ""Name"": ""multiSchedule"",
                    ""JobName"": ""someJob"",
                    ""Simple"": { ""RepeatCount"": 0, ""Interval"": ""00:00:01"" },
                    ""Cron"": { ""Expression"": ""0 0 * * * ?"" }
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();

        Action act = () => processor.ProcessJsonContent(json);
        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*multiple schedule types*");
    }

    [Test]
    public void MissingCronExpression_Throws()
    {
        string json = @"{
            ""Schedule"": {
                ""Triggers"": [{
                    ""Name"": ""noCron"",
                    ""JobName"": ""someJob"",
                    ""Cron"": { }
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();

        Action act = () => processor.ProcessJsonContent(json);
        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*missing required 'Expression'*");
    }

    [Test]
    public void StartTimeAndStartTimeSecondsInFuture_MutuallyExclusive()
    {
        string json = @"{
            ""Schedule"": {
                ""Triggers"": [{
                    ""Name"": ""conflictTrigger"",
                    ""JobName"": ""someJob"",
                    ""StartTime"": ""2024-01-01T00:00:00Z"",
                    ""StartTimeSecondsInFuture"": 30,
                    ""Cron"": { ""Expression"": ""0 0 * * * ?"" }
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();

        Action act = () => processor.ProcessJsonContent(json);
        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*mutually exclusive*");
    }

    [Test]
    public async Task ProcessJsonFileAndScheduleJobs_FullRoundTrip()
    {
        string json = @"{
            ""ProcessingDirectives"": {
                ""OverWriteExistingData"": true
            },
            ""Schedule"": {
                ""Jobs"": [{
                    ""Name"": ""fileJob"",
                    ""Group"": ""fileGroup"",
                    ""JobType"": ""Quartz.Job.NativeJob, Quartz.Jobs"",
                    ""Durable"": true
                }],
                ""Triggers"": [{
                    ""Name"": ""fileTrigger"",
                    ""Group"": ""fileGroup"",
                    ""JobName"": ""fileJob"",
                    ""JobGroup"": ""fileGroup"",
                    ""Cron"": { ""Expression"": ""0/30 * * * * ?"" }
                }]
            }
        }";

        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);

            IScheduler mockScheduler = A.Fake<IScheduler>();
            A.CallTo(() => mockScheduler.GetJobGroupNames(A<System.Threading.CancellationToken>._))
                .Returns(Array.Empty<string>());
            A.CallTo(() => mockScheduler.GetTriggerGroupNames(A<System.Threading.CancellationToken>._))
                .Returns(Array.Empty<string>());

            JsonSchedulingDataProcessor processor = CreateProcessor();
            await processor.ProcessJsonFileAndScheduleJobs(tempFile, mockScheduler);

            A.CallTo(() => mockScheduler.AddJob(
                    A<IJobDetail>.That.Matches(j => j.Key.Name == "fileJob"),
                    A<bool>._,
                    A<bool>._,
                    A<System.Threading.CancellationToken>._))
                .MustHaveHappened();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public void NullJsonContent_Throws()
    {
        JsonSchedulingDataProcessor processor = CreateProcessor();

        Action act = () => processor.ProcessJsonContent("null");
        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*null after deserialization*");
    }

    [Test]
    public void EmptySchedule_ProducesNoJobsOrTriggers()
    {
        string json = @"{ ""Schedule"": { ""Jobs"": [], ""Triggers"": [] } }";

        JsonSchedulingDataProcessor processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.ParsedJobs.Should().BeEmpty();
        processor.ParsedTriggers.Should().BeEmpty();
    }

    [Test]
    public void TriggerPriorityAndDescription_Parsed()
    {
        string json = @"{
            ""Schedule"": {
                ""Triggers"": [{
                    ""Name"": ""priorityTrigger"",
                    ""JobName"": ""someJob"",
                    ""Description"": ""High priority trigger"",
                    ""Priority"": 10,
                    ""Cron"": { ""Expression"": ""0 0 12 * * ?"" }
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        ITrigger trigger = processor.ParsedTriggers[0];
        trigger.Description.Should().Be("High priority trigger");
        trigger.Priority.Should().Be(10);
    }

    [Test]
    public void TriggerJobDataMap_Parsed()
    {
        string json = @"{
            ""Schedule"": {
                ""Triggers"": [{
                    ""Name"": ""dataTrigger"",
                    ""JobName"": ""someJob"",
                    ""Cron"": { ""Expression"": ""0 0 12 * * ?"" },
                    ""JobDataMap"": { ""triggerKey"": ""triggerValue"" }
                }]
            }
        }";

        JsonSchedulingDataProcessor processor = CreateProcessor();
        processor.ProcessJsonContent(json);

        processor.ParsedTriggers[0].JobDataMap["triggerKey"].Should().Be("triggerValue");
    }
}
