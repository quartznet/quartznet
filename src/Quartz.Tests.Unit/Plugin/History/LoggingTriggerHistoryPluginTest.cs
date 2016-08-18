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

#if FAKE_IT_EASY

using System;
using System.Threading.Tasks;

using FakeItEasy;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Job;
using Quartz.Logging;
using Quartz.Plugin.History;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Plugin.History
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class LoggingTriggerHistoryPluginTest
    {
        private LoggingTriggerHistoryPlugin plugin;
        private ILog mockLog;

        [SetUp]
        public void SetUp()
        {
            mockLog = A.Fake<ILog>();
            plugin = new LoggingTriggerHistoryPlugin();
            plugin.Log = mockLog;
        }

        [Test]
        public async Task TestTriggerFiredMessage()
        {
            // arrange
            A.CallTo(() => mockLog.Log(LogLevel.Info, null, null, null)).Returns(true);

            ITrigger t = TriggerBuilder.Create()
                .WithSchedule(SimpleScheduleBuilder.Create())
                .Build();

            IJobExecutionContext ctx = new JobExecutionContextImpl(
                null,
                TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), (IOperableTrigger) t),
                null);

            // act
            await plugin.TriggerFired(t, ctx);

            // assert
            A.CallTo(() => mockLog.Log(A<LogLevel>.That.IsEqualTo(LogLevel.Info), A<Func<string>>.That.IsNull(), A<Exception>.That.IsNull(), A<object[]>.That.Not.IsNull())).MustHaveHappened();
        }


        [Test]
        public async Task TestTriggerMisfiredMessage()
        {
            // arrange
            A.CallTo(() => mockLog.Log(LogLevel.Info, null, null, null)).Returns(true);
            IOperableTrigger t = (IOperableTrigger) TriggerBuilder.Create()
                .WithSchedule(SimpleScheduleBuilder.Create())
                .Build();

            t.JobKey = new JobKey("name", "group");

            // act
            await plugin.TriggerMisfired(t);

            // assert
            A.CallTo(() => mockLog.Log(A<LogLevel>.That.IsEqualTo(LogLevel.Info), A<Func<string>>.That.IsNull(), A<Exception>.That.IsNull(), A<object[]>.That.Not.IsNull())).MustHaveHappened();
        }

        [Test]
        public async Task TestTriggerCompleteMessage()
        {
            // arrange
            A.CallTo(() => mockLog.Log(LogLevel.Info, null, null, null)).Returns(true);

            ITrigger t = TriggerBuilder.Create()
                .WithSchedule(SimpleScheduleBuilder.Create())
                .Build();

            IJobExecutionContext ctx = new JobExecutionContextImpl(
                null,
                TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), (IOperableTrigger) t),
                null);

            // act
            await plugin.TriggerComplete(t, ctx, SchedulerInstruction.ReExecuteJob);

            // assert
            A.CallTo(() => mockLog.Log(A<LogLevel>.That.IsEqualTo(LogLevel.Info), A<Func<string>>.That.IsNull(), A<Exception>.That.IsNull(), A<object[]>.That.Not.IsNull())).MustHaveHappened();
        }
    }
}

#endif