using System;
using System.Diagnostics.CodeAnalysis;

namespace Quartz
{
    internal static class ExceptionHelper
    {
        [DoesNotReturn]
        public static void ThrowArgumentNullException(string paramName)
        {
            throw new ArgumentNullException(paramName);
        }

        [DoesNotReturn]
        public static void ThrowArgumentNullException(string? paramName, string? message)
        {
            throw new ArgumentNullException(paramName, message);
        }

        [DoesNotReturn]
        public static void ThrowArgumentException(string message, string paramName)
        {
            throw new ArgumentException(message, paramName);
        }

        [DoesNotReturn]
        public static void ThrowArgumentOutOfRangeException(string message, string paramName)
        {
            throw new ArgumentOutOfRangeException(paramName, message);
        }

    }
}