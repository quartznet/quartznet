using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Quartz.Examples.AspNetCore
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
            logger.LogInformation("Example job executing");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        public void Dispose()
        {
            logger.LogInformation("Example job disposing");
        }
    }
}