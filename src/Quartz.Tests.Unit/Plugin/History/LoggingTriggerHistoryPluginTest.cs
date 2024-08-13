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
using Quartz.Job;
using Quartz.Plugin.History;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Plugin.History;

/// <author>Marko Lahma (.NET)</author>
[TestFixture]
public class LoggingTriggerHistoryPluginTest
{
    private RecordingLoggingTriggerHistoryPlugin plugin;

    [SetUp]
    public void SetUp()
    {
        plugin = new RecordingLoggingTriggerHistoryPlugin();
    }

    [Test]
    public async Task TestTriggerFiredMessage()
    {
        ITrigger t = TriggerBuilder.Create()
            .WithSchedule(SimpleScheduleBuilder.Create())
            .Build();

        IJobExecutionContext ctx = new JobExecutionContextImpl(
            null,
            TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), (IOperableTrigger) t),
            null);

        await plugin.TriggerFired(t, ctx);

        Assert.That(plugin.InfoMessages, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TestTriggerMisfiredMessage()
    {
        IOperableTrigger t = (IOperableTrigger) TriggerBuilder.Create()
            .WithSchedule(SimpleScheduleBuilder.Create())
            .Build();

        t.JobKey = new JobKey("name", "group");

        await plugin.TriggerMisfired(t);

        Assert.That(plugin.InfoMessages, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TestTriggerCompleteMessage()
    {
        ITrigger t = TriggerBuilder.Create()
            .WithSchedule(SimpleScheduleBuilder.Create())
            .Build();

        IJobExecutionContext ctx = new JobExecutionContextImpl(
            null,
            TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), (IOperableTrigger) t),
            null);

        await plugin.TriggerComplete(t, ctx, SchedulerInstruction.ReExecuteJob);

        Assert.That(plugin.InfoMessages, Has.Count.EqualTo(1));
    }

    private class RecordingLoggingTriggerHistoryPlugin : LoggingTriggerHistoryPlugin
    {
        public List<string> InfoMessages { get; } = new List<string>();

        protected override bool IsInfoEnabled => true;

        protected override void WriteInfo(string message)
        {
            InfoMessages.Add(message);
        }
    }
}