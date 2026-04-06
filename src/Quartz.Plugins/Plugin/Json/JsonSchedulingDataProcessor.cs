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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.Matchers;
using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;
using Quartz.Xml;

namespace Quartz.Plugin.Json;

/// <summary>
/// Parses a JSON file that declares jobs and their schedules (triggers),
/// and schedules them with the scheduler. This is the JSON analog of
/// <see cref="XMLSchedulingDataProcessor"/>.
/// </summary>
/// <remarks>
/// Inherits from <see cref="XMLSchedulingDataProcessor"/> to reuse its
/// <see cref="XMLSchedulingDataProcessor.ScheduleJobs"/> method which handles
/// duplicate resolution, overwrite logic, and clustered environment race conditions.
/// Pre-processing commands (delete jobs/triggers) are handled directly since the
/// base class's delete lists are private.
/// </remarks>
internal sealed class JsonSchedulingDataProcessor : XMLSchedulingDataProcessor
{
    public const string QuartzJsonFileName = "quartz_jobs.json";

    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILog log;

    // Pre-processing commands (base class lists are private, so we maintain our own)
    private readonly List<string> jsonJobGroupsToDelete = new List<string>();
    private readonly List<string> jsonTriggerGroupsToDelete = new List<string>();
    private readonly List<JobKey> jsonJobsToDelete = new List<JobKey>();
    private readonly List<TriggerKey> jsonTriggersToDelete = new List<TriggerKey>();

    // Groups protected from deletion (mirrors base class's private neverDelete lists)
    private readonly HashSet<string> protectedJobGroups = new HashSet<string>();
    private readonly HashSet<string> protectedTriggerGroups = new HashSet<string>();

    public JsonSchedulingDataProcessor(ITypeLoadHelper typeLoadHelper) : base(typeLoadHelper)
    {
        log = LogProvider.GetLogger(GetType());
    }

    // Internal accessors for testing — base class properties are protected
    internal IReadOnlyList<IJobDetail> ParsedJobs => LoadedJobs;
    internal IReadOnlyList<ITrigger> ParsedTriggers => LoadedTriggers;

    /// <summary>
    /// Marks a job group as protected from pre-processing delete commands.
    /// Call this alongside <see cref="XMLSchedulingDataProcessor.AddJobGroupToNeverDelete"/>.
    /// </summary>
    internal void ProtectJobGroup(string groupName)
    {
        protectedJobGroups.Add(groupName);
    }

    /// <summary>
    /// Marks a trigger group as protected from pre-processing delete commands.
    /// Call this alongside <see cref="XMLSchedulingDataProcessor.AddTriggerGroupToNeverDelete"/>.
    /// </summary>
    internal void ProtectTriggerGroup(string groupName)
    {
        protectedTriggerGroups.Add(groupName);
    }

    /// <summary>
    /// Process the JSON file and schedule all jobs defined within it.
    /// </summary>
    public async Task ProcessJsonFileAndScheduleJobs(
        string fileName,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        fileName = FileUtil.ResolveFile(fileName) ?? fileName;

        log.InfoFormat("Parsing JSON file: {0}", fileName);

        string json;
        using (FileStream stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (StreamReader reader = new StreamReader(stream))
        {
            json = await reader.ReadToEndAsync().ConfigureAwait(false);
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

        JsonJobSchedulingData? data = JsonSerializer.Deserialize<JsonJobSchedulingData>(json, jsonOptions);
        if (data is null)
        {
            throw new SchedulerConfigException("Job definition data from JSON was null after deserialization.");
        }

        // Extract pre-processing commands
        if (data.PreProcessingCommands is not null)
        {
            ExtractPreProcessingCommands(data.PreProcessingCommands);
        }

        // Extract directives
        if (data.ProcessingDirectives is not null)
        {
            OverWriteExistingData = data.ProcessingDirectives.OverWriteExistingData;
            IgnoreDuplicates = data.ProcessingDirectives.IgnoreDuplicates;
            ScheduleTriggerRelativeToReplacedTrigger = data.ProcessingDirectives.ScheduleTriggerRelativeToReplacedTrigger;
        }

        // Extract jobs and triggers
        if (data.Schedule is not null)
        {
            if (data.Schedule.Jobs is not null)
            {
                ProcessJobs(data.Schedule.Jobs);
            }

            if (data.Schedule.Triggers is not null)
            {
                ProcessTriggers(data.Schedule.Triggers);
            }
        }
    }

    private void ExtractPreProcessingCommands(JsonPreProcessingCommands commands)
    {
        if (commands.DeleteJobsInGroup is not null)
        {
            foreach (string group in commands.DeleteJobsInGroup)
            {
                string? trimmed = group.NullSafeTrim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    jsonJobGroupsToDelete.Add(trimmed!);
                }
            }
        }

        if (commands.DeleteTriggersInGroup is not null)
        {
            foreach (string group in commands.DeleteTriggersInGroup)
            {
                string? trimmed = group.NullSafeTrim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    jsonTriggerGroupsToDelete.Add(trimmed!);
                }
            }
        }

        if (commands.DeleteJobs is not null)
        {
            foreach (JsonDeleteJobCommand cmd in commands.DeleteJobs)
            {
                string? name = cmd.Name?.TrimEmptyToNull();
                if (name is null)
                {
                    throw new SchedulerConfigException("Encountered a 'DeleteJobs' command without a name specified.");
                }
                string? group = NormalizeEmpty(cmd.Group);
                jsonJobsToDelete.Add(group is not null ? new JobKey(name, group) : new JobKey(name));
            }
        }

        if (commands.DeleteTriggers is not null)
        {
            foreach (JsonDeleteTriggerCommand cmd in commands.DeleteTriggers)
            {
                string? name = cmd.Name?.TrimEmptyToNull();
                if (name is null)
                {
                    throw new SchedulerConfigException("Encountered a 'DeleteTriggers' command without a name specified.");
                }
                string? group = NormalizeEmpty(cmd.Group);
                jsonTriggersToDelete.Add(group is not null ? new TriggerKey(name, group) : new TriggerKey(name));
            }
        }
    }

    private async Task ExecuteJsonPreProcessCommands(
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        foreach (string group in jsonJobGroupsToDelete)
        {
            if (group.Equals("*"))
            {
                log.Info("Deleting all jobs in ALL groups.");
                foreach (string groupName in await scheduler.GetJobGroupNames(cancellationToken).ConfigureAwait(false))
                {
                    if (!protectedJobGroups.Contains(groupName))
                    {
                        foreach (JobKey key in await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(groupName), cancellationToken).ConfigureAwait(false))
                        {
                            await scheduler.DeleteJob(key, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            else
            {
                if (!protectedJobGroups.Contains(group))
                {
                    log.InfoFormat("Deleting all jobs in group: {0}", group);
                    foreach (JobKey key in await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group), cancellationToken).ConfigureAwait(false))
                    {
                        await scheduler.DeleteJob(key, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        foreach (string group in jsonTriggerGroupsToDelete)
        {
            if (group.Equals("*"))
            {
                log.Info("Deleting all triggers in ALL groups.");
                foreach (string groupName in await scheduler.GetTriggerGroupNames(cancellationToken).ConfigureAwait(false))
                {
                    if (!protectedTriggerGroups.Contains(groupName))
                    {
                        foreach (TriggerKey key in await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(groupName), cancellationToken).ConfigureAwait(false))
                        {
                            await scheduler.UnscheduleJob(key, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            else
            {
                if (!protectedTriggerGroups.Contains(group))
                {
                    log.InfoFormat("Deleting all triggers in group: {0}", group);
                    foreach (TriggerKey key in await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(group), cancellationToken).ConfigureAwait(false))
                    {
                        await scheduler.UnscheduleJob(key, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        foreach (JobKey key in jsonJobsToDelete)
        {
            if (!protectedJobGroups.Contains(key.Group))
            {
                log.InfoFormat("Deleting job: {0}", key);
                await scheduler.DeleteJob(key, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (TriggerKey key in jsonTriggersToDelete)
        {
            if (!protectedTriggerGroups.Contains(key.Group))
            {
                log.InfoFormat("Deleting trigger: {0}", key);
                await scheduler.UnscheduleJob(key, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void ProcessJobs(List<JsonFileJobDefinition> jobDefs)
    {
        foreach (JsonFileJobDefinition jobDef in jobDefs)
        {
            string? jobName = jobDef.Name?.TrimEmptyToNull();
            string? jobTypeName = jobDef.JobType?.TrimEmptyToNull();

            if (jobName is null)
            {
                throw new SchedulerConfigException("JSON job definition is missing required 'Name' property.");
            }

            if (jobTypeName is null)
            {
                throw new SchedulerConfigException($"JSON job definition '{jobName}' is missing required 'JobType' property.");
            }

            Type? jobType = TypeLoadHelper.LoadType(jobTypeName);
            if (jobType is null)
            {
                throw new SchedulerConfigException($"JSON job definition '{jobName}': could not load type '{jobTypeName}'.");
            }

            string? jobGroup = NormalizeEmpty(jobDef.Group);

            JobBuilder jobBuilder = JobBuilder.Create(jobType);
            if (jobGroup is not null)
            {
                jobBuilder.WithIdentity(jobName, jobGroup);
            }
            else
            {
                jobBuilder.WithIdentity(jobName);
            }

            IJobDetail jobDetail = jobBuilder
                .WithDescription(jobDef.Description?.TrimEmptyToNull())
                .StoreDurably(jobDef.Durable)
                .RequestRecovery(jobDef.Recover)
                .Build();

            if (jobDef.JobDataMap is not null)
            {
                foreach (KeyValuePair<string, string> entry in jobDef.JobDataMap)
                {
                    jobDetail.JobDataMap[entry.Key] = entry.Value;
                }
            }

            AddJobToSchedule(jobDetail);
        }
    }

    private void ProcessTriggers(List<JsonFileTriggerDefinition> triggerDefs)
    {
        foreach (JsonFileTriggerDefinition triggerDef in triggerDefs)
        {
            string? triggerName = triggerDef.Name?.TrimEmptyToNull();
            string? triggerJobName = triggerDef.JobName?.TrimEmptyToNull();

            if (triggerName is null)
            {
                throw new SchedulerConfigException("JSON trigger definition is missing required 'Name' property.");
            }

            if (triggerJobName is null)
            {
                throw new SchedulerConfigException($"JSON trigger definition '{triggerName}' is missing required 'JobName' property.");
            }

            if (triggerDef.StartTime is not null && triggerDef.StartTimeSecondsInFuture is not null)
            {
                throw new SchedulerConfigException($"JSON trigger '{triggerName}': 'StartTime' and 'StartTimeSecondsInFuture' are mutually exclusive.");
            }

            int priority = TriggerConstants.DefaultPriority;
            if (triggerDef.Priority.HasValue)
            {
                priority = triggerDef.Priority.Value;
            }

            DateTimeOffset startTime = SystemTime.UtcNow();
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
                if (!DateTimeOffset.TryParse(triggerDef.EndTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsedEnd))
                {
                    throw new SchedulerConfigException($"JSON trigger '{triggerName}': invalid EndTime value '{triggerDef.EndTime}'.");
                }
                endTime = parsedEnd;
            }

            IScheduleBuilder schedule = BuildSchedule(triggerDef, triggerName);
            string? triggerGroup = NormalizeEmpty(triggerDef.Group);
            string? triggerJobGroup = NormalizeEmpty(triggerDef.JobGroup);

            TriggerBuilder tb = TriggerBuilder.Create();
            if (triggerGroup is not null)
            {
                tb.WithIdentity(triggerName, triggerGroup);
            }
            else
            {
                tb.WithIdentity(triggerName);
            }

            if (triggerJobGroup is not null)
            {
                tb.ForJob(triggerJobName, triggerJobGroup);
            }
            else
            {
                tb.ForJob(triggerJobName);
            }

            IMutableTrigger trigger = (IMutableTrigger) tb
                .WithDescription(triggerDef.Description?.TrimEmptyToNull())
                .StartAt(startTime)
                .EndAt(endTime)
                .WithPriority(priority)
                .ModifiedByCalendar(triggerDef.CalendarName?.TrimEmptyToNull())
                .WithExecutionGroup(triggerDef.ExecutionGroup?.TrimEmptyToNull())
                .WithPreferredNode(triggerDef.PreferredNode?.TrimEmptyToNull())
                .WithSchedule(schedule)
                .Build();

            if (triggerDef.JobDataMap is not null)
            {
                foreach (KeyValuePair<string, string> entry in triggerDef.JobDataMap)
                {
                    trigger.JobDataMap[entry.Key] = entry.Value;
                }
            }

            AddTriggerToSchedule(trigger);
        }
    }

    private static IScheduleBuilder BuildSchedule(JsonFileTriggerDefinition def, string triggerName)
    {
        int count = 0;
        if (def.Simple is not null) count++;
        if (def.Cron is not null) count++;
        if (def.CalendarInterval is not null) count++;
        if (def.DailyTimeInterval is not null) count++;

        if (count == 0)
        {
            throw new SchedulerConfigException(
                $"JSON trigger '{triggerName}' must specify exactly one schedule type: Simple, Cron, CalendarInterval, or DailyTimeInterval.");
        }

        if (count > 1)
        {
            throw new SchedulerConfigException(
                $"JSON trigger '{triggerName}' has multiple schedule types. Specify exactly one.");
        }

        if (def.Simple is not null) return BuildSimpleSchedule(def.Simple);
        if (def.Cron is not null) return BuildCronSchedule(def.Cron, triggerName);
        if (def.CalendarInterval is not null) return BuildCalendarIntervalSchedule(def.CalendarInterval);
        return BuildDailyTimeIntervalSchedule(def.DailyTimeInterval!);
    }

    private static SimpleScheduleBuilder BuildSimpleSchedule(JsonFileSimpleSchedule simple)
    {
        TimeSpan interval = TimeSpan.Parse(simple.Interval, CultureInfo.InvariantCulture);
        SimpleScheduleBuilder builder = SimpleScheduleBuilder.Create()
            .WithInterval(interval)
            .WithRepeatCount(simple.RepeatCount);

        if (simple.MisfireInstruction is not null)
        {
            builder.WithMisfireHandlingInstruction(ParseMisfireInstruction(simple.MisfireInstruction));
        }

        return builder;
    }

    private static CronScheduleBuilder BuildCronSchedule(JsonFileCronSchedule cron, string triggerName)
    {
        if (string.IsNullOrWhiteSpace(cron.Expression))
        {
            throw new SchedulerConfigException($"JSON trigger '{triggerName}': Cron schedule is missing required 'Expression' property.");
        }

        CronScheduleBuilder builder;
        try
        {
            builder = CronScheduleBuilder.CronSchedule(cron.Expression);
        }
        catch (FormatException ex)
        {
            throw new SchedulerConfigException($"JSON trigger '{triggerName}': invalid cron expression '{cron.Expression}': {ex.Message}", ex);
        }

        if (cron.TimeZone is not null)
        {
            builder.InTimeZone(TimeZoneUtil.FindTimeZoneById(cron.TimeZone));
        }

        if (cron.MisfireInstruction is not null)
        {
            builder.WithMisfireHandlingInstruction(ParseMisfireInstruction(cron.MisfireInstruction));
        }

        return builder;
    }

    private static CalendarIntervalScheduleBuilder BuildCalendarIntervalSchedule(JsonFileCalendarIntervalSchedule cal)
    {
        IntervalUnit unit = SafeParseEnum<IntervalUnit>(cal.RepeatIntervalUnit, "CalendarInterval.RepeatIntervalUnit");
        CalendarIntervalScheduleBuilder builder = CalendarIntervalScheduleBuilder.Create()
            .WithInterval(cal.RepeatInterval, unit);

        if (cal.MisfireInstruction is not null)
        {
            builder.WithMisfireHandlingInstruction(ParseMisfireInstruction(cal.MisfireInstruction));
        }

        return builder;
    }

    private static DailyTimeIntervalScheduleBuilder BuildDailyTimeIntervalSchedule(JsonFileDailyTimeIntervalSchedule daily)
    {
        IntervalUnit unit = SafeParseEnum<IntervalUnit>(daily.RepeatIntervalUnit, "DailyTimeInterval.RepeatIntervalUnit");
        DailyTimeIntervalScheduleBuilder builder = DailyTimeIntervalScheduleBuilder.Create()
            .WithInterval(daily.RepeatInterval, unit)
            .WithRepeatCount(daily.RepeatCount);

        if (daily.StartTimeOfDay is not null)
        {
            builder.StartingDailyAt(ParseTimeOfDay(daily.StartTimeOfDay));
        }

        if (daily.EndTimeOfDay is not null)
        {
            builder.EndingDailyAt(ParseTimeOfDay(daily.EndTimeOfDay));
        }

        if (daily.DaysOfWeek is not null && daily.DaysOfWeek.Count > 0)
        {
            HashSet<DayOfWeek> days = new HashSet<DayOfWeek>();
            foreach (string dayStr in daily.DaysOfWeek)
            {
                days.Add(SafeParseEnum<DayOfWeek>(dayStr, "DailyTimeInterval.DaysOfWeek"));
            }
            builder.OnDaysOfTheWeek(days);
        }

        if (daily.TimeZone is not null)
        {
            builder.InTimeZone(TimeZoneUtil.FindTimeZoneById(daily.TimeZone));
        }

        if (daily.MisfireInstruction is not null)
        {
            int instruction = ParseMisfireInstruction(daily.MisfireInstruction);
            if (instruction == MisfireInstruction.IgnoreMisfirePolicy)
            {
                builder.WithMisfireHandlingInstructionIgnoreMisfires();
            }
            else if (instruction == MisfireInstruction.DailyTimeIntervalTrigger.DoNothing)
            {
                builder.WithMisfireHandlingInstructionDoNothing();
            }
            else if (instruction == MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow)
            {
                builder.WithMisfireHandlingInstructionFireAndProceed();
            }
        }

        return builder;
    }

    private static TimeOfDay ParseTimeOfDay(string value)
    {
        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan ts))
        {
            throw new SchedulerConfigException($"JSON schedule: invalid TimeOfDay value '{value}'.");
        }
        if (ts < TimeSpan.Zero || ts >= TimeSpan.FromHours(24))
        {
            throw new SchedulerConfigException($"JSON schedule: TimeOfDay value '{value}' must be between 00:00:00 and 23:59:59.");
        }
        if (ts.Milliseconds != 0 || ts.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new SchedulerConfigException($"JSON schedule: TimeOfDay value '{value}' must not contain fractional seconds.");
        }
        return new TimeOfDay(ts.Hours, ts.Minutes, ts.Seconds);
    }

    private static int ParseMisfireInstruction(string value)
    {
        // Search through MisfireInstruction and all its nested types for a matching constant
        Type misfireType = typeof(MisfireInstruction);
        Type[] types = new[] { misfireType }
            .Concat(misfireType.GetNestedTypes(BindingFlags.Public))
            .ToArray();

        foreach (Type type in types)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (string.Equals(field.Name, value, StringComparison.OrdinalIgnoreCase))
                {
                    return (int) field.GetValue(null)!;
                }
            }
        }

        throw new SchedulerConfigException($"Unknown misfire instruction: '{value}'");
    }

    private static T SafeParseEnum<T>(string value, string property) where T : struct
    {
        if (!Enum.TryParse(value, ignoreCase: true, out T result))
        {
            throw new SchedulerConfigException($"JSON schedule: invalid {typeof(T).Name} value '{value}' for '{property}'.");
        }
        return result;
    }

    private static string? NormalizeEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
