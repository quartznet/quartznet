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

using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Spi;

namespace Quartz.Tests.Unit;

/// <summary>
/// Utility class for tests.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
public static class TestUtil
{
    /// <summary>
    /// Creates the minimal fired bundle with job detail that has
    /// given the job type.
    /// </summary>
    /// <param name="jobType">Type of the job.</param>
    /// <param name="trigger">The trigger.</param>
    /// <returns>Minimal TriggerFiredBundle</returns>
    public static TriggerFiredBundle CreateMinimalFiredBundleWithTypedJobDetail(Type jobType, IOperableTrigger trigger)
    {
        var jd = JobBuilder.Create()
            .OfType(jobType)
            .WithIdentity(new JobKey("jobName", "jobGroup"))
            .Build();
        TriggerFiredBundle bundle = new TriggerFiredBundle(jd, trigger, null, false, DateTimeOffset.UtcNow, null, null, null);
        return bundle;
    }

    public static TriggerFiredBundle NewMinimalRecoveringTriggerFiredBundle()
    {
        return NewMinimalTriggerFiredBundle(true);
    }

    public static TriggerFiredBundle NewMinimalTriggerFiredBundle()
    {
        return NewMinimalTriggerFiredBundle(false);
    }

    private static TriggerFiredBundle NewMinimalTriggerFiredBundle(bool isRecovering)
    {
        IJobDetail jd = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey("jobName", "jobGroup"))
            .Build();
        IOperableTrigger trigger = new SimpleTriggerImpl("triggerName", "triggerGroup");
        TriggerFiredBundle retValue = new TriggerFiredBundle(jd, trigger, null, isRecovering, DateTimeOffset.UtcNow, null, null, null);

        return retValue;
    }

    public static IJobExecutionContext NewJobExecutionContextFor(IJob job)
    {
        return new JobExecutionContextImpl(null, NewMinimalTriggerFiredBundle(), job);
    }
}