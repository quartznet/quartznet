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

using System.Text;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Tests.Unit.Core;

namespace Quartz.Tests.Unit.Impl;

/// <author>Marko Lahma (.NET)</author>
[TestFixture]
public class DirectSchedulerFactoryTest
{
    private readonly DirectSchedulerFactory _directSchedulerFactory;
    private readonly Random _random;
    private readonly SchedulerRepository _schedulerRepository;
    private readonly DefaultThreadPool _threadPool;
    private readonly RAMJobStore _jobStore;

    public DirectSchedulerFactoryTest()
    {
        _random = new Random();
        _schedulerRepository = SchedulerRepository.Instance;
        _threadPool = new DefaultThreadPool();
        _jobStore = new RAMJobStore();

        _directSchedulerFactory = DirectSchedulerFactory.Instance;
    }

    [TearDown]
    public async Task TearDown()
    {
        var schedulers = _schedulerRepository.LookupAll();
        foreach (var scheduler in schedulers)
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task CreateScheduler_ThreadPoolAndJobStore()
    {
        await _directSchedulerFactory.CreateScheduler(_threadPool, _jobStore);

        var scheduler = _schedulerRepository.Lookup(DirectSchedulerFactory.DefaultSchedulerName);
        Assert.Multiple(() => { 
            Assert.That(scheduler, Is.Not.Null);
            Assert.That(scheduler.GetType(), Is.EqualTo(typeof(StdScheduler)));
        });

        var stdScheduler = (StdScheduler) scheduler;

        Assert.Multiple(() =>
        {
            Assert.That(stdScheduler.SchedulerName, Is.SameAs(DirectSchedulerFactory.DefaultSchedulerName));
            Assert.That(stdScheduler.SchedulerInstanceId, Is.SameAs(DirectSchedulerFactory.DefaultInstanceId));
            Assert.That(stdScheduler.sched.resources.ThreadPool, Is.SameAs(_threadPool));
            Assert.That(stdScheduler.sched.resources.JobStore, Is.SameAs(_jobStore));
            Assert.That(stdScheduler.sched.resources.IdleWaitTime, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(stdScheduler.sched.resources.MaxBatchSize, Is.EqualTo(1));
            Assert.That(stdScheduler.sched.resources.BatchTimeWindow, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public async Task CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStore()
    {
        var schedulerName = _random.Next().ToString();
        var schedulerInstanceId = _random.Next().ToString();

        await _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore);

        var scheduler = _schedulerRepository.Lookup(schedulerName);
        Assert.That(scheduler, Is.Not.Null);
        Assert.That(scheduler.GetType(), Is.EqualTo(typeof(StdScheduler)));

        var stdScheduler = (StdScheduler) scheduler;

        Assert.Multiple(() =>
        {
            Assert.That(stdScheduler.SchedulerName, Is.SameAs(schedulerName));
            Assert.That(stdScheduler.SchedulerInstanceId, Is.SameAs(schedulerInstanceId));
            Assert.That(stdScheduler.sched.resources.SchedulerPlugins, Is.Empty);
            Assert.That(stdScheduler.sched.resources.ThreadPool, Is.SameAs(_threadPool));
            Assert.That(stdScheduler.sched.resources.JobStore, Is.SameAs(_jobStore));
            Assert.That(stdScheduler.sched.resources.IdleWaitTime, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(stdScheduler.sched.resources.MaxBatchSize, Is.EqualTo(1));
            Assert.That(stdScheduler.sched.resources.BatchTimeWindow, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public async Task CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndIdleWaitTime_IdleWaitTimeNotValid([ValueSource(nameof(InvalidIdleWaitTimes))] TimeSpan idleWaitTime)
    {
        var schedulerName = _random.Next().ToString();
        var schedulerInstanceId = _random.Next().ToString();

        try
        {
            await _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, idleWaitTime);
            Assert.Fail();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Assert.That(ex.ParamName, Is.EqualTo(nameof(idleWaitTime)));
        }
    }

    [Test]
    public async Task CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTime([ValueSource(nameof(ValidIdleWaitTimes))] TimeSpan idleWaitTime,
        [ValueSource(nameof(ValidSchedulerPluginMaps))] IDictionary<string, ISchedulerPlugin> schedulerPluginMap)
    {
        var schedulerName = _random.Next().ToString();
        var schedulerInstanceId = _random.Next().ToString();

        await _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime);

        var scheduler = _schedulerRepository.Lookup(schedulerName);
        Assert.Multiple(() =>
        {
            Assert.That(scheduler, Is.Not.Null);
            Assert.That(scheduler.GetType(), Is.EqualTo(typeof(StdScheduler)));
        });

        var stdScheduler = (StdScheduler) scheduler;

        Assert.Multiple(() =>
        {
            Assert.That(stdScheduler.SchedulerName, Is.SameAs(schedulerName));
            Assert.That(stdScheduler.SchedulerInstanceId, Is.EqualTo(schedulerInstanceId));
            Assert.That(stdScheduler.sched.resources.ThreadPool, Is.SameAs(_threadPool));
            Assert.That(stdScheduler.sched.resources.JobStore, Is.SameAs(_jobStore));
            Assert.That(stdScheduler.sched.resources.IdleWaitTime, Is.EqualTo(idleWaitTime));
            Assert.That(stdScheduler.sched.resources.MaxBatchSize, Is.EqualTo(1));
            Assert.That(stdScheduler.sched.resources.BatchTimeWindow, Is.EqualTo(TimeSpan.Zero));
        });

        if (schedulerPluginMap is null)
        {
            Assert.That(stdScheduler.sched.resources.SchedulerPlugins, Is.Empty);
        }
        else
        {
            Assert.That(stdScheduler.sched.resources.SchedulerPlugins, Has.Count.EqualTo(schedulerPluginMap.Count));
            foreach (var plugin in schedulerPluginMap.Values)
            {
                Assert.That(stdScheduler.sched.resources.SchedulerPlugins.Contains(plugin), Is.True);
            }
        }
    }

    [Test]
    public async Task CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTime_IdleWaitTimeNotValid([ValueSource(nameof(InvalidIdleWaitTimes))] TimeSpan idleWaitTime)
    {
        var schedulerName = _random.Next().ToString();
        var schedulerInstanceId = _random.Next().ToString();
        IDictionary<string, ISchedulerPlugin> schedulerPluginMap = null;

        try
        {
            await _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime);
            Assert.Fail();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Assert.That(ex.ParamName, Is.EqualTo(nameof(idleWaitTime)));
        }
    }

    [Test]
    public async Task CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTimeAndMaxBatchSizeAndBatchTimeWindow([ValueSource(nameof(ValidSchedulerPluginMaps))] IDictionary<string, ISchedulerPlugin> schedulerPluginMap,
        [ValueSource(nameof(ValidIdleWaitTimes))] TimeSpan idleWaitTime,
        [ValueSource(nameof(ValidMaxBatchSizes))] int maxBatchSize,
        [ValueSource(nameof(ValidBatchTimeWindows))] TimeSpan batchTimeWindow)
    {
        var schedulerName = _random.Next().ToString();
        var schedulerInstanceId = _random.Next().ToString();

        await _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime, maxBatchSize, batchTimeWindow);

        var scheduler = _schedulerRepository.Lookup(schedulerName);
        Assert.Multiple(() =>
        {
            Assert.That(scheduler, Is.Not.Null);
            Assert.That(scheduler.GetType(), Is.EqualTo(typeof(StdScheduler)));
        });

        var stdScheduler = (StdScheduler) scheduler;

        Assert.Multiple(() =>{
            Assert.That(stdScheduler.SchedulerName, Is.SameAs(schedulerName));
            Assert.That(stdScheduler.SchedulerInstanceId, Is.SameAs(schedulerInstanceId));
            Assert.That(stdScheduler.sched.resources.ThreadPool, Is.SameAs(_threadPool));
            Assert.That(stdScheduler.sched.resources.JobStore, Is.SameAs(_jobStore));
            Assert.That(stdScheduler.sched.resources.IdleWaitTime, Is.EqualTo(idleWaitTime));
            Assert.That(stdScheduler.sched.resources.MaxBatchSize, Is.EqualTo(maxBatchSize));
            Assert.That(stdScheduler.sched.resources.BatchTimeWindow, Is.EqualTo(batchTimeWindow));
        });

        if (schedulerPluginMap is null)
        {
            Assert.That(stdScheduler.sched.resources.SchedulerPlugins, Is.Empty);
        }
        else
        {
            Assert.That(stdScheduler.sched.resources.SchedulerPlugins, Has.Count.EqualTo(schedulerPluginMap.Count));
            foreach (var plugin in schedulerPluginMap.Values)
            {
                Assert.That(stdScheduler.sched.resources.SchedulerPlugins.Contains(plugin), Is.True);
            }
        }
    }

    [Test]
    public async Task CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTimeAndMaxBatchSizeAndBatchTimeWindow_IdleWaitTimeNotValid([ValueSource(nameof(InvalidIdleWaitTimes))] TimeSpan idleWaitTime)
    {
        var schedulerName = _random.Next().ToString();
        var schedulerInstanceId = _random.Next().ToString();
        var schedulerPluginMap = new Dictionary<string, ISchedulerPlugin>();
        var maxBatchSize = ValidMaxBatchSizes().First();
        var batchTimeWindow = ValidBatchTimeWindows().First();

        try
        {
            await _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime, maxBatchSize, batchTimeWindow);
            Assert.Fail();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Assert.That(ex.ParamName, Is.EqualTo(nameof(idleWaitTime)));
        }
    }

    [Test]
    public async Task CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTimeAndMaxBatchSizeAndBatchTimeWindowAndSchedulerExporter([ValueSource(nameof(ValidSchedulerPluginMaps))] IDictionary<string, ISchedulerPlugin> schedulerPluginMap,
        [ValueSource(nameof(ValidIdleWaitTimes))] TimeSpan idleWaitTime,
        [ValueSource(nameof(ValidMaxBatchSizes))] int maxBatchSize,
        [ValueSource(nameof(ValidBatchTimeWindows))] TimeSpan batchTimeWindow)
    {
        var schedulerName = _random.Next().ToString();
        var schedulerInstanceId = _random.Next().ToString();

        await _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime, maxBatchSize, batchTimeWindow);

        var scheduler = _schedulerRepository.Lookup(schedulerName);
        Assert.That(scheduler, Is.Not.Null);
        Assert.That(scheduler.GetType(), Is.EqualTo(typeof(StdScheduler)));

        var stdScheduler = (StdScheduler) scheduler;

        Assert.Multiple(() =>
        {
            Assert.That(stdScheduler.SchedulerName, Is.SameAs(schedulerName));
            Assert.That(stdScheduler.SchedulerInstanceId, Is.SameAs(schedulerInstanceId));
            Assert.That(stdScheduler.sched.resources.ThreadPool, Is.SameAs(_threadPool));
            Assert.That(stdScheduler.sched.resources.JobStore, Is.SameAs(_jobStore));
            Assert.That(stdScheduler.sched.resources.IdleWaitTime, Is.EqualTo(idleWaitTime));
            Assert.That(stdScheduler.sched.resources.MaxBatchSize, Is.EqualTo(maxBatchSize));
            Assert.That(stdScheduler.sched.resources.BatchTimeWindow, Is.EqualTo(batchTimeWindow));
        });

        if (schedulerPluginMap is null)
        {
            Assert.That(stdScheduler.sched.resources.SchedulerPlugins, Is.Empty);
        }
        else
        {
            Assert.That(stdScheduler.sched.resources.SchedulerPlugins, Has.Count.EqualTo(schedulerPluginMap.Count));
            foreach (var plugin in schedulerPluginMap.Values)
            {
                Assert.That(stdScheduler.sched.resources.SchedulerPlugins.Contains(plugin), Is.True);
            }
        }
    }

    [Test]
    public async Task CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTimeAndMaxBatchSizeAndBatchTimeWindowAndSchedulerExporter_IdleWaitTimeNotValid([ValueSource(nameof(InvalidIdleWaitTimes))] TimeSpan idleWaitTime)
    {
        var schedulerName = _random.Next().ToString();
        var schedulerInstanceId = _random.Next().ToString();
        var schedulerPluginMap = new Dictionary<string, ISchedulerPlugin>();
        var maxBatchSize = ValidMaxBatchSizes().First();
        var batchTimeWindow = ValidBatchTimeWindows().First();

        try
        {
            await _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime, maxBatchSize, batchTimeWindow).ConfigureAwait(false);
            Assert.Fail();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Assert.That(ex.ParamName, Is.EqualTo(nameof(idleWaitTime)));
        }
    }

    [Test]
    public async Task CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTimeAndMaxBatchSizeAndBatchTimeWindowAndSchedulerExporter_MaxBatchSizeNotValid([ValueSource(nameof(InvalidMaxBatchSizes))] int maxBatchSize)
    {
        var schedulerName = _random.Next().ToString();
        var schedulerInstanceId = _random.Next().ToString();
        var schedulerPluginMap = new Dictionary<string, ISchedulerPlugin>();
        var idleWaitTime = ValidIdleWaitTimes().First();
        var batchTimeWindow = ValidBatchTimeWindows().First();

        try
        {
            await _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime, maxBatchSize, batchTimeWindow);
            Assert.Fail();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Assert.That(ex.ParamName, Is.EqualTo(nameof(maxBatchSize)));
        }
    }

    [Test]
    public async Task CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTimeAndMaxBatchSizeAndBatchTimeWindowAndSchedulerExporter_BatchTimeWindowNotValid([ValueSource(nameof(InvalidBatchTimeWindows))] TimeSpan batchTimeWindow)
    {
        var schedulerName = _random.Next().ToString();
        var schedulerInstanceId = _random.Next().ToString();
        var schedulerPluginMap = new Dictionary<string, ISchedulerPlugin>();
        var idleWaitTime = ValidIdleWaitTimes().First();
        var maxBatchSize = ValidMaxBatchSizes().First();

        try
        {
            await _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime, maxBatchSize, batchTimeWindow);
            Assert.Fail();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Assert.That(ex.ParamName, Is.EqualTo(nameof(batchTimeWindow)));
        }
    }

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
        await DirectSchedulerFactory.Instance.CreateScheduler(
            "MyScheduler", "Instance1", threadPool,
            new RAMJobStore(), data,
            TimeSpan.Zero);


        IScheduler scheduler = await DirectSchedulerFactory.Instance.GetScheduler("MyScheduler");
        await scheduler.Start();
        await scheduler.Shutdown();

        Assert.That(result.ToString(), Is.EqualTo("TestPlugin|MyScheduler|Start|Shutdown"));
    }

    private static IEnumerable<TimeSpan> ValidIdleWaitTimes()
    {
        return QuartzSchedulerResourcesTest.ValidIdleWaitTimes();
    }

    private static IEnumerable<TimeSpan> InvalidIdleWaitTimes()
    {
        return QuartzSchedulerResourcesTest.InvalidIdleWaitTimes();
    }

    private static IEnumerable<IDictionary<string, ISchedulerPlugin>> ValidSchedulerPluginMaps()
    {
        var plugin1 = new TestPlugin(new StringBuilder());
        var plugin2 = new TestPlugin(new StringBuilder());

        yield return null;
        yield return new Dictionary<string, ISchedulerPlugin> { { "TestPlugin1", plugin1 } };
        yield return new Dictionary<string, ISchedulerPlugin> { { "TestPlugin1", plugin1 }, { "TestPlugin2", plugin2 } };
        yield return new Dictionary<string, ISchedulerPlugin>();
    }

    private static IEnumerable<int> ValidMaxBatchSizes()
    {
        return QuartzSchedulerResourcesTest.ValidMaxBatchSizes();
    }

    private static IEnumerable<int> InvalidMaxBatchSizes()
    {
        return QuartzSchedulerResourcesTest.InvalidMaxBatchSizes();
    }

    private static IEnumerable<TimeSpan> ValidBatchTimeWindows()
    {
        return QuartzSchedulerResourcesTest.ValidBatchTimeWindows();
    }

    private static IEnumerable<TimeSpan> InvalidBatchTimeWindows()
    {
        return QuartzSchedulerResourcesTest.InvalidBatchTimeWindows();
    }

    private class TestPlugin : ISchedulerPlugin
    {
        private readonly StringBuilder result;

        public TestPlugin(StringBuilder result)
        {
            this.result = result;
        }

        public ValueTask Initialize(string name, IScheduler scheduler, CancellationToken cancellationToken)
        {
            result.Append(name).Append("|").Append(scheduler.SchedulerName);
            return default;
        }

        ValueTask ISchedulerPlugin.Start(CancellationToken cancellationToken)
        {
            result.Append("|Start");
            return default;
        }

        public ValueTask Shutdown(CancellationToken cancellationToken)
        {
            result.Append("|Shutdown");
            return default;
        }
    }
}