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

using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Impl.Matchers;
using Quartz.Spi;
using Quartz.Util;
using Quartz.Xml;

namespace Quartz.Plugin.Json;

/// <summary>
/// Parses a JSON file that declares jobs and their schedules (triggers),
/// and schedules them with the scheduler. This is the JSON analog of
/// <see cref="XMLSchedulingDataProcessor"/>.
/// </summary>
internal sealed class JsonSchedulingDataProcessor : XMLSchedulingDataProcessor
{
    public const string QuartzJsonFileName = "quartz_jobs.json";

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

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
        ITypeLoadHelper typeLoadHelper,
        TimeProvider timeProvider)
        : base(LogProvider.CreateLogger<XMLSchedulingDataProcessor>(), typeLoadHelper, timeProvider)
    {
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    internal IReadOnlyList<IJobDetail> ParsedJobs => LoadedJobs;
    internal IReadOnlyList<ITrigger> ParsedTriggers => LoadedTriggers;

    internal void ProtectJobGroup(string groupName) => protectedJobGroups.Add(groupName);
    internal void ProtectTriggerGroup(string groupName) => protectedTriggerGroups.Add(groupName);

    public async Task ProcessJsonFileAndScheduleJobs(
        string fileName,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        fileName = FileUtil.ResolveFile(fileName) ?? fileName;

        logger.LogInformation("Parsing JSON file: {FileName}", fileName);

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

    internal void ProcessJsonContent(string json)
    {
        PrepForProcessing();
        jsonJobGroupsToDelete.Clear();
        jsonTriggerGroupsToDelete.Clear();
        jsonJobsToDelete.Clear();
        jsonTriggersToDelete.Clear();

        var data = JsonSerializer.Deserialize<JsonJobSchedulingData>(json, jsonOptions)
            ?? throw new SchedulerConfigException("Job definition data from JSON was null after deserialization.");

        if (data.PreProcessingCommands is not null)
        {
            ExtractPreProcessingCommands(data.PreProcessingCommands);
        }

        if (data.ProcessingDirectives is not null)
        {
            OverWriteExistingData = data.ProcessingDirectives.OverWriteExistingData;
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
                logger.LogInformation("Deleting all jobs in ALL groups");
                foreach (var groupName in await scheduler.GetJobGroupNames(cancellationToken).ConfigureAwait(false))
                {
                    if (protectedJobGroups.Contains(groupName)) continue;
                    foreach (var key in await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(groupName), cancellationToken).ConfigureAwait(false))
                    {
                        await scheduler.DeleteJob(key, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else if (!protectedJobGroups.Contains(group))
            {
                logger.LogInformation("Deleting all jobs in group: {Group}", group);
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
                logger.LogInformation("Deleting all triggers in ALL groups");
                foreach (var groupName in await scheduler.GetTriggerGroupNames(cancellationToken).ConfigureAwait(false))
                {
                    if (protectedTriggerGroups.Contains(groupName)) continue;
                    foreach (var key in await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(groupName), cancellationToken).ConfigureAwait(false))
                    {
                        await scheduler.UnscheduleJob(key, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else if (!protectedTriggerGroups.Contains(group))
            {
                logger.LogInformation("Deleting all triggers in group: {Group}", group);
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
                logger.LogInformation("Deleting job: {JobKey}", key);
                await scheduler.DeleteJob(key, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var key in jsonTriggersToDelete)
        {
            if (!protectedTriggerGroups.Contains(key.Group))
            {
                logger.LogInformation("Deleting trigger: {TriggerKey}", key);
                await scheduler.UnscheduleJob(key, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void ProcessJobs(List<JsonFileJobDefinition> jobDefs)
    {
        foreach (var jobDef in jobDefs)
        {
            var jobName = jobDef.Name?.TrimEmptyToNull()
                ?? throw new SchedulerConfigException("JSON job definition is missing required 'Name' property.");
            var jobTypeName = jobDef.JobType?.TrimEmptyToNull()
                ?? throw new SchedulerConfigException($"JSON job definition '{jobName}' is missing required 'JobType' property.");

            var jobType = TypeLoadHelper.LoadType(jobTypeName)
                ?? throw new SchedulerConfigException($"JSON job definition '{jobName}': could not load type '{jobTypeName}'.");

            var jobGroup = NormalizeEmpty(jobDef.Group);
            var builder = JobBuilder.Create(jobType);
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

            var tb = TriggerBuilder.Create();
            if (triggerGroup is not null) tb.WithIdentity(triggerName, triggerGroup);
            else tb.WithIdentity(triggerName);
            if (triggerJobGroup is not null) tb.ForJob(triggerJobName, triggerJobGroup);
            else tb.ForJob(triggerJobName);

            var trigger = (IMutableTrigger) tb
                .WithDescription(triggerDef.Description?.TrimEmptyToNull())
                .StartAt(startTime)
                .EndAt(endTime)
                .WithPriority(priority)
                .ModifiedByCalendar(NormalizeEmpty(triggerDef.CalendarName))
                .WithSchedule(schedule)
                .Build();

            if (triggerDef.JobDataMap is not null)
            {
                foreach (var (key, value) in triggerDef.JobDataMap) trigger.JobDataMap[key] = value;
            }

            AddTriggerToSchedule(trigger);
        }
    }

    private static IScheduleBuilder BuildSchedule(JsonFileTriggerDefinition def, string triggerName)
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

    private static SimpleScheduleBuilder BuildSimpleSchedule(JsonFileSimpleSchedule simple)
    {
        var interval = TimeSpan.Parse(simple.Interval, CultureInfo.InvariantCulture);
        var builder = SimpleScheduleBuilder.Create().WithInterval(interval).WithRepeatCount(simple.RepeatCount);
        if (simple.MisfireInstruction is not null) builder.WithMisfireHandlingInstruction(ParseMisfireInstruction(simple.MisfireInstruction));
        return builder;
    }

    private static CronScheduleBuilder BuildCronSchedule(JsonFileCronSchedule cron, string triggerName)
    {
        if (string.IsNullOrWhiteSpace(cron.Expression))
            throw new SchedulerConfigException($"JSON trigger '{triggerName}': Cron schedule is missing required 'Expression' property.");

        CronScheduleBuilder builder;
        try
        {
            builder = CronScheduleBuilder.CronSchedule(cron.Expression);
        }
        catch (Exception ex)
        {
            throw new SchedulerConfigException($"JSON trigger '{triggerName}': invalid cron expression '{cron.Expression}'. {ex.Message}", ex);
        }

        if (cron.TimeZone is not null) builder.InTimeZone(TimeZoneUtil.FindTimeZoneById(cron.TimeZone));
        if (cron.MisfireInstruction is not null) builder.WithMisfireHandlingInstruction(ParseMisfireInstruction(cron.MisfireInstruction));
        return builder;
    }

    private static CalendarIntervalScheduleBuilder BuildCalendarIntervalSchedule(JsonFileCalendarIntervalSchedule cal)
    {
        var unit = SafeParseEnum<IntervalUnit>(cal.RepeatIntervalUnit, "CalendarInterval.RepeatIntervalUnit");
        var builder = CalendarIntervalScheduleBuilder.Create().WithInterval(cal.RepeatInterval, unit);
        if (cal.MisfireInstruction is not null) builder.WithMisfireHandlingInstruction(ParseMisfireInstruction(cal.MisfireInstruction));
        return builder;
    }

    private static DailyTimeIntervalScheduleBuilder BuildDailyTimeIntervalSchedule(JsonFileDailyTimeIntervalSchedule daily)
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

        if (daily.TimeZone is not null) builder.InTimeZone(TimeZoneUtil.FindTimeZoneById(daily.TimeZone));

        if (daily.MisfireInstruction is not null)
        {
            var instruction = ParseMisfireInstruction(daily.MisfireInstruction);
            if (instruction == MisfireInstruction.IgnoreMisfirePolicy) builder.WithMisfireHandlingInstructionIgnoreMisfires();
            else if (instruction == MisfireInstruction.DailyTimeIntervalTrigger.DoNothing) builder.WithMisfireHandlingInstructionDoNothing();
            else if (instruction == MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow) builder.WithMisfireHandlingInstructionFireAndProceed();
        }

        return builder;
    }

    private static TimeOfDay ParseTimeOfDay(string value)
    {
        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var ts))
        {
            throw new SchedulerConfigException($"Invalid TimeOfDay value '{value}'. Expected format 'HH:mm:ss'.");
        }

        if (ts < TimeSpan.Zero || ts >= TimeSpan.FromHours(24))
        {
            throw new SchedulerConfigException($"TimeOfDay value '{value}' is out of range. Must be between 00:00:00 and 23:59:59.");
        }
        return new TimeOfDay(ts.Hours, ts.Minutes, ts.Seconds);
    }

    private static int ParseMisfireInstruction(string value)
    {
        Constants c = new(typeof(MisfireInstruction), typeof(MisfireInstruction.CronTrigger),
            typeof(MisfireInstruction.SimpleTrigger), typeof(MisfireInstruction.CalendarIntervalTrigger),
            typeof(MisfireInstruction.DailyTimeIntervalTrigger));
        return c.AsNumber(value);
    }

    private static T SafeParseEnum<T>(string value, string context) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, ignoreCase: true, out var result)) return result;
        throw new SchedulerConfigException($"Invalid {typeof(T).Name} value '{value}' for {context}.");
    }

    private static string? NormalizeEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
