using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using System.Reflection;

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
        if (!scheduleSection.Exists())
        {
            return;
        }

        services.AddSingleton<IConfigureOptions<QuartzOptions>>(serviceProvider =>
        {
            ITypeLoadHelper typeLoadHelper = serviceProvider.GetRequiredService<ITypeLoadHelper>();
            List<IJobDetail> jobs = ReadJobs(scheduleSection.GetSection("Jobs"), typeLoadHelper);
            List<ITrigger> triggers = ReadTriggers(scheduleSection.GetSection("Triggers"));

            return new ConfigureNamedOptions<QuartzOptions>(optionsName ?? Options.DefaultName, options =>
            {
                foreach (IJobDetail job in jobs)
                {
                    options.jobDetails.Add(job);
                }

                foreach (ITrigger trigger in triggers)
                {
                    options.triggers.Add(trigger);
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

            string? group = jobSection[nameof(JsonJobDefinition.Group)];
            string? description = jobSection[nameof(JsonJobDefinition.Description)];
            bool durable = ParseBool(jobSection[nameof(JsonJobDefinition.Durable)]);
            bool recover = ParseBool(jobSection[nameof(JsonJobDefinition.Recover)]);

            Type jobType = typeLoadHelper.LoadType(jobTypeName!)!;

            IJobDetail jobDetail = JobBuilder.Create(jobType)
                .WithIdentity(name!, group!)
                .WithDescription(description)
                .StoreDurably(durable)
                .RequestRecovery(recover)
                .Build();

            IConfigurationSection dataMapSection = jobSection.GetSection(nameof(JsonJobDefinition.JobDataMap));
            if (dataMapSection.Exists())
            {
                foreach (IConfigurationSection entry in dataMapSection.GetChildren())
                {
                    jobDetail.JobDataMap[entry.Key] = entry.Value!;
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

            string? group = triggerSection[nameof(JsonTriggerDefinition.Group)];
            string? jobGroup = triggerSection[nameof(JsonTriggerDefinition.JobGroup)];
            string? description = triggerSection[nameof(JsonTriggerDefinition.Description)];
            string? calendarName = triggerSection[nameof(JsonTriggerDefinition.CalendarName)];
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
                priority = int.Parse(priorityStr, CultureInfo.InvariantCulture);
            }

            DateTimeOffset startTime = SystemTime.UtcNow();
            if (startTimeStr is not null)
            {
                startTime = DateTimeOffset.Parse(startTimeStr, CultureInfo.InvariantCulture);
            }
            else if (startTimeFutureStr is not null)
            {
                int seconds = int.Parse(startTimeFutureStr, CultureInfo.InvariantCulture);
                startTime = startTime.AddSeconds(seconds);
            }

            DateTimeOffset? endTime = null;
            if (endTimeStr is not null)
            {
                endTime = DateTimeOffset.Parse(endTimeStr, CultureInfo.InvariantCulture);
            }

            IScheduleBuilder schedule = BuildSchedule(triggerSection, name!);

            IMutableTrigger trigger = (IMutableTrigger) TriggerBuilder.Create()
                .WithIdentity(name!, group!)
                .WithDescription(description)
                .ForJob(jobName!, jobGroup!)
                .StartAt(startTime)
                .EndAt(endTime)
                .WithPriority(priority)
                .ModifiedByCalendar(calendarName)
                .WithSchedule(schedule)
                .Build();

            IConfigurationSection dataMapSection = triggerSection.GetSection(nameof(JsonTriggerDefinition.JobDataMap));
            if (dataMapSection.Exists())
            {
                foreach (IConfigurationSection entry in dataMapSection.GetChildren())
                {
                    trigger.JobDataMap[entry.Key] = entry.Value!;
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
            ? TimeSpan.Parse(intervalStr, CultureInfo.InvariantCulture)
            : TimeSpan.Zero;

        int repeatCount = repeatCountStr is not null
            ? int.Parse(repeatCountStr, CultureInfo.InvariantCulture)
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

        CronScheduleBuilder builder = CronScheduleBuilder.CronSchedule(expression!);
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
            ? int.Parse(intervalStr, CultureInfo.InvariantCulture)
            : 1;

        IntervalUnit unit = unitStr is not null
            ? (IntervalUnit) Enum.Parse(typeof(IntervalUnit), unitStr, ignoreCase: true)
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
            ? int.Parse(intervalStr, CultureInfo.InvariantCulture)
            : 1;

        IntervalUnit unit = unitStr is not null
            ? (IntervalUnit) Enum.Parse(typeof(IntervalUnit), unitStr, ignoreCase: true)
            : IntervalUnit.Minute;

        DailyTimeIntervalScheduleBuilder builder = DailyTimeIntervalScheduleBuilder.Create()
            .WithInterval(interval, unit);

        if (repeatCountStr is not null)
        {
            int repeatCount = int.Parse(repeatCountStr, CultureInfo.InvariantCulture);
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
                    days.Add((DayOfWeek) Enum.Parse(typeof(DayOfWeek), day.Value, ignoreCase: true));
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
        TimeSpan ts = TimeSpan.Parse(value, CultureInfo.InvariantCulture);
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

    private static bool ParseBool(string? value)
    {
        if (value is null)
        {
            return false;
        }

        return bool.Parse(value);
    }
}
