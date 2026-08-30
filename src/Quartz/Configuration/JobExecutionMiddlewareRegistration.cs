using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Configuration;

/// <summary>
/// One middleware a scheduler was told to wrap its job executions in, as a type, a factory or a
/// ready-made instance.
/// </summary>
/// <remarks>
/// <para>
/// The same shape a listener registration has, and registered the same way: held under the scheduler's
/// service key, so a named scheduler's middleware is its own and is never even seen by another
/// scheduler in the container. Resolution preserves registration order, which is what makes "outermost
/// first" a thing an application can rely on.
/// </para>
/// <para>
/// Unlike a listener, there is nothing to verify about the shape. Every member of the listener
/// interfaces has a default implementation, so a method with the right name and the wrong signature
/// compiles and silently stops implementing anything; <see cref="IJobExecutionMiddleware.Invoke" /> has
/// no default, so the compiler is the check.
/// </para>
/// </remarks>
internal sealed class JobExecutionMiddlewareRegistration
{
    private readonly Func<IServiceProvider, IJobExecutionMiddleware>? middlewareFactory;
    private readonly IJobExecutionMiddleware? middlewareInstance;

    /// <summary>
    /// Backing field for <see cref="MiddlewareType" />, written out rather than left to the compiler.
    /// </summary>
    /// <remarks>
    /// An auto-property's annotation does not reach its generated backing field, and ILCompiler checks
    /// the field — see the same field on <see cref="ListenerRegistration{TListener}" />, where a native
    /// AOT publish reported IL2078 for a requirement that is satisfied at both ends in the source.
    /// </remarks>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
    private readonly Type middlewareType;

    public JobExecutionMiddlewareRegistration(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        Type middlewareType,
        Func<IServiceProvider, IJobExecutionMiddleware>? middlewareFactory = null,
        IJobExecutionMiddleware? middlewareInstance = null)
    {
        this.middlewareType = middlewareType;
        this.middlewareFactory = middlewareFactory;
        this.middlewareInstance = middlewareInstance;
    }

    /// <summary>
    /// The type the middleware was registered as.
    /// </summary>
    /// <remarks>
    /// Only used to describe the registration. A factory overload may well return a subtype, so this is
    /// not an identity the middleware can be found by.
    /// </remarks>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
    public Type MiddlewareType => middlewareType;

    /// <summary>
    /// Produces the middleware, once there is a scheduler to compose it into.
    /// </summary>
    /// <param name="serviceProvider">
    /// The provider of the scheduler this registration belongs to, so a middleware built here is given
    /// that scheduler's collaborators rather than the default scheduler's.
    /// </param>
    public IJobExecutionMiddleware CreateMiddleware(IServiceProvider serviceProvider)
    {
        if (middlewareInstance is not null)
        {
            return middlewareInstance;
        }

        if (middlewareFactory is not null)
        {
            return middlewareFactory(serviceProvider);
        }

        return (IJobExecutionMiddleware) ActivatorUtilities.CreateInstance(serviceProvider, MiddlewareType);
    }
}
