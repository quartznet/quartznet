using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz;

/// <summary>
/// Settings for the job factory that builds jobs from the container.
/// </summary>
/// <remarks>
/// Per-scheduler, like every other component's options: under <c>AddQuartz("reporting", …)</c> these are
/// <c>reporting</c>'s, and the default scheduler's are its own.
/// </remarks>
public sealed class JobFactoryOptions
{
    /// <summary>
    /// Prepares the dependency injection scope a job is about to be built in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the hook for services that are scoped and need the ambient context of a job — a tenant
    /// read from the trigger's data, a correlation id, an <see cref="System.Threading.AsyncLocal{T}"/>
    /// the job's dependencies read. It runs <em>before</em> the job is resolved, so anything it sets is
    /// in place while the job and everything it injects are constructed.
    /// </para>
    /// <para>
    /// It is deliberately synchronous. An asynchronous hook would be awaited, and an
    /// <see cref="System.Threading.ExecutionContext"/> restored on the way back would discard exactly the
    /// <see cref="System.Threading.AsyncLocal{T}"/> values this exists to set.
    /// </para>
    /// <para>
    /// Setting it combines rather than replaces, so two callbacks each get their say; they run in the
    /// order they were added. It was previously reachable only by deriving from
    /// <c>MicrosoftDependencyInjectionJobFactory</c> and overriding a protected method — which meant
    /// writing and registering a job factory to set one <see cref="System.Threading.AsyncLocal{T}"/>.
    /// </para>
    /// </remarks>
    public Action<IServiceScope, TriggerFiredBundle, IScheduler>? ConfigureScope { get; set; }
}
