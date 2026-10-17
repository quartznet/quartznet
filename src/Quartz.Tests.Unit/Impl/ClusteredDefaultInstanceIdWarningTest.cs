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

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading.Tasks;

using Quartz.Impl;
using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// A clustered scheduler left on the instance id <c>NON_CLUSTERED</c> says so at startup (#4091).
/// </summary>
/// <remarks>
/// <para>
/// <c>UseClustering()</c> and <c>quartz.jobStore.clustered</c> leave the id at its default, so every node
/// configured that way shares it: each reads the others' fired triggers as its own and none sees another
/// fail. 4.6 generates an id; this branch only warns, because changing what an existing deployment's
/// rows are keyed by is too much for a maintenance release.
/// </para>
/// <para>
/// The store is in-memory and merely reports itself clustered, which is all the check reads; no
/// database is needed. Non-parallelizable because the process-wide <see cref="LogProvider" /> is handed a
/// recorder of its own.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class ClusteredDefaultInstanceIdWarningTest
{
    private RecordingLogProvider recorder;

    [SetUp]
    public void RecordWarnings()
    {
        // Before any factory is built: StdSchedulerFactory takes its logger as it initializes.
        recorder = new RecordingLogProvider();
        LogProvider.SetCurrentLogProvider(recorder);
    }

    [TearDown]
    public void ForgetTheRecordingLogProvider()
    {
        LogProvider.SetCurrentLogProvider(null);
    }

    [Test]
    public async Task AClusteredStoreOnTheDefaultInstanceIdWarns()
    {
        await BuildAndShutDown(typeof(ClusteredRamJobStore), instanceId: null);

        recorder.Warnings.Should().Contain(x => x.Contains("NON_CLUSTERED") && x.Contains("clustered job store"),
            "every node left at the default shares the id, and nothing else tells an operator so");
        recorder.Warnings.Should().Contain(x => x.Contains("quartz.scheduler.instanceId = AUTO"),
            "the warning names the fix as well as the risk");
    }

    [TestCase("AUTO")]
    [TestCase("node-1")]
    public async Task AClusteredStoreWithAnInstanceIdOfItsOwnDoesNotWarn(string instanceId)
    {
        await BuildAndShutDown(typeof(ClusteredRamJobStore), instanceId);

        recorder.Warnings.Should().NotContain(x => x.Contains("NON_CLUSTERED"),
            "a generated id and a distinct literal one are both what a cluster needs");
    }

    [Test]
    public async Task AnUnclusteredStoreOnTheDefaultInstanceIdDoesNotWarn()
    {
        await BuildAndShutDown(typeof(RAMJobStore), instanceId: null);

        recorder.Warnings.Should().NotContain(x => x.Contains("NON_CLUSTERED"),
            "NON_CLUSTERED is exactly right for a scheduler that shares its store with nobody");
    }

    private static async Task BuildAndShutDown(Type jobStoreType, string instanceId)
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "ClusteredDefaultInstanceId_" + Guid.NewGuid().ToString("N"),
            ["quartz.jobStore.type"] = jobStoreType.AssemblyQualifiedName,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
        };

        if (instanceId is not null)
        {
            properties["quartz.scheduler.instanceId"] = instanceId;
        }

        IScheduler scheduler = await new StdSchedulerFactory(properties).GetScheduler();
        await scheduler.Shutdown();
    }

    /// <summary>
    /// An in-memory store that answers <see cref="IJobStore.Clustered" /> the way a database-backed store
    /// configured for a cluster does. It re-implements the interface to answer that one member, since
    /// <see cref="RAMJobStore" /> declares it non-virtual.
    /// </summary>
    public sealed class ClusteredRamJobStore : RAMJobStore, IJobStore
    {
        bool IJobStore.Clustered => true;
    }

    /// <summary>
    /// Collects what was logged at warning level or above.
    /// </summary>
    private sealed class RecordingLogProvider : ILogProvider
    {
        private readonly List<string> warnings = new List<string>();

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (warnings)
                {
                    return warnings.ToArray();
                }
            }
        }

        public Logger GetLogger(string name)
        {
            return (logLevel, messageFunc, exception, formatParameters) =>
            {
                // LibLog asks whether a level is enabled by passing no message.
                if (messageFunc is null)
                {
                    return true;
                }

                if (logLevel >= LogLevel.Warn)
                {
                    lock (warnings)
                    {
                        warnings.Add(messageFunc());
                    }
                }

                return true;
            };
        }

        public IDisposable OpenNestedContext(string message) => NoContext.Instance;

        public IDisposable OpenMappedContext(string key, object value, bool destructure = false) => NoContext.Instance;

        private sealed class NoContext : IDisposable
        {
            public static readonly NoContext Instance = new NoContext();

            public void Dispose()
            {
            }
        }
    }
}
