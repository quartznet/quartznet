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

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class DaylightSavingTimeTest
    {
        private Func<DateTimeOffset> OriginalUtcNow;

        [TestFixtureSetUp]
        public void Init()
        {
            OriginalUtcNow = SystemTime.UtcNow;
            SystemTime.UtcNow = () => new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }

        [TestFixtureTearDown]
        public void Dispose()
        {
            SystemTime.UtcNow = OriginalUtcNow;
        }

        [Test]
        public void ShouldHandleDstSpringForwardTransition()
        {
            TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger1", "group1")
                .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(2, 30).InTimeZone(tz))
                .ForJob("job1", "group1")
                .Build();

            DateTimeOffset midnight = new DateTimeOffset(2016, 3, 13, 0, 0, 0, TimeSpan.FromHours(-8));
            DateTimeOffset? fireTime = trigger.GetFireTimeAfter(midnight);

            // It should fire at the equivalent valid local time.  2:30-8 does not exist, so it should run at 3:30-7.
            DateTimeOffset expectedTime = new DateTimeOffset(2016, 3, 13, 3, 30, 0, TimeSpan.FromHours(-7));

            // We should definitely have a value
            Assert.NotNull(fireTime);

            // fireTime always is in UTC, but DateTimeOffset comparison normalized to UTC anyway.
            // Conversion here is for clarity of interpreting errors if the test fails.
            DateTimeOffset convertedFireTime = TimeZoneInfo.ConvertTime(fireTime.Value, tz);
            Assert.AreEqual(expectedTime, convertedFireTime);
        }

        [Test]
        public void ShouldHandleDstFallBackTransition()
        {
            TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger1", "group1")
                .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(1, 30).InTimeZone(tz))
                .ForJob("job1", "group1")
                .Build();

            DateTimeOffset midnight = new DateTimeOffset(2016, 11, 6, 0, 0, 0, TimeSpan.FromHours(-7));
            DateTimeOffset? fireTime = trigger.GetFireTimeAfter(midnight);

            // It should fire at the first instance, which is 1:30-7 - the DAYLIGHT time, not the standard time.
            DateTimeOffset expectedTime = new DateTimeOffset(2016, 11, 6, 1, 30, 0, TimeSpan.FromHours(-7));

            // We should definitely have a value
            Assert.NotNull(fireTime);

            // fireTime always is in UTC, but DateTimeOffset comparison normalized to UTC anyway.
            // Conversion here is for clarity of interpreting errors if the test fails.
            DateTimeOffset convertedFireTime = TimeZoneInfo.ConvertTime(fireTime.Value, tz);
            Assert.AreEqual(expectedTime, convertedFireTime);
        }

        [Test]
        public void ShouldHandleDstFallBackTransition_AndNotRunTwiceOnTheSameDay()
        {
            TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger1", "group1")
                .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(1, 30).InTimeZone(tz))
                .ForJob("job1", "group1")
                .Build();

            DateTimeOffset firstRun = new DateTimeOffset(2016, 11, 6, 1, 30, 0, TimeSpan.FromHours(-7));
            DateTimeOffset? fireTime = trigger.GetFireTimeAfter(firstRun);

            // It should not fire again at 1:30-8 on the same day, but should instead fire at 1:30-8 the following day.
            DateTimeOffset expectedTime = new DateTimeOffset(2016, 11, 7, 1, 30, 0, TimeSpan.FromHours(-8));

            // We should definitely have a value
            Assert.NotNull(fireTime);

            // fireTime always is in UTC, but DateTimeOffset comparison normalized to UTC anyway.
            // Conversion here is for clarity of interpreting errors if the test fails.
            DateTimeOffset convertedFireTime = TimeZoneInfo.ConvertTime(fireTime.Value, tz);
            Assert.AreEqual(expectedTime, convertedFireTime);
        }
    }
}
