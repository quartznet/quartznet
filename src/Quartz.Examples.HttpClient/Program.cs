using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz;

// Using HttpClientFactory with host builder
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHttpClient("QuartzHttpClient", client =>
        {
            client.BaseAddress = new Uri("http://localhost:5000/quartz-api/");
            client.DefaultRequestHeaders.Add("X-Quartz-ApiKey", "MySuperSecretApiKey");
        });

        // You can also use AddQuartzHttpClient(schedulerName, HttpClient) override if you do not want to use HttpClientFactory (AddHttpClient method call above)
        services.AddQuartzHttpClient("Quartz ASP.NET Core Sample Scheduler", "QuartzHttpClient");
    })
    .Build();

var httpScheduler = host.Services.GetRequiredService<IScheduler>();

/* Simply instantiating new HttpScheduler
using var httpClient = new HttpClient
{
    BaseAddress = new Uri("http://localhost:5000/quartz-api/"),
    DefaultRequestHeaders =
    {
        { "X-Quartz-ApiKey", "MySuperSecretApiKey" }
    }
};

var httpScheduler = new Quartz.HttpScheduler("Quartz ASP.NET Core Sample Scheduler", httpClient);
*/

/* A scheduler of your own, talking to the remote one. AddQuartzHttpClient is the only way to reach a
   remote scheduler: QuartzSchedulerBuilder builds a scheduler that runs here, and Quartz has no
   remoting of its own on modern .NET.

   To send your own headers or to change the address without HttpClientFactory, configure the
   HttpClient and hand it over:

using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5000/quartz-api/") };
httpClient.DefaultRequestHeaders.Add("X-Quartz-ApiKey", "MySuperSecretApiKey");

var httpScheduler = new Quartz.HttpScheduler("Quartz ASP.NET Core Sample Scheduler", httpClient);
*/

/* You can register multiple schedulers by creating marker interfaces for those
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHttpClient("QuartzHttpClient", client =>
        {
            client.BaseAddress = new Uri("http://localhost:5000/quartz-api/");
            client.DefaultRequestHeaders.Add("X-Quartz-ApiKey", "MySuperSecretApiKey");
        });

        services.AddQuartzHttpClient("Quartz ASP.NET Core Sample Scheduler", "QuartzHttpClient");
        services.AddQuartzHttpClient<IMyScheduler>("MyScheduler", "QuartzHttpClient");
        services.AddQuartzHttpClient<MyNamespace.IMySecondScheduler>("MySecondScheduler", "QuartzHttpClient");
    })
    .Build();

var myScheduler = host.Services.GetRequiredService<IMyScheduler>();
var mySecondScheduler = host.Services.GetRequiredService<MyNamespace.IMySecondScheduler>();
var httpScheduler = host.Services.GetRequiredService<IScheduler>();*/

while (true)
{
    Console.WriteLine();
    Console.Write("Press enter to check if scheduler is started");

    var line = Console.ReadLine();
    if (line == "exit")
    {
        break;
    }

    try
    {
        Console.WriteLine("Scheduler.IsStarted: " + httpScheduler.IsStarted);
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
    }
}

#pragma warning disable CA1050
public interface IMyScheduler : IScheduler
#pragma warning restore CA1050
{
}

namespace MyNamespace
{
    public interface IMySecondScheduler : IScheduler
    {
    }
}