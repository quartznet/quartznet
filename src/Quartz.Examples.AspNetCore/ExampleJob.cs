using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Quartz.Examples.AspNetCore;

public class ExampleJob : IJob, IDisposable
{
    private readonly ILogger<ExampleJob> logger;

    public ExampleJob(ILogger<ExampleJob> logger)
    {
        this.logger = logger;
    }

    public string? InjectedString { get; set; }
    public bool InjectedBool { get; set; }

    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation(
            "Job {Job} executing, triggered by {Trigger}. InjectedString: {InjectedString}, InjectedBool: {InjectedBool}",
            context.JobDetail.Key,
            context.Trigger.Key,
            InjectedString,
            InjectedBool);

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        logger.LogInformation("Example job disposing");
    }
}