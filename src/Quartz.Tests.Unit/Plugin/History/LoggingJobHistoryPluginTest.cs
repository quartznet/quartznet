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

using System;
using System.Threading.Tasks;

using FakeItEasy;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Logging;
using Quartz.Plugin.History;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Plugin.History
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class LoggingJobHistoryPluginTest
    {
        private LoggingJobHistoryPlugin plugin;
        private ILog mockLog;

        [SetUp]
        public void SetUp()
        {
            mockLog = A.Fake<ILog>();           
            plugin = new LoggingJobHistoryPlugin();
            plugin.Log = mockLog;
        }

        [Test]
        public async Task TestJobFailedMessage()
        {
            // arrange
            A.CallTo(() => mockLog.IsWarnEnabled()).Returns(true);

            // act
            JobExecutionException ex = new JobExecutionException("test error");
            await plugin.JobWasExecutedAsync(CreateJobExecutionContext(), ex);

            // assert
            A.CallTo(() => mockLog.Log(A<LogLevel>.That.IsEqualTo(LogLevel.Warn), A<Func<string>>.That.Not.IsNull(), A<Exception>.That.Not.IsNull(), A<object[]>.That.Not.IsNull())).MustHaveHappened();
        }

        [Test]
        public async Task TestJobSuccessMessage()
        {
            // arrange
            A.CallTo(() => mockLog.IsInfoEnabled()).Returns(true);

            // act
            await plugin.JobWasExecutedAsync(CreateJobExecutionContext(), null);

            // assert
            A.CallTo(() => mockLog.Log(A<LogLevel>.That.IsEqualTo(LogLevel.Info), A<Func<string>>.That.Not.IsNull(), A<Exception>.That.IsNull(), A<object[]>.That.Not.IsNull())).MustHaveHappened();
        }

        [Test]
        public async Task TestJobToBeFiredMessage()
        {
            // arrange
            A.CallTo(() => mockLog.IsInfoEnabled()).Returns(true);

            // act
            await plugin.JobToBeExecutedAsync(CreateJobExecutionContext());

            // assert
            A.CallTo(() => mockLog.Log(A<LogLevel>.That.IsEqualTo(LogLevel.Info), A<Func<string>>.That.Not.IsNull(), A<Exception>.That.IsNull(), A<object[]>.That.Not.IsNull())).MustHaveHappened();
        }

        [Test]
        public void TestJobWasVetoedMessage()
        {
            // arrange
            A.CallTo(() => mockLog.IsInfoEnabled()).Returns(true);

            // act
            plugin.JobExecutionVetoedAsync(CreateJobExecutionContext());

            // assert
            A.CallTo(() => mockLog.Log(A<LogLevel>.That.IsEqualTo(LogLevel.Info), A<Func<string>>.That.Not.IsNull(), A<Exception>.That.IsNull(), A<object[]>.That.Not.IsNull())).MustHaveHappened();
        }

        protected virtual ICancellableJobExecutionContext CreateJobExecutionContext()
        {
            IOperableTrigger t = new SimpleTriggerImpl("name", "group");
            TriggerFiredBundle firedBundle = TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), t);
            ICancellableJobExecutionContext ctx = new JobExecutionContextImpl(null,  firedBundle, null);
            return ctx;
        }

    }
}
