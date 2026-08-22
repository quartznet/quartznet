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

    private static IJobExecutionContext CreateJobExecutionContext()
    {
        IOperableTrigger t = new SimpleTriggerImpl { Key = new TriggerKey("name", "group"), StartTimeUtc = TimeProvider.System.GetUtcNow() };
        TriggerFiredBundle firedBundle = TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), t);
        IJobExecutionContext ctx = new JobExecutionContextImpl(null, firedBundle, null);
        return ctx;
    }
}