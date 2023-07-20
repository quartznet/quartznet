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
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Impl.Calendar;

/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(BinaryObjectSerializer))]
[TestFixture(typeof(JsonObjectSerializer))]
public class CronCalendarTest : SerializationTestSupport<CronCalendar, ICalendar>
{
    public CronCalendarTest(Type serializerType) : base(serializerType)
    {
    }

    [Test]
    public void TestTimeIncluded()
    {
        CronCalendar calendar = new CronCalendar("0/15 * * * * ?");
        string fault = "Time was included when it was not supposed to be";
        DateTime tst = DateTime.UtcNow.AddMinutes(2);
        tst = new DateTime(tst.Year, tst.Month, tst.Day, tst.Hour, tst.Minute, 30);
        Assert.IsFalse(calendar.IsTimeIncluded(tst), fault);

        calendar.SetCronExpressionString("0/25 * * * * ?");
        fault = "Time was not included as expected";
        Assert.IsTrue(calendar.IsTimeIncluded(tst), fault);
    }

    [Test]
    public void TestClone()
    {
        CronCalendar calendar = new CronCalendar("0/15 * * * * ?");
        CronCalendar clone = (CronCalendar) calendar.Clone();
        Assert.AreEqual(calendar.CronExpression, clone.CronExpression);
    }

    [Test]
    public void MillisecondsShouldBeIgnored()
    {
        var calendar = new CronCalendar("* * 1-3 ? * *")
        {
            TimeZone = TimeZoneInfo.Utc
        };
        var dateTime = new DateTimeOffset(2017, 7, 27, 2, 0, 1, 123, TimeSpan.Zero);
        Assert.That(calendar.IsTimeIncluded(dateTime), Is.False);
    }

    protected override CronCalendar GetTargetObject()
    {
        return new CronCalendar("* * 1-3 ? * *")
        {
            Description = "my description"
        };
    }

    protected override void VerifyMatch(CronCalendar original, CronCalendar deserialized)
    {
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.Description, deserialized.Description);
        Assert.AreEqual(original.CronExpression, deserialized.CronExpression);
        Assert.AreEqual(original.TimeZone, deserialized.TimeZone);
    }
}