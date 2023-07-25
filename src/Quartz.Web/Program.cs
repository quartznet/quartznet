using Microsoft.AspNetCore;

namespace Quartz.Web;

public class Program
{
    public static void Main(string[] args)
    {
        BuildWebHost(args).Run();
    }

    public static IWebHost BuildWebHost(string[] args) =>
        WebHost.CreateDefaultBuilder(args)
            .UseWebRoot("App")
            .UseStartup<Startup>()
            .Build();
}