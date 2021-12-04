using NUnit.Framework;
using Quartz.Core;
using System;
using System.Collections.Generic;

namespace Quartz.Tests.Unit.Core
{
    [TestFixture]
    public class QuartzSchedulerResourcesTest
    {
        private QuartzSchedulerResources _resources;

        [SetUp]
        public void SetUp()
        {
            _resources = new QuartzSchedulerResources();
        }

        [Test]
        public void DefaultCtor()
        {
            var resources = new QuartzSchedulerResources();
            Assert.AreEqual(TimeSpan.Zero, resources.BatchTimeWindow);
            Assert.IsNull(resources.InstanceId);
            Assert.IsFalse(resources.InterruptJobsOnShutdown);
            Assert.IsFalse(resources.InterruptJobsOnShutdownWithWait);
            Assert.IsNull(resources.JobRunShellFactory);
            Assert.IsNull(resources.JobStore);
            Assert.IsFalse(resources.MakeSchedulerThreadDaemon);
            Assert.AreEqual(1, resources.MaxBatchSize);
            Assert.IsNull(resources.Name);
            Assert.IsNull(resources.SchedulerExporter);
            Assert.IsNotNull(resources.SchedulerPlugins);
            Assert.IsEmpty(resources.SchedulerPlugins);
            Assert.IsNull(resources.ThreadName);
            Assert.IsNull(resources.ThreadPool);
        }

        [Test]
        public void IdleWaitTime_ValidValues([ValueSource(nameof(ValidIdleWaitTimes))] TimeSpan idleWaitTime)
        {
            _resources.IdleWaitTime = idleWaitTime;
            Assert.AreEqual(idleWaitTime, _resources.IdleWaitTime);
        }

        [Test]
        public void IdleWaitTime_InvalidValues([ValueSource(nameof(InvalidIdleWaitTimes))] TimeSpan idleWaitTime)
        {
            try
            {
                _resources.IdleWaitTime = idleWaitTime;
                Assert.Fail();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Assert.AreEqual("value", ex.ParamName);
            }
        }

        [Test]
        public void BatchTimeWindow_ValidValues([ValueSource(nameof(ValidBatchTimeWindows))] TimeSpan batchTimeWindow)
        {
            _resources.BatchTimeWindow = batchTimeWindow;
            Assert.AreEqual(batchTimeWindow, _resources.BatchTimeWindow);
        }

        [Test]
        public void BatchTimeWindow_InvalidValues([ValueSource(nameof(InvalidBatchTimeWindows))] TimeSpan batchTimeWindow)
        {
            try
            {
                _resources.BatchTimeWindow = batchTimeWindow;
                Assert.Fail();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Assert.AreEqual("value", ex.ParamName);
            }
        }

        [Test]
        public void MaxBatchSize_ValidValues([ValueSource(nameof(ValidMaxBatchSizes))] int maxBatchSize)
        {
            _resources.MaxBatchSize = maxBatchSize;
            Assert.AreEqual(maxBatchSize, _resources.MaxBatchSize);
        }

        [Test]
        public void MaxBatchSize_InvalidValues([ValueSource(nameof(InvalidMaxBatchSizes))] int maxBatchSize)
        {
            try
            {
                _resources.MaxBatchSize = maxBatchSize;
                Assert.Fail();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Assert.AreEqual("value", ex.ParamName);
            }
        }

        internal static IEnumerable<TimeSpan> ValidIdleWaitTimes()
        {
            yield return TimeSpan.Zero;
            yield return TimeSpan.FromTicks(1);
            yield return TimeSpan.MaxValue;
        }

        internal static IEnumerable<TimeSpan> InvalidIdleWaitTimes()
        {
            yield return TimeSpan.FromTicks(-1);
            yield return TimeSpan.FromDays(-30);
            yield return TimeSpan.MinValue;
        }

        internal static IEnumerable<int> ValidMaxBatchSizes()
        {
            yield return 1;
            yield return 8;
            yield return int.MaxValue;
        }

        internal static IEnumerable<int> InvalidMaxBatchSizes()
        {
            yield return 0;
            yield return -1;
            yield return int.MinValue;
        }

        internal static IEnumerable<TimeSpan> ValidBatchTimeWindows()
        {
            yield return TimeSpan.Zero;
            yield return TimeSpan.FromTicks(1);
            yield return TimeSpan.FromDays(30);
        }

        internal static IEnumerable<TimeSpan> InvalidBatchTimeWindows()
        {
            yield return TimeSpan.FromTicks(-1);
            yield return TimeSpan.FromDays(-30);
            yield return TimeSpan.MinValue;
        }

    }
}
