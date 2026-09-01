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

#nullable enable

namespace Quartz.Tests.Unit;

/// <summary>
/// The five-field Unix crontab dialect, which <see cref="CronFormat.Unix" /> reads, and the
/// <c>@</c> macros, which need no dialect at all.
/// </summary>
/// <remarks>
/// The dialect is a reading of a string and nothing more: every expression here is rewritten into
/// Quartz's own six-field form before the parser sees it, which is why these tests assert on
/// <see cref="CronExpression.CronExpressionString" /> as much as on fire times. Nothing downstream
/// of the parser - the evaluator, the serializers, the dashboard, the database - knows a format
/// was ever chosen.
/// </remarks>
[TestFixture]
public class UnixCronFormatTest
{
    #region The rewrite

    [TestCase("30 4 * * 1", "0 30 4 ? * MON", "crontab's day-of-week 1 is Monday, where Quartz's 1 is Sunday")]
    [TestCase("0 12 1 * *", "0 0 12 1 * ?", "a wildcard day-of-week is spelled '?' so the other day field reads as the one that decides")]
    [TestCase("* * * * *", "0 * * * * ?", "every minute of every day")]
    [TestCase("0 0 * * 0-6", "0 0 0 * * ?", "SUN through SAT is the whole week, which renumbering would otherwise collapse")]
    [TestCase("0 0 * * 0-7", "0 0 0 * * ?", "crontab has two Sundays, 0 and 7, so 0-7 is also the whole week")]
    [TestCase("0 0 * * 1-7", "0 0 0 * * ?", "MON through SUN is seven days, so it too is the whole week")]
    [TestCase("0 0 * * 5-1", "0 0 0 ? * FRI-MON", "a range may wrap the end of the week")]
    [TestCase("0 0 * * 1/2", "0 0 0 ? * 2/2", "a step start renumbers as a number, never as a name - Quartz rejects 'MON/2' outright, so a name here would make the rewrite emit an expression its own parser refuses")]
    [TestCase("15 10 * * 1-5", "0 15 10 ? * MON-FRI", "the crontab weekday range everybody writes")]
    [TestCase("0 0 * * MON-FRI", "0 0 0 ? * MON-FRI", "names mean the same day in both dialects, so they pass through")]
    [TestCase("0 0 * * 0", "0 0 0 ? * SUN", "crontab's 0 is Sunday")]
    [TestCase("0 0 * * 7", "0 0 0 ? * SUN", "and so is crontab's 7")]
    [TestCase("0 0 * * 6", "0 0 0 ? * SAT", "crontab's 6 is Saturday, which is Quartz's 7")]
    [TestCase("0 0 * * 1,3,5", "0 0 0 ? * MON,WED,FRI", "every member of a list renumbers")]
    [TestCase("0 0 * * */2", "0 0 0 ? * */2", "a wildcard step names no day to renumber and picks the same days either way")]
    [TestCase("0 0 * * 0-6/2", "0 0 0 ? * 1-7/2", "a step through the whole week keeps the phase its first day gave it, so it cannot degenerate to '*/2'")]
    [TestCase("0 0 * * 1-7/2", "0 0 0 ? * 2-1/2", "and when that first day is Monday the range has to wrap to hold seven days")]
    [TestCase("0 0 * * 0-7/3", "0 0 0 ? * 1-7/3", "crontab's second Sunday at the end of the range adds no day a step could land on")]
    [TestCase("0 0 ? * *", "0 0 0 ? * ?", "'?' is a wildcard in both dialects and in both day fields")]
    [TestCase("0 0 * * 1,", "0 0 0 ? * MON,", "a trailing comma leaves an empty token, which is nothing to renumber - the rewrite passes it on for the parser to have its own opinion about, as it would in a Quartz expression")]
    [TestCase("0 0 * * 0L", "0 0 0 ? * 1L", "'L' after a day renumbers with it - the last Sunday of the month")]
    [TestCase("0 0 * * 5#3", "0 0 0 ? * 6#3", "'#' after a day renumbers with it - the third Friday of the month")]
    [TestCase("0 0 L * *", "0 0 0 L * ?", "'L' in day-of-month is Quartz's, and works inside the five-field layout")]
    [TestCase("0 0 15W * *", "0 0 0 15W * ?", "'W' likewise")]
    [TestCase("0 0 * * L", "0 0 0 ? * L", "'L' alone in day-of-week is not a number, so it keeps its Quartz meaning of Saturday")]
    [TestCase("  30   4  *  *  1  ", "0 30 4 ? * MON", "runs of whitespace separate fields, as they do in crontab")]
    [TestCase("30 4 * * mon", "0 30 4 ? * MON", "the expression is upper-cased, as every expression is")]
    public void AUnixExpressionIsRewrittenIntoTheCanonicalQuartzForm(string unix, string canonical, string because)
    {
        CronExpression expression = CronExpression.Parse(unix, CronFormat.Unix);

        expression.CronExpressionString.Should().Be(canonical, because);
    }

    [Test]
    public void BothDayFieldsRestrictedFiresOnTheUnionAsCrontabDoes()
    {
        CronExpression expression = CronExpression.Parse("0 0 13 * 5", CronFormat.Unix);

        expression.CronExpressionString.Should().Be("0 0 0 13 * FRI",
            "both day fields name days, so both are kept and the expression fires on the union - crontab's rule, "
            + "and deliberately not Cronos's, which ANDs the two and would fire only on Friday the 13th");

        DateTimeOffset october2010 = new DateTimeOffset(2010, 10, 1, 0, 0, 0, TimeSpan.Zero);
        List<int> days = FireDays(expression.WithTimeZone(TimeZoneInfo.Utc), october2010, 31);

        days.Should().Equal([1, 8, 13, 15, 22, 29],
            "October 2010's Fridays are the 1st, 8th, 15th, 22nd and 29th, and the 13th joins them by union");
    }

    [Test]
    public void ARewrittenExpressionReParsesAsItself()
    {
        CronExpression unix = CronExpression.Parse("30 4 * * 1", CronFormat.Unix);

        CronExpression reread = CronExpression.Parse(unix.CronExpressionString);

        reread.CronExpressionString.Should().Be(unix.CronExpressionString,
            "the canonical form is what the store writes and reads back, so it must parse with no format");
    }

    [Test]
    public void TheOriginalTextIsNotRecoverable()
    {
        CronExpression unix = CronExpression.Parse("30 4 * * 1", CronFormat.Unix);

        unix.ToString().Should().Be("0 30 4 ? * MON",
            "the format is a way of reading the string, not a property of the expression - there is no column "
            + "for it and nothing downstream would know what to do with one");
    }

    [Test]
    public void AHashTokenSurvivesTheRewriteAndIsSpreadByTheTriggerKey()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("spread-me", "unix")
            .WithSchedule(CronScheduleBuilder.Create("H 3 * * 1", CronFormat.Unix))
            .Build();

        string expression = ((ICronTrigger) trigger).CronExpressionString!;

        expression.Should().MatchRegex(@"^0 \d{1,2} 3 \? \* MON$",
            "the rewrite runs ahead of the parser, so H is still an H when the trigger key resolves it, "
            + "and it is hashed over the Quartz field it has by then landed in");
    }

    [Test]
    public void ADayOfWeekOutsideTheCrontabRangeIsNamedInTheError()
    {
        Action act = () => CronExpression.Parse("0 0 * * 8", CronFormat.Unix);

        act.Should().Throw<FormatException>()
            .WithMessage("*'8' is not a day of the week*")
            .WithMessage("*0-7*", "crontab's range is what the caller was writing against, so that is the range to quote");
    }

    #endregion

    #region Expressions copied from Spring

    // Spring's @Scheduled(cron = ...) is six fields with seconds first, which is Quartz's shape exactly,
    // so a pasted Spring expression parses. Its day-of-week is numbered the Unix way, 0-7 from Sunday,
    // where Quartz numbers 1-7 from Sunday - so every numeric day is read one day early, and nothing
    // says so. These pin what cron-expressions.md warns about; there is no CronFormat.Spring to fix it
    // with, because the two six-field dialects are the same shape and nothing in the string tells them
    // apart.

    [TestCase("0 0 9 * * 1", DayOfWeek.Sunday, DayOfWeek.Monday)]
    [TestCase("0 0 9 * * 5", DayOfWeek.Thursday, DayOfWeek.Friday)]
    [TestCase("0 0 9 * * 6", DayOfWeek.Friday, DayOfWeek.Saturday)]
    public void ASpringExpressionWithANumericDayOfWeekIsReadOneDayEarly(string spring, DayOfWeek quartzFiresOn, DayOfWeek springMeant)
    {
        CronExpression expression = CronExpression.Parse(spring).WithTimeZone(TimeZoneInfo.Utc);

        DateTimeOffset? next = expression.GetNextValidTimeAfter(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        next!.Value.UtcDateTime.DayOfWeek.Should().Be(quartzFiresOn,
            "Quartz numbers the days of the week 1-7 from Sunday, so the digit Spring wrote lands a day early");
        next.Value.UtcDateTime.DayOfWeek.Should().NotBe(springMeant,
            "the whole trap is that the expression is accepted and means something else");
    }

    [TestCase("0 0 9 * * MON", DayOfWeek.Monday)]
    [TestCase("0 0 9 * * FRI", DayOfWeek.Friday)]
    [TestCase("0 0 9 * * SAT", DayOfWeek.Saturday)]
    public void ADayNameMeansTheSameDayInEveryDialect(string expression, DayOfWeek fires)
    {
        DateTimeOffset? next = CronExpression.Parse(expression).WithTimeZone(TimeZoneInfo.Utc)
            .GetNextValidTimeAfter(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        next!.Value.UtcDateTime.DayOfWeek.Should().Be(fires,
            "a name is the spelling that survives being pasted in either direction, which is what the "
            + "documentation tells a reader coming from Spring or crontab to write");
    }

    #endregion

    #region Field counts

    [TestCase("0 0 12 * * ?", 6)]
    [TestCase("0 0 12 * * ? 2030", 7)]
    public void AQuartzExpressionReadAsUnixIsRejectedByItsFieldCount(string quartz, int fields)
    {
        Action act = () => CronExpression.Parse(quartz, CronFormat.Unix);

        act.Should().Throw<FormatException>()
            .WithMessage($"*has {fields} fields*", "the count is the thing that is wrong, so the message says the count")
            .WithMessage("*CronFormat.Quartz*", "and names the format that does read it");
    }

    [Test]
    public void AFiveFieldExpressionReadAsQuartzKeepsItsOwnMessageAndNowNamesTheFormatThatReadsIt()
    {
        Action act = () => CronExpression.Parse("30 4 * * 1", CronFormat.Quartz);

        act.Should().Throw<FormatException>()
            .WithMessage("*Unix/crontab*", "the most common first-run failure is a 5-field expression copied from crontab or Kubernetes")
            .WithMessage("*\"0 30 4 * * 1\"*", "the error must show the fixed expression, not just the constraint")
            .WithMessage("*CronFormat.Unix*", "prepending a zero is no longer the only way out");
    }

    [Test]
    public void ADayNameInTheMonthFieldPointsAtTheUnixFormat()
    {
        // '0 12 * * MON' never reaches the field count: the fifth field is Quartz's month, and 'MON' is
        // rejected as a month name before anything counts. That is the shape a real crontab line has.
        Action act = () => new CronExpression("0 12 * * MON");

        act.Should().Throw<FormatException>()
            .WithMessage("*Invalid Month value: 'MON'*", "the field really is invalid, and saying so first is honest")
            .WithMessage("*5-field Unix/crontab*", "but a day name in the month field is what a pasted crontab line looks like")
            .WithMessage("*CronFormat.Unix*", "and the way out is worth naming where the failure happens");
    }

    [Test]
    public void AnUnknownFormatIsARejectedArgumentRatherThanAParseFailure()
    {
        Action act = () => CronExpression.Parse("0 0 12 * * ?", (CronFormat) 42);

        act.Should().Throw<ArgumentOutOfRangeException>("an undefined format is a mistake in the caller's code, not in the expression");
    }

    #endregion

    #region TryParse and the builder

    [Test]
    public void TryParseReadsTheUnixForm()
    {
        CronExpression.TryParse("30 4 * * 1", CronFormat.Unix, out CronExpression? result).Should().BeTrue();

        result!.CronExpressionString.Should().Be("0 30 4 ? * MON");
    }

    [TestCase(null, "there is nothing to parse")]
    [TestCase("0 0 12 * * ?", "six fields is not the Unix form")]
    [TestCase("nonsense", "and neither is this")]
    public void TryParseReturnsFalseRatherThanThrowing(string? expression, string because)
    {
        CronExpression.TryParse(expression, CronFormat.Unix, out CronExpression? result).Should().BeFalse(because);

        result.Should().BeNull();
    }

    [Test]
    public void TheScheduleBuilderReadsTheUnixFormAndStoresTheCanonicalOne()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("crontab")
            .WithSchedule(CronScheduleBuilder.Create("30 4 * * 1", CronFormat.Unix))
            .Build();

        ((ICronTrigger) trigger).CronExpressionString.Should().Be("0 30 4 ? * MON",
            "a trigger carries the canonical expression, whichever dialect the caller wrote");
    }

    [Test]
    public void TheScheduleBuilderRefusesAnExpressionThatIsNotTheFormatItWasToldTo()
    {
        Action act = () => CronScheduleBuilder.Create("0 0 12 * * ?", CronFormat.Unix);

        act.Should().Throw<FormatException>("the builder rewrites before it validates, so the count is checked at the call that named it");
    }

    [Test]
    public void TheScheduleBuilderRefusesANullExpression()
    {
        Action act = () => CronScheduleBuilder.Create(null!, CronFormat.Unix);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ParseRefusesANullExpression()
    {
        Action act = () => CronExpression.Parse(null!, CronFormat.Unix);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void ATimeZoneComposesWithTheFormat()
    {
        CronExpression expression = CronExpression.Parse("30 4 * * 1", CronFormat.Unix)
            .WithTimeZone(TimeZoneInfo.Utc);

        expression.TimeZone.Should().Be(TimeZoneInfo.Utc);
        expression.CronExpressionString.Should().Be("0 30 4 ? * MON");
    }

    #endregion

    #region Macros

    [TestCase("@yearly", "0 0 0 1 1 ?")]
    [TestCase("@annually", "0 0 0 1 1 ?")]
    [TestCase("@monthly", "0 0 0 1 * ?")]
    [TestCase("@weekly", "0 0 0 ? * SUN")]
    [TestCase("@daily", "0 0 0 * * ?")]
    [TestCase("@midnight", "0 0 0 * * ?")]
    [TestCase("@hourly", "0 0 * * * ?")]
    [TestCase("@DAILY", "0 0 0 * * ?")]
    [TestCase("  @daily  ", "0 0 0 * * ?")]
    public void AMacroExpandsToItsCanonicalExpression(string macro, string canonical)
    {
        new CronExpression(macro).CronExpressionString.Should().Be(canonical,
            "a macro is a name for a schedule, and the schedule is what gets stored");
    }

    [TestCase("@yearly")]
    [TestCase("@annually")]
    [TestCase("@monthly")]
    [TestCase("@weekly")]
    [TestCase("@daily")]
    [TestCase("@midnight")]
    [TestCase("@hourly")]
    public void AMacroNeedsNoFormat(string macro)
    {
        CronExpression quartz = CronExpression.Parse(macro, CronFormat.Quartz);
        CronExpression unix = CronExpression.Parse(macro, CronFormat.Unix);

        unix.CronExpressionString.Should().Be(quartz.CronExpressionString,
            "'@daily' is '@daily' in every cron there is, so the format has nothing to say about it");
    }

    [Test]
    public void AMacroWorksWhereverAnExpressionStringIsRead()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("nightly")
            .WithCronSchedule("@daily")
            .Build();

        ((ICronTrigger) trigger).CronExpressionString.Should().Be("0 0 0 * * ?",
            "macros are expanded by the constructor, which is what every entry point goes through - "
            + "the XML scheduling files and the HTTP API included");
    }

    [Test]
    public void AMacroIsExpandedByTheSetterTheStoreAndTheHttpApiGoThrough()
    {
        Quartz.Impl.Triggers.CronTriggerImpl trigger = new Quartz.Impl.Triggers.CronTriggerImpl();

        trigger.CronExpressionString = "@hourly";

        trigger.CronExpressionString.Should().Be("0 0 * * * ?",
            "this setter is what materialises a trigger out of a database row and out of the HTTP API's "
            + "payload, so a macro written anywhere at all arrives here and leaves canonical");
    }

    [Test]
    public void RebootIsRejectedByName()
    {
        Action act = () => new CronExpression("@reboot");

        act.Should().Throw<FormatException>()
            .WithMessage("*'@reboot' is not supported*")
            .WithMessage("*a scheduler has no reboot*",
                "the generic unknown-macro message would send the reader looking for a spelling mistake "
                + "instead of telling them the concept does not apply");
    }

    [Test]
    public void AnUnknownMacroListsTheOnesThatExist()
    {
        Action act = () => new CronExpression("@fortnightly");

        act.Should().Throw<FormatException>()
            .WithMessage("*Unknown cron macro '@FORTNIGHTLY'*")
            .WithMessage("*@yearly (@annually), @monthly, @weekly, @daily (@midnight) and @hourly*",
                "the whole set is short enough to print, and printing it saves a trip to the documentation");
    }

    [Test]
    public void ThereIsNoEveryMinuteMacro()
    {
        Action act = () => new CronExpression("@every_minute");

        act.Should().Throw<FormatException>("Cronos has this one and Vixie does not; '0 * * * * ?' is already short");
    }

    [Test]
    public void AnAtSignInTheMiddleOfAnExpressionIsStillJustAnUnexpectedCharacter()
    {
        Action act = () => new CronExpression("0 0 @ * * ?");

        act.Should().Throw<FormatException>()
            .WithMessage("*Unexpected character: @*", "only a leading '@' names a macro");
    }

    [TestCase("@hourly", "0 0 * * * ?")]
    [TestCase("@daily", "0 0 0 * * ?")]
    public void AMacroFiresWhatItSays(string macro, string equivalent)
    {
        DateTimeOffset from = new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero);

        FireTimes(new CronExpression(macro).WithTimeZone(TimeZoneInfo.Utc), from, 50)
            .Should().Equal(FireTimes(new CronExpression(equivalent).WithTimeZone(TimeZoneInfo.Utc), from, 50));
    }

    #endregion

    #region Fire-time equivalence

    [TestCase("* * * * *", "0 * * * * ?")]
    [TestCase("30 4 * * 1", "0 30 4 ? * MON")]
    [TestCase("0 12 1 * *", "0 0 12 1 * ?")]
    [TestCase("0 0 13 * 5", "0 0 0 13 * FRI")]
    [TestCase("15 10 * * 1-5", "0 15 10 ? * MON-FRI")]
    [TestCase("0 */6 * * *", "0 0 */6 * * ?")]
    [TestCase("0 0 * * 0", "0 0 0 ? * SUN")]
    [TestCase("0 0 * * 7", "0 0 0 ? * SUN")]
    [TestCase("0 0 * * 6", "0 0 0 ? * SAT")]
    [TestCase("0 0 * * 5-1", "0 0 0 ? * FRI-MON")]
    [TestCase("0 0 * * 1/2", "0 0 0 ? * 2/2")]
    [TestCase("0 0 * * 0-6", "0 0 0 * * ?")]
    [TestCase("0 0 * * 0-6/2", "0 0 0 ? * 1-7/2")]
    [TestCase("0 0 * * 1-7/2", "0 0 0 ? * 2-1/2")]
    [TestCase("0 0 * * 1,3,5", "0 0 0 ? * MON,WED,FRI")]
    [TestCase("5 0 1,15 * *", "0 5 0 1,15 * ?")]
    [TestCase("0 0 * * 0L", "0 0 0 ? * 1L")]
    public void TheTwoSpellingsFireAtTheSameInstantsForSixtyDays(string unix, string quartz)
    {
        DateTimeOffset start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset end = start.AddDays(60);

        List<DateTimeOffset> fromUnix = FireTimesUntil(CronExpression.Parse(unix, CronFormat.Unix).WithTimeZone(TimeZoneInfo.Utc), start, end);
        List<DateTimeOffset> fromQuartz = FireTimesUntil(CronExpression.Parse(quartz).WithTimeZone(TimeZoneInfo.Utc), start, end);

        fromUnix.Should().NotBeEmpty("an expression that never fires would make this comparison vacuous");
        fromUnix.Should().Equal(fromQuartz,
            "the format decides how the string is read and nothing else - the schedule it names is the same schedule");
    }

    #endregion

    private static List<DateTimeOffset> FireTimes(CronExpression expression, DateTimeOffset from, int count)
    {
        List<DateTimeOffset> times = new List<DateTimeOffset>(count);
        DateTimeOffset cursor = from;
        for (int i = 0; i < count; i++)
        {
            DateTimeOffset? next = expression.GetNextValidTimeAfter(cursor);
            if (next is null)
            {
                break;
            }

            times.Add(next.Value);
            cursor = next.Value;
        }

        return times;
    }

    private static List<DateTimeOffset> FireTimesUntil(CronExpression expression, DateTimeOffset from, DateTimeOffset until)
    {
        List<DateTimeOffset> times = new List<DateTimeOffset>();
        DateTimeOffset cursor = from;
        while (true)
        {
            DateTimeOffset? next = expression.GetNextValidTimeAfter(cursor);
            if (next is null || next.Value >= until)
            {
                return times;
            }

            times.Add(next.Value);
            cursor = next.Value;
        }
    }

    private static List<int> FireDays(CronExpression expression, DateTimeOffset from, int days)
    {
        DateTimeOffset until = from.AddDays(days);
        List<int> result = new List<int>();
        foreach (DateTimeOffset fire in FireTimesUntil(expression, from.AddSeconds(-1), until))
        {
            result.Add(fire.Day);
        }

        return result;
    }
}
