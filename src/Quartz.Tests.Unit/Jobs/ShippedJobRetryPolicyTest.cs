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
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Job;

/// <summary>
/// Which of the jobs <c>Quartz.Jobs</c> ships a scheduler's default retry policy may retry.
/// </summary>
public sealed class ShippedJobRetryPolicyTest
{
    [TestCase(typeof(SendMailJob))]
    [TestCase(typeof(NativeJob))]
    public void AJobWhoseRetryIsASecondEffectRefusesTheDefault(Type jobType)
    {
        JobTypeInformation.GetOrCreate(jobType).RetryPolicy.Should().BeSameAs(RetryPolicy.None,
            "running it again sends a second message or runs a command a second time, so only a trigger that names a policy of "
            + "its own may retry it");
    }

    [TestCase(typeof(FileScanJob))]
    [TestCase(typeof(DirectoryScanJob))]
    [TestCase(typeof(NoOpJob))]
    public void AJobThatIsSafeToRunAgainLeavesItToTheScheduler(Type jobType)
    {
        JobTypeInformation.GetOrCreate(jobType).RetryPolicy.Should().BeNull(
            "a scan reads and compares, so running it again is harmless, and the scheduler's default is free to apply");
    }
}
