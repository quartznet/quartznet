namespace Quartz;

public interface ITriggerConfigurator
{
    /// <summary>
    /// Use a <see cref="TriggerKey" /> with the given name and default group to
    /// identify the Trigger.
    /// </summary>
    /// <remarks>
    /// <para>If none of the 'withIdentity' methods are set on the TriggerBuilder,
    /// then a random, unique TriggerKey will be generated.</para>
    /// </remarks>
    /// <param name="name">the name element for the Trigger's TriggerKey</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="TriggerKey" />
    /// <seealso cref="ITrigger.Key" />
    ITriggerConfigurator WithIdentity(string name);

    /// <summary>
    /// Use a TriggerKey with the given name and group to
    /// identify the Trigger.
    /// </summary>
    /// <remarks>
    /// <para>If none of the 'withIdentity' methods are set on the TriggerBuilder,
    /// then a random, unique TriggerKey will be generated.</para>
    /// </remarks>
    /// <param name="name">the name element for the Trigger's TriggerKey</param>
    /// <param name="group">the group element for the Trigger's TriggerKey</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="TriggerKey" />
    /// <seealso cref="ITrigger.Key" />
    ITriggerConfigurator WithIdentity(string name, string group);

    /// <summary>
    /// Use the given TriggerKey to identify the Trigger.
    /// </summary>
    /// <remarks>
    /// <para>If none of the 'withIdentity' methods are set on the TriggerBuilder,
    /// then a random, unique TriggerKey will be generated.</para>
    /// </remarks>
    /// <param name="key">the TriggerKey for the Trigger to be built</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="TriggerKey" />
    /// <seealso cref="ITrigger.Key" />
    ITriggerConfigurator WithIdentity(TriggerKey key);

    /// <summary>
    /// Set the given (human-meaningful) description of the Trigger.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="description">the description for the Trigger</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.Description" />
    ITriggerConfigurator WithDescription(string? description);

    /// <summary>
    /// Set the Trigger's priority.  When more than one Trigger have the same
    /// fire time, the scheduler will fire the one with the highest priority
    /// first.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="priority">the priority for the Trigger</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="TriggerConstants.DefaultPriority" />
    /// <seealso cref="ITrigger.Priority" />
    ITriggerConfigurator WithPriority(int priority);

    /// <summary>
    /// Set the name of the <see cref="ICalendar" /> that should be applied to this
    /// Trigger's schedule.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="calendarName">the name of the Calendar to reference.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ICalendar" />
    /// <seealso cref="ITrigger.CalendarName" />
    ITriggerConfigurator ModifiedByCalendar(string? calendarName);

    /// <summary>
    /// Set the time the Trigger should start at - the trigger may or may
    /// not fire at this time - depending upon the schedule configured for
    /// the Trigger.  However the Trigger will NOT fire before this time,
    /// regardless of the Trigger's schedule.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="startTimeUtc">the start time for the Trigger.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.StartTimeUtc" />
    /// <seealso cref="DateBuilder" />
    ITriggerConfigurator StartAt(DateTimeOffset startTimeUtc);

    /// <summary>
    /// Set the time the Trigger should start at to the current moment -
    /// the trigger may or may not fire at this time - depending upon the
    /// schedule configured for the Trigger.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.StartTimeUtc" />
    ITriggerConfigurator StartNow();

    /// <summary>
    /// Set the time at which the Trigger will no longer fire - even if it's
    /// schedule has remaining repeats.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="endTimeUtc">the end time for the Trigger.  If null, the end time is indefinite.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.EndTimeUtc" />
    /// <seealso cref="DateBuilder" />
    ITriggerConfigurator EndAt(DateTimeOffset? endTimeUtc);

    /// <summary>
    /// Set the <see cref="IScheduleBuilder" /> that will be used to define the
    /// Trigger's schedule.
    /// </summary>
    /// <remarks>
    /// <para>The particular <see cref="IScheduleBuilder" /> used will dictate
    /// the concrete type of Trigger that is produced by the TriggerBuilder.</para>
    /// </remarks>
    /// <param name="scheduleBuilder">the SchedulerBuilder to use.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="IScheduleBuilder" />
    /// <seealso cref="SimpleScheduleBuilder" />
    /// <seealso cref="CronScheduleBuilder" />
    /// <seealso cref="CalendarIntervalScheduleBuilder" />
    ITriggerConfigurator WithSchedule(IScheduleBuilder scheduleBuilder);

    /// <summary>
    /// Set the identity of the Job which should be fired by the produced
    /// Trigger.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobKey">the identity of the Job to fire.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobKey" />
    ITriggerConfigurator ForJob(JobKey jobKey);

    /// <summary>
    /// Set the identity of the Job which should be fired by the produced
    /// Trigger - a <see cref="JobKey" /> will be produced with the given
    /// name and default group.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobName">the name of the job (in default group) to fire.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobKey" />
    ITriggerConfigurator ForJob(string jobName);

    /// <summary>
    /// Set the identity of the Job which should be fired by the produced
    /// Trigger - a <see cref="JobKey" /> will be produced with the given
    /// name and group.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobName">the name of the job to fire.</param>
    /// <param name="jobGroup">the group of the job to fire.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobKey" />
    ITriggerConfigurator ForJob(string jobName, string jobGroup);

    /// <summary>
    /// Set the identity of the Job which should be fired by the produced
    /// Trigger, by extracting the JobKey from the given job.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobDetail">the Job to fire.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobKey" />
    ITriggerConfigurator ForJob(IJobDetail jobDetail);

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    ITriggerConfigurator UsingJobData(JobDataMap newJobDataMap);

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    ITriggerConfigurator UsingJobData(string key, string value);

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    ITriggerConfigurator UsingJobData(string key, int value);

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    ITriggerConfigurator UsingJobData(string key, long value);

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    ITriggerConfigurator UsingJobData(string key, float value);

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    ITriggerConfigurator UsingJobData(string key, double value);

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    ITriggerConfigurator UsingJobData(string key, decimal value);

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    ITriggerConfigurator UsingJobData(string key, bool value);

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    ITriggerConfigurator UsingJobData(string key, Guid value);

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    ITriggerConfigurator UsingJobData(string key, char value);
}