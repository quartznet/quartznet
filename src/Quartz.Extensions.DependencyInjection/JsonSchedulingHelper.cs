using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using System.Reflection;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz;

/// <summary>
/// Reads job and trigger definitions from an <see cref="IConfiguration"/> section
/// and populates <see cref="QuartzOptions"/> accordingly.
/// </summary>
internal static class JsonSchedulingHelper
{
    /// <summary>
    /// Registers an <see cref="IConfigureOptions{QuartzOptions}"/> that reads job and trigger definitions
    /// from the <c>Schedule</c> sub-section of the given configuration.
    /// </summary>
    internal static void ConfigureOptionsFromConfiguration(
        IServiceCollection services,
        IConfiguration configuration,
        string? optionsName = null)
    {
        IConfigurationSection scheduleSection = configuration.GetSection("Schedule");
        IConfigurationSection schedulingSection = configuration.GetSection("Scheduling");
        if (!scheduleSection.Exists() && !schedulingSection.Exists())
        {
            return;
        }

        services.AddSingleton<IConfigureOptions<QuartzOptions>>(serviceProvider =>
        {
            return new ConfigureNamedOptions<QuartzOptions>(optionsName ?? Options.DefaultName, options =>
            {
                // Bind Scheduling directives (OverWriteExistingData, IgnoreDuplicates, etc.)
                if (schedulingSection.Exists())
                {
                    BindSchedulingOptions(schedulingSection, options.Scheduling);
                }

                // Read jobs and triggers from the Schedule section
                if (scheduleSection.Exists())
                {
                    ITypeLoadHelper typeLoadHelper = ResolveTypeLoadHelper(options, serviceProvider);
                    List<IJobDetail> jobs = ReadJobs(scheduleSection.GetSection("Jobs"), typeLoadHelper);
                    List<ITrigger> triggers = ReadTriggers(scheduleSection.GetSection("Triggers"));

                    foreach (IJobDetail job in jobs)
                    {
                        options.jobDetails.Add(job);
                    }

                    foreach (ITrigger trigger in triggers)
                    {
                        options.triggers.Add(trigger);
                    }
                }
            });
        });
    }

    internal static List<IJobDetail> ReadJobs(IConfigurationSection jobsSection, ITypeLoadHelper typeLoadHelper)
    {
        List<IJobDetail> jobs = new List<IJobDetail>();

        if (!jobsSection.Exists())
        {
            return jobs;
        }

        foreach (IConfigurationSection jobSection in jobsSection.GetChildren())
        {
            string? name = jobSection[nameof(JsonJobDefinition.Name)];
            string? jobTypeName = jobSection[nameof(JsonJobDefinition.JobType)];

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new SchedulerConfigException("JSON job definition is missing required 'Name' property.");
            }

            if (string.IsNullOrWhiteSpace(jobTypeName))
            {
                throw new SchedulerConfigException($"JSON job definition '{name}' is missing required 'JobType' property.");
            }

            string? group = NormalizeEmpty(jobSection[nameof(JsonJobDefinition.Group)]);
            string? description = jobSection[nameof(JsonJobDefinition.Description)];
            bool durable = ParseBool(jobSection[nameof(JsonJobDefinition.Durable)]);
            bool recover = ParseBool(jobSection[nameof(JsonJobDefinition.Recover)]);

            Type? jobType = typeLoadHelper.LoadType(jobTypeName!);
            if (jobType is null)
            {
                throw new SchedulerConfigException($"JSON job definition '{name}': could not load type '{jobTypeName}'.");
            }

            JobBuilder builder = JobBuilder.Create(jobType);
            if (group is not null)
            {
                builder.WithIdentity(name!, group);
            }
            else
            {
                builder.WithIdentity(name!);
            }

            IJobDetail jobDetail = builder
                .WithDescription(description)
                .StoreDurably(durable)
                .RequestRecovery(recover)
                .Build();

            IConfigurationSection dataMapSection = jobSection.GetSection(nameof(JsonJobDefinition.JobDataMap));
            if (dataMapSection.Exists())
            {
                foreach (IConfigurationSection entry in dataMapSection.GetChildren())
                {
                    if (entry.Value is null)
                    {
                        throw new SchedulerConfigException($"JSON job '{name}': JobDataMap entry '{entry.Key}' has a null value. Only string values are supported.");
                    }
                    jobDetail.JobDataMap[entry.Key] = entry.Value;
                }
            }

            jobs.Add(jobDetail);
        }

        return jobs;
    }

    internal static List<ITrigger> ReadTriggers(IConfigurationSection triggersSection)
    {
        List<ITrigger> triggers = new List<ITrigger>();

        if (!triggersSection.Exists())
        {
            return triggers;
        }

        foreach (IConfigurationSection triggerSection in triggersSection.GetChildren())
        {
            string? name = triggerSection[nameof(JsonTriggerDefinition.Name)];
            string? jobName = triggerSection[nameof(JsonTriggerDefinition.JobName)];

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new SchedulerConfigException("JSON trigger definition is missing required 'Name' property.");
            }

            if (string.IsNullOrWhiteSpace(jobName))
            {
                throw new SchedulerConfigException($"JSON trigger definition '{name}' is missing required 'JobName' property.");
            }

            string? group = NormalizeEmpty(triggerSection[nameof(JsonTriggerDefinition.Group)]);
            string? jobGroup = NormalizeEmpty(triggerSection[nameof(JsonTriggerDefinition.JobGroup)]);
            string? description = triggerSection[nameof(JsonTriggerDefinition.Description)];
            string? calendarName = NormalizeEmpty(triggerSection[nameof(JsonTriggerDefinition.CalendarName)]);
            string? executionGroup = NormalizeEmpty(triggerSection[nameof(JsonTriggerDefinition.ExecutionGroup)]);
            string? priorityStr = triggerSection[nameof(JsonTriggerDefinition.Priority)];
            string? startTimeStr = triggerSection[nameof(JsonTriggerDefinition.StartTime)];
            string? startTimeFutureStr = triggerSection[nameof(JsonTriggerDefinition.StartTimeSecondsInFuture)];
            string? endTimeStr = triggerSection[nameof(JsonTriggerDefinition.EndTime)];

            if (startTimeStr is not null && startTimeFutureStr is not null)
            {
                throw new SchedulerConfigException($"JSON trigger '{name}': 'StartTime' and 'StartTimeSecondsInFuture' are mutually exclusive.");
            }

            int priority = TriggerConstants.DefaultPriority;
            if (priorityStr is not null)
            {
                priority = SafeParseInt(priorityStr, "Priority", name!);
            }

            DateTimeOffset startTime = SystemTime.UtcNow();
            if (startTimeStr is not null)
            {
                if (!DateTimeOffset.TryParse(startTimeStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out startTime))
                {
                    throw new SchedulerConfigException($"JSON trigger '{name}': invalid StartTime value '{startTimeStr}'.");
                }
            }
            else if (startTimeFutureStr is not null)
            {
                int seconds = SafeParseInt(startTimeFutureStr, "StartTimeSecondsInFuture", name!);
                startTime = startTime.AddSeconds(seconds);
            }

            DateTimeOffset? endTime = null;
            if (endTimeStr is not null)
            {
                if (!DateTimeOffset.TryParse(endTimeStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsedEnd))
                {
                    throw new SchedulerConfigException($"JSON trigger '{name}': invalid EndTime value '{endTimeStr}'.");
                }
                endTime = parsedEnd;
            }

            IScheduleBuilder schedule = BuildSchedule(triggerSection, name!);

            TriggerBuilder triggerBuilder = TriggerBuilder.Create();
            if (group is not null)
            {
                triggerBuilder.WithIdentity(name!, group);
            }
            else
            {
                triggerBuilder.WithIdentity(name!);
            }

            if (jobGroup is not null)
            {
                triggerBuilder.ForJob(jobName!, jobGroup);
            }
            else
            {
                triggerBuilder.ForJob(jobName!);
            }

            IMutableTrigger trigger = (IMutableTrigger) triggerBuilder
                .WithDescription(description)
                .StartAt(startTime)
                .EndAt(endTime)
                .WithPriority(priority)
                .ModifiedByCalendar(calendarName)
                .WithExecutionGroup(executionGroup)
                .WithSchedule(schedule)
                .Build();

            IConfigurationSection dataMapSection = triggerSection.GetSection(nameof(JsonTriggerDefinition.JobDataMap));
            if (dataMapSection.Exists())
            {
                foreach (IConfigurationSection entry in dataMapSection.GetChildren())
                {
                    if (entry.Value is null)
                    {
                        throw new SchedulerConfigException($"JSON trigger '{name}': JobDataMap entry '{entry.Key}' has a null value. Only string values are supported.");
                    }
                    trigger.JobDataMap[entry.Key] = entry.Value;
                }
            }

            triggers.Add(trigger);
        }

        return triggers;
    }

    private static IScheduleBuilder BuildSchedule(IConfigurationSection triggerSection, string triggerName)
    {
        IConfigurationSection simpleSection = triggerSection.GetSection(nameof(JsonTriggerDefinition.Simple));
        IConfigurationSection cronSection = triggerSection.GetSection(nameof(JsonTriggerDefinition.Cron));
        IConfigurationSection calendarSection = triggerSection.GetSection(nameof(JsonTriggerDefinition.CalendarInterval));
        IConfigurationSection dailySection = triggerSection.GetSection(nameof(JsonTriggerDefinition.DailyTimeInterval));

        int scheduleCount = 0;
        if (simpleSection.Exists()) scheduleCount++;
        if (cronSection.Exists()) scheduleCount++;
        if (calendarSection.Exists()) scheduleCount++;
        if (dailySection.Exists()) scheduleCount++;

        if (scheduleCount == 0)
        {
            throw new SchedulerConfigException(
                $"JSON trigger '{triggerName}' must specify exactly one schedule type: Simple, Cron, CalendarInterval, or DailyTimeInterval.");
        }

        if (scheduleCount > 1)
        {
            throw new SchedulerConfigException(
                $"JSON trigger '{triggerName}' has multiple schedule types. Specify exactly one: Simple, Cron, CalendarInterval, or DailyTimeInterval.");
        }

        if (simpleSection.Exists())
        {
            return BuildSimpleSchedule(simpleSection);
        }

        if (cronSection.Exists())
        {
            return BuildCronSchedule(cronSection, triggerName);
        }

        if (calendarSection.Exists())
        {
            return BuildCalendarIntervalSchedule(calendarSection);
        }

        return BuildDailyTimeIntervalSchedule(dailySection);
    }

    private static SimpleScheduleBuilder BuildSimpleSchedule(IConfigurationSection section)
    {
        string? intervalStr = section[nameof(JsonSimpleSchedule.Interval)];
        string? repeatCountStr = section[nameof(JsonSimpleSchedule.RepeatCount)];

        TimeSpan interval = intervalStr is not null
            ? SafeParseTimeSpan(intervalStr, "Simple.Interval")
            : TimeSpan.Zero;

        int repeatCount = repeatCountStr is not null
            ? SafeParseInt(repeatCountStr, "Simple.RepeatCount")
            : 0;

        SimpleScheduleBuilder builder = SimpleScheduleBuilder.Create()
            .WithInterval(interval)
            .WithRepeatCount(repeatCount);

        string? misfireInstruction = section[nameof(JsonSimpleSchedule.MisfireInstruction)];
        if (misfireInstruction is not null)
        {
            builder.WithMisfireHandlingInstruction(ParseMisfireInstruction(misfireInstruction));
        }

        return builder;
    }

    private static CronScheduleBuilder BuildCronSchedule(IConfigurationSection section, string triggerName)
    {
        string? expression = section[nameof(JsonCronSchedule.Expression)];
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new SchedulerConfigException($"JSON trigger '{triggerName}': Cron schedule is missing required 'Expression' property.");
        }

        string? timeZoneStr = section[nameof(JsonCronSchedule.TimeZone)];
        TimeZoneInfo? tz = timeZoneStr is not null ? TimeZoneUtil.FindTimeZoneById(timeZoneStr) : null;

        CronScheduleBuilder builder;
        try
        {
            builder = CronScheduleBuilder.CronSchedule(expression!);
        }
        catch (FormatException ex)
        {
            throw new SchedulerConfigException($"JSON trigger '{triggerName}': invalid cron expression '{expression}': {ex.Message}", ex);
        }

        if (tz is not null)
        {
            builder.InTimeZone(tz);
        }

        string? misfireInstruction = section[nameof(JsonCronSchedule.MisfireInstruction)];
        if (misfireInstruction is not null)
        {
            builder.WithMisfireHandlingInstruction(ParseMisfireInstruction(misfireInstruction));
        }

        return builder;
    }

    private static CalendarIntervalScheduleBuilder BuildCalendarIntervalSchedule(IConfigurationSection section)
    {
        string? intervalStr = section[nameof(JsonCalendarIntervalSchedule.RepeatInterval)];
        string? unitStr = section[nameof(JsonCalendarIntervalSchedule.RepeatIntervalUnit)];

        int interval = intervalStr is not null
            ? SafeParseInt(intervalStr, "CalendarInterval.RepeatInterval")
            : 1;

        IntervalUnit unit = unitStr is not null
            ? SafeParseEnum<IntervalUnit>(unitStr, "CalendarInterval.RepeatIntervalUnit")
            : IntervalUnit.Day;

        CalendarIntervalScheduleBuilder builder = CalendarIntervalScheduleBuilder.Create()
            .WithInterval(interval, unit);

        string? misfireInstruction = section[nameof(JsonCalendarIntervalSchedule.MisfireInstruction)];
        if (misfireInstruction is not null)
        {
            builder.WithMisfireHandlingInstruction(ParseMisfireInstruction(misfireInstruction));
        }

        return builder;
    }

    private static DailyTimeIntervalScheduleBuilder BuildDailyTimeIntervalSchedule(IConfigurationSection section)
    {
        string? intervalStr = section[nameof(JsonDailyTimeIntervalSchedule.RepeatInterval)];
        string? unitStr = section[nameof(JsonDailyTimeIntervalSchedule.RepeatIntervalUnit)];
        string? repeatCountStr = section[nameof(JsonDailyTimeIntervalSchedule.RepeatCount)];
        string? startTodStr = section[nameof(JsonDailyTimeIntervalSchedule.StartTimeOfDay)];
        string? endTodStr = section[nameof(JsonDailyTimeIntervalSchedule.EndTimeOfDay)];
        string? timeZoneStr = section[nameof(JsonDailyTimeIntervalSchedule.TimeZone)];

        int interval = intervalStr is not null
            ? SafeParseInt(intervalStr, "DailyTimeInterval.RepeatInterval")
            : 1;

        IntervalUnit unit = unitStr is not null
            ? SafeParseEnum<IntervalUnit>(unitStr, "DailyTimeInterval.RepeatIntervalUnit")
            : IntervalUnit.Minute;

        DailyTimeIntervalScheduleBuilder builder = DailyTimeIntervalScheduleBuilder.Create()
            .WithInterval(interval, unit);

        if (repeatCountStr is not null)
        {
            int repeatCount = SafeParseInt(repeatCountStr, "DailyTimeInterval.RepeatCount");
            builder.WithRepeatCount(repeatCount);
        }

        if (startTodStr is not null)
        {
            builder.StartingDailyAt(ParseTimeOfDay(startTodStr));
        }

        if (endTodStr is not null)
        {
            builder.EndingDailyAt(ParseTimeOfDay(endTodStr));
        }

        IConfigurationSection daysSection = section.GetSection(nameof(JsonDailyTimeIntervalSchedule.DaysOfWeek));
        if (daysSection.Exists())
        {
            HashSet<DayOfWeek> days = new HashSet<DayOfWeek>();
            foreach (IConfigurationSection day in daysSection.GetChildren())
            {
                if (day.Value is not null)
                {
                    days.Add(SafeParseEnum<DayOfWeek>(day.Value, "DailyTimeInterval.DaysOfWeek"));
                }
            }

            if (days.Count > 0)
            {
                builder.OnDaysOfTheWeek(days);
            }
        }

        if (timeZoneStr is not null)
        {
            builder.InTimeZone(TimeZoneUtil.FindTimeZoneById(timeZoneStr));
        }

        string? misfireInstruction = section[nameof(JsonDailyTimeIntervalSchedule.MisfireInstruction)];
        if (misfireInstruction is not null)
        {
            int instruction = ParseMisfireInstruction(misfireInstruction);
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
        return new TimeOfDay(ts.Hours, ts.Minutes, ts.Seconds);
    }

    private static int ParseMisfireInstruction(string value)
    {
        // Search through MisfireInstruction and all its nested types for a matching constant
        Type misfireType = typeof(MisfireInstruction);
        Type[] types = new[] { misfireType }.Concat(misfireType.GetNestedTypes(BindingFlags.Public)).ToArray();
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

    private static bool ParseBool(string? value, string context = "")
    {
        if (value is null)
        {
            return false;
        }

        if (!bool.TryParse(value, out bool result))
        {
            throw new SchedulerConfigException($"JSON configuration: invalid boolean value '{value}'{(context.Length > 0 ? $" for '{context}'" : "")}.");
        }
        return result;
    }

    private static int SafeParseInt(string value, string property, string? context = null)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
        {
            string location = context is not null ? $"'{context}': " : "";
            throw new SchedulerConfigException($"JSON schedule: {location}invalid integer value '{value}' for '{property}'.");
        }
        return result;
    }

    private static TimeSpan SafeParseTimeSpan(string value, string property, string? context = null)
    {
        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan result))
        {
            string location = context is not null ? $"'{context}': " : "";
            throw new SchedulerConfigException($"JSON schedule: {location}invalid TimeSpan value '{value}' for '{property}'.");
        }
        return result;
    }

    private static T SafeParseEnum<T>(string value, string property, string? context = null) where T : struct
    {
        if (!Enum.TryParse(value, ignoreCase: true, out T result))
        {
            string location = context is not null ? $"'{context}': " : "";
            throw new SchedulerConfigException($"JSON schedule: {location}invalid {typeof(T).Name} value '{value}' for '{property}'.");
        }
        return result;
    }

    /// <summary>
    /// Resolves the <see cref="ITypeLoadHelper"/> to use for loading job types from JSON configuration.
    /// Checks the scheduler-specific property first (set via <c>UseTypeLoader&lt;T&gt;()</c>),
    /// then falls back to the DI-registered singleton, then to <see cref="SimpleTypeLoadHelper"/>.
    /// </summary>
    private static ITypeLoadHelper ResolveTypeLoadHelper(QuartzOptions options, IServiceProvider serviceProvider)
    {
        ITypeLoadHelper typeLoadHelper;

        if (options.TryGetValue(StdSchedulerFactory.PropertySchedulerTypeLoadHelperType, out string? typeName)
            && !string.IsNullOrWhiteSpace(typeName))
        {
            Type type = Type.GetType(typeName, throwOnError: true)!;
            typeLoadHelper = ObjectUtils.InstantiateType<ITypeLoadHelper>(type);
        }
        else
        {
            typeLoadHelper = serviceProvider.GetService<ITypeLoadHelper>() ?? new SimpleTypeLoadHelper();
        }

        typeLoadHelper.Initialize();
        return typeLoadHelper;
    }

    /// <summary>
    /// Binds values from the <c>Scheduling</c> configuration section into a <see cref="SchedulingOptions"/> instance.
    /// </summary>
    private static void BindSchedulingOptions(IConfigurationSection section, SchedulingOptions scheduling)
    {
        string? overwrite = section[nameof(SchedulingOptions.OverWriteExistingData)];
        if (overwrite is not null)
        {
            scheduling.OverWriteExistingData = ParseBool(overwrite, nameof(SchedulingOptions.OverWriteExistingData));
        }

        string? ignoreDups = section[nameof(SchedulingOptions.IgnoreDuplicates)];
        if (ignoreDups is not null)
        {
            scheduling.IgnoreDuplicates = ParseBool(ignoreDups, nameof(SchedulingOptions.IgnoreDuplicates));
        }

        string? relative = section[nameof(SchedulingOptions.ScheduleTriggerRelativeToReplacedTrigger)];
        if (relative is not null)
        {
            scheduling.ScheduleTriggerRelativeToReplacedTrigger = ParseBool(relative, nameof(SchedulingOptions.ScheduleTriggerRelativeToReplacedTrigger));
        }
    }

    /// <summary>
    /// Normalizes empty or whitespace-only strings to null so they fall back to Quartz defaults
    /// (e.g., the default group) instead of throwing ArgumentException.
    /// </summary>
    private static string? NormalizeEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
