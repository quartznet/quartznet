using System.Linq.Expressions;

namespace Quartz;

/// <summary>
/// The part of trigger configuration that does not depend on the job's type: choosing the
/// trigger's schedule.
/// </summary>
/// <remarks>
/// The <c>WithXSchedule</c> extension methods are written against this interface and return the
/// receiver's own type, so they read the same whether the receiver is a
/// <see cref="TriggerBuilder{TJob}" /> or an <see cref="ITriggerConfigurator{TJob}" />.
/// </remarks>
/// <seealso cref="TriggerConfiguratorExtensions" />
public interface ITriggerConfigurator
{
    /// <summary>
    /// Set the <see cref="IScheduleBuilder" /> that will be used to define the
    /// Trigger's schedule.
    /// </summary>
    /// <remarks>
    /// <para>The particular <see cref="IScheduleBuilder" /> used will dictate
    /// the concrete type of Trigger that is produced by the TriggerBuilder.</para>
    /// </remarks>
    /// <param name="scheduleBuilder">the schedule builder to use.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="IScheduleBuilder" />
    /// <seealso cref="SimpleScheduleBuilder" />
    /// <seealso cref="CronScheduleBuilder" />
    /// <seealso cref="CalendarIntervalScheduleBuilder" />
    ITriggerConfigurator WithSchedule(IScheduleBuilder scheduleBuilder);
}

/// <summary>
/// Configures a trigger for a job of a known type, so that job data can be bound by naming the
/// job's own properties.
/// </summary>
/// <typeparam name="TJob">the type of job the trigger fires.</typeparam>
public interface ITriggerConfigurator<TJob> : ITriggerConfigurator where TJob : IJob
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
    ITriggerConfigurator<TJob> WithIdentity(string name);

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
    ITriggerConfigurator<TJob> WithIdentity(string name, string group);

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
    ITriggerConfigurator<TJob> WithIdentity(TriggerKey key);

    /// <summary>
    /// Set the given (human-meaningful) description of the Trigger.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="description">the description for the Trigger</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.Description" />
    ITriggerConfigurator<TJob> WithDescription(string? description);

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
    ITriggerConfigurator<TJob> WithPriority(int priority);

    /// <summary>
    /// Set the execution group for the Trigger. Execution groups allow thread
    /// limits to be configured - per node or across the cluster - so that
    /// resource-intensive jobs do not saturate all available threads.
    /// </summary>
    /// <param name="executionGroup">the execution group name, or <see langword="null"/> to clear</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.ExecutionGroup" />
    ITriggerConfigurator<TJob> WithExecutionGroup(string? executionGroup);

    /// <summary>
    /// Pin the Trigger to a specific scheduler node, or to the node that first fires it.
    /// </summary>
    /// <param name="preferredNode">
    /// The pin: <see cref="Quartz.PreferredNode.None" /> to clear,
    /// <see cref="Quartz.PreferredNode.Auto" /> for automatic first-fire pinning, or
    /// <see cref="Quartz.PreferredNode.For" /> to name a node.
    /// </param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.PreferredNode" />
    ITriggerConfigurator<TJob> WithPreferredNode(PreferredNode preferredNode);

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
    ITriggerConfigurator<TJob> WithCalendarName(string? calendarName);

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
    ITriggerConfigurator<TJob> StartAt(DateTimeOffset startTimeUtc);

    /// <summary>
    /// Set the time the Trigger should start at to the current moment -
    /// the trigger may or may not fire at this time - depending upon the
    /// schedule configured for the Trigger.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.StartTimeUtc" />
    ITriggerConfigurator<TJob> StartNow();

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
    ITriggerConfigurator<TJob> EndAt(DateTimeOffset? endTimeUtc);

    /// <summary>
    /// Set the <see cref="IScheduleBuilder" /> that will be used to define the
    /// Trigger's schedule.
    /// </summary>
    /// <remarks>
    /// <para>The particular <see cref="IScheduleBuilder" /> used will dictate
    /// the concrete type of Trigger that is produced by the TriggerBuilder.</para>
    /// <para>Redeclared so that the chain keeps the job's type; the
    /// <c>WithXSchedule</c> extension methods do the same for the same reason.</para>
    /// </remarks>
    /// <param name="scheduleBuilder">the schedule builder to use.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="IScheduleBuilder" />
    new ITriggerConfigurator<TJob> WithSchedule(IScheduleBuilder scheduleBuilder);

    /// <summary>
    /// Set the identity of the Job which should be fired by the produced
    /// Trigger.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobKey">the identity of the Job to fire.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobKey" />
    ITriggerConfigurator<TJob> ForJob(JobKey jobKey);

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
    ITriggerConfigurator<TJob> ForJob(string jobName);

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
    ITriggerConfigurator<TJob> ForJob(string jobName, string jobGroup);

    /// <summary>
    /// Set the identity of the Job which should be fired by the produced
    /// Trigger, by extracting the JobKey from the given job.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobDetail">the Job to fire.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobKey" />
    ITriggerConfigurator<TJob> ForJob(IJobDetail jobDetail);

    /// <summary>
    /// Add a value to the Trigger's <see cref="JobDataMap" /> under the name of the job property it is
    /// meant to end up on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is how one job is given different inputs per trigger without spelling its property names:
    /// trigger data overrides job data in the map the job finally sees.
    /// </para>
    /// <para>
    /// It has to be a public settable property read directly off the job - a path through another property
    /// has nowhere to land, since the job factory sets properties on the job instance itself. Properties
    /// inherited from a base job are fine. Whether the property belongs to the job this trigger actually
    /// fires can only be checked when the trigger was pointed at the job with
    /// <see cref="ForJob(IJobDetail)" /> and that job's type resolves; pointed at a key, or at a job named
    /// by a type this process cannot load, the job type only names the properties.
    /// </para>
    /// <para>
    /// The value is stored in the property's own type, so an implicit widening at the call site is undone
    /// and a value that does not fit is rejected here. An enum property takes the enum's name.
    /// </para>
    /// <para>
    /// The same care applies as to any other job data: a persistent job store can only hold what its
    /// serializer round-trips, and AdoJobStore's <c>StoreJobDataAsStrings</c> mode only strings. Nothing beyond
    /// enums is converted for you.
    /// </para>
    /// </remarks>
    /// <param name="jobProperty">an expression naming the job property, such as <c>job =&gt; job.Parameter</c></param>
    /// <param name="value">the value to bind to that property</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    ITriggerConfigurator<TJob> UsingJobData<TValue>(Expression<Func<TJob, TValue>> jobProperty, TValue value);

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    ITriggerConfigurator<TJob> UsingJobData(JobDataMap newJobDataMap);

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// The value is stored as given. A persistent job store can only hold what its serializer
    /// round-trips, and AdoJobStore's <c>StoreJobDataAsStrings</c> mode only strings.
    /// </remarks>
    /// <param name="key">the key to store the value under</param>
    /// <param name="value">the value to store</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    ITriggerConfigurator<TJob> UsingJobData(string key, object? value);
}