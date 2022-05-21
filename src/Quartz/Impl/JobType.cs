using System;

using Quartz.Util;

namespace Quartz.Impl;

/// <summary>
/// Store the Job Type and storable backing field name for serialization
/// </summary>
public class JobType
{
    private string storableTypeName = string.Empty;
    private Lazy<Type> lazyType = new(() => throw new InvalidOperationException("Type not defined"));

    public JobType()
    {
    }

    public JobType(string? typeStorageName)
    {
        this.StorableTypeName = typeStorageName ?? throw new ArgumentNullException(nameof(typeStorageName));
    }

    /// <summary>
    /// Job Type declaration
    /// </summary>
    public JobType(Type type)
    {
        Type = type;
    }

    /// <summary>
    /// Type as a string represented storable name.
    /// </summary>
    public string StorableTypeName
    {
        get => storableTypeName;
        set
        {
            storableTypeName = value;
            lazyType = new Lazy<Type>(() =>
                Type.GetType(value) ?? throw new InvalidOperationException("Job class Type cannot be resolved."));
        }
    }

    public virtual Type Type
    {
        get => string.IsNullOrEmpty(StorableTypeName) ? null! : lazyType.Value;
        private set
        {
            if (value == null)
            {
                throw new ArgumentException("Job class cannot be null.");
            }

            if (!typeof(IJob).IsAssignableFrom(value))
            {
                throw new ArgumentException("Job class must implement the Job interface.");
            }

            lazyType = new Lazy<Type>(() => value);
            storableTypeName = GetStorableJobTypeName(value);
        }
    }

    private string GetStorableJobTypeName(Type jobType)
    {
        if (jobType.AssemblyQualifiedName == null)
        {
            throw new ArgumentException("Cannot determine job type name when type's AssemblyQualifiedName is null");
        }

        return jobType.AssemblyQualifiedNameWithoutVersion();
    }
}