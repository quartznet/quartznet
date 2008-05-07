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
using System.Globalization;

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
		private static readonly string[] Versions = new string[] {"0.6"};
    
		[Test]
		public void TestGetFireTimeAfter()
		{
			DateTime startCalendar = new DateTime(2005, 6, 1, 9, 30, 17);

			// Test yearly
			NthIncludedDayTrigger yearlyTrigger = new NthIncludedDayTrigger();
			yearlyTrigger.IntervalType = NthIncludedDayTrigger.IntervalTypeYearly;
			yearlyTrigger.StartTimeUtc = startCalendar;
			yearlyTrigger.N = 10;
			yearlyTrigger.FireAtTime = "14:35:15";
        
			DateTime targetCalendar = new DateTime(2006, 1, 10, 14, 35, 15).ToUniversalTime();
            NullableDateTime nextFireTimeUtc;

            nextFireTimeUtc = yearlyTrigger.GetFireTimeAfter(startCalendar.AddMilliseconds(1000));
			Assert.AreEqual(targetCalendar, nextFireTimeUtc.Value);
        
			// Test monthly
			NthIncludedDayTrigger monthlyTrigger = new NthIncludedDayTrigger();
			monthlyTrigger.IntervalType = NthIncludedDayTrigger.IntervalTypeMonthly;
			monthlyTrigger.StartTimeUtc = startCalendar;
			monthlyTrigger.N = 5;
			monthlyTrigger.FireAtTime = "14:35:15";
        
			targetCalendar = new DateTime(2005, 6, 5, 14, 35, 15).ToUniversalTime();
			nextFireTimeUtc = monthlyTrigger.GetFireTimeAfter(startCalendar.AddMilliseconds(1000));
			Assert.AreEqual(targetCalendar, nextFireTimeUtc.Value);
        
			// Test weekly
			NthIncludedDayTrigger weeklyTrigger = new NthIncludedDayTrigger();
			weeklyTrigger.IntervalType = NthIncludedDayTrigger.IntervalTypeWeekly;
			weeklyTrigger.StartTimeUtc = startCalendar;
			weeklyTrigger.N = 3;
			weeklyTrigger.FireAtTime = "14:35:15";

            //roll start date forward to first day of the next week
            while (startCalendar.DayOfWeek != DateTimeFormatInfo.CurrentInfo.FirstDayOfWeek)
            {
                startCalendar = startCalendar.AddDays(1);
            }

            //calculate expected fire date
		    targetCalendar = new DateTime(startCalendar.Year, startCalendar.Month, startCalendar.Day, 14, 35, 15);
            
            //first day of the week counts as one. add two more to get N=3.
            targetCalendar = targetCalendar.AddDays(2);

			nextFireTimeUtc = weeklyTrigger.GetFireTimeAfter(startCalendar.AddMilliseconds(1000));
			Assert.AreEqual(targetCalendar.ToUniversalTime(), nextFireTimeUtc.Value);
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

        /* TODO, when we have 3.5 TimeZones..
        public void testTimeZone() throws Exception 
        {
            TimeZone GMT = TimeZone.getTimeZone("GMT-0:00");
            TimeZone EST = TimeZone.getTimeZone("GMT-5:00");
            
            Calendar startTime = Calendar.getInstance(EST);
            startTime.set(2006, Calendar.MARCH, 7, 7, 0, 0);
            
            // Same timezone
            {
                NthIncludedDayTrigger t = new NthIncludedDayTrigger("name", "group");
                t.setIntervalType(NthIncludedDayTrigger.INTERVAL_TYPE_WEEKLY);
                t.setN(3);
                t.setStartTime(startTime.getTime());
                t.setFireAtTime("8:00");
                t.setTimeZone(EST);
                
                Date firstTime = t.computeFirstFireTime(null);
                Calendar firstTimeCal = Calendar.getInstance(EST);
                firstTimeCal.setTime(startTime.getTime());
                firstTimeCal.set(Calendar.HOUR_OF_DAY, 8);
                firstTimeCal.set(Calendar.MINUTE, 0);
                firstTimeCal.set(Calendar.SECOND, 0);
                firstTimeCal.set(Calendar.MILLISECOND, 0);
                
                //roll start date forward to first day of the next week
                while (firstTimeCal.get(Calendar.DAY_OF_WEEK) != firstTimeCal.getFirstDayOfWeek()) {
                    firstTimeCal.add(Calendar.DAY_OF_YEAR, -1);
                }
                
                //first day of the week counts as one. add two more to get N=3.
                firstTimeCal.add(Calendar.DAY_OF_WEEK, 2);
                
                //if we went back too far, shift forward a week.
                if (firstTimeCal.getTime().before(startTime.getTime())) {
                    firstTimeCal.add(Calendar.DAY_OF_MONTH, 7);
                }

                assertTrue(firstTime.equals(firstTimeCal.getTime()));
            }

            // Different timezones
            {
                NthIncludedDayTrigger t = new NthIncludedDayTrigger("name", "group");
                t.setIntervalType(NthIncludedDayTrigger.INTERVAL_TYPE_WEEKLY);
                t.setN(3);
                t.setStartTime(startTime.getTime());
                t.setFireAtTime("8:00");
                t.setTimeZone(GMT);
                
                Date firstTime = t.computeFirstFireTime(null);
                Calendar firstTimeCal = Calendar.getInstance(EST);
                firstTimeCal.setTime(startTime.getTime());
                firstTimeCal.set(Calendar.HOUR_OF_DAY, 8);
                firstTimeCal.set(Calendar.MINUTE, 0);
                firstTimeCal.set(Calendar.SECOND, 0);
                firstTimeCal.set(Calendar.MILLISECOND, 0);
                
                //EST is GMT-5
                firstTimeCal.add(Calendar.HOUR_OF_DAY, -5);
                
                //roll start date forward to first day of the next week
                while (firstTimeCal.get(Calendar.DAY_OF_WEEK) != firstTimeCal.getFirstDayOfWeek()) {
                    firstTimeCal.add(Calendar.DAY_OF_YEAR, -1);
                }
                
                //first day of the week counts as one. add two more to get N=3.
                firstTimeCal.add(Calendar.DAY_OF_WEEK, 2);
                
                //if we went back too far, shift forward a week.
                if (firstTimeCal.getTime().before(startTime.getTime())) {
                    firstTimeCal.add(Calendar.DAY_OF_MONTH, 7);
                }

                assertTrue(firstTime.equals(firstTimeCal.getTime()));
            }
        }
        */


        /// <summary>
        /// Get the object to serialize when generating serialized file for future
        /// tests, and against which to validate deserialized object.
        /// </summary>
        /// <returns></returns>
		protected override object GetTargetObject() 
		{
			DateTime startTime = new DateTime(2005, 6, 1, 11, 30, 0);
        
			NthIncludedDayTrigger t = new NthIncludedDayTrigger("name", "group");
			t.IntervalType = (NthIncludedDayTrigger.IntervalTypeMonthly);
			t.N = 3;
			t.StartTimeUtc = (startTime);
			t.FireAtTime = ("12:15");
			t.NextFireCutoffInterval = (13);
        
			return t;
		}


        /// <summary>
        /// Get the Quartz versions for which we should verify
        /// serialization backwards compatibility.
        /// </summary>
        /// <returns></returns>
		protected override string[] GetVersions() 
		{
			return Versions;
		}


        /// <summary>
        /// Verify that the target object and the object we just deserialized
        /// match.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="deserialized"></param>
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