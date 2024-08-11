namespace Quartz;

/// <summary>
/// Common constants for triggers.
/// </summary>
public static class TriggerConstants
{
    /// <summary>
    /// The default value for priority.
    /// </summary>
    public const int DefaultPriority = 5;

    internal static readonly int YearToGiveUpSchedulingAt = TimeProvider.System.GetUtcNow().Year + 100;
    internal const int EarliestYear = 1970;
}