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

namespace Quartz.Examples.Example12;

/// <summary>
/// This is just a simple job that gets fired off many times
/// by ConfigureJobSchedulingByUsingXmlConfigurations Example.
/// </summary>
/// <remarks>
/// It is named in <c>quartz_jobs.xml</c> by assembly-qualified name, which is how the XML scheduling
/// plugin finds a job type it was never handed in code.
/// </remarks>
/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class SimpleJob : IJob
{
    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a
    /// <see cref="ITrigger" /> fires that is associated with the <see cref="IJob" />.
    /// </summary>
    public virtual ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"SimpleJob says: {context.JobDetail.Key} fired by {context.Trigger.Key} at {context.FireTimeUtc.LocalDateTime:HH:mm:ss}");
        return default;
    }
}
