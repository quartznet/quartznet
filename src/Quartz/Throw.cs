using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

using Quartz.Impl.AdoJobStore;

namespace Quartz;

internal static class Throw
{
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ArgumentNullException(string paramName)
    {
        throw new ArgumentNullException(paramName);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ArgumentNullException(string? paramName, string? message)
    {
        throw new ArgumentNullException(paramName, message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ArgumentException(string message, string paramName, Exception innerException)
    {
        throw new ArgumentException(message, paramName, innerException);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ArgumentException(string message, string paramName)
    {
        throw new ArgumentException(message, paramName);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ArgumentException(string message)
    {
#pragma warning disable MA0015
        throw new ArgumentException(message);
#pragma warning restore MA0015
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static T ArgumentException<T>(string message)
    {
#pragma warning disable MA0015
        throw new ArgumentException(message);
#pragma warning restore MA0015
    }


    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ArgumentOutOfRangeException(string? paramName = null)
    {
        throw new ArgumentOutOfRangeException(paramName);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ArgumentOutOfRangeException(string paramName, string message)
    {
        throw new ArgumentOutOfRangeException(paramName, message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void NotSupportedException(string? message = null)
    {
        throw new NotSupportedException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void InvalidOperationException(string? message = null)
    {
        throw new InvalidOperationException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SchedulerException(string message, Exception? cause = null)
    {
        throw new SchedulerException(message, cause);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void InvalidCastException(string message)
    {
        throw new InvalidCastException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void FormatException(string message, Exception? innerException = null)
    {
        throw new FormatException(message, innerException);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void JobPersistenceException(string message, Exception? innerException = null)
    {
        throw new JobPersistenceException(message, innerException);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void NotImplementedException()
    {
#pragma warning disable MA0025
        throw new NotImplementedException();
#pragma warning restore MA0025
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SchedulerConfigException(string message, Exception? innerException = null)
    {
        throw new SchedulerConfigException(message, innerException);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ObjectAlreadyExistsException(IJobDetail offendingJob)
    {
        throw new ObjectAlreadyExistsException(offendingJob);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ObjectAlreadyExistsException(ITrigger offendingTrigger)
    {
        throw new ObjectAlreadyExistsException(offendingTrigger);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ObjectAlreadyExistsException(string message)
    {
        throw new ObjectAlreadyExistsException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void UnableToInterruptJobException(SchedulerException se)
    {
        throw new UnableToInterruptJobException(se);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void KeyNotFoundException()
    {
        throw new KeyNotFoundException();
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void LockException(string message, Exception? innerException = null)
    {
        throw new LockException(message, innerException);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void MemberAccessException(string message)
    {
        throw new MemberAccessException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void NoSuchDelegateException(string message, Exception? innerException = null)
    {
        throw new NoSuchDelegateException(message, innerException);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void InvalidConfigurationException(string message)
    {
        throw new InvalidConfigurationException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ObjectDisposedException(string objectName)
    {
        throw new ObjectDisposedException(objectName);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void TypeLoadException(string message)
    {
        throw new TypeLoadException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SerializationException(string message)
    {
        throw new SerializationException(message);
    }
}