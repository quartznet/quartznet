using System;
using System.IO;
using System.Reflection;

using log4net.Config;

using Topshelf;

namespace Quartz.Server
{
    /// <summary>
    /// The server's main entry point.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main.
        /// </summary>
        public static void Main()
        {
            // change from service account's dir to more logical one
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            var logRepository = log4net.LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));

            HostFactory.Run(x =>
            {
                x.RunAsLocalSystem();

                x.SetDescription(Configuration.ServiceDescription);
                x.SetDisplayName(Configuration.ServiceDisplayName);
                x.SetServiceName(Configuration.ServiceName);

                x.Service(factory =>
                {
                    QuartzServer server = QuartzServerFactory.CreateServer();
                    server.Initialize().GetAwaiter().GetResult();
                    return server;
                });
            });
        }
    }
}