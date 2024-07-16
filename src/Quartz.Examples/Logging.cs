using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Quartz.Diagnostics;
using Serilog;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Examples;

public static class Logging
{
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
                    options.TimestampFormat = "hh:mm:ss ";
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