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

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Job {Job} executing, triggered by {Trigger}. InjectedString: {InjectedString}, InjectedBool: {InjectedBool}",
            context.JobDetail.Key,
            context.Trigger.Key,
            InjectedString,
            InjectedBool);

        // The point of the call is that a job can take an IHttpClientFactory and use it. The endpoint is
        // this application's own health check, which MapHealthChecks writes as plain text ("Healthy")
        // unless a response writer says otherwise - so the answer is read as text rather than as JSON.
        using var httpClient = httpClientFactory.CreateClient("example");
        var result = await httpClient.GetStringAsync("http://localhost:5000/healthz", cancellationToken);
        logger.LogInformation("Got health check result {Result}", result);

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        logger.LogInformation("Example job disposing");
    }
}