using Microsoft.Extensions.Hosting;

using System.Diagnostics;

namespace Quartz.OpenTracing
{
    /// <summary>
    /// Starts and stops OpenTracing instrumentation component.
    /// </summary>
    internal sealed class InstrumentationService : IHostedService
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
