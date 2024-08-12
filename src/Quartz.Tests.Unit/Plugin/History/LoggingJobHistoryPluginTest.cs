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
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Plugin.History;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Plugin.History;

/// <author>Marko Lahma (.NET)</author>
[TestFixture]
public class LoggingJobHistoryPluginTest
{
    private RecordingLoggingJobHistoryPlugin plugin;

    [SetUp]
    public void SetUp()
    {
        plugin = new RecordingLoggingJobHistoryPlugin();
    }

    [Test]
    public async Task TestJobFailedMessage()
    {
        JobExecutionException ex = new JobExecutionException("test error");
        await plugin.JobWasExecuted(CreateJobExecutionContext(), ex);

        Assert.That(plugin.WarnMessages, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TestJobSuccessMessage()
    {
        await plugin.JobWasExecuted(CreateJobExecutionContext(), null);

        Assert.That(plugin.InfoMessages, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TestJobToBeFiredMessage()
    {
        await plugin.JobToBeExecuted(CreateJobExecutionContext());

        Assert.That(plugin.InfoMessages, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TestJobWasVetoedMessage()
    {
        await plugin.JobExecutionVetoed(CreateJobExecutionContext());

        Assert.That(plugin.InfoMessages, Has.Count.EqualTo(1));
    }

    protected virtual ICancellableJobExecutionContext CreateJobExecutionContext()
    {
        IOperableTrigger t = new SimpleTriggerImpl("name", "group");
        TriggerFiredBundle firedBundle = TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(typeof(NoOpJob), t);
        ICancellableJobExecutionContext ctx = new JobExecutionContextImpl(null, firedBundle, null);
        return ctx;
    }

    private class RecordingLoggingJobHistoryPlugin : LoggingJobHistoryPlugin
    {
        public List<string> InfoMessages { get; } = new List<string>();
        public List<string> WarnMessages { get; } = new List<string>();

        protected override bool IsInfoEnabled => true;
        protected override bool IsWarnEnabled => true;

        protected override void WriteInfo(string message)
        {
            InfoMessages.Add(message);
        }

        protected override void WriteWarning(string message, Exception ex)
        {
            WarnMessages.Add(message);
        }
    }
}