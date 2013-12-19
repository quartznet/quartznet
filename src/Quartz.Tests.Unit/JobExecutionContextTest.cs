#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

using NUnit.Framework;

using Quartz.Impl;

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Tests for JobExecutionContext.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class JobExecutionContextTest
    {   
        [Test]
        public void TestToString()
        {
            // QRTZNET-48
            IJobExecutionContext ctx = new JobExecutionContextImpl(null, TestUtil.NewMinimalTriggerFiredBundle(), null);
            ctx.ToString();
        }

		[Test]
		public void RecoveryTriggerKeyAndGroup()
		{
			IJobExecutionContext ctx = new JobExecutionContextImpl(null, TestUtil.NewMinimalRecoveringTriggerFiredBundle(), null);
			ctx.MergedJobDataMap[SchedulerConstants.FailedJobOriginalTriggerName] = "originalTriggerName";
			ctx.MergedJobDataMap[SchedulerConstants.FailedJobOriginalTriggerGroup] = "originalTriggerGroup";
			var recoveringTriggerKey = ctx.RecoveringTriggerKey;
			Assert.That(recoveringTriggerKey.Name, Is.EqualTo("originalTriggerName"));
			Assert.That(recoveringTriggerKey.Group, Is.EqualTo("originalTriggerGroup"));
		}
    }
}
