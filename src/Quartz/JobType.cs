using System.Diagnostics.CodeAnalysis;
using System.Reflection;

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
    /// What every API that turns a type <i>name</i> into a job says, in the same words. Named as a
    /// constant so the sentence cannot drift between the constructor, the cast and the builder.
    /// </summary>
    internal const string NamedTypeIsNotGuaranteedToSurviveTrimming =
        "Register every job type with AddJob<T>() or reference it from JobBuilder.Create<T>(); a type named only by a string in configuration or in the database is not guaranteed to survive trimming.";

    /// <summary>
    /// How a name becomes a type when the caller had nothing better to offer: the runtime's own lookup.
    /// </summary>
    private static readonly Func<string, Type?> defaultResolver = static name => Type.GetType(name);

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
    /// <param name="fullName">Type full name</param>
    /// <exception cref="ArgumentNullException"><paramref name="fullName"/> is <see langword="null" /></exception>
    [RequiresUnreferencedCode(NamedTypeIsNotGuaranteedToSurviveTrimming)]
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
                Throw.InvalidOperationException($"Job type {fullName} cannot be resolved.");
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
    }

    /// <summary>
    /// JobType Serialized Full name
    /// </summary>
    public string FullName { get; }

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
    /// The suppression costs an application that never names a job by string nothing, because every way
    /// of producing a name-constructed <see cref="JobType" /> that Quartz offers a caller is itself
    /// <see cref="RequiresUnreferencedCodeAttribute" />: <see cref="JobType(string)" />, the explicit
    /// cast from <see cref="string" />, and <c>JobBuilder&lt;TJob&gt;.OfType(string)</c>. The rest are
    /// Quartz's own persistence and configuration readers, whose string contracts are recorded in
    /// <c>TrimAnalysisBaseline.cs</c>: an application that keeps jobs in a database or names them in
    /// <c>quartz.*</c> keys is naming types by string and has to root them itself.
    /// </para>
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2068", Justification = "A name-constructed JobType has no annotation to carry; every API that produces one is itself RequiresUnreferencedCode. See the remarks.")]
    [return: DynamicallyAccessedMembers(JobTypeMembers.Required)]
    private static Type? FoundByName(Type? resolved) => resolved;

    /// <summary>
    /// Resolves a stored job type name through a scheduler's type loader, for a caller that wants the
    /// type and not a <see cref="JobType" />.
    /// </summary>
    /// <remarks>
    /// The ADO store's acquisition loop asks this per trigger, purely to find out whether the job
    /// carries <see cref="DisallowConcurrentExecutionAttribute" />, and would otherwise build a
    /// <see cref="JobType" /> it immediately throws away. It goes through <see cref="FoundByName" />,
    /// so it is the same fork and not a second one.
    /// </remarks>
    [return: DynamicallyAccessedMembers(JobTypeMembers.Required)]
    internal static Type? Resolve(string fullName, Extensibility.ITypeLoader typeLoader) => FoundByName(typeLoader.LoadType(fullName));

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
    /// <exception cref="ArgumentNullException"><paramref name="fullName"/> is <see langword="null" />.</exception>
    [RequiresUnreferencedCode(NamedTypeIsNotGuaranteedToSurviveTrimming)]
    public static explicit operator JobType(string fullName) => new(fullName);

    public bool Equals(JobType? other)
    {
        return other is not null && (ReferenceEquals(this, other) || FullName == other.FullName);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as JobType);
    }

    public static bool operator ==(JobType? left, JobType? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    public static bool operator !=(JobType? left, JobType? right)
    {
        return !(left == right);
    }

    public override string ToString()
    {
        return FullName;
    }

    public override int GetHashCode()
    {
        return FullName.GetHashCode();
    }
}