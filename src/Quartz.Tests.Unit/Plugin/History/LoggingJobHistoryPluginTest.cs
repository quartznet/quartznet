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
    public class LoggingJobHistoryPluginTest
    {
        private MockRepository mockery;
        private LoggingJobHistoryPlugin plugin;
        private ILog mockLog;

        [SetUp]
        public void SetUp()
        {
            mockery = new MockRepository();
            mockLog = (ILog) mockery.CreateMock(typeof (ILog));           
            plugin = new LoggingJobHistoryPlugin();
            plugin.Log = mockLog;
        }

        [Test]
        public void TestJobFailedMessage()
        {
            // expectations
            Expect.Call(mockLog.IsWarnEnabled).Return(true);
            mockLog.Warn(null, null);
            LastCall.IgnoreArguments();

            mockery.ReplayAll();

            JobExecutionException ex = new JobExecutionException("test error");
            plugin.JobWasExecuted(CreateJobExecutionContext(), ex);
        }

        [Test]
        public void TestJobSuccessMessage()
        {
            // expectations
            Expect.Call(mockLog.IsInfoEnabled).Return(true);
            mockLog.Info(null);
            LastCall.IgnoreArguments();

            mockery.ReplayAll();

            plugin.JobWasExecuted(CreateJobExecutionContext(), null);
        }

        [Test]
        public void TestJobToBeFiredMessage()
        {
            // expectations
            Expect.Call(mockLog.IsInfoEnabled).Return(true);
            mockLog.Info(null);
            LastCall.IgnoreArguments();

            mockery.ReplayAll();

            plugin.JobToBeExecuted(CreateJobExecutionContext());
        }

        [Test]
        public void TestJobWasVetoedMessage()
        {
            // expectations
            Expect.Call(mockLog.IsInfoEnabled).Return(true);
            mockLog.Info(null);
            LastCall.IgnoreArguments();

            mockery.ReplayAll();

            plugin.JobExecutionVetoed(CreateJobExecutionContext());
        }

        protected virtual JobExecutionContext CreateJobExecutionContext()
        {
            Trigger t = new SimpleTrigger();

            JobExecutionContext ctx = new JobExecutionContext(
                null, 
                TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), t), 
                null);
            return ctx;
        }


        [TearDown]
        public void TearDown()
        {
            mockery.VerifyAll();
        }
    }
}
