using Microsoft.AspNetCore;

using Quartz.Impl.Calendar;
using Quartz.Logging;
using Quartz.Spi;

namespace Quartz.Web;

public class WebConsolePlugin : ISchedulerPlugin
{
    private static readonly ILogger<WebConsolePlugin> log = LogProvider.CreateLogger<WebConsolePlugin>();
    private IDisposable? host;

    public string HostName { get; set; } = null!;
    public int? Port { get; set; }

    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken)
    {
        // var liveLogPlugin = new LiveLogPlugin();
        // scheduler.ListenerManager.AddJobListener(liveLogPlugin);
        // scheduler.ListenerManager.AddTriggerListener(liveLogPlugin);
        // scheduler.ListenerManager.AddSchedulerListener(liveLogPlugin);

        // TODO REMOVE
        scheduler.AddCalendar(nameof(AnnualCalendar), new AnnualCalendar(), false, false, cancellationToken);
        scheduler.AddCalendar(nameof(CronCalendar), new CronCalendar("0 0/5 * * * ?"), false, false, cancellationToken);
        scheduler.AddCalendar(nameof(DailyCalendar), new DailyCalendar("12:01", "13:04"), false, false, cancellationToken);
        scheduler.AddCalendar(nameof(HolidayCalendar), new HolidayCalendar(), false, false, cancellationToken);
        scheduler.AddCalendar(nameof(MonthlyCalendar), new MonthlyCalendar(), false, false, cancellationToken);
        scheduler.AddCalendar(nameof(WeeklyCalendar), new WeeklyCalendar(), false, false, cancellationToken);

        return default;
    }

    public ValueTask Start(CancellationToken cancellationToken = default)
    {
        string baseAddress = $"http://{HostName ?? "localhost"}:{Port ?? 28682}/";

        //host = WebApp.Start<Startup>(url: baseAddress);
        host = WebHost.CreateDefaultBuilder()
            .UseStartup<Startup>()
            .Build();

        log.LogInformation("Quartz Web Console bound to address {BaseAddress}", baseAddress);
        return default;
    }

    public ValueTask Shutdown(CancellationToken cancellationToken)
    {
        host?.Dispose();
        return default;
    }
}