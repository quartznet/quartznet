using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz;

// Using HttpClientFactory with the host application builder
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient("QuartzHttpClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:5000/quartz-api/");
    client.DefaultRequestHeaders.Add("X-Quartz-ApiKey", "MySuperSecretApiKey");
});

// You can also use AddQuartzHttpClient(schedulerName, HttpClient) override if you do not want to use HttpClientFactory (AddHttpClient method call above)
builder.Services.AddQuartzHttpClient("Quartz ASP.NET Core Sample Scheduler", "QuartzHttpClient");

using IHost host = builder.Build();

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

/* Several remote schedulers are told apart by name, which is the service key each is registered under
builder.Services.AddQuartzHttpClient("Quartz ASP.NET Core Sample Scheduler", "QuartzHttpClient");
builder.Services.AddQuartzHttpClient("MyScheduler", "QuartzHttpClient");
builder.Services.AddQuartzHttpClient("MySecondScheduler", "QuartzHttpClient");

var myScheduler = host.Services.GetRequiredKeyedService<IScheduler>("MyScheduler");
var mySecondScheduler = host.Services.GetRequiredKeyedService<IScheduler>("MySecondScheduler");

// A class of your own reaches one the same way: [FromKeyedServices("MyScheduler")] IScheduler scheduler.
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
