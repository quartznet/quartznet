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

using FakeItEasy;

using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
///  Unit test for PropertySettingJobFactory.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
public class PropertySettingJobFactoryTest
{
    private PropertySettingJobFactory factory;

    [SetUp]
    public void SetUp()
    {
        factory = new PropertySettingJobFactory
        {
            ThrowIfPropertyNotFound = true
        };
    }

    [Test]
    public void BuildJobDataMapMergesTriggerDataOverJobDataAndNothingElse()
    {
        var jobDetail = JobBuilder.Create()
            .OfType(typeof(SampleJob))
            .WithIdentity("job")
            .UsingJobData("fromJob", "job")
            .UsingJobData("shared", "job")
            .Build();

        var trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger")
            .UsingJobData("fromTrigger", "trigger")
            .UsingJobData("shared", "trigger")
            .Build();

        var scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.Context).Returns(new SchedulerContext { ["fromContext"] = "context" });

        var bundle = new TriggerFiredBundle
        {
            JobDetail = jobDetail,
            Trigger = trigger,
            Recovering = false,
            FireTimeUtc = DateTimeOffset.UtcNow,
            ScheduledFireTimeUtc = null,
            PreviousFireTimeUtc = null,
            NextFireTimeUtc = null,
        };

        JobDataMap map = new ExposingJobFactory().BuildFor(bundle, scheduler);

        map.GetString("fromJob").Should().Be("job");
        map.GetString("fromTrigger").Should().Be("trigger");
        map.GetString("shared").Should().Be("trigger", "trigger data merges over job data");
        map.Should().NotContainKey("fromContext",
            "scheduler context entries are no longer injected into job properties; jobs read context.Scheduler.Context");
    }

    private sealed class ExposingJobFactory : PropertySettingJobFactory
    {
        public JobDataMap BuildFor(TriggerFiredBundle bundle, IScheduler scheduler) => BuildJobDataMap(bundle, scheduler);
    }

    private sealed class SampleJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [Test]
    [TestCase(null)]
    [TestCase(typeof(NewtonsoftJsonObjectSerializer))]
    [TestCase(typeof(SystemTextJsonObjectSerializer))]
    public void TestSetObjectPropsPrimitives(Type serializerType)
    {
        JobDataMap jobDataMap = new JobDataMap();
        jobDataMap["intValue"] = 1;
        jobDataMap["longValue"] = 2L;
        jobDataMap["floatValue"] = 3.0f;
        jobDataMap["doubleValue"] = 4.0;
        jobDataMap["booleanValue"] = true;
        jobDataMap["shortValue"] = 5;
        jobDataMap["charValue"] = 'a';
        jobDataMap["byteValue"] = 6;
        jobDataMap["stringValue"] = "S1";
        jobDataMap["enumValue1"] = DayOfWeek.Monday;
        jobDataMap["enumValue2"] = 1;
        jobDataMap["enumValue3"] = "Monday";

        var map = new Dictionary<string, string>();
        map.Add("A", "B");
        jobDataMap["mapValue"] = map;

        if (serializerType is not null)
        {
            var serializer = (IObjectSerializer) Activator.CreateInstance(serializerType);
            var serialized = serializer.Serialize(jobDataMap);
            jobDataMap = serializer.Deserialize<JobDataMap>(serialized);
        }

        TestObject myObject = new TestObject();
        factory.SetObjectProperties(myObject, jobDataMap);

        Assert.Multiple(() =>
        {
            Assert.That(myObject.IntValue, Is.EqualTo(1));
            Assert.That(myObject.LongValue, Is.EqualTo(2));
            Assert.That(myObject.FloatValue, Is.EqualTo(3.0f));
            Assert.That(myObject.DoubleValue, Is.EqualTo(4.0));
            Assert.That(myObject.BooleanValue, Is.True);
            Assert.That(myObject.ShortValue, Is.EqualTo(5));
            Assert.That(myObject.CharValue, Is.EqualTo('a'));
            Assert.That(myObject.ByteValue, Is.EqualTo((byte) 6));
            Assert.That(myObject.StringValue, Is.EqualTo("S1"));
            Assert.That(myObject.EnumValue1, Is.EqualTo(DayOfWeek.Monday));
            Assert.That(myObject.EnumValue2, Is.EqualTo(DayOfWeek.Monday));
            Assert.That(myObject.EnumValue3, Is.EqualTo(DayOfWeek.Monday));
            Assert.That(myObject.MapValue.ContainsKey("A"), Is.True);
        });
    }

    [Test]
    public void TestSetObjectPropsUnknownProperty()
    {
        JobDataMap jobDataMap = new JobDataMap();
        jobDataMap["bogusValue"] = 1;
        try
        {
            factory.SetObjectProperties(new TestObject(), jobDataMap);
            Assert.Fail();
        }
        catch (SchedulerException)
        {
        }
    }

    [Test]
    public void TestSetObjectPropsNullPrimitive()
    {
        JobDataMap jobDataMap = new JobDataMap();
        jobDataMap["intValue"] = null;
        try
        {
            factory.SetObjectProperties(new TestObject(), jobDataMap);
            Assert.Fail();
        }
        catch (SchedulerException)
        {
        }
    }

    [Test]
    public void TestSetObjectPropsNullNonPrimitive()
    {
        JobDataMap jobDataMap = new JobDataMap();
        jobDataMap["mapValue"] = null;
        TestObject testObject = new TestObject();
        Dictionary<string, string> map = new Dictionary<string, string>();
        map.Add("A", "B");
        testObject.MapValue = map;
        factory.SetObjectProperties(testObject, jobDataMap);
        Assert.That(testObject.MapValue, Is.Null);
    }

    [Test]
    public void TestSetObjectPropsWrongPrimitiveType()
    {
        JobDataMap jobDataMap = new JobDataMap();
        jobDataMap["intValue"] = "myvalue";
        try
        {
            factory.SetObjectProperties(new TestObject(), jobDataMap);
            Assert.Fail();
        }
        catch (SchedulerException)
        {
        }
    }

    [Test]
    public void TestSetObjectPropsWrongNonPrimitiveType()
    {
        JobDataMap jobDataMap = new JobDataMap();
        jobDataMap["mapValue"] = 7.2f;
        try
        {
            factory.SetObjectProperties(new TestObject(), jobDataMap);
            Assert.Fail();
        }
        catch (SchedulerException)
        {
        }
    }

    [Test]
    public void TestSetObjectPropsCharStringTooShort()
    {
        JobDataMap jobDataMap = new JobDataMap();
        jobDataMap["charValue"] = "";
        try
        {
            factory.SetObjectProperties(new TestObject(), jobDataMap);
            Assert.Fail();
        }
        catch (SchedulerException)
        {
        }
    }

    [Test]
    public void TestSetObjectPropsCharStringTooLong()
    {
        JobDataMap jobDataMap = new JobDataMap();
        jobDataMap["charValue"] = "abba";
        try
        {
            factory.SetObjectProperties(new TestObject(), jobDataMap);
            Assert.Fail();
        }
        catch (SchedulerException)
        {
        }
    }

    [Test]
    public void TestSetObjectPropsFromStrings()
    {
        JobDataMap jobDataMap = new JobDataMap();
        jobDataMap["intValue"] = "1";
        jobDataMap["longValue"] = "2";
        jobDataMap["floatValue"] = "3.0";
        jobDataMap["doubleValue"] = "4.0";
        jobDataMap["booleanValue"] = "true";
        jobDataMap["shortValue"] = "5";
        jobDataMap["charValue"] = "a";
        jobDataMap["byteValue"] = "6";

        TestObject myObject = new TestObject();
        factory.SetObjectProperties(myObject, jobDataMap);

        Assert.Multiple(() =>
        {
            Assert.That(myObject.IntValue, Is.EqualTo(1));
            Assert.That(myObject.LongValue, Is.EqualTo(2L));
            Assert.That(myObject.FloatValue, Is.EqualTo(3.0f));
            Assert.That(myObject.DoubleValue, Is.EqualTo(4.0));
            Assert.That(myObject.BooleanValue, Is.EqualTo(true));
            Assert.That(myObject.ShortValue, Is.EqualTo(5));
            Assert.That(myObject.CharValue, Is.EqualTo('a'));
            Assert.That(myObject.ByteValue, Is.EqualTo((byte) 6));
        });
    }

    internal sealed class TestObject
    {
        public bool BooleanValue { get; set; }

        public double DoubleValue { set; get; }

        public float FloatValue { set; get; }

        public int IntValue { set; get; }

        public long LongValue { set; get; }

        public Dictionary<string, string> MapValue { set; get; }

        public string StringValue { set; get; }

        public byte ByteValue { set; get; }

        public char CharValue { set; get; }

        public short ShortValue { set; get; }

        public DayOfWeek EnumValue1 { get; set; }

        public DayOfWeek EnumValue2 { get; set; }
        public DayOfWeek EnumValue3 { get; set; }
    }
}