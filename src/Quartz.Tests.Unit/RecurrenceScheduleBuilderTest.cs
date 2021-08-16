using System;
using System.Collections.Generic;
using NUnit.Framework;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Unit tests for <see cref="RecurrenceScheduleBuilder"/>
    /// </summary>
    /// <author>Tommaso Peduzzi</author>
    [TestFixture]
    public class RecurrenceScheduleBuilderTest
    {
        [Test]
        public void TestWeekly()
        {
            var weekDays = new List<DayOfWeek>();
            weekDays.Add(DayOfWeek.Monday);
            weekDays.Add(DayOfWeek.Wednesday);
            var trigger = TriggerBuilder.Create().WithIdentity("test")
                .WithSchedule(RecurrenceScheduleBuilder.WeeklyAtDays(weekDays.ToArray(), 1).WithMaximumOccurrences(5))
                .StartAt(new DateTime(2021, 8, 14, 12,0,0))
                .Build();
            var instance = ((RecurrenceTriggerImpl) trigger).ComputeFirstFireTimeUtc(null);
            var expectedTime = new DateTimeOffset(2021, 8, 16, 12, 0, 0, new TimeSpan(0, +2, 0, 0));
            Assert.AreEqual(expectedTime.DateTime, instance.Value!.DateTime);
            expectedTime = new DateTimeOffset(2021, 8, 18, 12, 0, 0, new TimeSpan(0, +2, 0, 0));
            Assert.AreEqual(expectedTime, trigger.GetFireTimeAfter(instance));
            Assert.AreEqual(new DateTime(2021, 8, 30, 12, 0, 0), ((RecurrenceTriggerImpl)trigger).Recurrence!.RecurUntil);
        }

        [Test]
        public void TestDaily()
        {
            var trigger = TriggerBuilder.Create()
                .WithSchedule(
                    RecurrenceScheduleBuilder.Daily(2)).StartAt(DateTimeOffset.Now).Build();
            var instances = ((RecurrenceTriggerImpl) trigger).Recurrence!.AllInstances();
            Assert.AreEqual(2, instances[1].Day-instances[0].Day);
        } 
    }
}