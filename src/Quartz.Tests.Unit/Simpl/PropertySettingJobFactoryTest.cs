#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

using System.Collections.Generic;

using NUnit.Framework;

using Quartz.Simpl;

namespace Quartz.Tests.Unit.Simpl
{
	/// <summary>
	///  Unit test for PropertySettingJobFactory.
	/// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
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
		public void TestSetObjectPropsPrimitives()
		{
			JobDataMap jobDataMap = new JobDataMap();
			jobDataMap.Put("intValue", 1);
			jobDataMap.Put("longValue", 2L);
			jobDataMap.Put("floatValue", 3.0f);
			jobDataMap.Put("doubleValue", 4.0);
			jobDataMap.Put("booleanValue", true);
			jobDataMap.Put("shortValue", 5);
			jobDataMap.Put("charValue", 'a');
			jobDataMap.Put("byteValue", 6);
			jobDataMap.Put("stringValue", "S1");
            Dictionary<string, string> map = new Dictionary<string, string>();
			map.Add("A", "B");
			jobDataMap.Put("mapValue", map);

			TestObject myObject = new TestObject();
			factory.SetObjectProperties(myObject, jobDataMap);

			Assert.AreEqual(1, myObject.IntValue);
			Assert.AreEqual(2, myObject.LongValue);
			Assert.AreEqual(3.0f, myObject.FloatValue);
			Assert.AreEqual(4.0, myObject.DoubleValue);
			Assert.IsTrue(myObject.BooleanValue);
			Assert.AreEqual(5, myObject.ShortValue);
			Assert.AreEqual('a', myObject.CharValue);
			Assert.AreEqual((byte) 6, myObject.ByteValue);
			Assert.AreEqual("S1", myObject.StringValue);
			Assert.IsTrue(myObject.MapValue.ContainsKey("A"));
		}

		[Test]
		public void TestSetObjectPropsUnknownProperty()
		{
			JobDataMap jobDataMap = new JobDataMap();
			jobDataMap.Put("bogusValue", 1);
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
			jobDataMap.Put("intValue", null);
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
			jobDataMap.Put("mapValue", null);
			TestObject testObject = new TestObject();
            Dictionary<string, string> map = new Dictionary<string, string>();
			map.Add("A", "B");
			testObject.MapValue = map;
			factory.SetObjectProperties(testObject, jobDataMap);
			Assert.IsNull(testObject.MapValue);
		}

		[Test]
		public void TestSetObjectPropsWrongPrimitiveType()
		{
			JobDataMap jobDataMap = new JobDataMap();
			jobDataMap.Put("intValue", "myvalue");
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
			jobDataMap.Put("mapValue", 7.2f);
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
			jobDataMap.Put("charValue", "");
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
			jobDataMap.Put("charValue", "abba");
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
			jobDataMap.Put("intValue", "1");
			jobDataMap.Put("longValue", "2");
			jobDataMap.Put("floatValue", "3.0");
			jobDataMap.Put("doubleValue", "4.0");
			jobDataMap.Put("booleanValue", "true");
			jobDataMap.Put("shortValue", "5");
			jobDataMap.Put("charValue", "a");
			jobDataMap.Put("byteValue", "6");

			TestObject myObject = new TestObject();
			factory.SetObjectProperties(myObject, jobDataMap);

			Assert.AreEqual(1, myObject.IntValue);
			Assert.AreEqual(2L, myObject.LongValue);
			Assert.AreEqual(3.0f, myObject.FloatValue);
			Assert.AreEqual(4.0, myObject.DoubleValue);
			Assert.AreEqual(true, myObject.BooleanValue);
			Assert.AreEqual(5, myObject.ShortValue);
			Assert.AreEqual('a', myObject.CharValue);
			Assert.AreEqual((byte) 6, myObject.ByteValue);
		}

		internal class TestObject
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
		}
	}
}