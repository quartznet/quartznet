using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Quartz.Examples.Worker
{
    public class ExampleJob : IJob, IDisposable
    {
        private readonly ILogger<ExampleJob> logger;

        public ExampleJob(ILogger<ExampleJob> logger)
        {
            this.logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            logger.LogInformation(context.JobDetail.Key + " job executing, triggered by " + context.Trigger.Key);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        public void Dispose()
        {
            logger.LogInformation("Example job disposing");
        }
    }
}