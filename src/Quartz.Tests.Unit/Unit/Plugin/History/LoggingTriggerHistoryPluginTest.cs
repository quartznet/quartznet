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

using Common.Logging;

using NUnit.Framework;

using Quartz.Job;
using Quartz.Plugin.History;

using Rhino.Mocks;

namespace Quartz.Tests.Unit.Plugin.History
{
    [TestFixture]
    public class LoggingTriggerHistoryPluginTest
    {
        private MockRepository mockery;
        private LoggingTriggerHistoryPlugin plugin;
        private ILog mockLog;

        [SetUp]
        public void SetUp()
        {
            mockery = new MockRepository();
            mockLog = (ILog)mockery.CreateMock(typeof(ILog));
            plugin = new LoggingTriggerHistoryPlugin();
            plugin.Log = mockLog;
        }

        [Test]
        public void TestTriggerFiredMessage()
        {
            // expectations
            Expect.Call(mockLog.IsInfoEnabled).Return(true);
            mockLog.Info(null);
            LastCall.IgnoreArguments();

            mockery.ReplayAll();

            Trigger t = new SimpleTrigger();
            
            JobExecutionContext ctx = new JobExecutionContext(
                null, 
                TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), t), 
                null);

            plugin.TriggerFired(t, ctx);
        }


        [Test]
        public void TestTriggerMisfiredMessage()
        {
            // expectations
            Expect.Call(mockLog.IsInfoEnabled).Return(true);
            mockLog.Info(null);
            LastCall.IgnoreArguments();

            mockery.ReplayAll();

            Trigger t = new SimpleTrigger();

            plugin.TriggerMisfired(t);
        }

        [Test]
        public void TestTriggerCompleteMessage()
        {
            // expectations
            Expect.Call(mockLog.IsInfoEnabled).Return(true);
            mockLog.Info(null);
            LastCall.IgnoreArguments();

            mockery.ReplayAll();

            Trigger t = new SimpleTrigger();
            
            JobExecutionContext ctx = new JobExecutionContext(
                null,
                TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), t),
                null);

            plugin.TriggerComplete(t, ctx, SchedulerInstruction.ReExecuteJob);
        }
        
    }
}
