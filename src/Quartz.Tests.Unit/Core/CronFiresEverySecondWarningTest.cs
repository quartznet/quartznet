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

#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// A cron trigger whose seconds field is <c>*</c> under a restricted minute or hour is scheduled as
/// written, and the scheduler says what that means — for the expressions QZ0007 never sees, because
/// they arrive from configuration or a database.
/// </summary>
public sealed class CronFiresEverySecondWarningTest
{
    private const int CronTriggerFiresEverySecondEventId = 1022;

    [Test]
    public async Task SchedulingAJobWithSuchATriggerWarns()
    {
        RecordingLoggerProvider recorder = new();
        await using ServiceProvider container = Container(recorder);
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.ScheduleJob(Job("report"), Trigger("every-fifth-minute", "* */5 * ? * *"));

        LogEntry entry = recorder.Entries.Should().ContainSingle(x => x.EventId.Id == CronTriggerFiresEverySecondEventId).Subject;
        entry.Level.Should().Be(LogLevel.Warning, "the trigger is stored and will fire sixty times a minute, which is rarely what was meant");
        entry.Message.Should().Be(
            "Cron trigger DEFAULT.every-fifth-minute's expression '* */5 * ? * *' fires every second of each minute it matches, "
            + "60 times a minute, because its seconds field is '*'; '0 */5 * ? * *' would fire once at the start of each of those minutes");
    }

    [Test]
    public async Task EverySchedulingPathWarns()
    {
        RecordingLoggerProvider recorder = new();
        await using ServiceProvider container = Container(recorder);
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        IJobDetail durable = JobBuilder.Create<NoOpJob>().WithIdentity("durable").StoreDurably().Build();
        await scheduler.AddJob(durable);

        await scheduler.ScheduleJob(Trigger("for-existing-job", "* 0 3 * * ?", durable.Key));
        await scheduler.ScheduleTrigger(Trigger("by-conflict-rule", "* 30 * * * ?", durable.Key), TriggerConflict.Replace);
        await scheduler.ScheduleJobs(
            new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [Job("batch")] = [Trigger("batched", "* * 9-17 ? * MON-FRI")] },
            default(ScheduleJobOptions));
        await scheduler.RescheduleJob(new TriggerKey("for-existing-job"), Trigger("rescheduled", "* 15 3 * * ?", durable.Key));

        recorder.Entries.Where(x => x.EventId.Id == CronTriggerFiresEverySecondEventId).Select(x => x.Message)
            .Should().SatisfyRespectively(
                x => x.Should().StartWith("Cron trigger DEFAULT.for-existing-job's"),
                x => x.Should().StartWith("Cron trigger DEFAULT.by-conflict-rule's"),
                x => x.Should().StartWith("Cron trigger DEFAULT.batched's"),
                x => x.Should().StartWith("Cron trigger DEFAULT.rescheduled's"));
    }

    [TestCase("0 */5 * ? * *", TestName = "seconds pinned to zero")]
    [TestCase("* * * * * ?", TestName = "every second, restricting nothing")]
    [TestCase("0-59 */5 * ? * *", TestName = "seconds written out as a range")]
    public async Task AnExpressionThatSaysWhatItMeansDoesNotWarn(string expression)
    {
        RecordingLoggerProvider recorder = new();
        await using ServiceProvider container = Container(recorder);
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.ScheduleJob(Job("report"), Trigger("t", expression));

        recorder.Entries.Should().NotContain(x => x.EventId.Id == CronTriggerFiresEverySecondEventId);
    }

    [Test]
    public async Task ATriggerFromConfigurationWarnsWhenTheSchedulerStoresIt()
    {
        RecordingLoggerProvider recorder = new();
        await using ServiceProvider container = Container(recorder, q => q.ScheduleJob<NoOpJob>(trigger => trigger
            .WithIdentity("configured")
            .WithCronSchedule(ReadFromConfiguration())));

        _ = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        recorder.Entries.Should().ContainSingle(x => x.EventId.Id == CronTriggerFiresEverySecondEventId)
            .Which.Message.Should().StartWith("Cron trigger DEFAULT.configured's expression '* */5 * ? * *'");
    }

    private static string ReadFromConfiguration() => "* */5 * ? * *";

    private static ServiceProvider Container(RecordingLoggerProvider recorder, Action<IQuartzBuilder>? configure = null)
    {
        ServiceCollection services = new();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(recorder);
        });
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "every-second-" + Guid.NewGuid().ToString("N"));
            configure?.Invoke(q);
        });

        return services.BuildServiceProvider();
    }

    private static IJobDetail Job(string name) => JobBuilder.Create<NoOpJob>().WithIdentity(name).Build();

    private static ITrigger Trigger(string name, string expression, JobKey? job = null)
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create().WithIdentity(name).WithCronSchedule(expression);
        return (job is null ? builder : builder.ForJob(job)).Build();
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
