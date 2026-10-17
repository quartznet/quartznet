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

namespace Quartz.Tests.Unit;

/// <summary>
/// A cron trigger with <c>*</c> in its seconds field under a restricted minute or hour fires every second
/// of each minute it matches. Analyzer QZ0007 reports a literal; the scheduler logs event 5112 for an
/// expression the compiler never saw.
/// </summary>
public class CronWildcardSecondsTest
{
    private const int WildcardSecondsEventId = 5112;

    private const string EverySecondOfEveryFifthMinute = "* */5 * ? * *";

    private const string EveryFifthMinute = "0 */5 * ? * *";

    [TestCase("* */5 * ? * *", true, "every second of every fifth minute")]
    [TestCase("* 0 9 * * ?", true, "every second of 09:00")]
    [TestCase("* * 9-17 * * ?", true, "every second from 09:00 to 17:59")]
    [TestCase("* H * * * ?", true, "an H resolves to one minute")]
    [TestCase("*,5 */5 * ? * *", true, "a listed '*' still stores the wildcard")]
    [TestCase("* */5 * ? * * 2099", true, "the year field changes nothing")]
    [TestCase("* * * * * ?", false, "nothing is restricted, so every second is plainly what it says")]
    [TestCase("0-59 */5 * ? * *", false, "0-59 is every second written on purpose")]
    [TestCase("*/1 */5 * ? * *", false, "*/1 is every second written on purpose")]
    [TestCase("0 */5 * ? * *", false, "once every five minutes")]
    [TestCase("* */1 * * * ?", false, "*/1 names every minute, so the minute field restricts nothing")]
    [TestCase("* 0-59 0-23 * * ?", false, "fields naming every value restrict nothing however they are spelled")]
    [TestCase("* * * 1 * ?", false, "only the minute and the hour count; a restricted day is every second of a day")]
    public void TheShapeIsReadFromTheParsedFields(string expression, bool expected, string because)
    {
        CronExpression parsed = CronExpression.ParseWithHash(expression, "key");

        parsed.HasWildcardSecondsUnderRestrictedMinuteOrHour.Should().Be(expected, because);
    }

    [Test]
    public void AUnixExpressionIsNeverTheShape()
    {
        CronExpression parsed = CronExpression.Parse("* */5 * * *", CronFormat.Unix);

        parsed.HasWildcardSecondsUnderRestrictedMinuteOrHour.Should().BeFalse(
            "the five-field form is read with a seconds field of 0, so it has no '*' there to warn about");
    }

    [TestCase("* */5 * ? * *", "0 */5 * ? * *")]
    [TestCase("  *\t*/5  * ? * mon-fri ", "0\t*/5  * ? * mon-fri")]
    [TestCase("*", "0")]
    public void TheSuggestionReplacesOnlyTheSecondsField(string expression, string expected)
    {
        CronExpression.WithSecondsAtZero(expression).Should().Be(expected,
            "the rest of the expression is the author's, spelled as they spelled it");
    }

    public static IEnumerable<TestCaseData> SchedulingCalls()
    {
        yield return Call("ScheduleJob(job, trigger)", (scheduler, job, trigger) => scheduler.ScheduleJob(job, trigger).AsTask());
        yield return Call("ScheduleJob(trigger)", async (scheduler, job, trigger) =>
        {
            await scheduler.AddJob(job);
            await scheduler.ScheduleJob(trigger);
        });
        yield return Call("ScheduleTrigger", async (scheduler, job, trigger) =>
        {
            await scheduler.AddJob(job);
            await scheduler.ScheduleTrigger(trigger, TriggerConflict.Replace);
        });
        yield return Call("ScheduleJob(job, triggers)", (scheduler, job, trigger) => scheduler.ScheduleJob(job, [trigger]).AsTask());
        yield return Call("ScheduleJobs", (scheduler, job, trigger) =>
            scheduler.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [job] = [trigger] }).AsTask());
        yield return Call("RescheduleJob", async (scheduler, job, trigger) =>
        {
            await scheduler.ScheduleJob(job, Cron(trigger.Key.Name, EveryFifthMinute));
            await scheduler.RescheduleJob(trigger.Key, trigger);
        });

        static TestCaseData Call(string name, Func<IScheduler, IJobDetail, ITrigger, Task> schedule) =>
            new TestCaseData(schedule).SetArgDisplayNames(name);
    }

    [TestCaseSource(nameof(SchedulingCalls))]
    public async Task SchedulingTheShapeLogsOneWarning(Func<IScheduler, IJobDetail, ITrigger, Task> schedule)
    {
        RecordingLoggerProvider recorder = new();
        await using ServiceProvider container = Container(recorder);
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            await schedule(scheduler, Job(), Cron("every-second", EverySecondOfEveryFifthMinute));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }

        LogEntry entry = recorder.Entries.Where(x => x.EventId.Id == WildcardSecondsEventId).Should()
            .ContainSingle("one scheduling call is one warning, whichever call stored the trigger").Subject;

        entry.Level.Should().Be(LogLevel.Warning, "the trigger will fire sixty times a minute, which is rarely what was meant");
        entry.Message.Should().Be(
            "Cron trigger 'DEFAULT.every-second' with '* */5 * ? * *' fires 60 times in each minute it matches, "
            + "because its seconds field is '*'; write '0 */5 * ? * *' to fire once in each of those minutes, "
            + "or '0-59' for the seconds if every second is intended",
            "the warning names the trigger, what it was scheduled with, and both ways out, as QZ0007 does");
    }

    [TestCase(EveryFifthMinute)]
    [TestCase("0-59 */5 * ? * *")]
    [TestCase("* * * * * ?")]
    public async Task SchedulingAnExpressionThatSaysWhatItMeansLogsNothing(string expression)
    {
        RecordingLoggerProvider recorder = new();
        await using ServiceProvider container = Container(recorder);
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            await scheduler.ScheduleJob(Job(), Cron("quiet", expression));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }

        recorder.Entries.Should().NotContain(x => x.EventId.Id == WildcardSecondsEventId,
            "a seconds field of 0, an explicit 0-59 and an unrestricted '*' each say what they mean");
    }

    /// <summary>
    /// The case the analyzer cannot see: an expression that arrives as data, here through <c>AddQuartz</c>
    /// from a string built at run time.
    /// </summary>
    [Test]
    public async Task AnExpressionFromConfigurationIsWarnedOfWhenTheSchedulerStoresIt()
    {
        RecordingLoggerProvider recorder = new();
        string configured = string.Join(" ", "*", "*/5", "*", "?", "*", "*");

        ServiceCollection services = new();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(recorder);
        });
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "wildcard-seconds-" + Guid.NewGuid().ToString("N"));
            q.ScheduleJob<NothingJob>(trigger => trigger.WithIdentity("configured").WithCronSchedule(configured));
        });

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Shutdown(waitForJobsToComplete: false);

        recorder.Entries.Should().ContainSingle(x => x.EventId.Id == WildcardSecondsEventId,
            "a schedule declared through AddQuartz is stored through the scheduler like any other");
    }

    private static ServiceProvider Container(RecordingLoggerProvider recorder)
    {
        ServiceCollection services = new();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(recorder);
        });
        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = "wildcard-seconds-" + Guid.NewGuid().ToString("N")));
        return services.BuildServiceProvider();
    }

    private static IJobDetail Job() => JobBuilder.Create<NothingJob>().WithIdentity("job").StoreDurably().Build();

    private static ITrigger Cron(string name, string expression) =>
        TriggerBuilder.Create().WithIdentity(name).ForJob("job").WithCronSchedule(expression).Build();

    private sealed class NothingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
