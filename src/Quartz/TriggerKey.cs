using System;
using Quartz.Util;

namespace Quartz
{
    ///<summary>
    /// Uniquely identifies a <see cref="ITrigger" />.
    /// </summary>
    /// <remarks>
    /// <para>Keys are composed of both a name and group, and the name must be unique
    /// within the group.  If only a name is specified then the default group
    /// name will be used.
    /// </para> 
    /// <para>
    /// Quartz provides a builder-style API for constructing scheduling-related
    /// entities via a Domain-Specific Language (DSL).  The DSL can best be
    /// utilized through the usage of static imports of the methods on the classes
    /// <see cref="TriggerBuilder" />, <see cref="JobBuilder" />,
    /// <see cref="DateBuilder" />, <see cref="JobKey" />, <see cref="TriggerKey" />
    /// and the various <see cref="IScheduleBuilder" /> implementations.
    /// </para>
    /// <para>
    /// Client code can then use the DSL to write code such as this:
    /// </para>
    /// <code>
    /// IJobDetail job = JobBuilder.Create&lt;MyJob>()
    ///     .WithIdentity("myJob")
    ///     .Build();
    /// ITrigger trigger = TriggerBuilder.Create()
    ///     .WithIdentity("myTrigger", "myTriggerGroup")
    ///     .WithSimpleSchedule(x => x
    ///         .WithIntervalInHours(1)
    ///         .RepeatForever())
    ///     .StartAt(DateBuilder.FutureDate(10, IntervalUnit.Minute))
    ///     .Build();
    /// scheduler.scheduleJob(job, trigger);
    /// </code>
    /// </remarks>
    /// <seealso cref="ITrigger" />
    /// <seealso cref="Key{T}.DefaultGroup" />
    [Serializable]
    public sealed class TriggerKey : Key<TriggerKey>
    {
        public TriggerKey(string name) : base(name, null)
        {
        }

        public TriggerKey(string name, string group) : base(name, group)
        {
        }
    }
}