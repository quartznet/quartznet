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

using NUnit.Framework;

using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Impl.Calendar
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class BaseCalendarTest
    {
        [Test]
        public void TestClone() {
            BaseCalendar baseCalendar = new BaseCalendar();
            baseCalendar.Description = "My description";
            baseCalendar.TimeZone = TimeZoneInfo.GetSystemTimeZones()[3];
            BaseCalendar clone = (BaseCalendar) baseCalendar.Clone();

            Assert.AreEqual(baseCalendar.Description, clone.Description);
            Assert.AreEqual(baseCalendar.CalendarBase, clone.CalendarBase);
            Assert.AreEqual(baseCalendar.TimeZone, clone.TimeZone);
        }

    }
}