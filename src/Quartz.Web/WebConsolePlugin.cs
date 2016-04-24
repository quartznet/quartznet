using System;
using System.Threading.Tasks;
using Microsoft.Owin.Hosting;
using Quartz.Impl.Calendar;
using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;
using Quartz.Web.LiveLog;

namespace Quartz.Web
{
    public class WebConsolePlugin : ISchedulerPlugin
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof (WebConsolePlugin));
        private IDisposable host;

        public string HostName { get; set; }
        public int? Port { get; set; }

        public void Initialize(string pluginName, IScheduler scheduler)
        {
            var liveLogPlugin = new LiveLogPlugin();
            scheduler.ListenerManager.AddJobListener(liveLogPlugin);
            scheduler.ListenerManager.AddTriggerListener(liveLogPlugin);
            scheduler.ListenerManager.AddSchedulerListener(liveLogPlugin);

            // TODO REMOVE
            scheduler.AddCalendar(typeof (AnnualCalendar).Name, new AnnualCalendar(), false, false);
            scheduler.AddCalendar(typeof (CronCalendar).Name, new CronCalendar("0 0/5 * * * ?"), false, false);
            scheduler.AddCalendar(typeof (DailyCalendar).Name, new DailyCalendar("12:01", "13:04"), false, false);
            scheduler.AddCalendar(typeof (HolidayCalendar).Name, new HolidayCalendar(), false, false);
            scheduler.AddCalendar(typeof (MonthlyCalendar).Name, new MonthlyCalendar(), false, false);
            scheduler.AddCalendar(typeof (WeeklyCalendar).Name, new WeeklyCalendar(), false, false);
        }

        public Task Start()
        {
            string baseAddress = $"http://{HostName ?? "localhost"}:{Port ?? 28682}/";

            host = WebApp.Start<Startup>(url: baseAddress);
            log.InfoFormat("Quartz Web Console bound to address {0}", baseAddress);
            return TaskUtil.CompletedTask;
        }

        public Task Shutdown()
        {
            host?.Dispose();
            return TaskUtil.CompletedTask;
        }
    }
}