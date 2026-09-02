namespace Quartz;

/// <summary>
/// Attribute to use with public <see cref="TimeSpan" /> properties that
/// can be set with Quartz configuration. Attribute can be used to advice
/// parsing to use correct type of time span (milliseconds, seconds, minutes, hours)
/// as it may depend on property.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
/// <seealso cref="TimeSpanParseRuleAttribute" />
[AttributeUsage(AttributeTargets.Property)]
public sealed class TimeSpanParseRuleAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TimeSpanParseRuleAttribute"/> class.
    /// </summary>
    /// <param name="rule">The rule.</param>
    public TimeSpanParseRuleAttribute(TimeSpanParseRule rule)
    {
        Rule = rule;
    }

    /// <summary>
    /// Gets the rule.
    /// </summary>
    /// <value>The rule.</value>
    public TimeSpanParseRule Rule { get; }
}

/// <summary>
/// Possible parse rules for <see cref="TimeSpan" />s.
/// </summary>
public enum TimeSpanParseRule
{
    /// <summary>
    /// The number is milliseconds, which is what a bare number in a property bag has always meant.
    /// </summary>
    Milliseconds = 0,

    /// <summary>
    /// The number is seconds.
    /// </summary>
    Seconds = 1,

    /// <summary>
    /// The number is minutes.
    /// </summary>
    Minutes = 2,

    /// <summary>
    /// The number is hours.
    /// </summary>
    Hours = 3
}