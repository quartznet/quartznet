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

#if NETFRAMEWORK || NETSTANDARD2_0
using System;
using System.Threading;
using System.Threading.Tasks;
#endif

namespace Quartz;

/// <summary>
/// <see cref="System.Threading.Tasks.Task" /> members the oldest targets lack, supplied as extension
/// methods so that a call site is written as it would be on .NET alone. A real member always wins over
/// an extension method, so on the targets that have one the BCL's binds and this class is empty.
/// </summary>
/// <remarks>
/// In the <c>Quartz</c> namespace because an enclosing namespace is searched for extension methods, so
/// <c>Quartz.Job</c> needs no <c>using</c>.
/// </remarks>
internal static class TaskPolyfills
{
#if NETFRAMEWORK || NETSTANDARD2_0
    /// <summary>
    /// <c>Task.WaitAsync(TimeSpan, CancellationToken)</c>, which arrived in .NET 6: the task's own outcome
    /// if it completes first, <see cref="TimeoutException" /> if <paramref name="timeout" /> passes first,
    /// <see cref="OperationCanceledException" /> if <paramref name="cancellationToken" /> is cancelled first.
    /// </summary>
    public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task first = await Task.WhenAny(task, Task.Delay(timeout, delayCancellation.Token)).ConfigureAwait(false);
        if (first == task)
        {
            // Stops the timer, and lets the task's own outcome through.
            delayCancellation.Cancel();
            await task.ConfigureAwait(false);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException();
    }
#endif
}
