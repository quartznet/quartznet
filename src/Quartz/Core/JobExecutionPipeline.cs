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
/// Folds a scheduler's <see cref="IJobExecutionMiddleware" /> list into the single delegate the run
/// shell calls.
/// </summary>
/// <remarks>
/// <para>
/// Composed once, when the scheduler's resources are built, rather than per firing: every stage of the
/// chain is a closure, and building the chain on the hot path would allocate one per middleware per
/// fire. The terminal step reads the job off the context rather than closing over it, which is what
/// makes the whole chain reusable — the only thing that differs between two firings is the context, and
/// that is an argument.
/// </para>
/// <para>
/// A scheduler with no middleware gets <see langword="null" /> rather than a one-element chain around
/// the job. The run shell calls the job directly in that case, so the pipeline costs a null check and
/// nothing else in the configuration nearly every application runs.
/// </para>
/// </remarks>
internal static class JobExecutionPipeline
{
    /// <summary>
    /// The end of every pipeline: the job itself.
    /// </summary>
    private static readonly JobExecutionDelegate executeJob =
        static (context, cancellationToken) => context.JobInstance.Execute(context, cancellationToken);

    /// <summary>
    /// Wraps the middleware around the job, first registered outermost.
    /// </summary>
    /// <param name="middleware">The middleware registered for this scheduler, in registration order.</param>
    /// <returns>
    /// The composed pipeline, or <see langword="null" /> when there is no middleware to compose.
    /// </returns>
    public static JobExecutionDelegate? Compose(IReadOnlyList<IJobExecutionMiddleware> middleware)
    {
        if (middleware.Count == 0)
        {
            return null;
        }

        // Built back to front, so the first registered middleware ends up holding the rest of the chain
        // and therefore runs first.
        JobExecutionDelegate next = executeJob;
        for (int i = middleware.Count - 1; i >= 0; i--)
        {
            IJobExecutionMiddleware stage = middleware[i];
            JobExecutionDelegate rest = next;
            next = (context, cancellationToken) => stage.Invoke(context, rest, cancellationToken);
        }

        return next;
    }
}
