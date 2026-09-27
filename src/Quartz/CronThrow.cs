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
/// The throw helpers the cron parser calls.
/// </summary>
/// <remarks>
/// <para>
/// The cron closure — <c>CronExpression</c>, <c>UnixCronRewriter</c> and the files they reach — throws
/// through this type rather than <c>Throw</c>, whose other members construct Quartz exceptions, two of
/// them from <c>Quartz.Impl.AdoJobStore</c>. Keeping the closure's throwers apart is what lets
/// <c>Quartz.Analyzers</c> link this file as it is, and what would let the closure move into an
/// assembly of its own without a job store coming along.
/// </para>
/// <para>
/// The name is deliberately not <c>Throw</c>. Were the closure split out and its internals made visible
/// to <c>Quartz</c>, a type declared in both assemblies would be CS0436, which this repository's
/// warnings-as-errors turns into a build break.
/// </para>
/// </remarks>
internal static class CronThrow
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
