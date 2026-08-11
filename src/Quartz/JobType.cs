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
    private readonly Type? declaredType;

    /// <summary>
    /// Construct a Job Type specifying the Assembly Qualified NameWithout Version.
    /// There is no check on construction this type is valid.
    /// </summary>
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
    /// <see cref="Extensibility.ITypeLoadHelper" />, not in the runtime. The name itself is kept exactly
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
    public JobType(Type type)
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
    public string FullName { get; private set; }

    public Type Type => type.Value;

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
    public bool TryResolve([NotNullWhen(true)] out Type? resolved)
    {
        if (declaredType is not null)
        {
            resolved = declaredType;
            return true;
        }

        if (type.IsValueCreated)
        {
            resolved = type.Value;
            return true;
        }

        try
        {
            // Suppressing not-found does not suppress an assembly that is found but cannot be loaded.
            resolved = resolver(FullName);
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
    public static implicit operator JobType(Type type) => new(type);

    /// <summary>
    /// Explicit on purpose: a name is accepted unvalidated, and resolving it is deferred to the
    /// first use of <see cref="Type" /> — which is where a bad name fails. The cast makes the leap
    /// of faith visible at the call site.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="fullName"/> is <see langword="null" />.</exception>
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