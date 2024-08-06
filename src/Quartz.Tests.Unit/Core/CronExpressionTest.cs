using NUnit.Framework;

namespace Quartz.Tests.Unit.Core;

[TestFixture]
public class CronExpressionTest
{
    private static readonly TimeZoneInfo testTimeZone = TimeZoneInfo.Local;

    [TestCaseSource(typeof(ExpressionGenerator), nameof(ExpressionGenerator.Expressions))]
    public void TestDayOfWeekSet((string expression, ICollection<int> set) test)
    {
        var cronExpression = new CronExpression(test.expression);
        cronExpression.TimeZone = testTimeZone;

        var set = cronExpression.GetSet(CronExpressionConstants.DayOfWeek);

        Assert.That(set, Is.Not.Null);
        Assert.IsNotEmpty(set);
        Assert.That(set, Is.EquivalentTo(test.set));
    }

    [TestCaseSource(typeof(ExpressionGenerator), nameof(ExpressionGenerator.Expressions))]
    public void NextValidSatisfiesItself((string expression, ICollection<int> set) test)
    {
        var cronExpression = new CronExpression(test.expression);
        cronExpression.TimeZone = testTimeZone;

        var now = DateTimeOffset.UtcNow;

        var next = cronExpression.GetNextValidTimeAfter(now);

        Assert.That(next, Is.Not.Null);
        Assert.That(() => next != default);

        var set = cronExpression.GetSet(CronExpressionConstants.DayOfWeek);
        if (set.Contains(CronExpressionConstants.AllSpec)) set = [1, 2, 3, 4, 5, 6, 7];
        var nextDow = CronExpression.CronWeekDayOf(next.Value);
                
        Assert.That(() => cronExpression.IsSatisfiedBy(next.Value));
        Assert.That(() => set.Contains(nextDow));
        Assert.That(() => (next.Value - now).TotalDays <= 7);
        Assert.That(() => next.Value > now);
    }

    [TestCaseSource(typeof(ExpressionGenerator), nameof(ExpressionGenerator.Expressions))]
    public void NextValidIsPatternWeekDay((string expression, ICollection<int> set) test)
    {
        var cronExpression = new CronExpression(test.expression);
        cronExpression.TimeZone = testTimeZone;

        var next = cronExpression.GetNextValidTimeAfter(DateTimeOffset.UtcNow);
        var dayOfWeek = CronExpression.CronWeekDayOf(next.Value);

        Assert.That(() => test.set.Contains(CronExpressionConstants.AllSpec) || test.set.Contains(dayOfWeek));
    }

    [TestCaseSource(typeof(ExpressionGenerator), nameof(ExpressionGenerator.Expressions))]
    public void ValidateSatisfaction((string expression, ICollection<int> set) test)
    {
        var cronExpression = new CronExpression(test.expression);
        cronExpression.TimeZone = testTimeZone;

        var set = cronExpression.GetSet(CronExpressionConstants.DayOfWeek);

        var now = DateTimeOffset.Parse("2024.01.01 09:30");

       if (set.Contains(CronExpressionConstants.AllSpec)) set = [1, 2, 3, 4, 5, 6, 7];

        foreach (var day in Enumerable.Range(1, 7))
        {
            var testDate = now.AddDays(day);
            var testDow = CronExpression.CronWeekDayOf(testDate);

            if (set.Contains(testDow))
            {
                Assert.IsTrue(cronExpression.IsSatisfiedBy(testDate), $"{testDate} matches {test.expression}");
            }
            else
            {
                Assert.IsFalse(cronExpression.IsSatisfiedBy(testDate));
            }                
        }
    }
}

public class ExpressionGenerator
{
    private const string StartSequence = "0 30 09 ? * ";

    public static IEnumerable<(string, ICollection<int>)> Expressions
    {
        get
        {
            yield return (StartSequence + "1", [1]);
            yield return (StartSequence + "2", [2]);
            yield return (StartSequence + "1,2", [1,2]);
            yield return (StartSequence + "1-3", [1,2,3]);
            yield return (StartSequence + "2-5", [2,3,4,5]);
            yield return (StartSequence + "1-7", [1,2,3,4,5,6,7]);
            yield return (StartSequence + "*", [CronExpressionConstants.AllSpec]); 
        }
    }
}
