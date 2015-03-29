using System;

using Microsoft.Owin.Hosting;

using Quartz.Logging;
using Quartz.Spi;

namespace Quartz.Web
{
    public class WebConsolePlugin : ISchedulerPlugin
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof(WebConsolePlugin));
        private IDisposable host;

        public string HostName { get; set; }
        public int? Port { get; set; }

        public void Initialize(string pluginName, IScheduler scheduler)
        {
        }

        public void Start()
        {
            string baseAddress = string.Format("http://{0}:{1}/", HostName ?? "localhost", Port ?? 28682);

            host = WebApp.Start<Startup>(url: baseAddress);
            log.InfoFormat("Quartz Web Console bound to address {0}", baseAddress);
        }

        public void Shutdown()
        {
            if (host != null)
            {
                host.Dispose();
            }
        }
    }
}