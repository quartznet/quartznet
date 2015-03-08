using System.IO;
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
            Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);

            HostFactory.Run(x =>
                                {
                                    x.RunAsLocalSystem();

                                    x.SetDescription(Configuration.ServiceDescription);
                                    x.SetDisplayName(Configuration.ServiceDisplayName);
                                    x.SetServiceName(Configuration.ServiceName);

                                    x.Service(factory =>
                                                  {
                                                      QuartzServer server = QuartzServerFactory.CreateServer();
                                                      server.Initialize();
                                                      return server;
                                                  });
                                });
        }
    }
}