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

using System;
using System.Collections.Specialized;

using NUnit.Framework;

using Quartz.Util;

namespace Quartz.Tests.Unit.Utils
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class ObjectUtilsTest
    {
        [Test]
        public void TestNullObjectForValueType()
        {
            try
            {
                ObjectUtils.ConvertValueIfNecessary(typeof (int), null);
                Assert.Fail("Accepted null");
            }
            catch
            {
                // ok
            }
        }

        [Test]
        public void TestNotConvertableData()
        {
            try
            {
                ObjectUtils.ConvertValueIfNecessary(typeof(int), new DirtyFlagMap<int, string>());
                Assert.Fail("Accepted null");
            }
            catch
            {
                // ok
            }
        }

        [Test]
        public void TestTimeSpanConversion()
        {
            TimeSpan ts = (TimeSpan) ObjectUtils.ConvertValueIfNecessary(typeof (TimeSpan), "1");
            Assert.AreEqual(1, ts.TotalDays);
            
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

        public class TimeSpanPropertyTest
        {
            private TimeSpan timeMinutes;
            private TimeSpan timeSeconds;
            private TimeSpan timeMilliseconds;
            private TimeSpan timeHours;
            private TimeSpan timeDefault;

            [TimeSpanParseRule(TimeSpanParseRule.Hours)]
            public TimeSpan TimeHours
            {
                get { return timeHours; }
                set { timeHours = value; }
            }

            [TimeSpanParseRule(TimeSpanParseRule.Minutes)]
            public TimeSpan TimeMinutes
            {
                get { return timeMinutes; }
                set { timeMinutes = value; }
            }

            [TimeSpanParseRule(TimeSpanParseRule.Seconds)]
            public TimeSpan TimeSeconds
            {
                get { return timeSeconds; }
                set { timeSeconds = value; }
            }

            [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
            public TimeSpan TimeMilliseconds
            {
                get { return timeMilliseconds; }
                set { timeMilliseconds = value; }
            }

            public TimeSpan TimeDefault
            {
                get { return timeDefault; }
                set { timeDefault = value; }
            }
        }
    }
}
