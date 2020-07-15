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
using System.Collections.Specialized;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Unit.Utils
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class ObjectUtilsTest
    {
        [Test]
        public void NullObjectForValueTypeShouldReturnDefaultforValueType()
        {
            object value = ObjectUtils.ConvertValueIfNecessary(typeof (int), null);
            Assert.AreEqual(0, value);
        }

        [Test]
        public void NotConvertableDataShouldThrowNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() => ObjectUtils.ConvertValueIfNecessary(typeof (int), new DirtyFlagMap<int, string>()));
        }

        [Test]
        public void TimeSpanConversionShouldWork()
        {
            TimeSpan ts = (TimeSpan) ObjectUtils.ConvertValueIfNecessary(typeof (TimeSpan), "1");
            Assert.AreEqual(1, ts.TotalDays);
        }

        [Test]
        public void TestConvertAssignable()
        {
            IComparable val = (IComparable) ObjectUtils.ConvertValueIfNecessary(typeof (IComparable), "test");
            Assert.AreEqual("test", val);
        }

        [Test]
        public void TestConvertStringToEnum()
        {
            DayOfWeek val = (DayOfWeek) ObjectUtils.ConvertValueIfNecessary(typeof (DayOfWeek), "Wednesday");
            Assert.AreEqual(DayOfWeek.Wednesday, val);
        }

        [Test]
        public void TestConvertEnumToString()
        {
            string val = (string) ObjectUtils.ConvertValueIfNecessary(typeof (string), DayOfWeek.Wednesday);
            Assert.AreEqual("Wednesday", val);
        }

        [Test]
        public void TestConvertIntToDouble()
        {
            double val = (double) ObjectUtils.ConvertValueIfNecessary(typeof (double), 1234);
            Assert.AreEqual(1234.0, val);
        }

        [Test]
        public void TestConvertDoubleToInt()
        {
            int val = (int) ObjectUtils.ConvertValueIfNecessary(typeof (int), 1234.5);
            Assert.AreEqual(1234, val);
        }

        [Test]
        public void TestConvertStringToType()
        {
            Type val = (Type) ObjectUtils.ConvertValueIfNecessary(typeof (Type), "System.String");
            Assert.AreEqual(typeof (string), val);
        }

        [Test]
        public void TestConvertTypeToString()
        {
            string val = (string) ObjectUtils.ConvertValueIfNecessary(typeof (string), typeof (string));
            Assert.AreEqual("System.String", val);
        }

        [Test]
        public void TestSetObjectTimeSpanProperties()
        {
            TimeSpanPropertyTest o = new TimeSpanPropertyTest();
            NameValueCollection props = new NameValueCollection();
            props["TimeHours"] = "1";
            props["TimeMinutes"] = "1";
            props["TimeSeconds"] = "1";
            props["TimeMilliseconds"] = "1";
            props["TimeDefault"] = "1";
            ObjectUtils.SetObjectProperties(o, props);

            Assert.AreEqual(1, o.TimeHours.TotalHours);
            Assert.AreEqual(1, o.TimeMilliseconds.TotalMilliseconds);
            Assert.AreEqual(1, o.TimeMinutes.TotalMinutes);
            Assert.AreEqual(1, o.TimeSeconds.TotalSeconds);
            Assert.AreEqual(1, o.TimeDefault.TotalDays);
        }

        [Test]
        public void TestIsAnnotationPresentOnSuperClass()
        {
            Assert.IsTrue(ObjectUtils.IsAttributePresent(typeof (BaseJob), typeof (DisallowConcurrentExecutionAttribute)));
            Assert.IsFalse(ObjectUtils.IsAttributePresent(typeof (BaseJob), typeof (PersistJobDataAfterExecutionAttribute)));
            Assert.IsTrue(ObjectUtils.IsAttributePresent(typeof (ExtendedJob), typeof (DisallowConcurrentExecutionAttribute)));
            Assert.IsFalse(ObjectUtils.IsAttributePresent(typeof (ExtendedJob), typeof (PersistJobDataAfterExecutionAttribute)));
            Assert.IsTrue(ObjectUtils.IsAttributePresent(typeof (ReallyExtendedJob), typeof (DisallowConcurrentExecutionAttribute)));
            Assert.IsTrue(ObjectUtils.IsAttributePresent(typeof (ReallyExtendedJob), typeof (PersistJobDataAfterExecutionAttribute)));
        }

        [Test]
        public void ShouldBeAbleToSetValuesToExplicitlyImplementedInterfaceMembers()
        {
            ExplicitImplementor testObject = new ExplicitImplementor();
            ObjectUtils.SetObjectProperties(testObject, new[] {"InstanceName"}, new object[] {"instance"});
            Assert.That(testObject.InstanceName, Is.EqualTo("instance"));
        }

        [DisallowConcurrentExecution]
        private class BaseJob : IJob
        {
            public Task Execute(IJobExecutionContext context)
            {
                // Console.WriteLine(GetType().Name);
                return Task.CompletedTask;
            }
        }

        private class ExtendedJob : BaseJob
        {
        }

        [PersistJobDataAfterExecution]
        private class ReallyExtendedJob : ExtendedJob
        {
        }

        public class TimeSpanPropertyTest
        {
            [TimeSpanParseRule(TimeSpanParseRule.Hours)]
            public TimeSpan TimeHours { get; set; }

            [TimeSpanParseRule(TimeSpanParseRule.Minutes)]
            public TimeSpan TimeMinutes { get; set; }

            [TimeSpanParseRule(TimeSpanParseRule.Seconds)]
            public TimeSpan TimeSeconds { get; set; }

            [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
            public TimeSpan TimeMilliseconds { get; set; }

            public TimeSpan TimeDefault { get; set; }
        }
    }

    internal class ExplicitImplementor : IThreadPool
    {
        public bool RunInThread(Func<Task> runnable)
        {
            throw new NotImplementedException();
        }

        int IThreadPool.BlockForAvailableThreads()
        {
            throw new NotImplementedException();
        }

        void IThreadPool.Initialize()
        {
            throw new NotImplementedException();
        }

        void IThreadPool.Shutdown(bool waitForJobsToComplete)
        {
            throw new NotImplementedException();
        }

        int IThreadPool.PoolSize => throw new NotImplementedException();

        public string InstanceId { get; set; }
        public string InstanceName { get; set; }
    }
}