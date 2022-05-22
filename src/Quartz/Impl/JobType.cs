using System;

using Quartz.Util;

namespace Quartz.Impl;

/// <summary>
/// Store the Job Type and FullName for serialization
/// </summary>
[Serializable]
public sealed class JobType
{
    private Lazy<Type> type = new(() => throw new InvalidOperationException("Type not defined"));

    public JobType(string? fullName)
    {
        SetWithFullName(fullName ?? throw new ArgumentNullException(nameof(fullName)));
    }

    /// <summary>
    /// Job Type declaration
    /// </summary>
    public JobType(Type type)
    {
        SetWithType(type);
    }

    /// <summary>
    /// JobType Serialized Full name
    /// </summary>
    public string FullName { get; private set; } = "undefined";

    private void SetWithFullName(string fullName)
    {
        this.FullName = fullName;
        type = new Lazy<Type>(() =>
            Type.GetType(fullName) ?? throw new InvalidOperationException($"Job class Type {fullName} cannot be resolved."));
    }

    /// <summary>
    /// Set the Job class type
    /// </summary>
    /// <param name="jobType"></param>
    /// <exception cref="ArgumentException"></exception>
    public void SetWithType(Type jobType)
    {
        if (jobType == null)
        {
            throw new ArgumentException("Job type cannot be null.");
        }

        if (!typeof(IJob).IsAssignableFrom(jobType))
        {
            throw new ArgumentException("Job class must implement Quartz.IJob interface.");
        }

        this.type = new Lazy<Type>(() => jobType);
        FullName = GetFullName(jobType);
    }

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

    public static implicit operator JobType(string? fullName) => new(fullName);

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