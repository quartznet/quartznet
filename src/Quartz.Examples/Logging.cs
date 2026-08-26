using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Quartz.Diagnostics;
using Serilog;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Examples;

/// <summary>
/// Where Quartz's own logging goes while an example runs.
/// </summary>
/// <remarks>
/// A console application has no container for Quartz to take an <see cref="ILoggerFactory" /> from, so
/// it hands one to <see cref="LogProvider" /> before building a scheduler. Under a host there is
/// nothing to do — the application's logging is already configured, and Quartz uses it.
/// </remarks>
public static class Logging
{
    /// <summary>
    /// The back-ends the tour can log through, the first being its default.
    /// </summary>
    public static string[] Names => ["microsoft", "serilog", "nlog"];

    /// <summary>
    /// Points Quartz at the named back-end, answering whether there is one by that name.
    /// </summary>
    public static bool Configure(string name)
    {
        switch (name)
        {
            case "microsoft":
                ConfigureMicrosoftLogger();
                return true;

            case "serilog":
                ConfigureSerilogLogger();
                return true;

            case "nlog":
                ConfigureNLogLogger();
                return true;

            default:
                return false;
        }
    }

    public static void ConfigureSerilogLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        var loggerFactory = new LoggerFactory()
            .AddSerilog(Log.Logger);
        LogProvider.SetLogProvider(loggerFactory);
    }

    public static void ConfigureMicrosoftLogger()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddSimpleConsole(options =>
                {
                    options.IncludeScopes = true;
                    options.SingleLine = true;
                    // HH, not hh: the examples print 24-hour times of their own, and two clocks
                    // twelve hours apart in one console is a puzzle nobody needs
                    options.TimestampFormat = "HH:mm:ss ";
                });
        });
        LogProvider.SetLogProvider(loggerFactory);
    }

    public static void ConfigureNLogLogger()
    {
        var loggerFactory = LoggerFactory.Create(
            builder => builder.AddNLog(new NLog.Config.XmlLoggingConfiguration("Nlog.config")));
        LogProvider.SetLogProvider(loggerFactory);
    }
}
