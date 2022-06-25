using System;

using Quartz.Util;

namespace Quartz.Impl;

/// <summary>
/// Store the Job Type and FullName for serialization
/// </summary>
[Serializable]
public sealed class JobType
{
    private Lazy<Type> type;

    /// <summary>
    /// Construct a Job Type specifying the Assembly Qualified NameWithout Version.
    /// There is no check on construction this type is valid.
    /// </summary>
    /// <param name="fullName">Type full name</param>
    /// <exception cref="ArgumentNullException"><paramref name="fullName"/> is <see langword="null" /></exception>
    public JobType(string fullName)
    {
        FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
        type = new Lazy<Type>(() =>
            Type.GetType(fullName) ?? throw new InvalidOperationException($"Job class Type {fullName} cannot be resolved."));
    }

    /// <summary>
    /// Job Type declaration
    /// </summary>
    /// <param name="type">The Job Type</param>
    /// <exception cref="ArgumentException"><paramref name="type"/> is not assignable from  <see cref="Quartz.IJob"/></exception>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null" /></exception>
    public JobType(Type type)
    {
        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        if (!typeof(IJob).IsAssignableFrom(type))
        {
            throw new ArgumentException("Job class must implement Quartz.IJob interface.");
        }

        this.type = new Lazy<Type>(() => type);
        FullName = GetFullName(type);
    }

    /// <summary>
    /// JobType Serialized Full name
    /// </summary>
    public string FullName { get; private set; }

    public Type Type => type.Value;

    private string GetFullName(Type jobType)
    {
        if (jobType.AssemblyQualifiedName == null)
        {
            throw new ArgumentException("Cannot determine job type name when type's AssemblyQualifiedName is null");
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

    public override int GetHashCode()
    {
        return FullName.GetHashCode();
    }
}