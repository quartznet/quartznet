using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Core;

namespace Quartz.Configuration;

/// <summary>
/// One listener a scheduler was told to carry, as a type, a factory or a ready-made instance.
/// </summary>
/// <remarks>
/// <para>
/// The registration is the pairing. Matchers used to be registered apart from the listener and re-joined
/// afterwards by listener type, which cannot tell two registrations of the same type apart: two
/// <c>AddJobListener&lt;AuditListener&gt;</c> calls with different matchers were paired in registration
/// order, and a factory overload returning a subtype was not recognised as configured at all. Carrying
/// the listener and its matchers in one registration leaves nothing to infer.
/// </para>
/// <para>
/// Registrations are held under the scheduler's service key, so a named scheduler's listeners are its
/// own, the same way its job store and thread pool are.
/// </para>
/// </remarks>
internal abstract class ListenerRegistration<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] TListener>
    where TListener : class
{
    private readonly Func<IServiceProvider, TListener>? listenerFactory;
    private readonly TListener? listenerInstance;

    protected ListenerRegistration(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        Type listenerType,
        Func<IServiceProvider, TListener>? listenerFactory,
        TListener? listenerInstance)
    {
        // Said while the application is still writing its configuration, which is the earliest moment
        // the listener's type is known. A factory overload is checked here too, but only against the
        // type it was declared with; whatever it actually produces is checked when it reaches the
        // listener manager, at the latest.
        ListenerShape.Verify(listenerType, typeof(TListener));

        ListenerType = listenerType;
        this.listenerFactory = listenerFactory;
        this.listenerInstance = listenerInstance;
    }

    /// <summary>
    /// The type the listener was registered as.
    /// </summary>
    /// <remarks>
    /// Only used to describe the registration when reporting a problem with it. A factory overload may
    /// well return a subtype, so this is not an identity the listener can be found by.
    /// </remarks>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
    public Type ListenerType { get; }

    /// <summary>
    /// Produces the listener, once there is a scheduler to attach it to.
    /// </summary>
    /// <param name="serviceProvider">
    /// The provider of the scheduler this registration belongs to, so a listener built here is given that
    /// scheduler's collaborators rather than the default scheduler's.
    /// </param>
    public TListener CreateListener(IServiceProvider serviceProvider)
    {
        if (listenerInstance is not null)
        {
            return listenerInstance;
        }

        if (listenerFactory is not null)
        {
            return listenerFactory(serviceProvider);
        }

        return (TListener) ActivatorUtilities.CreateInstance(serviceProvider, ListenerType);
    }
}

/// <summary>
/// A job listener and the matchers deciding which jobs it hears about.
/// </summary>
internal sealed class JobListenerRegistration : ListenerRegistration<IJobListener>
{
    public JobListenerRegistration(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        Type listenerType,
        IMatcher<JobKey>[] matchers,
        Func<IServiceProvider, IJobListener>? listenerFactory = null,
        IJobListener? listenerInstance = null)
        : base(listenerType, listenerFactory, listenerInstance)
    {
        Matchers = matchers;
    }

    /// <summary>
    /// The matchers this listener was registered with. Empty means every job, as it always has.
    /// </summary>
    public IMatcher<JobKey>[] Matchers { get; }
}

/// <summary>
/// A trigger listener and the matchers deciding which triggers it hears about.
/// </summary>
internal sealed class TriggerListenerRegistration : ListenerRegistration<ITriggerListener>
{
    public TriggerListenerRegistration(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        Type listenerType,
        IMatcher<TriggerKey>[] matchers,
        Func<IServiceProvider, ITriggerListener>? listenerFactory = null,
        ITriggerListener? listenerInstance = null)
        : base(listenerType, listenerFactory, listenerInstance)
    {
        Matchers = matchers;
    }

    /// <summary>
    /// The matchers this listener was registered with. Empty means every trigger, as it always has.
    /// </summary>
    public IMatcher<TriggerKey>[] Matchers { get; }
}

/// <summary>
/// A scheduler listener, which has no matchers to carry because it hears about the scheduler itself.
/// </summary>
internal sealed class SchedulerListenerRegistration : ListenerRegistration<ISchedulerListener>
{
    public SchedulerListenerRegistration(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        Type listenerType,
        Func<IServiceProvider, ISchedulerListener>? listenerFactory = null,
        ISchedulerListener? listenerInstance = null)
        : base(listenerType, listenerFactory, listenerInstance)
    {
    }
}
