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

namespace Quartz.Trimming.Canary.Wire;

/// <summary>
/// Runs a check's steps in order, one line per step, stopping at the first failure: each step leans on
/// the one before it, and there is no trigger to pause once scheduling has failed.
/// </summary>
internal static class CanarySteps
{
    /// <summary>
    /// How long a step may wait for something the other end does on its own — a job to run, a heartbeat
    /// to arrive — before the wait is the failure.
    /// </summary>
    public const int PatienceSeconds = 60;

    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(PatienceSeconds);

    /// <summary>
    /// Runs every step, writing a line for each, and says whether all of them passed.
    /// </summary>
    /// <param name="prefix">The check's name, which every line carries so the two checks' lines read apart.</param>
    /// <param name="steps">The steps, each answering with what it proved.</param>
    public static async Task<bool> Run(string prefix, (string Name, Func<Task<string>> Step)[] steps)
    {
        foreach ((string name, Func<Task<string>> step) in steps)
        {
            try
            {
                string passed = await step().ConfigureAwait(false);
                Console.WriteLine($"PASS {prefix} {name}: {passed}");
            }
            catch (CanaryFailedException failure)
            {
                Console.WriteLine($"FAIL {prefix} {name}: {failure.Message}");
                return false;
            }
            catch (Exception e)
            {
                Console.WriteLine($"FAIL {prefix} {name}: {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e}");
                return false;
            }
        }

        return true;
    }

    public static void Expect([DoesNotReturnIf(false)] bool condition, string failure)
    {
        if (!condition)
        {
            throw new CanaryFailedException(failure);
        }
    }

    /// <summary>
    /// Waits for something the other end does on its own, and fails in the step's own words when it did
    /// not happen within <see cref="Patience" />.
    /// </summary>
    public static async Task<T> Await<T>(Task<T> awaited, string failure, CancellationToken cancellationToken)
    {
        try
        {
            return await awaited.WaitAsync(Patience, TimeProvider.System, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new CanaryFailedException(failure);
        }
    }

    /// <inheritdoc cref="Await{T}" />
    public static async Task Await(Task awaited, string failure, CancellationToken cancellationToken)
    {
        try
        {
            await awaited.WaitAsync(Patience, TimeProvider.System, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new CanaryFailedException(failure);
        }
    }
}

/// <summary>
/// A step's own check failing, as opposed to something it called throwing: the message is the whole
/// story, and a stack trace would only say which line of the check noticed.
/// </summary>
/// <remarks>
/// It never leaves <see cref="CanarySteps.Run" />, which catches it to write the step's line, and an
/// executable has no caller that could catch it by type.
/// </remarks>
internal sealed class CanaryFailedException(string message) : Exception(message);
