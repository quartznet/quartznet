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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Impl;

/// <author>Marko Lahma (.NET)</author>
[NonParallelizable]
public class DirectSchedulerFactoryTest
{
    [Test]
    public async Task TestPlugins()
    {
        StringBuilder result = new StringBuilder();

        IDictionary<string, ISchedulerPlugin> data = new Dictionary<string, ISchedulerPlugin>();
        data["TestPlugin"] = new TestPlugin(result);

        IThreadPool threadPool = new DedicatedThreadPool
        {
            ThreadCount = 1
        };
        threadPool.Initialize();
        DirectSchedulerFactory.Instance.CreateScheduler(
            "MyScheduler", "Instance1", threadPool,
            new RAMJobStore(), data,
            TimeSpan.Zero);


        IScheduler scheduler = await DirectSchedulerFactory.Instance.GetScheduler("MyScheduler");
        await scheduler.Start();
        await scheduler.Shutdown();

        Assert.AreEqual("TestPlugin|MyScheduler|Start|Shutdown", result.ToString());
    }

    /// <summary>
    /// A scheduling call made as soon as CreateScheduler returns reaches a store that has finished
    /// initializing (#3908).
    /// </summary>
    [Test]
    public async Task CreateSchedulerReturnsOnlyOnceTheJobStoreHasInitialized()
    {
        SlowlyInitializingJobStore jobStore = new SlowlyInitializingJobStore();

        DirectSchedulerFactory.Instance.CreateScheduler("SlowStore", "Instance1", new DedicatedThreadPool { ThreadCount = 1 }, jobStore);
        try
        {
            jobStore.Initialized.Should().BeTrue(
                "a caller takes CreateScheduler's return as the scheduler being ready, and a store still initializing behind it is half-configured for the first call");
        }
        finally
        {
            IScheduler scheduler = await DirectSchedulerFactory.Instance.GetScheduler("SlowStore");
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// A store that cannot initialize fails CreateScheduler, instead of being handed out to fail its
    /// first call; nothing of the scheduler is bound.
    /// </summary>
    [Test]
    public void CreateSchedulerReportsAJobStoreThatCannotInitialize()
    {
        Action act = () => DirectSchedulerFactory.Instance.CreateScheduler("FailingStore", "Instance1", new DedicatedThreadPool { ThreadCount = 1 }, new FailingJobStore());

        act.Should().Throw<SchedulerConfigException>().WithMessage(FailingJobStore.Message);
        SchedulerRepository.Instance.Lookup("FailingStore").Should().BeNull(
            "a scheduler whose store failed to initialize would fail every call made to it");
    }

    /// <summary>
    /// Initializes the way a database store does: its first await goes out to the database and returns
    /// to the caller, which is what let CreateScheduler return with the store still configuring itself.
    /// </summary>
    private sealed class SlowlyInitializingJobStore : RAMJobStore
    {
        public bool Initialized { get; private set; }

        public override async Task Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            await base.Initialize(loadHelper, signaler, cancellationToken).ConfigureAwait(false);
            Initialized = true;
        }
    }

    private sealed class FailingJobStore : RAMJobStore
    {
        public const string Message = "the store's data source is not configured";

        public override async Task Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new SchedulerConfigException(Message);
        }
    }

    class TestPlugin : ISchedulerPlugin
    {
        readonly StringBuilder result;

        public TestPlugin(StringBuilder result)
        {
            this.result = result;
        }

        public Task Initialize(string name, IScheduler scheduler, CancellationToken cancellationToken)
        {
            result.Append(name).Append("|").Append(scheduler.SchedulerName);
            return Task.FromResult(true);
        }

        Task ISchedulerPlugin.Start(CancellationToken cancellationToken)
        {
            result.Append("|Start");
            return Task.FromResult(true);
        }

        public Task Shutdown(CancellationToken cancellationToken)
        {
            result.Append("|Shutdown");
            return Task.FromResult(true);
        }
    }
}