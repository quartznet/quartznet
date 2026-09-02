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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Util;
using Quartz.Xml;

namespace Quartz.Plugins.Json;

/// <summary>
/// Parses a JSON file that declares jobs and their schedules (triggers),
/// and schedules them with the scheduler. This is the JSON analog of
/// <see cref="XmlSchedulingDataProcessor"/>.
/// </summary>
internal sealed class JsonSchedulingDataProcessor : XmlSchedulingDataProcessor
{
    public const string QuartzJsonFileName = "quartz_jobs.json";

    /// <summary>
    /// What a schedule file spells as text and the trimmer therefore cannot follow. The wording matches
    /// <see cref="XmlSchedulingDataProcessor" />'s, because it is the same contract in a different file
    /// format: the shape of the file is metadata the compiler wrote, and the job type inside it is not.
    /// </summary>
    private const string JobTypeNamedByString =
        "Register every job type with AddJob<T>() or reference it from JobBuilder.Create<T>(); a type named only by a string in quartz_jobs.json is not guaranteed to survive trimming.";

    private readonly ILogger<JsonSchedulingDataProcessor> logger;
    private readonly TimeProvider timeProvider;

    private readonly List<string> jsonJobGroupsToDelete = [];
    private readonly List<string> jsonTriggerGroupsToDelete = [];
    private readonly List<JobKey> jsonJobsToDelete = [];
    private readonly List<TriggerKey> jsonTriggersToDelete = [];

    private readonly HashSet<string> protectedJobGroups = [];
    private readonly HashSet<string> protectedTriggerGroups = [];

    public JsonSchedulingDataProcessor(
        ILogger<JsonSchedulingDataProcessor> logger,
        ITypeLoader typeLoader,
        TimeProvider timeProvider)
        : base(LogProvider.CreateLogger<XmlSchedulingDataProcessor>(), typeLoader, timeProvider)
    {
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    internal IReadOnlyList<IJobDetail> ParsedJobs => LoadedJobs;
    internal IReadOnlyList<ITrigger> ParsedTriggers => LoadedTriggers;

    internal void ProtectJobGroup(string groupName) => protectedJobGroups.Add(groupName);
    internal void ProtectTriggerGroup(string groupName) => protectedTriggerGroups.Add(groupName);

    [RequiresUnreferencedCode(JobTypeNamedByString)]
    public async Task ProcessJsonFileAndScheduleJobs(
        string fileName,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        fileName = FileUtil.ResolveFile(fileName) ?? fileName;

        logger.ParsingFile(fileName);

        var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        string json;
        try
        {
            using var reader = new StreamReader(stream);
            json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        ProcessJsonContent(json);
        await ExecuteJsonPreProcessCommands(scheduler, cancellationToken).ConfigureAwait(false);
        await ScheduleJobs(scheduler, cancellationToken).ConfigureAwait(false);
    }

    [RequiresUnreferencedCode(JobTypeNamedByString)]
    internal void ProcessJsonContent(string json)
    {
        PrepareForProcessing();
        jsonJobGroupsToDelete.Clear();
        jsonTriggerGroupsToDelete.Clear();
        jsonJobsToDelete.Clear();
        jsonTriggersToDelete.Clear();

        JsonJobSchedulingData data = JsonSerializer.Deserialize(json, JsonSchedulingDataContext.Default.JsonJobSchedulingData)
            ?? throw new SchedulerConfigException("Job definition data from JSON was null after deserialization.");

        if (data.PreProcessingCommands is not null)
        {
            ExtractPreProcessingCommands(data.PreProcessingCommands);
        }

        if (data.ProcessingDirectives is not null)
        {
            OverwriteExistingData = data.ProcessingDirectives.OverwriteExistingData;
            IgnoreDuplicates = data.ProcessingDirectives.IgnoreDuplicates;
            ScheduleTriggerRelativeToReplacedTrigger = data.ProcessingDirectives.ScheduleTriggerRelativeToReplacedTrigger;
        }

        if (data.Schedule is not null)
        {
            if (data.Schedule.Jobs is not null) ProcessJobs(data.Schedule.Jobs);
            if (data.Schedule.Triggers is not null) ProcessTriggers(data.Schedule.Triggers);
        }
    }

    private void ExtractPreProcessingCommands(JsonPreProcessingCommands commands)
    {
        if (commands.DeleteJobsInGroup is not null)
        {
            foreach (var group in commands.DeleteJobsInGroup)
            {
                var trimmed = group.NullSafeTrim();
                if (!string.IsNullOrEmpty(trimmed)) jsonJobGroupsToDelete.Add(trimmed!);
            }
        }

        if (commands.DeleteTriggersInGroup is not null)
        {
            foreach (var group in commands.DeleteTriggersInGroup)
            {
                var trimmed = group.NullSafeTrim();
                if (!string.IsNullOrEmpty(trimmed)) jsonTriggerGroupsToDelete.Add(trimmed!);
            }
        }

        if (commands.DeleteJobs is not null)
        {
            foreach (var cmd in commands.DeleteJobs)
            {
                var name = cmd.Name?.TrimEmptyToNull()
                    ?? throw new SchedulerConfigException("Encountered a 'DeleteJobs' command without a name specified.");
                var group = NormalizeEmpty(cmd.Group);
                jsonJobsToDelete.Add(group is not null ? new JobKey(name, group) : new JobKey(name));
            }
        }

        if (commands.DeleteTriggers is not null)
        {
            foreach (var cmd in commands.DeleteTriggers)
            {
                var name = cmd.Name?.TrimEmptyToNull()
                    ?? throw new SchedulerConfigException("Encountered a 'DeleteTriggers' command without a name specified.");
                var group = NormalizeEmpty(cmd.Group);
                jsonTriggersToDelete.Add(group is not null ? new TriggerKey(name, group) : new TriggerKey(name));
            }
        }
    }

    private async Task ExecuteJsonPreProcessCommands(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        foreach (var group in jsonJobGroupsToDelete)
        {
            if (group.Equals("*", StringComparison.Ordinal))
            {
                logger.DeletingAllJobsInAllGroups();
                // deliberately unbounded: deleting only the first page would leave survivors behind
                PagedResult<JobHeader> allJobs = await scheduler.QueryJobs(new JobQuery { Take = PagedQuery.All }, cancellationToken).ConfigureAwait(false);
                foreach (JobHeader job in allJobs.Items)
                {
                    if (protectedJobGroups.Contains(job.Key.Group)) continue;
                    await scheduler.DeleteJob(job.Key, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (!protectedJobGroups.Contains(group))
            {
                logger.DeletingAllJobsInGroup(group);
                foreach (var key in await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group), cancellationToken).ConfigureAwait(false))
                {
                    await scheduler.DeleteJob(key, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        foreach (var group in jsonTriggerGroupsToDelete)
        {
            if (group.Equals("*", StringComparison.Ordinal))
            {
                logger.DeletingAllTriggersInAllGroups();
                // deliberately unbounded: unscheduling only the first page would leave survivors behind
                PagedResult<TriggerHeader> allTriggers = await scheduler.QueryTriggers(new TriggerQuery { Take = PagedQuery.All }, cancellationToken).ConfigureAwait(false);
                foreach (TriggerHeader trigger in allTriggers.Items)
                {
                    if (protectedTriggerGroups.Contains(trigger.Key.Group)) continue;
                    await scheduler.UnscheduleJob(trigger.Key, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (!protectedTriggerGroups.Contains(group))
            {
                logger.DeletingAllTriggersInGroup(group);
                foreach (var key in await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(group), cancellationToken).ConfigureAwait(false))
                {
                    await scheduler.UnscheduleJob(key, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        foreach (var key in jsonJobsToDelete)
        {
            if (!protectedJobGroups.Contains(key.Group))
            {
                logger.DeletingJob(key);
                await scheduler.DeleteJob(key, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var key in jsonTriggersToDelete)
        {
            if (!protectedTriggerGroups.Contains(key.Group))
            {
                logger.DeletingTrigger(key);
                await scheduler.UnscheduleJob(key, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [RequiresUnreferencedCode(JobTypeNamedByString)]
    private void ProcessJobs(List<JsonFileJobDefinition> jobDefs)
    {
        foreach (var jobDef in jobDefs)
        {
            var jobName = jobDef.Name?.TrimEmptyToNull()
                ?? throw new SchedulerConfigException("JSON job definition is missing required 'Name' property.");
            var jobTypeName = jobDef.JobType?.TrimEmptyToNull()
                ?? throw new SchedulerConfigException($"JSON job definition '{jobName}' is missing required 'JobType' property.");

            var jobType = TypeLoader.LoadType(jobTypeName)
                ?? throw new SchedulerConfigException($"JSON job definition '{jobName}': could not load type '{jobTypeName}'.");

            var jobGroup = NormalizeEmpty(jobDef.Group);
            var builder = JobBuilder.Create().OfType(jobType);
            if (jobGroup is not null) builder.WithIdentity(jobName, jobGroup);
            else builder.WithIdentity(jobName);

            var jobDetail = builder
                .WithDescription(jobDef.Description?.TrimEmptyToNull())
                .StoreDurably(jobDef.Durable)
                .RequestRecovery(jobDef.Recover)
                .Build();

            if (jobDef.JobDataMap is not null)
            {
                foreach (var (key, value) in jobDef.JobDataMap) jobDetail.JobDataMap[key] = value;
            }

            AddJobToSchedule(jobDetail);
        }
    }

    private void ProcessTriggers(List<JsonFileTriggerDefinition> triggerDefs)
    {
        foreach (var triggerDef in triggerDefs)
        {
            var triggerName = triggerDef.Name?.TrimEmptyToNull()
                ?? throw new SchedulerConfigException("JSON trigger definition is missing required 'Name' property.");
            var triggerJobName = triggerDef.JobName?.TrimEmptyToNull()
                ?? throw new SchedulerConfigException($"JSON trigger definition '{triggerName}' is missing required 'JobName' property.");

            if (triggerDef.StartTime is not null && triggerDef.StartTimeSecondsInFuture is not null)
            {
                throw new SchedulerConfigException($"JSON trigger '{triggerName}': 'StartTime' and 'StartTimeSecondsInFuture' are mutually exclusive.");
            }

            var priority = triggerDef.Priority ?? TriggerConstants.DefaultPriority;

            var startTime = timeProvider.GetUtcNow();
            if (triggerDef.StartTime is not null)
            {
                if (!DateTimeOffset.TryParse(triggerDef.StartTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out startTime))
                {
                    throw new SchedulerConfigException($"JSON trigger '{triggerName}': invalid StartTime value '{triggerDef.StartTime}'.");
                }
            }
            else if (triggerDef.StartTimeSecondsInFuture.HasValue)
            {
                startTime = startTime.AddSeconds(triggerDef.StartTimeSecondsInFuture.Value);
            }

            DateTimeOffset? endTime = null;
            if (triggerDef.EndTime is not null)
            {
                if (!DateTimeOffset.TryParse(triggerDef.EndTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    throw new SchedulerConfigException($"JSON trigger '{triggerName}': invalid EndTime value '{triggerDef.EndTime}'.");
                }
                endTime = parsed;
            }

            var schedule = BuildSchedule(triggerDef, triggerName);
            var triggerGroup = NormalizeEmpty(triggerDef.Group);
            var triggerJobGroup = NormalizeEmpty(triggerDef.JobGroup);

            var tb = TriggerBuilder.Create(timeProvider);
            if (triggerGroup is not null) tb.WithIdentity(triggerName, triggerGroup);
            else tb.WithIdentity(triggerName);
            if (triggerJobGroup is not null) tb.ForJob(triggerJobName, triggerJobGroup);
            else tb.ForJob(triggerJobName);

            var trigger = (IMutableTrigger) tb
                .WithDescription(triggerDef.Description?.TrimEmptyToNull())
                .StartAt(startTime)
                .EndAt(endTime)
                .WithPriority(priority)
                .WithCalendarName(NormalizeEmpty(triggerDef.CalendarName))
                .WithExecutionGroup(NormalizeEmpty(triggerDef.ExecutionGroup))
                .WithRetryPolicy(ParseRetryPolicy(NormalizeEmpty(triggerDef.RetryPolicy), triggerName))
                .WithSchedule(schedule)
                .Build();

            if (triggerDef.JobDataMap is not null)
            {
                foreach (var (key, value) in triggerDef.JobDataMap) trigger.JobDataMap[key] = value;
            }

            AddTriggerToSchedule(trigger);
        }
    }

    private IScheduleBuilder BuildSchedule(JsonFileTriggerDefinition def, string triggerName)
    {
        var count = (def.Simple is not null ? 1 : 0) + (def.Cron is not null ? 1 : 0) +
                    (def.CalendarInterval is not null ? 1 : 0) + (def.DailyTimeInterval is not null ? 1 : 0);

        if (count == 0) throw new SchedulerConfigException($"JSON trigger '{triggerName}' must specify exactly one schedule type: Simple, Cron, CalendarInterval, or DailyTimeInterval.");
        if (count > 1) throw new SchedulerConfigException($"JSON trigger '{triggerName}' has multiple schedule types. Specify exactly one.");

        if (def.Simple is not null) return BuildSimpleSchedule(def.Simple);
        if (def.Cron is not null) return BuildCronSchedule(def.Cron, triggerName);
        if (def.CalendarInterval is not null) return BuildCalendarIntervalSchedule(def.CalendarInterval);
        return BuildDailyTimeIntervalSchedule(def.DailyTimeInterval!);
    }

    private SimpleScheduleBuilder BuildSimpleSchedule(JsonFileSimpleSchedule simple)
    {
        var interval = TimeSpan.Parse(simple.Interval, CultureInfo.InvariantCulture);
        var builder = SimpleScheduleBuilder.Create().WithInterval(interval).WithRepeatCount(simple.RepeatCount);
        if (simple.MisfireInstruction is not null) builder.WithMisfireInstruction((SimpleTriggerMisfireInstruction) MisfireInstructionNames.Resolve(TriggerFamily.Simple, simple.MisfireInstruction, logger));
        return builder;
    }

    private CronScheduleBuilder BuildCronSchedule(JsonFileCronSchedule cron, string triggerName)
    {
        if (string.IsNullOrWhiteSpace(cron.Expression))
            throw new SchedulerConfigException($"JSON trigger '{triggerName}': Cron schedule is missing required 'Expression' property.");

        CronScheduleBuilder builder;
        try
        {
            builder = CronScheduleBuilder.Create(cron.Expression);
        }
        catch (Exception ex)
        {
            throw new SchedulerConfigException($"JSON trigger '{triggerName}': invalid cron expression '{cron.Expression}'. {ex.Message}", ex);
        }

        if (cron.TimeZone is not null) builder.InTimeZone(TimeZones.FindById(cron.TimeZone));
        if (cron.MisfireInstruction is not null) builder.WithMisfireInstruction((CronTriggerMisfireInstruction) MisfireInstructionNames.Resolve(TriggerFamily.Cron, cron.MisfireInstruction, logger));
        return builder;
    }

    private CalendarIntervalScheduleBuilder BuildCalendarIntervalSchedule(JsonFileCalendarIntervalSchedule calendar)
    {
        var unit = SafeParseEnum<IntervalUnit>(calendar.RepeatIntervalUnit, "CalendarInterval.RepeatIntervalUnit");
        var builder = CalendarIntervalScheduleBuilder.Create().WithInterval(calendar.RepeatInterval, unit);
        if (calendar.MisfireInstruction is not null) builder.WithMisfireInstruction((CalendarIntervalTriggerMisfireInstruction) MisfireInstructionNames.Resolve(TriggerFamily.CalendarInterval, calendar.MisfireInstruction, logger));
        return builder;
    }

    private DailyTimeIntervalScheduleBuilder BuildDailyTimeIntervalSchedule(JsonFileDailyTimeIntervalSchedule daily)
    {
        var unit = SafeParseEnum<IntervalUnit>(daily.RepeatIntervalUnit, "DailyTimeInterval.RepeatIntervalUnit");
        var builder = DailyTimeIntervalScheduleBuilder.Create().WithInterval(daily.RepeatInterval, unit).WithRepeatCount(daily.RepeatCount);

        if (daily.StartTimeOfDay is not null) builder.StartingDailyAt(ParseTimeOfDay(daily.StartTimeOfDay));
        if (daily.EndTimeOfDay is not null) builder.EndingDailyAt(ParseTimeOfDay(daily.EndTimeOfDay));

        if (daily.DaysOfWeek is { Count: > 0 })
        {
            var days = daily.DaysOfWeek.Select(d => SafeParseEnum<DayOfWeek>(d, "DailyTimeInterval.DaysOfWeek")).ToHashSet();
            builder.OnDaysOfTheWeek(days);
        }

        if (daily.TimeZone is not null) builder.InTimeZone(TimeZones.FindById(daily.TimeZone));

        if (daily.MisfireInstruction is not null)
        {
            var instruction = (DailyTimeIntervalTriggerMisfireInstruction) MisfireInstructionNames.Resolve(TriggerFamily.DailyTimeInterval, daily.MisfireInstruction, logger);
            if (Enum.IsDefined(instruction)) builder.WithMisfireInstruction(instruction);
        }

        return builder;
    }

    private static TimeOnly ParseTimeOfDay(string value)
    {
        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeSpan))
        {
            throw new SchedulerConfigException($"Invalid TimeOfDay value '{value}'. Expected format 'HH:mm:ss'.");
        }

        if (timeSpan < TimeSpan.Zero || timeSpan >= TimeSpan.FromHours(24))
        {
            throw new SchedulerConfigException($"TimeOfDay value '{value}' is out of range. Must be between 00:00:00 and 23:59:59.");
        }
        if (timeSpan.Milliseconds != 0 || timeSpan.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new SchedulerConfigException($"TimeOfDay value '{value}' must not contain fractional seconds.");
        }
        return new TimeOnly(timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
    }

    private static T SafeParseEnum<T>(string value, string context) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, ignoreCase: true, out var result)) return result;
        throw new SchedulerConfigException($"Invalid {typeof(T).Name} value '{value}' for {context}.");
    }

    private static string? NormalizeEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Reads a retry policy out of a scheduling file, refusing one that cannot be read rather than
    /// silently scheduling a trigger that will never retry.
    /// </summary>
    private static RetryPolicy? ParseRetryPolicy(string? value, string triggerName)
    {
        if (value is null)
        {
            return null;
        }

        if (!RetryPolicy.TryParse(value, out RetryPolicy? policy))
        {
            throw new SchedulerConfigException($"Trigger '{triggerName}': '{value}' is not a retry policy.");
        }

        return policy;
    }
}
