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
using Quartz.Job;
using Quartz.Plugin.History;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Plugin.History;

public class StructuredLoggingJobHistoryPluginTest
{
    private StructuredLoggingJobHistoryPlugin plugin;
    private RecordingLoggerProvider loggerProvider;

    [SetUp]
    public void SetUp()
    {
        loggerProvider = new RecordingLoggerProvider();
        ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
        plugin = new StructuredLoggingJobHistoryPlugin(
            factory.CreateLogger<StructuredLoggingJobHistoryPlugin>(),
            TimeProvider.System);
    }

    [TearDown]
    public void TearDown()
    {
        loggerProvider.Dispose();
    }

    [Test]
    public async Task TestJobToBeFiredMessage()
    {
        await plugin.JobToBeExecuted(CreateJobExecutionContext());

        Assert.That(loggerProvider.Entries, Has.Count.EqualTo(1));
        Assert.That(loggerProvider.Entries[0].Level, Is.EqualTo(LogLevel.Information));
        Assert.That(loggerProvider.Entries[0].Message, Does.Contain("jobGroup.jobName"));
    }

    [Test]
    public async Task TestJobSuccessMessage()
    {
        await plugin.JobWasExecuted(CreateJobExecutionContext(), null);

        Assert.That(loggerProvider.Entries, Has.Count.EqualTo(1));
        Assert.That(loggerProvider.Entries[0].Level, Is.EqualTo(LogLevel.Information));
        Assert.That(loggerProvider.Entries[0].Message, Does.Contain("execution complete"));
    }

    [Test]
    public async Task TestJobFailedMessage()
    {
        JobExecutionException ex = new JobExecutionException("test error");
        await plugin.JobWasExecuted(CreateJobExecutionContext(), ex);

        Assert.That(loggerProvider.Entries, Has.Count.EqualTo(1));
        Assert.That(loggerProvider.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(loggerProvider.Entries[0].Exception, Is.SameAs(ex));
        Assert.That(loggerProvider.Entries[0].Message, Does.Contain("test error"));
    }

    [Test]
    public async Task TestJobWasVetoedMessage()
    {
        await plugin.JobExecutionVetoed(CreateJobExecutionContext());

        Assert.That(loggerProvider.Entries, Has.Count.EqualTo(1));
        Assert.That(loggerProvider.Entries[0].Level, Is.EqualTo(LogLevel.Information));
        Assert.That(loggerProvider.Entries[0].Message, Does.Contain("was vetoed"));
    }

    private static ICancellableJobExecutionContext CreateJobExecutionContext()
    {
        IOperableTrigger t = new SimpleTriggerImpl("name", "group");
        TriggerFiredBundle firedBundle = TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), t);
        ICancellableJobExecutionContext ctx = new JobExecutionContextImpl(null, firedBundle, null);
        return ctx;
    }
}
