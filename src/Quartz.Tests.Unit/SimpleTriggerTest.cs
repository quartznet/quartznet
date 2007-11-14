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
using System;
using System.Collections;

#if NET_20
using NullableDateTime = System.Nullable<System.DateTime>;
#else
using Nullables;
#endif

using NUnit.Framework;

using Quartz.Collection;

namespace Quartz.Tests.Unit
{
	/// <summary>
	/// Unit test for SimpleTrigger serialization backwards compatibility. 
	/// </summary>
	public class SimpleTriggerTest : SerializationTestSupport
	{
		private static readonly string[] Versions = new string[] {"0.6"};

		//private static TimeZone EST_TIME_ZONE = TimeZone.CurrentTimeZone; 
		private static readonly DateTime StartTime;
		private static readonly DateTime EndTime;

		static SimpleTriggerTest()
		{
			StartTime = new DateTime(2006, 6, 1, 10, 5, 15);
			// StartTime.setTimeZone(EST_TIME_ZONE);
			EndTime = new DateTime(2008, 5, 2, 20, 15, 30);
			// EndTime.setTimeZone(EST_TIME_ZONE);
		}


		/// <summary>
		/// Get the object to serialize when generating serialized file for future
		/// tests, and against which to validate deserialized object.
		/// </summary>
		/// <returns></returns>
		protected override object GetTargetObject()
		{
			JobDataMap jobDataMap = new JobDataMap();
			jobDataMap.Put("A", "B");

			SimpleTrigger t = new SimpleTrigger("SimpleTrigger", "SimpleGroup",
			                                    "JobName", "JobGroup", StartTime,
			                                    EndTime, 5, 1000);
			t.CalendarName = "MyCalendar";
			t.Description = "SimpleTriggerDesc";
			t.JobDataMap = jobDataMap;
            t.MisfireInstruction = (MisfirePolicy.SimpleTrigger.RescheduleNextWithRemainingCount);
			t.Volatile = true;

			t.AddTriggerListener("L1");
			t.AddTriggerListener("L2");

			return t;
		}


		/// <summary>
		/// Get the Quartz versions for which we should verify
		/// serialization backwards compatibility.
		/// </summary>
		/// <returns></returns>
		protected override String[] GetVersions()
		{
			return Versions;
		}

		/// <summary>
		/// Verify that the target object and the object we just deserialized 
		/// match.
		/// </summary>
		/// <param name="target"></param>
		/// <param name="deserialized"></param>
		protected override void VerifyMatch(Object target, Object deserialized)
		{
			SimpleTrigger targetSimpleTrigger = (SimpleTrigger) target;
			SimpleTrigger deserializedSimpleTrigger = (SimpleTrigger) deserialized;

			Assert.IsNotNull(deserializedSimpleTrigger);
			Assert.AreEqual(targetSimpleTrigger.Name, deserializedSimpleTrigger.Name);
			Assert.AreEqual(targetSimpleTrigger.Group, deserializedSimpleTrigger.Group);
			Assert.AreEqual(targetSimpleTrigger.JobName, deserializedSimpleTrigger.JobName);
			Assert.AreEqual(targetSimpleTrigger.JobGroup, deserializedSimpleTrigger.JobGroup);
			Assert.AreEqual(targetSimpleTrigger.StartTimeUtc, deserializedSimpleTrigger.StartTimeUtc);
			Assert.AreEqual(targetSimpleTrigger.EndTimeUtc, deserializedSimpleTrigger.EndTimeUtc);
			Assert.AreEqual(targetSimpleTrigger.RepeatCount, deserializedSimpleTrigger.RepeatCount);
			Assert.AreEqual(targetSimpleTrigger.RepeatInterval, deserializedSimpleTrigger.RepeatInterval);
			Assert.AreEqual(targetSimpleTrigger.CalendarName, deserializedSimpleTrigger.CalendarName);
			Assert.AreEqual(targetSimpleTrigger.Description, deserializedSimpleTrigger.Description);
			Assert.AreEqual(targetSimpleTrigger.JobDataMap, deserializedSimpleTrigger.JobDataMap);
			Assert.AreEqual(targetSimpleTrigger.MisfireInstruction, deserializedSimpleTrigger.MisfireInstruction);
			Assert.IsTrue(targetSimpleTrigger.Volatile);
			Assert.AreEqual(2, deserializedSimpleTrigger.TriggerListenerNames.Length);
		}

		[Test]
		public void TestUpdateAfterMisfire()
		{
			DateTime startTime = new DateTime(2005, 7, 5, 9, 0, 0);

			DateTime endTime = new DateTime(2005, 7, 5, 10, 0, 0);

			SimpleTrigger simpleTrigger = new SimpleTrigger();
			simpleTrigger.MisfireInstruction = MisfirePolicy.SimpleTrigger.RescheduleNowWithExistingRepeatCount;
			simpleTrigger.RepeatCount = 5;
			simpleTrigger.StartTimeUtc = startTime;
			simpleTrigger.EndTimeUtc = endTime;

			simpleTrigger.UpdateAfterMisfire(null);
			Assert.AreEqual(startTime, simpleTrigger.StartTimeUtc);
			Assert.AreEqual(endTime, simpleTrigger.EndTimeUtc.Value);
			Assert.IsTrue(!simpleTrigger.GetNextFireTimeUtc().HasValue);
		}

		[Test]
		public void TestGetFireTimeAfter()
		{
			SimpleTrigger simpleTrigger = new SimpleTrigger();

			DateTime startTime = TriggerUtils.GetEvenSecondDate(DateTime.Now);

			simpleTrigger.StartTimeUtc = startTime;
			simpleTrigger.RepeatInterval = 10;
			simpleTrigger.RepeatCount = 4;

            NullableDateTime fireTimeAfter;
            fireTimeAfter = simpleTrigger.GetFireTimeAfter(startTime.AddMilliseconds(34));
			Assert.AreEqual(startTime.AddMilliseconds(40), fireTimeAfter.Value);
		}

		[Test]
		public void TestAddTriggerListener()
		{
			string[] listenerNames = new string[] {"X", "A", "B"};

			// Verify that a HashSet shuffles order, so we know that order test
			// below is actually testing something
			HashSet hashSet = new HashSet(listenerNames);
			Assert.IsFalse(new ArrayList(listenerNames).Equals(new ArrayList(hashSet)));

			SimpleTrigger simpleTrigger = new SimpleTrigger();
			for (int i = 0; i < listenerNames.Length; i++)
			{
				simpleTrigger.AddTriggerListener(listenerNames[i]);
			}

			// Make sure order was maintained
			TestUtil.AssertCollectionEquality(new ArrayList(listenerNames),
			                new ArrayList(simpleTrigger.TriggerListenerNames));

			// Make sure uniqueness is enforced
			for (int i = 0; i < listenerNames.Length; i++)
			{
				try
				{
					simpleTrigger.AddTriggerListener(listenerNames[i]);
					Assert.Fail();
				}
				catch (ArgumentException)
				{
				}
			}
		}

		[Test]
		public void TestClone()
		{
			SimpleTrigger simpleTrigger = new SimpleTrigger();

			// Make sure empty sub-objects are cloned okay
			Trigger clone = (Trigger) simpleTrigger.Clone();
			Assert.AreEqual(0, clone.TriggerListenerNames.Length);
			Assert.AreEqual(0, clone.JobDataMap.Count);

			// Make sure non-empty sub-objects are cloned okay
			simpleTrigger.AddTriggerListener("L1");
			simpleTrigger.AddTriggerListener("L2");
			simpleTrigger.JobDataMap.Put("K1", "V1");
			simpleTrigger.JobDataMap.Put("K2", "V2");
			clone = (Trigger) simpleTrigger.Clone();
			Assert.AreEqual(2, clone.TriggerListenerNames.Length);
			TestUtil.AssertCollectionEquality(new ArrayList(new string[] {"L1", "L2"}), new ArrayList(clone.TriggerListenerNames));
			Assert.AreEqual(2, clone.JobDataMap.Count);
			Assert.AreEqual("V1", clone.JobDataMap.Get("K1"));
			Assert.AreEqual("V2", clone.JobDataMap.Get("K2"));

			// Make sure sub-object collections have really been cloned by ensuring 
			// their modification does not change the source Trigger 
			clone.RemoveTriggerListener("L2");
			Assert.AreEqual(1, clone.TriggerListenerNames.Length);
			TestUtil.AssertCollectionEquality(new ArrayList(new string[] {"L1"}), new ArrayList(clone.TriggerListenerNames));
			clone.JobDataMap.Remove("K1");
			Assert.AreEqual(1, clone.JobDataMap.Count);

			Assert.AreEqual(2, simpleTrigger.TriggerListenerNames.Length);
			TestUtil.AssertCollectionEquality(new ArrayList(new string[] {"L1", "L2"}), new ArrayList(simpleTrigger.TriggerListenerNames));
			Assert.AreEqual(2, simpleTrigger.JobDataMap.Count);
			Assert.AreEqual("V1", simpleTrigger.JobDataMap.Get("K1"));
			Assert.AreEqual("V2", simpleTrigger.JobDataMap.Get("K2"));
		}

        [Test]
        public void TestSetTriggerListenerNames()
        {
            SimpleTrigger simpleTrigger = new SimpleTrigger();
            
            simpleTrigger.TriggerListenerNames = null;
            Assert.IsNotNull(simpleTrigger.TriggerListenerNames);
            Assert.IsEmpty(simpleTrigger.TriggerListenerNames);
            
            simpleTrigger.TriggerListenerNames = new string[] { "FOO", "BAR"};
            Assert.AreEqual(2, simpleTrigger.TriggerListenerNames.Length);

            simpleTrigger.TriggerListenerNames = new string[] {"BAZ"};
            Assert.AreEqual(1, simpleTrigger.TriggerListenerNames.Length);

            simpleTrigger.TriggerListenerNames = null;
            Assert.IsNotNull(simpleTrigger.TriggerListenerNames);
            Assert.IsEmpty(simpleTrigger.TriggerListenerNames);
            
        }
	}
}
