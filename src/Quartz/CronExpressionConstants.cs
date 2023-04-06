namespace Quartz;

internal static class CronExpressionConstants
{
    /// <summary>
    /// Field specification for second.
    /// </summary>
    public const int Second = 0;

    /// <summary>
    /// Field specification for minute.
    /// </summary>
    public const int Minute = 1;

    /// <summary>
    /// Field specification for hour.
    /// </summary>
    public const int Hour = 2;

    /// <summary>
    /// Field specification for day of month.
    /// </summary>
    public const int DayOfMonth = 3;

    /// <summary>
    /// Field specification for month.
    /// </summary>
    public const int Month = 4;

    /// <summary>
    /// Field specification for day of week.
    /// </summary>
    public const int DayOfWeek = 5;

    /// <summary>
    /// Field specification for year.
    /// </summary>
    public const int Year = 6;

    /// <summary>
    /// Field specification for all wildcard value '*'.
    /// </summary>
    public const int AllSpecInt = 99; // '*'

    /// <summary>
    /// Field specification for not specified value '?'.
    /// </summary>
    public const int NoSpecInt = 98; // '?'

    /// <summary>
    /// Field specification for wildcard '*'.
    /// </summary>
    public const int AllSpec = AllSpecInt;

    /// <summary>
    /// Field specification for no specification at all '?'.
    /// </summary>
    public const int NoSpec = NoSpecInt;
}