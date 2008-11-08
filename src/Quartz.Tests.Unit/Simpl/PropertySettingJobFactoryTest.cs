/* 
 * Copyright 2004-2006 OpenSymphony 
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
 */
using System.Collections.Generic;

using NUnit.Framework;

using Quartz.Simpl;

namespace Quartz.Tests.Unit.Simpl
{
	/// <summary>
	///  Unit test for PropertySettingJobFactory.
	/// </summary>
	[TestFixture]
	public class PropertySettingJobFactoryTest
	{
		private PropertySettingJobFactory factory;

		[SetUp]
		protected void SetUp()
		{
			factory = new PropertySettingJobFactory();
			factory.ThrowIfPropertyNotFound = true;
		}

		[Test]
		public void TestSetObjectPropsPrimatives()
		{
			JobDataMap jobDataMap = new JobDataMap();
			jobDataMap.Put("intValue", 1);
			jobDataMap.Put("longValue", 2L);
			jobDataMap.Put("floatValue", 3.0f);
			jobDataMap.Put("doubleValue", 4.0);
			jobDataMap.Put("booleanValue", true);
			jobDataMap.Put("shortValue", (short) 5);
			jobDataMap.Put("charValue", 'a');
			jobDataMap.Put("byteValue", (byte) 6);
			jobDataMap.Put("stringValue", "S1");
            Dictionary<string, string> map = new Dictionary<string, string>();
			map.Add("A", "B");
			jobDataMap.Put("mapValue", map);

			TestObject myObject = new TestObject();
			factory.SetObjectProperties(myObject, jobDataMap);

			Assert.AreEqual(1, myObject.IntValue);
			Assert.AreEqual(2, myObject.LongValue);
			Assert.AreEqual(3.0f, myObject.FloatValue, 0.0001);
			Assert.AreEqual(4.0, myObject.DoubleValue, 0.0001);
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
		public void TestSetObjectPropsNullPrimative()
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
		public void TestSetObjectPropsNullNonPrimative()
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
		public void TestSetObjectPropsWrongPrimativeType()
		{
			JobDataMap jobDataMap = new JobDataMap();
			jobDataMap.Put("intValue", (float) 7);
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
		public void TestSetObjectPropsWrongNonPrimativeType()
		{
			JobDataMap jobDataMap = new JobDataMap();
			jobDataMap.Put("mapValue", (float) 7);
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
			Assert.AreEqual(3.0f, myObject.FloatValue, 0.0001);
			Assert.AreEqual(4.0, myObject.DoubleValue, 0.0001);
			Assert.AreEqual(true, myObject.BooleanValue);
			Assert.AreEqual(5, myObject.ShortValue);
			Assert.AreEqual('a', myObject.CharValue);
			Assert.AreEqual((byte) 6, myObject.ByteValue);
		}

		private class TestObject
		{
			private int intValue;
			private long longValue;
			private float floatValue;
			private double doubleValue;
			private bool booleanValue;
			private byte byteValue;
			private short shortValue;
			private char charValue;
			private string stringValue;
            private Dictionary<string, string> mapValue;

			public bool BooleanValue
			{
				set { booleanValue = value; }
				get { return booleanValue; }
			}

			public double DoubleValue
			{
				set { doubleValue = value; }
				get { return doubleValue; }
			}

			public float FloatValue
			{
				set { floatValue = value; }
				get { return floatValue; }
			}

			public int IntValue
			{
				set { intValue = value; }
				get { return intValue; }
			}

			public long LongValue
			{
				set { longValue = value; }
				get { return longValue; }
			}

            public Dictionary<string, string> MapValue
			{
				set { mapValue = value; }
				get { return mapValue; }
			}

			public string StringValue
			{
				set { stringValue = value; }
				get { return stringValue; }
			}

			public byte ByteValue
			{
				set { byteValue = value; }
				get { return byteValue; }
			}

			public char CharValue
			{
				set { charValue = value; }
				get { return charValue; }
			}

			public short ShortValue
			{
				set { shortValue = value; }
				get { return shortValue; }
			}
		}
	}
}