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

namespace Quartz.Core;

/// <summary>
/// What a job threw, read back out of the wrappers <see cref="JobRunShell" /> reports it in.
/// </summary>
/// <remarks>
/// The run shell reports an exception a job throws, other than a <see cref="JobExecutionException" />, as
/// <c>JobExecutionException</c> → <c>JobExecutionProcessException</c> → what the job threw, and both
/// wrappers say "Job threw an unhandled exception". That is true of every failure there is and tells a
/// reader nothing, so whatever names a failure — a history row, a job's status, a pause reason, the
/// <c>error.type</c> attribute — looks through that one shape here.
/// </remarks>
internal static class JobFailure
{
    /// <summary>
    /// The exception the job threw: the cause under the run shell's two wrappers, or
    /// <paramref name="exception" /> itself when it is not in them.
    /// </summary>
    /// <remarks>
    /// Only that exact pair is peeled off. A <see cref="JobExecutionException" /> the job threw itself has no
    /// <see cref="JobExecutionProcessException" /> under it, so it is what the job chose to say; and the
    /// cause is never unwrapped further, because an <see cref="AggregateException" /> is what failed, not its
    /// children.
    /// </remarks>
    internal static Exception Thrown(Exception exception)
    {
        return exception is JobExecutionException { InnerException: JobExecutionProcessException { InnerException: { } thrown } }
            ? thrown
            : exception;
    }

    /// <summary>
    /// The message of what the job threw.
    /// </summary>
    internal static string MessageOf(JobExecutionException exception) => Thrown(exception).Message;
}
