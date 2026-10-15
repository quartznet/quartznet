using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Quartz.Impl;
using Quartz.Util;

namespace Quartz;

/// <summary>
/// Store the Job Type and FullName for serialization
/// </summary>
/// <remarks>
/// A <see cref="System.Type" /> converts implicitly — that construction validates the type
/// implements <see cref="IJob" /> and cannot fail later. A string converts only explicitly (or via
/// the constructor), because a name is unvalidated at construction: resolving it happens on first
/// use of <see cref="Type" /> and can throw there. Two instances are equal when their
/// <see cref="FullName" /> is equal.
/// </remarks>
public sealed class JobType : IEquatable<JobType>
{
    /// <summary>
    /// What the reflective half of <see cref="ResolveWithoutLoader" /> says for itself. Every call to it
    /// is guarded by <see cref="FeatureSwitches.IsTypeNameResolutionSupported" />.
    /// </summary>
    private const string ResolvesByReflection =
        "A job type name nothing registered is resolved with Type.GetType, which a trimmed application cannot rely on; "
        + FeatureSwitches.TypeNameResolutionSwitch + " guards every call.";

    /// <summary>
    /// How a name becomes a type when the caller had nothing better to offer.
    /// </summary>
    /// <remarks>
    /// A delegate to a method that <em>contains</em> the guard, never to the guarded method: taking the
    /// address of a <see cref="RequiresUnreferencedCodeAttribute" /> method is itself <c>IL2026</c>.
    /// </remarks>
    private static readonly Func<string, Type?> defaultResolver = ResolveWithoutLoader;

    private readonly Lazy<Type> type;

    /// <summary>
    /// Turns <see cref="FullName" /> into a type. Only consulted for a name-constructed instance.
    /// </summary>
    private readonly Func<string, Type?> resolver;

    /// <summary>
    /// The type this was constructed from, when it was constructed from one at all.
    /// </summary>
    [DynamicallyAccessedMembers(JobTypeMembers.Required)]
    private readonly Type? declaredType;

    /// <summary>
    /// Construct a Job Type specifying the Assembly Qualified NameWithout Version.
    /// There is no check on construction this type is valid.
    /// </summary>
    /// <remarks>
    /// The name resolves on first use of <see cref="Type" />: from the types this process registered —
    /// <c>AddJob&lt;T&gt;()</c>, <c>AddJobType&lt;T&gt;()</c>, <c>JobBuilder.Create&lt;T&gt;()</c>,
    /// <c>UseTypeLoader(o =&gt; o.Map(name, typeof(T)))</c> — and, while the
    /// <c>Quartz.TypeNameResolution.IsSupported</c> switch is on, from the runtime after them. A trimmed
    /// or native publish turns the switch off, so there a name nothing registered fails to resolve, with
    /// a message naming the registration to add.
    /// </remarks>
    /// <param name="fullName">Type full name</param>
    /// <exception cref="ArgumentNullException"><paramref name="fullName"/> is <see langword="null" /></exception>
    public JobType(string fullName) : this(fullName, defaultResolver)
    {
    }

    /// <summary>
    /// Construct a Job Type from a name that something other than <see cref="Type.GetType(string)" />
    /// knows how to resolve.
    /// </summary>
    /// <remarks>
    /// A name read out of a job store was written by whichever version of Quartz stored it, so resolving
    /// it may need the same rename fallbacks that a configured type name gets - which live in an
    /// <see cref="Extensibility.ITypeLoader" />, not in the runtime. The name itself is kept exactly
    /// as given: <see cref="FullName" /> reports the stored spelling however the type was found, so
    /// reading a job never rewrites what is persisted for it.
    /// </remarks>
    /// <param name="fullName">Type full name</param>
    /// <param name="resolver">Resolves <paramref name="fullName" />, returning <see langword="null" /> when it names nothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fullName"/> is <see langword="null" /></exception>
    internal JobType(string fullName, Func<string, Type?> resolver)
    {
        if (fullName is null)
        {
            Throw.ArgumentNullException(nameof(fullName));
        }
        FullName = fullName;
        this.resolver = resolver;
        type = new Lazy<Type>(() =>
        {
            Type? loadedType = resolver(fullName);
            if (loadedType is null)
            {
                Throw.InvalidOperationException(FeatureSwitches.IsTypeNameResolutionSupported
                    ? $"Job type {fullName} cannot be resolved."
                    : $"Job type {fullName} cannot be resolved. {RegisteredTypeNames.NotRegistered(fullName)}");
            }
            return loadedType!;
        });
    }

    /// <summary>
    /// Job Type declaration
    /// </summary>
    /// <param name="type">The Job Type</param>
    /// <exception cref="ArgumentException"><paramref name="type"/> is not assignable from  <see cref="IJob"/></exception>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null" /></exception>
    public JobType([DynamicallyAccessedMembers(JobTypeMembers.Required)] Type type)
    {
        if (type is null)
        {
            Throw.ArgumentNullException(nameof(type));
        }

        if (!typeof(IJob).IsAssignableFrom(type))
        {
            Throw.ArgumentException("Job type must implement Quartz.IJob interface", nameof(type));
        }

        this.type = new Lazy<Type>(() => type);
        resolver = defaultResolver;
        declaredType = type;
        FullName = GetFullName(type);

        // Every typed way of naming a job comes through here, so this is where the name learns what it
        // means for the readers that only ever see the name: a store, a schedule file, a request body.
        RegisteredTypeNames.Register(type);
    }

    /// <summary>
    /// JobType Serialized Full name
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// The resolved type, loaded on first read from the name this instance was built with.
    /// </summary>
    public Type Type => type.Value;

    /// <summary>
    /// The type this was constructed from, or <see langword="null" /> when it was constructed from a
    /// name. Annotated truthfully: the only way to set it is the constructor that takes a
    /// <see cref="System.Type" />, and that constructor asks its caller for the same members.
    /// </summary>
    [DynamicallyAccessedMembers(JobTypeMembers.Required)]
    internal Type? DeclaredType => declaredType;

    /// <summary>
    /// The job's type, carrying the annotation the reflection Quartz does on it requires.
    /// </summary>
    /// <remarks>
    /// A <see cref="JobType" /> built from a <see cref="System.Type" /> took that type from a caller who
    /// declared these members, so the annotation is earned. One built from a name goes through
    /// <see cref="FoundByName" />, which is where the difference between the two is admitted.
    /// </remarks>
    [DynamicallyAccessedMembers(JobTypeMembers.Required)]
    internal Type ResolvedType => DeclaredType ?? FoundByName(type.Value)!;

    /// <summary>
    /// Hands a type that was found by name on as one Quartz may reflect over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the fork the whole of issue #3341, step 3 turns on: the single place in Quartz where a
    /// type nobody declared statically is given the annotation the fire path asks for. Everything
    /// downstream — the attribute checks, both job factories, the ADO store's acquisition loop — reads
    /// an annotated type and needs no suppression of its own.
    /// </para>
    /// <para>
    /// The suppression is earned rather than waived. A type found by name comes out of
    /// <see cref="RegisteredTypeNames" />, which every entry went into through a parameter or type
    /// argument carrying this same annotation — <see cref="JobType(Type)" />, <c>AddJobType&lt;T&gt;()</c>,
    /// <c>TypeLoaderOptions.Map</c> — so its members were kept; or, while
    /// <see cref="FeatureSwitches.IsTypeNameResolutionSupported" /> is on, out of the runtime, which
    /// only a publish that chose to keep reflection reaches, and which is the <c>IL2026</c> such a
    /// publish is told about at every guarded call. A trimmed publish with the switch off never sees a
    /// type here that the table did not hold.
    /// </para>
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2068", Justification = "A type found by name comes from RegisteredTypeNames, whose entries arrived annotated, or from reflection a trimmed publish has switched off. See the remarks.")]
    [return: DynamicallyAccessedMembers(JobTypeMembers.Required)]
    private static Type? FoundByName(Type? resolved) => resolved;

    /// <summary>
    /// Resolves a stored job type name through a scheduler's type loader, for a caller that wants the
    /// type and not a <see cref="JobType" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ADO store's acquisition loop asks this per trigger, purely to find out whether the job
    /// carries <see cref="DisallowConcurrentExecutionAttribute" />, and would otherwise build a
    /// <see cref="JobType" /> it immediately throws away. It goes through <see cref="FoundByName" />,
    /// so it is the same fork and not a second one.
    /// </para>
    /// <para>
    /// The two readers that hand the type straight on to <c>JobBuilder.OfType(Type)</c> ask it too:
    /// <c>StdAdoDelegate.SelectJobForTrigger</c> when told to load the job's class, and the
    /// <c>Quartz:Schedule:Jobs</c> configuration reader. Each resolves eagerly on purpose — the loader's
    /// <see cref="TypeLoadException" /> is the failure their callers see — so a lazily resolved
    /// <see cref="JobType" /> would not be the same answer, only the same fork.
    /// </para>
    /// </remarks>
    [return: DynamicallyAccessedMembers(JobTypeMembers.Required)]
    internal static Type? Resolve(string fullName, Extensibility.ITypeLoader typeLoader) => FoundByName(typeLoader.LoadType(fullName));

    /// <summary>
    /// Resolves a name with no <see cref="Extensibility.ITypeLoader" /> in front of it: the registered
    /// types first, and the runtime's own lookup after them while the switch allows it.
    /// </summary>
    /// <remarks>
    /// What a name-constructed <see cref="JobType" /> falls back to when nothing better was offered, and
    /// what <c>StdAdoDelegate</c> falls back to when the scheduler's loader refuses a stored name — a
    /// loader may answer only for the names it knows and throw for the rest, as its contract asks. With
    /// <see cref="FeatureSwitches.IsTypeNameResolutionSupported" /> off, the table is the whole of the
    /// fallback: a name it does not hold is <see langword="null" /> here and the registration hint at
    /// <see cref="Type" />, and nothing reflects.
    /// </remarks>
    /// <param name="fullName">The assembly-qualified name to resolve.</param>
    /// <returns>The type, or <see langword="null" /> when nothing registered the name and the runtime does not know it.</returns>
    internal static Type? ResolveWithoutLoader(string fullName) =>
        RegisteredTypeNames.Find(fullName)
        ?? (FeatureSwitches.IsTypeNameResolutionSupported ? ResolveByReflection(fullName) : null);

    [RequiresUnreferencedCode(ResolvesByReflection)]
    private static Type? ResolveByReflection(string fullName) => Type.GetType(fullName);

    /// <summary>
    /// Refuses a type that is not an <see cref="IJob" />, before anything constructs it.
    /// </summary>
    /// <remarks>
    /// A job type that arrived as a name is checked only for the shape of a name, so the first thing that
    /// establishes it is a job at all is the cast that follows construction — by which time the type's
    /// static constructor, its module initializer and its instance constructor have run, with the
    /// scheduler scope's services injected into them. The check therefore belongs in front of every path
    /// that builds an instance: the container lookup, <c>ActivatorUtilities</c>, and
    /// <see cref="Util.TypeActivator" />.
    /// </remarks>
    /// <exception cref="SchedulerException"><paramref name="jobType" /> does not implement <see cref="IJob" />.</exception>
    internal static void EnsureIsJob(Type jobType)
    {
        if (!typeof(IJob).IsAssignableFrom(jobType))
        {
            Throw.SchedulerException($"Job type '{jobType.FullName}' does not implement {typeof(IJob).FullName}; no instance of it is created.");
        }
    }

    /// <summary>
    /// The type this instance already holds, without going looking for one: the <see cref="System.Type" />
    /// it was built from, or the one an earlier resolution settled. <see langword="null" /> when all it
    /// has is a name that nothing has resolved yet.
    /// </summary>
    /// <remarks>
    /// For the readers that must answer about a job without probing for its assembly — projecting a job
    /// onto the wire, rendering it on a dashboard — where a probe would be work done on a caller's say-so
    /// and, on a client, work a *server* asked for. They report what is known and say nothing where
    /// nothing is; <see cref="TryResolve" /> is for the callers that need the type itself.
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2073", Justification = "Both sources are values that arrived through an annotated path: the declared type, or a type FoundByName already returned.")]
    [return: DynamicallyAccessedMembers(JobTypeMembers.Required)]
    internal Type? GetLoadedType()
    {
        if (DeclaredType is { } declared)
        {
            return declared;
        }

        return type.IsValueCreated ? type.Value : null;
    }

    /// <summary>
    /// Resolves the type for a caller that can carry on without it, without throwing and without settling
    /// <see cref="Type" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="JobType" /> built from a <see cref="System.Type" /> hands back that exact instance, so a
    /// caller checking a job's type gets the right answer even when the assembly is not reachable by name -
    /// from a plugin load context, say. One built from a name has to go looking, and simply has no answer
    /// when the name does not resolve.
    /// </para>
    /// <para>
    /// The lookup deliberately does not go through <see cref="Type" />: that caches its outcome for the
    /// life of this instance, so a probe made before the job's assembly is loaded would leave the type
    /// permanently unresolvable. A speculative caller must not be able to poison the real one.
    /// </para>
    /// </remarks>
    public bool TryResolve([NotNullWhen(true)][DynamicallyAccessedMembers(JobTypeMembers.Required)] out Type? resolved)
    {
        if (DeclaredType is { } declared)
        {
            resolved = declared;
            return true;
        }

        if (type.IsValueCreated)
        {
            resolved = ResolvedType;
            return true;
        }

        try
        {
            // Suppressing not-found does not suppress an assembly that is found but cannot be loaded.
            resolved = FoundByName(resolver(FullName));
        }
        catch (Exception e) when (e is ArgumentException or FileLoadException or BadImageFormatException or TypeLoadException or TargetInvocationException)
        {
            resolved = null;
        }

        return resolved is not null;
    }

    private static string GetFullName(Type jobType)
    {
        if (jobType.AssemblyQualifiedName is null)
        {
            Throw.ArgumentException("Cannot determine job type name when type's AssemblyQualifiedName is null", nameof(jobType));
        }

        return jobType.AssemblyQualifiedNameWithoutVersion();
    }

    /// <summary>
    /// The validated direction: construction from a <see cref="System.Type" /> checks
    /// <see cref="IJob" /> assignability and the conversion cannot fail later.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="type"/> does not implement <see cref="IJob"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null" />.</exception>
    public static implicit operator JobType([DynamicallyAccessedMembers(JobTypeMembers.Required)] Type type) => new(type);

    /// <summary>
    /// Explicit on purpose: a name is accepted unvalidated, and resolving it is deferred to the
    /// first use of <see cref="Type" /> — which is where a bad name fails. The cast makes the leap
    /// of faith visible at the call site.
    /// </summary>
    /// <inheritdoc cref="JobType(string)" path="/remarks" />
    /// <exception cref="ArgumentNullException"><paramref name="fullName"/> is <see langword="null" />.</exception>
    public static explicit operator JobType(string fullName) => new(fullName);

    /// <summary>
    /// Whether this and <paramref name="other" /> name the same job type.
    /// </summary>
    /// <param name="other">The job type to compare with.</param>
    public bool Equals(JobType? other)
    {
        return other is not null && (ReferenceEquals(this, other) || FullName == other.FullName);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as JobType);
    }

    /// <summary>
    /// Whether two job types name the same type.
    /// </summary>
    public static bool operator ==(JobType? left, JobType? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    /// <summary>
    /// Whether two job types name different types.
    /// </summary>
    public static bool operator !=(JobType? left, JobType? right)
    {
        return !(left == right);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return FullName;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return FullName.GetHashCode();
    }
}