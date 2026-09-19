#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Quartz;

/// <summary>
/// The five members of <c>Quartz.Throw</c> the linked cron sources call, with the same bodies.
/// </summary>
/// <remarks>
/// <c>src/Quartz/Throw.cs</c> is thirty members wide and two of them construct exceptions that live
/// in <c>Quartz.Impl.AdoJobStore</c>, so linking it would drag a job store into an analyzer. The cron
/// closure calls five, none of them that pair, and this is those five. Nothing here decides what an
/// expression means — a thrower only shapes the exception — so the two copies cannot diverge in a way
/// <c>CronParityTest</c> would not see: the message is built at the call site, in the linked source.
/// </remarks>
internal static class Throw
{
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
    internal static void FormatException(string message, Exception? innerException = null)
    {
        throw new FormatException(message, innerException);
    }
}
