using System;

using Quartz.Impl.Triggers;

namespace Quartz
{
    /// <summary> 
    /// A <see cref="ITrigger" /> that is used to fire a <see cref="IJob" />
    /// at a given moment in time, and optionally repeated at a specified interval.
    /// </summary>
    /// <seealso cref="TriggerBuilder" />
    /// <seealso cref="SimpleScheduleBuilder" />
    /// <author>James House</author>
    /// <author>Contributions by Lieven Govaerts of Ebitec Nv, Belgium.</author>
    /// <author>Marko Lahma (.NET)</author>
    public interface ISimpleTrigger : ITrigger
    {
        /// <summary>
        /// Get or set the number of times the <see cref="ISimpleTrigger" /> should
        /// repeat, after which it will be automatically deleted.
        /// </summary>
        /// <seealso cref="SimpleTriggerImpl.RepeatIndefinitely" />
        int RepeatCount { get; set; }

        /// <summary>
        /// Get or set the time interval at which the <see cref="ISimpleTrigger" /> should repeat.
        /// </summary>
        TimeSpan RepeatInterval { get; set; }

        /// <summary>
        /// Get or set the number of times the <see cref="ISimpleTrigger" /> has already
        /// fired.
        /// </summary>
        int TimesTriggered { get; set; }
    }
}