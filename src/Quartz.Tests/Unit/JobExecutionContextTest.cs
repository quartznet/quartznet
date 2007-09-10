/* 
 * Copyright 2004-2007 OpenSymphony 
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
 */
using System;

using MbUnit.Framework;

using Quartz.Job;
using Quartz.Spi;

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Tests for JobExecutionContext.
    /// </summary>
    [TestFixture]
    public class JobExecutionContextTest
    {   
        [Test]
        public void TestToString()
        {
            // QRTZNET-48
            JobExecutionContext ctx = new JobExecutionContext(null, ConstructMinimalTriggerFiredBundle(), null);
            ctx.ToString();
        }

        private static TriggerFiredBundle ConstructMinimalTriggerFiredBundle()
        {
            JobDetail jd = new JobDetail("jobName", "jobGroup", typeof(NoOpJob));
            SimpleTrigger trigger = new SimpleTrigger("triggerName", "triggerGroup");
            TriggerFiredBundle retValue = new TriggerFiredBundle(jd, trigger, null, false, null, null, null, null);

            return retValue;
        }
    }
}
