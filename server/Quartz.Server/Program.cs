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
            HostFactory.Run(x =>
                                {
                                    x.RunAsLocalSystem();

                                    x.SetDescription(Configuration.ServiceDescription);
                                    x.SetDisplayName(Configuration.ServiceDisplayName);
                                    x.SetServiceName(Configuration.ServiceName);

                                    x.Service(factory =>
                                                  {
                                                      QuartzServer server = new QuartzServer();
                                                      server.Initialize();
                                                      return server;
                                                  });
                                });
        }
    }
}