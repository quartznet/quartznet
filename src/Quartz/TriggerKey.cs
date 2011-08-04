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
    /// name will be used.</para> 
    ///
    ///
    /// <para>Quartz provides a builder-style API for constructing scheduling-related
    /// entities via a Domain-Specific Language (DSL).  The DSL can best be
    /// utilized through the usage of static imports of the methods on the classes
    /// <code>TriggerBuilder</code>, <code>JobBuilder</code>, 
    /// <code>DateBuilder</code>, <code>JobKey</code>, <code>TriggerKey</code> 
    /// and the various <code>ScheduleBuilder</code> implementations.</para>
    /// 
    /// <para>Client code can then use the DSL to write code such as this:</para>
    /// <pre>
    ///         JobDetail job = newJob(MyJob.class)
    ///             .withIdentity("myJob")
    ///             .build();
    ///             
    ///         Trigger trigger = newTrigger() 
    ///             .withIdentity(triggerKey("myTrigger", "myTriggerGroup"))
    ///             .withSchedule(simpleSchedule()
    ///                 .withIntervalInHours(1)
    ///                 .repeatForever())
    ///             .startAt(futureDate(10, MINUTES))
    ///             .build();
    ///         
    ///         scheduler.scheduleJob(job, trigger);
    /// </pre>
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