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

namespace Quartz.Examples.Example07;

/// <summary>
/// A job that spends a while working, and notices when it is interrupted.
/// </summary>
/// <remarks>
/// <para>
/// The token this job is handed is the interrupt. <see cref="IScheduler.Interrupt(JobKey, CancellationToken)" />
/// cancels it, and everything the job is awaiting on it unblocks at once. A job that ignores the token
/// cannot be interrupted at all - it runs to its own end, whatever the scheduler was asked for.
/// </para>
/// <para>
/// It is the same token as <see cref="IJobExecutionContext.CancellationToken" />, passed as a parameter
/// so that the compiler's CA2016 can point at every await that forgot to forward it.
/// </para>
/// </remarks>
/// <author>  <a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a></author>
/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class InterruptableJob : IJob
{
    /// <summary>
    /// Three seconds of "work" each, so a job left alone runs for twenty-four seconds.
    /// </summary>
    public const int WorkSteps = 8;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
    /// fires that is associated with the <see cref="IJob" />.
    /// </summary>
    public virtual async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobKey jobKey = context.JobDetail.Key;
        Console.WriteLine($"---- {jobKey} started at {context.FireTimeUtc.LocalDateTime:HH:mm:ss} on thread {Environment.CurrentManagedThreadId}");

        try
        {
            // main job loop... in this example we are 'simulating' work by sleeping
            for (int i = 1; i <= WorkSteps; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

                // an await is a suspension, not a thread: the continuation runs on whatever pool
                // thread is free, which is usually not the one the job started on
                Console.WriteLine($"---- {jobKey} still working, now on thread {Environment.CurrentManagedThreadId} ({i}/{WorkSteps})");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"--- {jobKey}  -- Interrupted... bailing out!");
            // could also choose to throw a JobExecutionException if that made for sense
            // based on the particular job's responsibilities/behaviors
        }
        finally
        {
            Console.WriteLine($"---- {jobKey} finished at {TimeProvider.System.GetLocalNow():HH:mm:ss}");
        }
    }
}
