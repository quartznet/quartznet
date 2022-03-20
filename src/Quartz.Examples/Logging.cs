using Microsoft.Extensions.Logging;

using Quartz.Logging;

using Serilog;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Examples
{

    public class Logging
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
    }
}