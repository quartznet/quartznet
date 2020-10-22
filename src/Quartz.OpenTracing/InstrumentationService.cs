using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Quartz.OpenTracing
{
    /// <summary>
    /// Starts and stops OpenTracing instrumentation component.
    /// </summary>
    internal class InstrumentationService : IHostedService
    {
        private readonly QuartzDiagnostic quartzDiagnostic;
        private IDisposable? subscription;

        public InstrumentationService(QuartzDiagnostic quartzDiagnostic)
        {
            this.quartzDiagnostic = quartzDiagnostic ?? throw new ArgumentNullException(nameof(quartzDiagnostic));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            subscription = DiagnosticListener.AllListeners.Subscribe(quartzDiagnostic);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            subscription?.Dispose();

            return Task.CompletedTask;
        }
    }
}
