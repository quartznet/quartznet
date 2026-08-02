using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Quartz.Util;

namespace Quartz;

/// <summary>
/// Store the Job Type and FullName for serialization
/// </summary>
public sealed class JobType
{
    private readonly Lazy<Type> type;

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
    public JobType(string fullName)
    {
        if (fullName is null)
        {
            Throw.ArgumentNullException(nameof(fullName));
        }
        FullName = fullName;
        type = new Lazy<Type>(() =>
        {
            var loadedType = Type.GetType(fullName);
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
    internal bool TryResolve([NotNullWhen(true)] out Type? resolved)
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
            resolved = Type.GetType(FullName, throwOnError: false);
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

    public static implicit operator Type(JobType jobType) => jobType.Type;

    public static implicit operator JobType(string fullName) => new(fullName);

    private bool Equals(JobType other)
    {
        return FullName == other.FullName;
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is JobType other && Equals(other);
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