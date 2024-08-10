using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

using Quartz.Impl.AdoJobStore;

namespace Quartz;

internal static class ThrowHelper
{
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowArgumentNullException(string paramName)
    {
        throw new ArgumentNullException(paramName);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowArgumentNullException(string? paramName, string? message)
    {
        throw new ArgumentNullException(paramName, message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowArgumentException(string message, string paramName, Exception innerException)
    {
        throw new ArgumentException(message, paramName, innerException);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowArgumentException(string message, string paramName)
    {
        throw new ArgumentException(message, paramName);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowArgumentException(string message)
    {
#pragma warning disable MA0015
        throw new ArgumentException(message);
#pragma warning restore MA0015
    }
    
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static T ThrowArgumentException<T>(string message)
    {
#pragma warning disable MA0015
        throw new ArgumentException(message);
#pragma warning restore MA0015
    }
    

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowArgumentOutOfRangeException(string? paramName = null)
    {
        throw new ArgumentOutOfRangeException(paramName);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowArgumentOutOfRangeException(string paramName, string message)
    {
        throw new ArgumentOutOfRangeException(paramName, message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowNotSupportedException(string? message = null)
    {
        throw new NotSupportedException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowInvalidOperationException(string? message = null)
    {
        throw new InvalidOperationException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowSchedulerException(string message, Exception? cause = null)
    {
        throw new SchedulerException(message, cause);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowInvalidCastException(string message)
    {
        throw new InvalidCastException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowFormatException(string message, Exception? innerException = null)
    {
        throw new FormatException(message, innerException);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowJobPersistenceException(string message, Exception? innerException = null)
    {
        throw new JobPersistenceException(message, innerException);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowNotImplementedException()
    {
#pragma warning disable MA0025
        throw new NotImplementedException();
#pragma warning restore MA0025
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowSchedulerConfigException(string message, Exception? innerException = null)
    {
        throw new SchedulerConfigException(message, innerException);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowObjectAlreadyExistsException(IJobDetail offendingJob)
    {
        throw new ObjectAlreadyExistsException(offendingJob);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowObjectAlreadyExistsException(ITrigger offendingTrigger)
    {
        throw new ObjectAlreadyExistsException(offendingTrigger);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowObjectAlreadyExistsException(string message)
    {
        throw new ObjectAlreadyExistsException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowUnableToInterruptJobException(SchedulerException se)
    {
        throw new UnableToInterruptJobException(se);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowKeyNotFoundException()
    {
        throw new KeyNotFoundException();
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowLockException(string message, Exception? innerException = null)
    {
        throw new LockException(message, innerException);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowMemberAccessException(string message)
    {
        throw new MemberAccessException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowNoSuchDelegateException(string message, Exception? innerException = null)
    {
        throw new NoSuchDelegateException(message, innerException);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowInvalidConfigurationException(string message)
    {
        throw new InvalidConfigurationException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowObjectDisposedException(string objectName)
    {
        throw new ObjectDisposedException(objectName);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowTypeLoadException(string message)
    {
        throw new TypeLoadException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowSerializationException(string message)
    {
        throw new SerializationException(message);
    }
}