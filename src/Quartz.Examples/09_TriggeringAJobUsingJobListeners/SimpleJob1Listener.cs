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

namespace Quartz.Examples.Example09;

/// <summary>
/// A job listener that schedules <see cref="SimpleJob2" /> as soon as <see cref="SimpleJob1" /> has
/// finished.
/// </summary>
/// <remarks>
/// Every member of <see cref="IJobListener" /> has a default implementation, so a listener writes only
/// the notification it has something to say about. This one wants <see cref="JobWasExecuted" /> and
/// nothing else, so that is all there is here.
/// </remarks>
/// <author>wkratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class SimpleJob1Listener : IJobListener
{
    // Name defaults to the type's name, which is right whenever a scheduler has one listener of a
    // type. Named here because the name says what the listener is for.
    public virtual string Name => "job1_to_job2";

    public virtual async ValueTask JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Job1Listener says: {context.JobDetail.Key} was executed, scheduling job2");

        IJobDetail job2 = JobBuilder.Create<SimpleJob2>()
            .WithIdentity("job2")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("job2Trigger")
            .StartNow()
            .Build();

        try
        {
            // the listener runs inside the scheduler, and schedules through the same scheduler the job
            // it is listening to ran on
            await context.Scheduler.ScheduleJob(job2, trigger, cancellationToken: cancellationToken);
        }
        catch (SchedulerException e)
        {
            await Console.Error.WriteLineAsync("Unable to schedule job2!");
            await Console.Error.WriteLineAsync(e.ToString());
        }
    }
}
