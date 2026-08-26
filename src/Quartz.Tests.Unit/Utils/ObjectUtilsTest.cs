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

using System.Collections.Specialized;

using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Tests.Unit.Utils;

/// <author>Marko Lahma (.NET)</author>
public class ObjectUtilsTest
{
    [Test]
    public void NullObjectForValueTypeShouldReturnDefaultforValueType()
    {
        object value = ObjectUtils.ConvertValueIfNecessary(typeof(int), null);
        Assert.That(value, Is.EqualTo(0));
    }

    [Test]
    public void NotConvertableDataShouldThrowNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => ObjectUtils.ConvertValueIfNecessary(typeof(int), new DirtyFlagMap<int, string>()));
    }

    [Test]
    public void TimeSpanConversionShouldWork()
    {
        TimeSpan timeSpan = (TimeSpan) ObjectUtils.ConvertValueIfNecessary(typeof(TimeSpan), "1");
        Assert.That(timeSpan.TotalDays, Is.EqualTo(1));
    }

    [Test]
    public void TestConvertAssignable()
    {
        IComparable val = (IComparable) ObjectUtils.ConvertValueIfNecessary(typeof(IComparable), "test");
        Assert.That(val, Is.EqualTo("test"));
    }

    [Test]
    public void TestConvertStringToEnum()
    {
        DayOfWeek val = (DayOfWeek) ObjectUtils.ConvertValueIfNecessary(typeof(DayOfWeek), "Wednesday");
        Assert.That(val, Is.EqualTo(DayOfWeek.Wednesday));
    }

    [Test]
    public void TestConvertEnumToString()
    {
        string val = (string) ObjectUtils.ConvertValueIfNecessary(typeof(string), DayOfWeek.Wednesday);
        Assert.That(val, Is.EqualTo("Wednesday"));
    }

    [Test]
    public void TestConvertIntToDouble()
    {
        double val = (double) ObjectUtils.ConvertValueIfNecessary(typeof(double), 1234);
        Assert.That(val, Is.EqualTo(1234.0));
    }

    [Test]
    public void TestConvertDoubleToInt()
    {
        int val = (int) ObjectUtils.ConvertValueIfNecessary(typeof(int), 1234.5);
        Assert.That(val, Is.EqualTo(1234));
    }

    [Test]
    public void TestConvertStringToType()
    {
        Type val = (Type) ObjectUtils.ConvertValueIfNecessary(typeof(Type), "System.String");
        Assert.That(val, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void TestConvertTypeToString()
    {
        string val = (string) ObjectUtils.ConvertValueIfNecessary(typeof(string), typeof(string));
        Assert.That(val, Is.EqualTo("System.String"));
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

        Assert.Multiple(() =>
        {
            Assert.That(o.TimeHours.TotalHours, Is.EqualTo(1));
            Assert.That(o.TimeMilliseconds.TotalMilliseconds, Is.EqualTo(1));
            Assert.That(o.TimeMinutes.TotalMinutes, Is.EqualTo(1));
            Assert.That(o.TimeSeconds.TotalSeconds, Is.EqualTo(1));
            Assert.That(o.TimeDefault.TotalDays, Is.EqualTo(1));
        });
    }

    [Test]
    public void ShouldBeAbleToSetValuesToExplicitlyImplementedInterfaceMembers()
    {
        ExplicitImplementor testObject = new ExplicitImplementor();
        ObjectUtils.SetObjectProperties(testObject, ["InstanceName"], ["instance"]);
        Assert.That(testObject.InstanceName, Is.EqualTo("instance"));
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

internal sealed class ExplicitImplementor : IThreadPool
{
    public ValueTask<bool> TryRun(Func<ValueTask> action, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    ValueTask<int> IThreadPool.WaitForAvailableThreads(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    ValueTask IThreadPool.Initialize(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    ValueTask IThreadPool.Shutdown(bool waitForJobsToComplete, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    int IThreadPool.PoolSize => throw new NotImplementedException();

    /// <summary>
    /// A plain settable property on a type whose interface members are all explicit — the thing
    /// this fake exists to prove <see cref="ObjectUtils.SetObjectProperties" /> can still reach.
    /// </summary>
    public string InstanceName { get; set; }
}