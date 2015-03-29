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
        public void TestTriggerFiredMessage()
        {
            // arrange
            A.CallTo(() => mockLog.IsInfoEnabled()).Returns(true);

            ITrigger t = TriggerBuilder.Create()
                                        .WithSchedule(SimpleScheduleBuilder.Create())
                                        .Build();
            
            IJobExecutionContext ctx = new JobExecutionContextImpl(
                null, 
                TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), (IOperableTrigger) t), 
                null);

            // act
            plugin.TriggerFired(t, ctx);

            // assert
            A.CallTo(() => mockLog.Log(A<LogLevel>.That.IsEqualTo(LogLevel.Info), A<Func<string>>.That.Not.IsNull(), A<Exception>.That.IsNull(), A<object[]>.That.Not.IsNull())).MustHaveHappened();
        }


        [Test]
        public void TestTriggerMisfiredMessage()
        {
            // arrange
            A.CallTo(() => mockLog.IsInfoEnabled()).Returns(true);
            IOperableTrigger t = (IOperableTrigger) TriggerBuilder.Create()
                                                        .WithSchedule(SimpleScheduleBuilder.Create())
                                                        .Build();

            t.JobKey = new JobKey("name", "group");
            
            // act
            plugin.TriggerMisfired(t);

            // assert
            A.CallTo(() => mockLog.Log(A<LogLevel>.That.IsEqualTo(LogLevel.Info), A<Func<string>>.That.Not.IsNull(), A<Exception>.That.IsNull(), A<object[]>.That.Not.IsNull())).MustHaveHappened();
        }

        [Test]
        public void TestTriggerCompleteMessage()
        {
            // arrange
            A.CallTo(() => mockLog.IsInfoEnabled()).Returns(true);

            ITrigger t = TriggerBuilder.Create()
                                        .WithSchedule(SimpleScheduleBuilder.Create())
                                        .Build();
            
            IJobExecutionContext ctx = new JobExecutionContextImpl(
                null,
                TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), (IOperableTrigger) t),
                null);

            // act
            plugin.TriggerComplete(t, ctx, SchedulerInstruction.ReExecuteJob);

            // assert
            A.CallTo(() => mockLog.Log(A<LogLevel>.That.IsEqualTo(LogLevel.Info), A<Func<string>>.That.Not.IsNull(), A<Exception>.That.IsNull(), A<object[]>.That.Not.IsNull())).MustHaveHappened();
        }        
    }
}
