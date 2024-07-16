using System.Text.Json.Nodes;

namespace Quartz.Examples.AspNetCore;

public class ExampleJob : IJob, IDisposable
{
    private readonly ILogger<ExampleJob> logger;
    private readonly IHttpClientFactory httpClientFactory;

    public ExampleJob(
        ILogger<ExampleJob> logger,
        IHttpClientFactory httpClientFactory)
    {
        this.logger = logger;
        this.httpClientFactory = httpClientFactory;
    }

    public string? InjectedString { get; set; }
    public bool InjectedBool { get; set; }

    public async ValueTask Execute(IJobExecutionContext context)
    {
        logger.LogInformation(
            "Job {Job} executing, triggered by {Trigger}. InjectedString: {InjectedString}, InjectedBool: {InjectedBool}",
            context.JobDetail.Key,
            context.Trigger.Key,
            InjectedString,
            InjectedBool);

        using var httpClient = httpClientFactory.CreateClient("example");
        var result = await httpClient.GetFromJsonAsync<JsonObject>("http://localhost:5000/healthz");
        logger.LogInformation("Got health check result {Result}", result);

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        logger.LogInformation("Example job disposing");
    }
}