using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Quartz.Impl.Calendar;
using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;
//using Quartz.Web.LiveLog;

namespace Quartz.Web
{
    public class WebConsolePlugin : ISchedulerPlugin
    {
        private static readonly ILogger<WebConsolePlugin> log = LogContext.CreateLogger<WebConsolePlugin>();
        private IDisposable host;

        public string HostName { get; set; }
        public int? Port { get; set; }

        public Task Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken)
        {
            // var liveLogPlugin = new LiveLogPlugin();
            // scheduler.ListenerManager.AddJobListener(liveLogPlugin);
            // scheduler.ListenerManager.AddTriggerListener(liveLogPlugin);
            // scheduler.ListenerManager.AddSchedulerListener(liveLogPlugin);

            // TODO REMOVE
            scheduler.AddCalendar(typeof (AnnualCalendar).Name, new AnnualCalendar(), false, false, cancellationToken);
            scheduler.AddCalendar(typeof (CronCalendar).Name, new CronCalendar("0 0/5 * * * ?"), false, false, cancellationToken);
            scheduler.AddCalendar(typeof (DailyCalendar).Name, new DailyCalendar("12:01", "13:04"), false, false, cancellationToken);
            scheduler.AddCalendar(typeof (HolidayCalendar).Name, new HolidayCalendar(), false, false, cancellationToken);
            scheduler.AddCalendar(typeof (MonthlyCalendar).Name, new MonthlyCalendar(), false, false, cancellationToken);
            scheduler.AddCalendar(typeof (WeeklyCalendar).Name, new WeeklyCalendar(), false, false, cancellationToken);

            return default;
        }

        public Task Start(CancellationToken cancellationToken)
        {
            string baseAddress = $"http://{HostName ?? "localhost"}:{Port ?? 28682}/";

            //host = WebApp.Start<Startup>(url: baseAddress);
            host = WebHost.CreateDefaultBuilder()
                .UseStartup<Startup>()
                .Build();
            
            log.LogInformation("Quartz Web Console bound to address {BaseAddress}", baseAddress);
            return TaskUtil.CompletedTask;
        }

        public Task Shutdown(CancellationToken cancellationToken)
        {
            host?.Dispose();
            return TaskUtil.CompletedTask;
        }
    }
}