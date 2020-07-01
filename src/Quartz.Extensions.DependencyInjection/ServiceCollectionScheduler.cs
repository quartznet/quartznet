using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;

namespace Quartz
{
    /// <summary>
    /// Wrapper to initialize registered jobs.
    /// </summary>
    internal class ServiceCollectionScheduler : ISchedulerFactory
    {
        private readonly IServiceProvider serviceProvider;
        private readonly StdSchedulerFactory inner;
        private bool initialized = false;

        public ServiceCollectionScheduler(IServiceProvider serviceProvider, StdSchedulerFactory inner)
        {
            this.serviceProvider = serviceProvider;
            this.inner = inner;
        }

        public Task<IReadOnlyList<IScheduler>> GetAllSchedulers(CancellationToken cancellationToken = default)
        {
            return inner.GetAllSchedulers(cancellationToken);
        }

        public async Task<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
        {
            var scheduler = await inner.GetScheduler(cancellationToken);

            if (initialized)
            {
                return scheduler;
            }

            var jobs = serviceProvider.GetServices<QuartzJobRegistration>();
            foreach (var job in jobs)
            {
                await job.Attach(scheduler);
            }

            initialized = true;
            return scheduler;
        }

        public Task<IScheduler?> GetScheduler(string schedName, CancellationToken cancellationToken = default)
        {
            return inner.GetScheduler(schedName, cancellationToken);
        }
    }
}