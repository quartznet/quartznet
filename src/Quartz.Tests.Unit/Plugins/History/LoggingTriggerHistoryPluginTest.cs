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

using FakeItEasy;

using Microsoft.Extensions.Logging;

using Quartz.Impl;
using Quartz.Jobs;
using Quartz.Plugins.History;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Plugin.History;

/// <author>Marko Lahma (.NET)</author>
public class LoggingTriggerHistoryPluginTest
{
    private LoggingTriggerHistoryPlugin plugin;
    private RecordingLoggerProvider loggerProvider;

    [SetUp]
    public void SetUp()
    {
        loggerProvider = new RecordingLoggerProvider();
        ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
        plugin = new LoggingTriggerHistoryPlugin(
            factory.CreateLogger<LoggingTriggerHistoryPlugin>(),
            TimeProvider.System);
    }

    [TearDown]
    public void TearDown()
    {
        loggerProvider.Dispose();
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

        loggerProvider.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
    }

    [Test]
    public async Task TestTriggerMisfiredMessage()
    {
        IOperableTrigger t = (IOperableTrigger) TriggerBuilder.Create()
            .WithSchedule(SimpleScheduleBuilder.Create())
            .Build();

        t.JobKey = new JobKey("name", "group");

        await plugin.TriggerMisfired(A.Fake<IScheduler>(), t);

        loggerProvider.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
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

        loggerProvider.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
    }

    [Test]
    public async Task ConfiguredMessagesAreRenderedVerbatimAndCarryAnEventIdOfTheirOwn()
    {
        plugin.TriggerFiredMessage = "fired {1}.{0} for {6}.{5}";
        plugin.TriggerMisfiredMessage = "misfired {1}.{0} for {6}.{5}";
        plugin.TriggerCompleteMessage = "completed {1}.{0} with {9}";

        IOperableTrigger t = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("triggerName", "triggerGroup")
            .WithSchedule(SimpleScheduleBuilder.Create())
            .Build();

        t.JobKey = new JobKey("jobName", "jobGroup");

        IJobExecutionContext ctx = new JobExecutionContextImpl(
            null,
            TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), t),
            null);

        await plugin.TriggerFired(t, ctx);
        await plugin.TriggerMisfired(A.Fake<IScheduler>(), t);
        await plugin.TriggerComplete(t, ctx, SchedulerInstruction.ReExecuteJob);

        (int EventId, string Message)[] expected =
        [
            (6010, "fired triggerGroup.triggerName for jobGroup.jobName"),
            (6011, "misfired triggerGroup.triggerName for jobGroup.jobName"),
            (6012, "completed triggerGroup.triggerName with RE-EXECUTE JOB"),
        ];

        loggerProvider.Entries.Select(x => (EventId: x.EventId.Id, x.Message)).Should().Equal(
            expected,
            "the template is the user's and is formatted here, so passing the result through a degenerate "
            + "\"{Message}\" event has to leave the text a sink renders exactly as it was - and the three "
            + "occurrences have to stay separately filterable, which is what an id per occurrence buys");
    }
}