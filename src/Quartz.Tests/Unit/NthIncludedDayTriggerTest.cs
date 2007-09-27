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

#if NET_20
using NullableDateTime = System.Nullable<System.DateTime>;
#else
using Nullables;
#endif

using NUnit.Framework;

namespace Quartz.Tests.Unit
{

	/// <summary>
	/// Unit test for NthIncludedDayTrigger serialization backwards compatibility.
	/// </summary>
	public class NthIncludedDayTriggerTest : SerializationTestSupport 
	{
    
		private static string[] VERSIONS = new string[] {"1.5.2"};
    
		[Test]
		public void TestGetFireTimeAfter()
		{
			DateTime startCalendar = new DateTime(2005, 6, 1, 9, 30, 17);

			// Test yearly
			NthIncludedDayTrigger yearlyTrigger = new NthIncludedDayTrigger();
			yearlyTrigger.IntervalType = NthIncludedDayTrigger.INTERVAL_TYPE_YEARLY;
			yearlyTrigger.StartTimeUtc = startCalendar;
			yearlyTrigger.N = 10;
			yearlyTrigger.FireAtTime = "14:35:15";
        
			DateTime targetCalendar = new DateTime(2006, 1, 10, 14, 35, 15).ToUniversalTime();
            NullableDateTime nextFireTime;

            nextFireTime = yearlyTrigger.GetFireTimeAfter(startCalendar.AddMilliseconds(1000));
			Assert.AreEqual(targetCalendar, nextFireTime.Value);
        
			// Test monthly
			NthIncludedDayTrigger monthlyTrigger = new NthIncludedDayTrigger();
			monthlyTrigger.IntervalType = NthIncludedDayTrigger.INTERVAL_TYPE_MONTHLY;
			monthlyTrigger.StartTimeUtc = startCalendar;
			monthlyTrigger.N = 5;
			monthlyTrigger.FireAtTime = "14:35:15";
        
			targetCalendar = new DateTime(2005, 6, 5, 14, 35, 15).ToUniversalTime();
			nextFireTime = monthlyTrigger.GetFireTimeAfter(startCalendar.AddMilliseconds(1000));
			Assert.AreEqual(targetCalendar, nextFireTime.Value);
        
			// Test weekly
			NthIncludedDayTrigger weeklyTrigger = new NthIncludedDayTrigger();
			weeklyTrigger.IntervalType = NthIncludedDayTrigger.INTERVAL_TYPE_WEEKLY;
			weeklyTrigger.StartTimeUtc = startCalendar;
			weeklyTrigger.N = 3;
			weeklyTrigger.FireAtTime = "14:35:15";

			targetCalendar = new DateTime(2005, 6, 7, 14, 35, 15).ToUniversalTime();
			nextFireTime = weeklyTrigger.GetFireTimeAfter(startCalendar.AddMilliseconds(1000));
			Assert.AreEqual(targetCalendar, nextFireTime.Value);
		}
    
		[Test]
		public void TestSetGetFireAtTime() 
		{
			NthIncludedDayTrigger trigger = new NthIncludedDayTrigger();
        
			// Make sure a bad fire at time doesn't reset fire time
			trigger.FireAtTime = "14:30:10";
			try 
			{
				trigger.FireAtTime = "blah";
				Assert.Fail();
			} 
			catch (ArgumentException) 
			{
			}
			Assert.AreEqual("14:30:10", trigger.FireAtTime);
        
			trigger.FireAtTime = "4:03:15";
			Assert.AreEqual("04:03:15", trigger.FireAtTime);
        
			try 
			{
				trigger.FireAtTime = "4:3";
				Assert.Fail();
			} 
			catch (ArgumentException) 
			{
			}
        
			try 
			{
				trigger.FireAtTime = ("4:3:15");
				Assert.Fail();
			} 
			catch (ArgumentException) 
			{
			}
        
			trigger.FireAtTime = ("23:17");
			Assert.AreEqual("23:17:00", trigger.FireAtTime);
        
			try 
			{
				trigger.FireAtTime = ("24:3:15");
				Assert.Fail();
			} 
			catch (ArgumentException) 
			{
			}

			try 
			{
				trigger.FireAtTime = ("-1:3:15");
				Assert.Fail();
			} 
			catch (ArgumentException) 
			{
			}

			try 
			{
				trigger.FireAtTime = ("23:60:15");
				Assert.Fail();
			} 
			catch (ArgumentException) 
			{
			}

			try 
			{
				trigger.FireAtTime = ("23:-1:15");
				Assert.Fail();
			} 
			catch (ArgumentException) 
			{
			}

			try 
			{
				trigger.FireAtTime = ("23:17:60");
				Assert.Fail();
			} 
			catch (ArgumentException) 
			{
			}
        
			try 
			{
				trigger.FireAtTime = ("23:17:-1");
				Assert.Fail();
			} 
			catch (ArgumentException) 
			{
			}
		}
    
		/*
		public void testTimeZone() throws Exception {
        
			TimeZone GMT = TimeZone.getTimeZone("GMT-0:00");
			TimeZone EST = TimeZone.getTimeZone("GMT-5:00");
        
			Calendar startTime = Calendar.getInstance(EST);
			startTime.set(2006, Calendar.MARCH, 7, 7, 0, 0);
        
			// Same timezone, so should just get back 8:00 that day
			{
				NthIncludedDayTrigger t = new NthIncludedDayTrigger("name", "group");
				t.setIntervalType(NthIncludedDayTrigger.INTERVAL_TYPE_WEEKLY);
				t.setN(3);
				t.setStartTime(startTime.getTime());
				t.setFireAtTime("8:00");
				t.setTimeZone(EST);
            
				Date firstTime = t.computeFirstFireTime(null);
				Calendar firstTimeCal = Calendar.getInstance(EST);
				firstTimeCal.setTime(firstTime);
				assertEquals(7, firstTimeCal.get(Calendar.DATE));
			}

			// Timezone is 5 hours later, so should just get back 8:00 a week later
			{
				NthIncludedDayTrigger t = new NthIncludedDayTrigger("name", "group");
				t.setIntervalType(NthIncludedDayTrigger.INTERVAL_TYPE_WEEKLY);
				t.setN(3);
				t.setStartTime(startTime.getTime());
				t.setFireAtTime("8:00");
				t.setTimeZone(GMT);
            
				Date firstTime = t.computeFirstFireTime(null);
				Calendar firstTimeCal = Calendar.getInstance(EST);
				firstTimeCal.setTime(firstTime);
				assertEquals(14, firstTimeCal.get(Calendar.DATE));
			}
		}
		*/
    
    
		/**
		 * Get the object to serialize when generating serialized file for future
		 * tests, and against which to validate deserialized object.
		 */
		protected override object GetTargetObject() 
		{
			DateTime startTime = new DateTime(2005, 6, 1, 11, 30, 0);
        
			NthIncludedDayTrigger t = new NthIncludedDayTrigger("name", "group");
			t.IntervalType = (NthIncludedDayTrigger.INTERVAL_TYPE_MONTHLY);
			t.N = 3;
			t.StartTimeUtc = (startTime);
			t.FireAtTime = ("12:15");
			t.NextFireCutoffInterval = (13);
        
			return t;
		}
    
		/**
		 * Get the Quartz versions for which we should verify
		 * serialization backwards compatibility.
		 */
		protected override string[] GetVersions() 
		{
			return VERSIONS;
		}
    
		/**
		 * Verify that the target object and the object we just deserialized 
		 * match.
		 */
		protected override void VerifyMatch(object target, object deserialized) 
		{
			NthIncludedDayTrigger targetTrigger = (NthIncludedDayTrigger)target;
			NthIncludedDayTrigger deserializedTrigger = (NthIncludedDayTrigger)deserialized;
        
			Assert.IsNotNull(deserializedTrigger);
			Assert.AreEqual(targetTrigger.Name, deserializedTrigger.Name);
			Assert.AreEqual(targetTrigger.Group, deserializedTrigger.Group);
			Assert.AreEqual(targetTrigger.IntervalType, deserializedTrigger.IntervalType);
			Assert.AreEqual(targetTrigger.N, deserializedTrigger.N);
			Assert.AreEqual(targetTrigger.StartTimeUtc, deserializedTrigger.StartTimeUtc);
			Assert.IsNull(targetTrigger.EndTimeUtc);
			Assert.AreEqual(targetTrigger.FireAtTime, deserializedTrigger.FireAtTime);
			Assert.AreEqual(targetTrigger.NextFireCutoffInterval, deserializedTrigger.NextFireCutoffInterval);
			// Assert.AreEqual(TimeZone.getDefault(), deserializedTrigger.getTimeZone());
		}
	}
}