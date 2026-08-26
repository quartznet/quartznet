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

using Microsoft.Extensions.Logging;

using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Jobs;
using Quartz.Plugins.History;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Plugin.History;

/// <author>Marko Lahma (.NET)</author>
public class LoggingJobHistoryPluginTest
{
    private LoggingJobHistoryPlugin plugin;
    private RecordingLoggerProvider loggerProvider;

    [SetUp]
    public void SetUp()
    {
        loggerProvider = new RecordingLoggerProvider();
        ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
        plugin = new LoggingJobHistoryPlugin(
            factory.CreateLogger<LoggingJobHistoryPlugin>(),
            TimeProvider.System);
    }

    [TearDown]
    public void TearDown()
    {
        loggerProvider.Dispose();
    }

    [Test]
    public async Task TestJobFailedMessage()
    {
        JobExecutionException ex = new JobExecutionException("test error");
        await plugin.JobWasExecuted(CreateJobExecutionContext(), ex);

        loggerProvider.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    [Test]
    public async Task TestJobSuccessMessage()
    {
        await plugin.JobWasExecuted(CreateJobExecutionContext(), null);

        loggerProvider.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
    }

    [Test]
    public async Task TestJobToBeFiredMessage()
    {
        await plugin.JobToBeExecuted(CreateJobExecutionContext());

        loggerProvider.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
    }

    [Test]
    public async Task TestJobWasVetoedMessage()
    {
        await plugin.JobExecutionVetoed(CreateJobExecutionContext());

        loggerProvider.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
    }

    [Test]
    public async Task ConfiguredMessagesAreRenderedVerbatimAndCarryAnEventIdOfTheirOwn()
    {
        plugin.JobToBeFiredMessage = "fired {1}.{0} by {4}.{3}";
        plugin.JobSuccessMessage = "done {1}.{0}";
        plugin.JobFailedMessage = "failed {1}.{0}: {8}";
        plugin.JobWasVetoedMessage = "vetoed {1}.{0}";

        await plugin.JobToBeExecuted(CreateJobExecutionContext());
        await plugin.JobWasExecuted(CreateJobExecutionContext(), null);
        await plugin.JobWasExecuted(CreateJobExecutionContext(), new JobExecutionException("boom"));
        await plugin.JobExecutionVetoed(CreateJobExecutionContext());

        (int EventId, string Message)[] expected =
        [
            (6000, "fired jobGroup.jobName by group.name"),
            (6001, "done jobGroup.jobName"),
            (6002, "failed jobGroup.jobName: boom"),
            (6003, "vetoed jobGroup.jobName"),
        ];

        loggerProvider.Entries.Select(x => (EventId: x.EventId.Id, x.Message)).Should().Equal(
            expected,
            "the template is the user's and is formatted here, so passing the result through a degenerate "
            + "\"{Message}\" event has to leave the text a sink renders exactly as it was - and the four "
            + "occurrences have to stay separately filterable, which is what an id per occurrence buys");
    }

    private static IJobExecutionContext CreateJobExecutionContext()
    {
        IOperableTrigger t = new SimpleTriggerImpl { Key = new TriggerKey("name", "group"), StartTimeUtc = TimeProvider.System.GetUtcNow() };
        TriggerFiredBundle firedBundle = TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), t);
        IJobExecutionContext ctx = new JobExecutionContextImpl(null, firedBundle, null);
        return ctx;
    }
}