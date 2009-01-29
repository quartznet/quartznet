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

using MbUnit.Framework;

using Quartz.Job;
using Quartz.Plugin.History;

using Rhino.Mocks;

namespace Quartz.Tests.Unit.Plugin.History
{
    [TestFixture]
    public class LoggingTriggerHistoryPluginTest
    {
        private LoggingTriggerHistoryPlugin plugin;
        private ILog mockLog;

        [SetUp]
        public void SetUp()
        {
            mockLog = MockRepository.GenerateMock<ILog>();
            plugin = new LoggingTriggerHistoryPlugin();
            plugin.Log = mockLog;
        }

        [Test]
        public void TestTriggerFiredMessage()
        {
            // arrange
            mockLog.Stub(log => log.IsInfoEnabled).Return(true);

            Trigger t = new SimpleTrigger();
            
            JobExecutionContext ctx = new JobExecutionContext(
                null, 
                TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), t), 
                null);

            // act
            plugin.TriggerFired(t, ctx);

            // assert
            mockLog.AssertWasCalled(log => log.Info(null), options => options.IgnoreArguments());
        }


        [Test]
        public void TestTriggerMisfiredMessage()
        {
            // arrange
            mockLog.Stub(log => log.IsInfoEnabled).Return(true);
            Trigger t = new SimpleTrigger();

            // act
            plugin.TriggerMisfired(t);

            // assert
            mockLog.AssertWasCalled(log => log.Info(null), options => options.IgnoreArguments());
        }

        [Test]
        public void TestTriggerCompleteMessage()
        {
            // arrange
            mockLog.Stub(log => log.IsInfoEnabled).Return(true);
            
            Trigger t = new SimpleTrigger();
            
            JobExecutionContext ctx = new JobExecutionContext(
                null,
                TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), t),
                null);

            // act
            plugin.TriggerComplete(t, ctx, SchedulerInstruction.ReExecuteJob);

            // assert
            mockLog.AssertWasCalled(log => log.Info(null), options => options.IgnoreArguments());
        }
        
    }
}
