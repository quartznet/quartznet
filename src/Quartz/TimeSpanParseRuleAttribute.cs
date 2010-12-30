using System;

namespace Quartz
{
    /// <summary>
    /// Attribute to use with public <see cref="TimeSpan" /> properties that
    /// can be set with Quartz configuration. Attribute can be used to advice
    /// parsing to use correct type of time span (milliseconds, seconds, minutes, hours)
    /// as it may depend on property.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    /// <seealso cref="TimeSpanParseRuleAttribute" />
    public class TimeSpanParseRuleAttribute : Attribute
    {
        private readonly TimeSpanParseRule rule;

        /// <summary>
        /// Initializes a new instance of the <see cref="TimeSpanParseRuleAttribute"/> class.
        /// </summary>
        /// <param name="rule">The rule.</param>
        public TimeSpanParseRuleAttribute(TimeSpanParseRule rule)
        {
            this.rule = rule;
        }

        /// <summary>
        /// Gets the rule.
        /// </summary>
        /// <value>The rule.</value>
        public TimeSpanParseRule Rule
        {
            get { return rule; }
        }
    }

    /// <summary>
    /// Possible parse rules for <see cref="TimeSpan" />s.
    /// </summary>
    public enum TimeSpanParseRule
    {
        /// <summary>
        /// 
        /// </summary>
        Milliseconds = 0,

        /// <summary>
        /// 
        /// </summary>
        Seconds = 1,

        /// <summary>
        /// 
        /// </summary>
        Minutes = 2,

        /// <summary>
        /// 
        /// </summary>
        Hours = 3,

        /// <summary>
        /// 
        /// </summary>
        Days = 3
    }
}