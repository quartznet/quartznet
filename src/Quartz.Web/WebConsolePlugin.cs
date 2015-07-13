using System;
using System.Threading.Tasks;

using Microsoft.Owin.Hosting;

using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;

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
            if (host != null)
            {
                host.Dispose();
            }
            return TaskUtil.CompletedTask;
        }
    }
}