namespace Quartz.Diagnostics;

internal sealed class InstrumentationOptions
{
    public const string MeterName = "Quartz";
    internal static readonly string? Version = typeof(InstrumentationOptions).Assembly.GetName().Version?.ToString();
}