using System.Reflection;

using log4net.Config;
using log4net.Repository;

using Topshelf;

namespace Quartz.Server;

/// <summary>
/// The server's main entry point.
/// </summary>
public static class Program
{
    /// <summary>
    /// Main.
    /// </summary>
    public static async Task Main()
    {
        // change from service account's dir to more logical one
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

        ILoggerRepository logRepository = log4net.LogManager.GetRepository(Assembly.GetEntryAssembly()!);
        XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));
        QuartzServer server = QuartzServerFactory.CreateServer();
        await server.Initialize().ConfigureAwait(false);

        HostFactory.Run(x =>
        {
            x.RunAsLocalSystem();

            x.SetDescription(Configuration.ServiceDescription);
            x.SetDisplayName(Configuration.ServiceDisplayName);
            x.SetServiceName(Configuration.ServiceName);

            x.Service(_ => server);
        });
    }
}