using System.Globalization;
using System.Reflection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Configuration;

/// <summary>
/// Reads job and trigger definitions from an <see cref="IConfiguration"/> section
/// and populates <see cref="QuartzOptions"/> accordingly.
/// </summary>
internal static class JsonSchedulingHelper
{
    internal static void ConfigureOptionsFromConfiguration(
        IServiceCollection services,
        IConfiguration configuration,
        string? optionsName = null)
    {
        var scheduleSection = configuration.GetSection("Schedule");
        if (!scheduleSection.Exists())
        {
            return;
        }

        services.AddSingleton<IConfigureOptions<QuartzOptions>>(serviceProvider =>
        {
            var typeLoadHelper = serviceProvider.GetService<ITypeLoadHelper>() ?? new SimpleTypeLoadHelper();
            var jobs = ReadJobs(scheduleSection.GetSection("Jobs"), typeLoadHelper);
            var triggers = ReadTriggers(scheduleSection.GetSection("Triggers"));

            return new ConfigureNamedOptions<QuartzOptions>(optionsName ?? Options.DefaultName, options =>
            {
                foreach (var job in jobs)
                {
                    options._jobDetails.Add(job);
                }

                foreach (var trigger in triggers)
                {
                    options._triggers.Add(trigger);
                }
            });
        });
    }

    internal static List<IJobDetail> ReadJobs(IConfigurationSection jobsSection, ITypeLoadHelper typeLoadHelper)
    {
        var jobs = new List<IJobDetail>();

        if (!jobsSection.Exists())
        {
            return jobs;
        }

        foreach (var jobSection in jobsSection.GetChildren())
        {
            var name = jobSection[nameof(JsonJobDefinition.Name)];
            var jobTypeName = jobSection[nameof(JsonJobDefinition.JobType)];

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new SchedulerConfigException("JSON job definition is missing required 'Name' property.");
            }

            if (string.IsNullOrWhiteSpace(jobTypeName))
            {
                throw new SchedulerConfigException($"JSON job definition '{name}' is missing required 'JobType' property.");
            }

            var group = NormalizeEmpty(jobSection[nameof(JsonJobDefinition.Group)]);
            var description = jobSection[nameof(JsonJobDefinition.Description)];
            var durable = ParseBool(jobSection[nameof(JsonJobDefinition.Durable)]);
            var recover = ParseBool(jobSection[nameof(JsonJobDefinition.Recover)]);

            var jobType = typeLoadHelper.LoadType(jobTypeName!)
                ?? throw new SchedulerConfigException($"JSON job definition '{name}': could not load type '{jobTypeName}'.");

            var builder = JobBuilder.Create(jobType);
            if (group is not null)
            {
                builder.WithIdentity(name!, group);
            }
            else
            {
                builder.WithIdentity(name!);
            }

            var jobDetail = builder
                .WithDescription(description)
                .StoreDurably(durable)
                .RequestRecovery(recover)
                .Build();

            var dataMapSection = jobSection.GetSection(nameof(JsonJobDefinition.JobDataMap));
            if (dataMapSection.Exists())
            {
                foreach (var entry in dataMapSection.GetChildren())
                {
                    if (entry.Value is null)
                    {
                        throw new SchedulerConfigException($"JSON job '{name}': JobDataMap key '{entry.Key}' has a non-string value. Only string values are supported.");
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
        var triggers = new List<ITrigger>();

        if (!triggersSection.Exists())
        {
            return triggers;
        }

        foreach (var triggerSection in triggersSection.GetChildren())
        {
            var name = triggerSection[nameof(JsonTriggerDefinition.Name)];
            var jobName = triggerSection[nameof(JsonTriggerDefinition.JobName)];

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new SchedulerConfigException("JSON trigger definition is missing required 'Name' property.");
            }

            if (string.IsNullOrWhiteSpace(jobName))
            {
                throw new SchedulerConfigException($"JSON trigger definition '{name}' is missing required 'JobName' property.");
            }

            var group = NormalizeEmpty(triggerSection[nameof(JsonTriggerDefinition.Group)]);
            var jobGroup = NormalizeEmpty(triggerSection[nameof(JsonTriggerDefinition.JobGroup)]);
            var description = triggerSection[nameof(JsonTriggerDefinition.Description)];
            var calendarName = NormalizeEmpty(triggerSection[nameof(JsonTriggerDefinition.CalendarName)]);
            var priorityStr = triggerSection[nameof(JsonTriggerDefinition.Priority)];
            var startTimeStr = triggerSection[nameof(JsonTriggerDefinition.StartTime)];
            var startTimeFutureStr = triggerSection[nameof(JsonTriggerDefinition.StartTimeSecondsInFuture)];
            var endTimeStr = triggerSection[nameof(JsonTriggerDefinition.EndTime)];

            if (startTimeStr is not null && startTimeFutureStr is not null)
            {
                throw new SchedulerConfigException($"JSON trigger '{name}': 'StartTime' and 'StartTimeSecondsInFuture' are mutually exclusive.");
            }

            var priority = TriggerConstants.DefaultPriority;
            if (priorityStr is not null)
            {
                if (!int.TryParse(priorityStr, CultureInfo.InvariantCulture, out priority))
                {
                    throw new SchedulerConfigException($"JSON trigger '{name}': invalid Priority value '{priorityStr}'.");
                }
            }

            var startTime = DateTimeOffset.UtcNow;
            if (startTimeStr is not null)
            {
                if (!DateTimeOffset.TryParse(startTimeStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out startTime))
                {
                    throw new SchedulerConfigException($"JSON trigger '{name}': invalid StartTime value '{startTimeStr}'.");
                }
            }
            else if (startTimeFutureStr is not null)
            {
                if (!int.TryParse(startTimeFutureStr, CultureInfo.InvariantCulture, out var seconds))
                {
                    throw new SchedulerConfigException($"JSON trigger '{name}': invalid StartTimeSecondsInFuture value '{startTimeFutureStr}'.");
                }
                startTime = startTime.AddSeconds(seconds);
            }

            DateTimeOffset? endTime = null;
            if (endTimeStr is not null)
            {
                if (!DateTimeOffset.TryParse(endTimeStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    throw new SchedulerConfigException($"JSON trigger '{name}': invalid EndTime value '{endTimeStr}'.");
                }
                endTime = parsed;
            }

            var schedule = BuildSchedule(triggerSection, name!);

            var triggerBuilder = TriggerBuilder.Create();
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

            var trigger = (IMutableTrigger) triggerBuilder
                .WithDescription(description)
                .StartAt(startTime)
                .EndAt(endTime)
                .WithPriority(priority)
                .ModifiedByCalendar(calendarName)
                .WithSchedule(schedule)
                .Build();

            var dataMapSection = triggerSection.GetSection(nameof(JsonTriggerDefinition.JobDataMap));
            if (dataMapSection.Exists())
            {
                foreach (var entry in dataMapSection.GetChildren())
                {
                    if (entry.Value is null)
                    {
                        throw new SchedulerConfigException($"JSON trigger '{name}': JobDataMap key '{entry.Key}' has a non-string value. Only string values are supported.");
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
        var simpleSection = triggerSection.GetSection(nameof(JsonTriggerDefinition.Simple));
        var cronSection = triggerSection.GetSection(nameof(JsonTriggerDefinition.Cron));
        var calendarSection = triggerSection.GetSection(nameof(JsonTriggerDefinition.CalendarInterval));
        var dailySection = triggerSection.GetSection(nameof(JsonTriggerDefinition.DailyTimeInterval));

        var scheduleCount = 0;
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

        if (simpleSection.Exists()) return BuildSimpleSchedule(simpleSection);
        if (cronSection.Exists()) return BuildCronSchedule(cronSection, triggerName);
        if (calendarSection.Exists()) return BuildCalendarIntervalSchedule(calendarSection);
        return BuildDailyTimeIntervalSchedule(dailySection);
    }

    private static SimpleScheduleBuilder BuildSimpleSchedule(IConfigurationSection section)
    {
        var intervalStr = section[nameof(JsonSimpleSchedule.Interval)];
        var repeatCountStr = section[nameof(JsonSimpleSchedule.RepeatCount)];

        var interval = intervalStr is not null ? SafeParseTimeSpan(intervalStr, "Simple.Interval") : TimeSpan.Zero;
        var repeatCount = repeatCountStr is not null ? SafeParseInt(repeatCountStr, "Simple.RepeatCount") : 0;

        var builder = SimpleScheduleBuilder.Create().WithInterval(interval).WithRepeatCount(repeatCount);

        var misfireInstruction = section[nameof(JsonSimpleSchedule.MisfireInstruction)];
        if (misfireInstruction is not null)
        {
            builder.WithMisfireHandlingInstruction(ParseMisfireInstruction(misfireInstruction));
        }

        return builder;
    }

    private static CronScheduleBuilder BuildCronSchedule(IConfigurationSection section, string triggerName)
    {
        var expression = section[nameof(JsonCronSchedule.Expression)];
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new SchedulerConfigException($"JSON trigger '{triggerName}': Cron schedule is missing required 'Expression' property.");
        }

        var timeZoneStr = section[nameof(JsonCronSchedule.TimeZone)];
        var tz = timeZoneStr is not null ? TimeZoneUtil.FindTimeZoneById(timeZoneStr) : null;

        CronScheduleBuilder builder;
        try
        {
            builder = CronScheduleBuilder.CronSchedule(expression!);
        }
        catch (Exception ex)
        {
            throw new SchedulerConfigException($"JSON trigger '{triggerName}': invalid cron expression '{expression}'. {ex.Message}", ex);
        }

        if (tz is not null)
        {
            builder.InTimeZone(tz);
        }

        var misfireInstruction = section[nameof(JsonCronSchedule.MisfireInstruction)];
        if (misfireInstruction is not null)
        {
            builder.WithMisfireHandlingInstruction(ParseMisfireInstruction(misfireInstruction));
        }

        return builder;
    }

    private static CalendarIntervalScheduleBuilder BuildCalendarIntervalSchedule(IConfigurationSection section)
    {
        var intervalStr = section[nameof(JsonCalendarIntervalSchedule.RepeatInterval)];
        var unitStr = section[nameof(JsonCalendarIntervalSchedule.RepeatIntervalUnit)];

        var interval = intervalStr is not null ? SafeParseInt(intervalStr, "CalendarInterval.RepeatInterval") : 1;
        var unit = unitStr is not null ? SafeParseEnum<IntervalUnit>(unitStr, "CalendarInterval.RepeatIntervalUnit") : IntervalUnit.Day;

        var builder = CalendarIntervalScheduleBuilder.Create().WithInterval(interval, unit);

        var misfireInstruction = section[nameof(JsonCalendarIntervalSchedule.MisfireInstruction)];
        if (misfireInstruction is not null)
        {
            builder.WithMisfireHandlingInstruction(ParseMisfireInstruction(misfireInstruction));
        }

        return builder;
    }

    private static DailyTimeIntervalScheduleBuilder BuildDailyTimeIntervalSchedule(IConfigurationSection section)
    {
        var intervalStr = section[nameof(JsonDailyTimeIntervalSchedule.RepeatInterval)];
        var unitStr = section[nameof(JsonDailyTimeIntervalSchedule.RepeatIntervalUnit)];
        var repeatCountStr = section[nameof(JsonDailyTimeIntervalSchedule.RepeatCount)];
        var startTodStr = section[nameof(JsonDailyTimeIntervalSchedule.StartTimeOfDay)];
        var endTodStr = section[nameof(JsonDailyTimeIntervalSchedule.EndTimeOfDay)];
        var timeZoneStr = section[nameof(JsonDailyTimeIntervalSchedule.TimeZone)];

        var interval = intervalStr is not null ? SafeParseInt(intervalStr, "DailyTimeInterval.RepeatInterval") : 1;
        var unit = unitStr is not null ? SafeParseEnum<IntervalUnit>(unitStr, "DailyTimeInterval.RepeatIntervalUnit") : IntervalUnit.Minute;

        var builder = DailyTimeIntervalScheduleBuilder.Create().WithInterval(interval, unit);

        if (repeatCountStr is not null)
        {
            builder.WithRepeatCount(SafeParseInt(repeatCountStr, "DailyTimeInterval.RepeatCount"));
        }

        if (startTodStr is not null)
        {
            builder.StartingDailyAt(ParseTimeOfDay(startTodStr));
        }

        if (endTodStr is not null)
        {
            builder.EndingDailyAt(ParseTimeOfDay(endTodStr));
        }

        var daysSection = section.GetSection(nameof(JsonDailyTimeIntervalSchedule.DaysOfWeek));
        if (daysSection.Exists())
        {
            var days = new HashSet<DayOfWeek>();
            foreach (var day in daysSection.GetChildren())
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

        var misfireInstruction = section[nameof(JsonDailyTimeIntervalSchedule.MisfireInstruction)];
        if (misfireInstruction is not null)
        {
            var instruction = ParseMisfireInstruction(misfireInstruction);
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
        var misfireType = typeof(MisfireInstruction);
        var types = new[] { misfireType }.Concat(misfireType.GetNestedTypes(BindingFlags.Public)).ToArray();
        foreach (var type in types)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
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
        if (value is null) return false;
        if (bool.TryParse(value, out var result)) return result;
        throw new SchedulerConfigException($"Invalid boolean value '{value}'{(context.Length > 0 ? " for " + context : "")}.");
    }

    private static int SafeParseInt(string value, string context)
    {
        if (int.TryParse(value, CultureInfo.InvariantCulture, out var result)) return result;
        throw new SchedulerConfigException($"Invalid integer value '{value}' for {context}.");
    }

    private static TimeSpan SafeParseTimeSpan(string value, string context)
    {
        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var result)) return result;
        throw new SchedulerConfigException($"Invalid TimeSpan value '{value}' for {context}.");
    }

    private static T SafeParseEnum<T>(string value, string context) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, ignoreCase: true, out var result)) return result;
        throw new SchedulerConfigException($"Invalid {typeof(T).Name} value '{value}' for {context}.");
    }

    private static string? NormalizeEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
