using System;
using System.Threading.Tasks;

using log4net;

using Microsoft.Extensions.Logging;

using Quartz.Logging;

namespace Quartz.Server
{
    /// <summary>
    /// A sample job that just prints info on console for demostration purposes.
    /// </summary>
    public class SampleJob : IJob
    {
        private static readonly ILogger<SampleJob> logger = LogProvider.CreateLogger<SampleJob>();

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
        /// fires that is associated with the <see cref="IJob" />.
        /// </summary>
        /// <remarks>
        /// The implementation may wish to set a  result object on the 
        /// JobExecutionContext before this method exits.  The result itself
        /// is meaningless to Quartz, but may be informative to 
        /// <see cref="IJobListener" />s or 
        /// <see cref="ITriggerListener" />s that are watching the job's 
        /// execution.
        /// </remarks>
        /// <param name="context">The execution context.</param>
        public async Task Execute(IJobExecutionContext context)
        {
            logger.LogInformation("SampleJob running...");
            await Task.Delay(TimeSpan.FromSeconds(5));
            logger.LogInformation("SampleJob run finished.");
        }
    }
}