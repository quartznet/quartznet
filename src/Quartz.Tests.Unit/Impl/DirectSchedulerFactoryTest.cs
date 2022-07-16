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

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Tests.Unit.Core;

namespace Quartz.Tests.Unit.Impl
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
	public class DirectSchedulerFactoryTest
	{
        private DirectSchedulerFactory _directSchedulerFactory;
        private Random _random;
        private SchedulerRepository _schedulerRepository;
        private DefaultThreadPool _threadPool;
        private RAMJobStore _jobStore;

        public DirectSchedulerFactoryTest()
        {
            _random = new Random();
            _schedulerRepository = SchedulerRepository.Instance;
            _threadPool = new DefaultThreadPool();
            _jobStore = new RAMJobStore();

            _directSchedulerFactory = DirectSchedulerFactory.Instance;
        }

        [TearDown]
        public void TearDown()
        {
            var schedulers = _schedulerRepository.LookupAll().GetAwaiter().GetResult();
            foreach (var scheduler in schedulers)
            {
                scheduler.Shutdown().GetAwaiter().GetResult();
            }
        }

        [Test]
        public void CreateScheduler_ThreadPoolAndJobStore()
        {
            _directSchedulerFactory.CreateScheduler(_threadPool, _jobStore);

            var scheduler = _schedulerRepository.Lookup(DirectSchedulerFactory.DefaultSchedulerName).GetAwaiter().GetResult();
            Assert.IsNotNull(scheduler);
            Assert.AreEqual(typeof(StdScheduler), scheduler.GetType());

            var stdScheduler = (StdScheduler) scheduler;

            Assert.AreSame(DirectSchedulerFactory.DefaultSchedulerName, stdScheduler.SchedulerName);
            Assert.AreSame(DirectSchedulerFactory.DefaultInstanceId, stdScheduler.SchedulerInstanceId);
            Assert.AreSame(_threadPool, stdScheduler.sched.resources.ThreadPool);
            Assert.AreSame(_jobStore, stdScheduler.sched.resources.JobStore);
            Assert.AreEqual(TimeSpan.FromSeconds(30), stdScheduler.sched.resources.IdleWaitTime);
            Assert.AreEqual(1, stdScheduler.sched.resources.MaxBatchSize);
            Assert.AreEqual(TimeSpan.Zero, stdScheduler.sched.resources.BatchTimeWindow);
        }

        [Test]
        public void CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStore()
        {
            var schedulerName = _random.Next().ToString();
            var schedulerInstanceId = _random.Next().ToString();

            _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore);

            var scheduler = _schedulerRepository.Lookup(schedulerName).GetAwaiter().GetResult();
            Assert.IsNotNull(scheduler);
            Assert.AreEqual(typeof(StdScheduler), scheduler.GetType());

            var stdScheduler = (StdScheduler) scheduler;

            Assert.AreSame(schedulerName, stdScheduler.SchedulerName);
            Assert.AreSame(schedulerInstanceId, stdScheduler.SchedulerInstanceId);
            Assert.AreEqual(0, stdScheduler.sched.resources.SchedulerPlugins.Count);
            Assert.AreSame(_threadPool, stdScheduler.sched.resources.ThreadPool);
            Assert.AreSame(_jobStore, stdScheduler.sched.resources.JobStore);
            Assert.AreEqual(TimeSpan.FromSeconds(30), stdScheduler.sched.resources.IdleWaitTime);
            Assert.AreEqual(1, stdScheduler.sched.resources.MaxBatchSize);
            Assert.AreEqual(TimeSpan.Zero, stdScheduler.sched.resources.BatchTimeWindow);
        }

        [Test]
        public void CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndIdleWaitTime_IdleWaitTimeNotValid([ValueSource(nameof(InvalidIdleWaitTimes))] TimeSpan idleWaitTime)
        {
            var schedulerName = _random.Next().ToString();
            var schedulerInstanceId = _random.Next().ToString();

            try
            {
                _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, idleWaitTime);
                Assert.Fail();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Assert.AreEqual(nameof(idleWaitTime), ex.ParamName);
            }
        }

        [Test]
        public void CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTime([ValueSource(nameof(ValidIdleWaitTimes))] TimeSpan idleWaitTime,
                                                                                                                                    [ValueSource(nameof(ValidSchedulerPluginMaps))] IDictionary<string, ISchedulerPlugin> schedulerPluginMap)
        {
            var schedulerName = _random.Next().ToString();
            var schedulerInstanceId = _random.Next().ToString();

            _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime);

            var scheduler = _schedulerRepository.Lookup(schedulerName).GetAwaiter().GetResult();
            Assert.IsNotNull(scheduler);
            Assert.AreEqual(typeof(StdScheduler), scheduler.GetType());

            var stdScheduler = (StdScheduler) scheduler;

            Assert.AreSame(schedulerName, stdScheduler.SchedulerName);
            Assert.AreEqual(schedulerInstanceId, stdScheduler.SchedulerInstanceId);
            Assert.AreSame(_threadPool, stdScheduler.sched.resources.ThreadPool);
            Assert.AreSame(_jobStore, stdScheduler.sched.resources.JobStore);
            Assert.AreEqual(idleWaitTime, stdScheduler.sched.resources.IdleWaitTime);
            Assert.AreEqual(1, stdScheduler.sched.resources.MaxBatchSize);
            Assert.AreEqual(TimeSpan.Zero, stdScheduler.sched.resources.BatchTimeWindow);

            if (schedulerPluginMap == null)
            {
                Assert.AreEqual(0, stdScheduler.sched.resources.SchedulerPlugins.Count);
            }
            else
            {
                Assert.AreEqual(schedulerPluginMap.Count, stdScheduler.sched.resources.SchedulerPlugins.Count);
                foreach (var plugin in schedulerPluginMap.Values)
                {
                    Assert.IsTrue(stdScheduler.sched.resources.SchedulerPlugins.Contains(plugin));
                }
            }
        }

        [Test]
        public void CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTime_IdleWaitTimeNotValid([ValueSource(nameof(InvalidIdleWaitTimes))] TimeSpan idleWaitTime)
        {
            var schedulerName = _random.Next().ToString();
            var schedulerInstanceId = _random.Next().ToString();
            IDictionary<string, ISchedulerPlugin> schedulerPluginMap = null;

            try
            {
                _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime);
                Assert.Fail();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Assert.AreEqual(nameof(idleWaitTime), ex.ParamName);
            }
        }

        [Test]
        public void CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTimeAndMaxBatchSizeAndBatchTimeWindow([ValueSource(nameof(ValidSchedulerPluginMaps))] IDictionary<string, ISchedulerPlugin> schedulerPluginMap,
                                                                                                                                                                     [ValueSource(nameof(ValidIdleWaitTimes))] TimeSpan idleWaitTime,
                                                                                                                                                                     [ValueSource(nameof(ValidMaxBatchSizes))] int maxBatchSize,
                                                                                                                                                                     [ValueSource(nameof(ValidBatchTimeWindows))] TimeSpan batchTimeWindow)
        {
            var schedulerName = _random.Next().ToString();
            var schedulerInstanceId = _random.Next().ToString();

            _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime, maxBatchSize, batchTimeWindow);

            var scheduler = _schedulerRepository.Lookup(schedulerName).GetAwaiter().GetResult();
            Assert.IsNotNull(scheduler);
            Assert.AreEqual(typeof(StdScheduler), scheduler.GetType());

            var stdScheduler = (StdScheduler) scheduler;

            Assert.AreSame(schedulerName, stdScheduler.SchedulerName);
            Assert.AreSame(schedulerInstanceId, stdScheduler.SchedulerInstanceId);
            Assert.AreSame(_threadPool, stdScheduler.sched.resources.ThreadPool);
            Assert.AreSame(_jobStore, stdScheduler.sched.resources.JobStore);
            Assert.AreEqual(idleWaitTime, stdScheduler.sched.resources.IdleWaitTime);
            Assert.AreEqual(maxBatchSize, stdScheduler.sched.resources.MaxBatchSize);
            Assert.AreEqual(batchTimeWindow, stdScheduler.sched.resources.BatchTimeWindow);

            if (schedulerPluginMap == null)
            {
                Assert.AreEqual(0, stdScheduler.sched.resources.SchedulerPlugins.Count);
            }
            else
            {
                Assert.AreEqual(schedulerPluginMap.Count, stdScheduler.sched.resources.SchedulerPlugins.Count);
                foreach (var plugin in schedulerPluginMap.Values)
                {
                    Assert.IsTrue(stdScheduler.sched.resources.SchedulerPlugins.Contains(plugin));
                }
            }
        }

        [Test]
        public void CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTimeAndMaxBatchSizeAndBatchTimeWindow_IdleWaitTimeNotValid([ValueSource(nameof(InvalidIdleWaitTimes))] TimeSpan idleWaitTime)
        {
            var schedulerName = _random.Next().ToString();
            var schedulerInstanceId = _random.Next().ToString();
            var schedulerPluginMap = new Dictionary<string, ISchedulerPlugin>();
            var maxBatchSize = ValidMaxBatchSizes().First();
            var batchTimeWindow = ValidBatchTimeWindows().First();

            try
            {
                _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime, maxBatchSize, batchTimeWindow);
                Assert.Fail();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Assert.AreEqual(nameof(idleWaitTime), ex.ParamName);
            }
        }

        [Test]
        public void CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTimeAndMaxBatchSizeAndBatchTimeWindowAndSchedulerExporter([ValueSource(nameof(ValidSchedulerPluginMaps))] IDictionary<string, ISchedulerPlugin> schedulerPluginMap,
                                                                                                                                                                                         [ValueSource(nameof(ValidIdleWaitTimes))] TimeSpan idleWaitTime,
                                                                                                                                                                                         [ValueSource(nameof(ValidMaxBatchSizes))] int maxBatchSize,
                                                                                                                                                                                         [ValueSource(nameof(ValidBatchTimeWindows))] TimeSpan batchTimeWindow,
                                                                                                                                                                                         [ValueSource(nameof(SchedulerExporters))] ISchedulerExporter schedulerExporter)
        {
            var schedulerName = _random.Next().ToString();
            var schedulerInstanceId = _random.Next().ToString();

            _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime, maxBatchSize, batchTimeWindow, schedulerExporter);

            var scheduler = _schedulerRepository.Lookup(schedulerName).GetAwaiter().GetResult();
            Assert.IsNotNull(scheduler);
            Assert.AreEqual(typeof(StdScheduler), scheduler.GetType());

            var stdScheduler = (StdScheduler) scheduler;

            Assert.AreSame(schedulerName, stdScheduler.SchedulerName);
            Assert.AreSame(schedulerInstanceId, stdScheduler.SchedulerInstanceId);
            Assert.AreSame(_threadPool, stdScheduler.sched.resources.ThreadPool);
            Assert.AreSame(_jobStore, stdScheduler.sched.resources.JobStore);
            Assert.AreEqual(idleWaitTime, stdScheduler.sched.resources.IdleWaitTime);
            Assert.AreEqual(maxBatchSize, stdScheduler.sched.resources.MaxBatchSize);
            Assert.AreEqual(batchTimeWindow, stdScheduler.sched.resources.BatchTimeWindow);
            Assert.AreSame(schedulerExporter, stdScheduler.sched.resources.SchedulerExporter);

            if (schedulerPluginMap == null)
            {
                Assert.AreEqual(0, stdScheduler.sched.resources.SchedulerPlugins.Count);
            }
            else
            {
                Assert.AreEqual(schedulerPluginMap.Count, stdScheduler.sched.resources.SchedulerPlugins.Count);
                foreach (var plugin in schedulerPluginMap.Values)
                {
                    Assert.IsTrue(stdScheduler.sched.resources.SchedulerPlugins.Contains(plugin));
                }
            }
        }

        [Test]
        public void CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTimeAndMaxBatchSizeAndBatchTimeWindowAndSchedulerExporter_IdleWaitTimeNotValid([ValueSource(nameof(InvalidIdleWaitTimes))] TimeSpan idleWaitTime)
        {
            var schedulerName = _random.Next().ToString();
            var schedulerInstanceId = _random.Next().ToString();
            var schedulerPluginMap = new Dictionary<string, ISchedulerPlugin>();
            var maxBatchSize = ValidMaxBatchSizes().First();
            var batchTimeWindow = ValidBatchTimeWindows().First();
            var schedulerExporter = new NoOpSchedulerExporter();

            try
            {
                _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime, maxBatchSize, batchTimeWindow, schedulerExporter);
                Assert.Fail();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Assert.AreEqual(nameof(idleWaitTime), ex.ParamName);
            }
        }

        [Test]
        public void CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTimeAndMaxBatchSizeAndBatchTimeWindowAndSchedulerExporter_MaxBatchSizeNotValid([ValueSource(nameof(InvalidMaxBatchSizes))] int maxBatchSize)
        {
            var schedulerName = _random.Next().ToString();
            var schedulerInstanceId = _random.Next().ToString();
            var schedulerPluginMap = new Dictionary<string, ISchedulerPlugin>();
            var idleWaitTime = ValidIdleWaitTimes().First();
            var batchTimeWindow = ValidBatchTimeWindows().First();
            var schedulerExporter = new NoOpSchedulerExporter();

            try
            {
                _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime, maxBatchSize, batchTimeWindow, schedulerExporter);
                Assert.Fail();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Assert.AreEqual(nameof(maxBatchSize), ex.ParamName);
            }
        }

        [Test]
        public void CreateScheduler_SchedulerNameAndSchedulerInstanceIdAndThreadPoolAndJobStoreAndSchedulerPluginMapAndIdleWaitTimeAndMaxBatchSizeAndBatchTimeWindowAndSchedulerExporter_BatchTimeWindowNotValid([ValueSource(nameof(InvalidBatchTimeWindows))] TimeSpan batchTimeWindow)
        {
            var schedulerName = _random.Next().ToString();
            var schedulerInstanceId = _random.Next().ToString();
            var schedulerPluginMap = new Dictionary<string, ISchedulerPlugin>();
            var idleWaitTime = ValidIdleWaitTimes().First();
            var maxBatchSize = ValidMaxBatchSizes().First();
            var schedulerExporter = new NoOpSchedulerExporter();

            try
            {
                _directSchedulerFactory.CreateScheduler(schedulerName, schedulerInstanceId, _threadPool, _jobStore, schedulerPluginMap, idleWaitTime, maxBatchSize, batchTimeWindow, schedulerExporter);
                Assert.Fail();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Assert.AreEqual(nameof(batchTimeWindow), ex.ParamName);
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
			DirectSchedulerFactory.Instance.CreateScheduler(
				"MyScheduler", "Instance1", threadPool,
				new RAMJobStore(), data,
				TimeSpan.Zero);


			IScheduler scheduler = await DirectSchedulerFactory.Instance.GetScheduler("MyScheduler");
			await scheduler.Start();
			await scheduler.Shutdown();

			Assert.AreEqual("TestPlugin|MyScheduler|Start|Shutdown", result.ToString());
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
            yield return new Dictionary<string, ISchedulerPlugin> {{ "TestPlugin1", plugin1 }};
            yield return new Dictionary<string, ISchedulerPlugin> {{ "TestPlugin1", plugin1 }, { "TestPlugin2", plugin2 }};
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

        private static IEnumerable<ISchedulerExporter> SchedulerExporters()
        {
            yield return null;
            yield return new NoOpSchedulerExporter();
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

        class NoOpSchedulerExporter : ISchedulerExporter
        {
            public void Bind(IRemotableQuartzScheduler scheduler)
            {
            }

            public void UnBind(IRemotableQuartzScheduler scheduler)
            {
            }
        }
    }
}