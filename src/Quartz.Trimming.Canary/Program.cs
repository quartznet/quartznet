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

using System.Collections.Specialized;
using System.Text;
using System.Text.Json;

using Quartz;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Calendar;

namespace Quartz.Trimming.Canary;

/// <summary>
/// Round-trips everything a persistent job store writes, out of a trimmed publish, through the ordinary
/// <see cref="SystemTextJsonObjectSerializer" /> with no test seam of any kind — and then runs a
/// persistent store for real.
/// </summary>
/// <remarks>
/// <para>
/// A trimmed or native-AOT publish sets <c>System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault</c>
/// to false, and that is the whole point: with it false, options carrying no resolver have nothing to
/// answer <c>GetTypeInfo</c> with, and before issue #3341's step 6 every ADO store threw on the first
/// trigger it wrote. The first check below asserts the switch really is off, so a green run cannot be a
/// run that happened to keep reflection.
/// </para>
/// <para>
/// Each check serializes, deserializes, and serializes again, then compares the two payloads byte for
/// byte. That catches both halves at once — a shape that cannot be written, and a shape that comes back
/// different — and it needs no equality implementation on the types involved.
/// </para>
/// <para>
/// Then <see cref="StoreCheck" /> runs the same blobs through a real database. The serializer checks
/// are still worth their place ahead of it — they name the payload that failed, where a store failure
/// only says a job did not fire — but the store is what the whole track was working towards, and it is
/// the half that a compile can substitute for least.
/// </para>
/// <para>
/// Last, <see cref="BindingCheck" /> builds a scheduler out of an <see cref="Microsoft.Extensions.Configuration.IConfiguration" />
/// and reads every bound value back. That one is here for the same reason as the rest, and it is the
/// only check whose failure needs the <em>native</em> leg rather than the trimmed one: built against
/// the reflection binder this repository used to have, it passes trimmed and fails natively, with
/// three of its values silently arriving as defaults.
/// </para>
/// </remarks>
internal static class Program
{
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task<int> Main()
    {
        List<string> failures = [];

        Console.WriteLine($"IsReflectionEnabledByDefault: {JsonSerializer.IsReflectionEnabledByDefault}");
        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            failures.Add("FAIL reflection-is-off: JsonSerializer.IsReflectionEnabledByDefault is true, so this run proves nothing. Publish with PublishTrimmed=true.");
        }

        SystemTextJsonObjectSerializer serializer = new();

        foreach ((string name, object value) in Payloads())
        {
            string? failure = RoundTrip(serializer, name, value);
            if (failure is null)
            {
                Console.WriteLine($"PASS {name}");
            }
            else
            {
                failures.Add(failure);
            }
        }

        if (await StoreCheck.Run().ConfigureAwait(false) is { } storeFailure)
        {
            failures.Add(storeFailure);
        }

        if (await BindingCheck.Run().ConfigureAwait(false) is { } bindingFailure)
        {
            failures.Add(bindingFailure);
        }

        foreach (string failure in failures)
        {
            Console.WriteLine(failure);
        }

        Console.WriteLine(failures.Count == 0
            ? "Quartz.Trimming.Canary: the store format round-trips, a persistent store schedules, fires and reads back, and configuration binds."
            : $"Quartz.Trimming.Canary: {failures.Count} check(s) failed.");

        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Everything a persistent job store hands the serializer: every built-in trigger, every built-in
    /// calendar and a chained one, a job data map holding each value the read side can produce, the
    /// property collection <c>useProperties</c> writes, and a cron expression.
    /// </summary>
    private static IEnumerable<(string Name, object Value)> Payloads()
    {
        yield return ("SimpleTrigger", Trigger("SimpleTrigger", SimpleScheduleBuilder.Create()
            .WithInterval(TimeSpan.FromMinutes(5))
            .WithRepeatCount(3)));

        yield return ("CronTrigger", Trigger("CronTrigger", CronScheduleBuilder.Create("0/5 * * * * ?")));

        yield return ("CalendarIntervalTrigger", Trigger("CalendarIntervalTrigger", CalendarIntervalScheduleBuilder.Create()
            .WithInterval(2, IntervalUnit.Day)));

        yield return ("DailyTimeIntervalTrigger", Trigger("DailyTimeIntervalTrigger", DailyTimeIntervalScheduleBuilder.Create()
            .WithInterval(30, IntervalUnit.Minute)
            .StartingDailyAt(new TimeOnly(8, 0))
            .EndingDailyAt(new TimeOnly(17, 0))));

        yield return ("RecurrenceTrigger", Trigger("RecurrenceTrigger", RecurrenceScheduleBuilder.Create("FREQ=DAILY")
            .InTimeZone(TimeZoneInfo.Utc)));

        yield return ("BaseCalendar", new BaseCalendar { Description = "BaseCalendar" });
        yield return ("AnnualCalendar", new AnnualCalendar { Description = "AnnualCalendar" });
        yield return ("CronCalendar", new CronCalendar("0/5 * * * * ?") { Description = "CronCalendar" });
        yield return ("DailyCalendar", new DailyCalendar(new TimeOnly(1, 0), new TimeOnly(2, 0)) { Description = "DailyCalendar" });
        yield return ("HolidayCalendar", new HolidayCalendar { Description = "HolidayCalendar" });
        yield return ("MonthlyCalendar", new MonthlyCalendar { Description = "MonthlyCalendar" });
        yield return ("WeeklyCalendar", new WeeklyCalendar { Description = "WeeklyCalendar" });

        yield return ("ChainedCalendars", new CronCalendar("0/5 * * * * ?")
        {
            Description = "ChainedCalendars",
            CalendarBase = new AnnualCalendar { Description = "the base of the chain" }
        });

        yield return ("JobDataMap", new JobDataMap
        {
            { "string", "value" },
            { "bool", true },
            { "int", 42 },
            { "long", 9_000_000_000L },
            { "double", 12.34 },
            { "null", null },
            { "dictionary", new Dictionary<string, string> { ["inner"] = "value" } }
        });

        yield return ("NameValueCollection", new NameValueCollection { { "key", "value" } });

        yield return ("CronExpression", new CronExpression("0/5 * * * * ?", TimeZoneInfo.Utc));
    }

    private static IOperableTrigger Trigger(string name, IScheduleBuilder schedule)
    {
        return (IOperableTrigger) TriggerBuilder.Create()
            .WithSchedule(schedule)
            .WithIdentity(name, "Canary")
            .ForJob("Job", "Canary")
            .StartAt(StartTime)
            .UsingJobData("value", "kept")
            .Build();
    }

    /// <summary>
    /// Writes the value, reads it back through the type the store names for it, and writes it again.
    /// </summary>
    private static string? RoundTrip(SystemTextJsonObjectSerializer serializer, string name, object value)
    {
        try
        {
            byte[] written = serializer.Serialize(value);
            object? restored = Read(serializer, value, written);

            if (restored is null)
            {
                return $"FAIL {name}: came back as null.";
            }

            byte[] rewritten = serializer.Serialize(restored);
            if (!written.AsSpan().SequenceEqual(rewritten))
            {
                return $"FAIL {name}: came back different.{Environment.NewLine}  wrote:   {Encoding.UTF8.GetString(written)}{Environment.NewLine}  rewrote: {Encoding.UTF8.GetString(rewritten)}";
            }

            return null;
        }
        catch (Exception e)
        {
            return $"FAIL {name}: {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e}";
        }
    }

    /// <summary>
    /// Reads a blob back under the very type <c>StdAdoDelegate</c> asks <c>GetObjectFromBlob</c> for.
    /// </summary>
    private static object? Read(SystemTextJsonObjectSerializer serializer, object value, byte[] written)
    {
        return value switch
        {
            ITrigger => serializer.Deserialize<IOperableTrigger>(written),
            ICalendar => serializer.Deserialize<ICalendar>(written),
            JobDataMap => serializer.Deserialize<JobDataMap>(written),
            NameValueCollection => serializer.Deserialize<NameValueCollection>(written),
            CronExpression => serializer.Deserialize<CronExpression>(written),
            _ => throw new InvalidOperationException($"No job store reads a {value.GetType()} back, so this payload does not belong in the canary.")
        };
    }
}
