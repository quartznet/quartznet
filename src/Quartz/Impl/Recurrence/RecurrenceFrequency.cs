namespace Quartz.Impl.Recurrence;

/// <summary>
/// Recurrence frequency values as defined by RFC 5545.
/// Values are ordered from finest to coarsest granularity.
/// This ordering is relied upon by <see cref="ByRuleExpander"/> to determine
/// whether a BY* rule expands or limits the result set (RFC 5545 Section 3.3.10).
/// </summary>
internal enum RecurrenceFrequency
{
    Secondly = 0,
    Minutely = 1,
    Hourly = 2,
    Daily = 3,
    Weekly = 4,
    Monthly = 5,
    Yearly = 6
}
