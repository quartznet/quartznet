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

// If you do not want to use HttpClientFactory (the AddHttpClient call above), the other overload takes
// a factory instead of a name: AddQuartzHttpClient(schedulerName, provider => BuildMyClient(provider)).
// The client it returns belongs to whoever made it - the scheduler never disposes it.
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

// One reading first, then a prompt only where somebody is there to answer it. Blocking on Console.ReadLine
// before the first call made this example unrunnable from a script, a container or a CI step -- and the
// one thing it exists to show is that the call works.
ReportStatus();

if (!Environment.UserInteractive || Console.IsInputRedirected)
{
    return;
}

while (true)
{
    Console.WriteLine();
    Console.Write("Press enter to check again, or type 'exit' to quit: ");

    var line = Console.ReadLine();
    if (line is null or "exit")
    {
        break;
    }

    ReportStatus();
}

void ReportStatus()
{
    try
    {
        Console.WriteLine("Scheduler.Status: " + httpScheduler.Status);
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
    }
}
