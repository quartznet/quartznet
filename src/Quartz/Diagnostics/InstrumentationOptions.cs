namespace Quartz.Diagnostics;

internal static class InstrumentationOptions
{
    public const string MeterName = "Quartz";
    internal static readonly string? Version = typeof(InstrumentationOptions).Assembly.GetName().Version?.ToString();
}