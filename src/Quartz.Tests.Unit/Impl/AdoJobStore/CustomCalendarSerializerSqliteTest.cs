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

using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.Calendar;
using Quartz.Serialization.SystemTextJson;
using Quartz.Serialization.SystemTextJson.Calendars;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What a calendar of one's own has to bring to a persistent store.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ICalendar" /> told implementors to be "Serializable", which is 3.x's requirement and
/// stores nothing in 4.x — there is no <c>BinaryFormatter</c> any more. The requirement now is a
/// <see cref="CalendarSerializer{TCalendar}" /> registered with <c>AddCalendarSerializer</c>, and
/// without one nothing fails until the first store write. This pins the sentence the interface now
/// carries, at both ends: the write that works, and the failure that names the type to write a
/// serializer for.
/// </para>
/// <para>
/// SQLite is a file, so the ADO path this exercises costs a temporary path rather than a container —
/// which is what lets the persistent half of the answer be covered from the unit project at all.
/// </para>
/// </remarks>
public sealed class CustomCalendarSerializerSqliteTest
{
    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-calendar-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    [Test]
    public async Task ACalendarWithARegisteredSerializerRoundTripsThroughTheStore()
    {
        IScheduler scheduler = await Scheduler(
            nameof(ACalendarWithARegisteredSerializerRoundTripsThroughTheStore),
            json => json.AddCalendarSerializer(new PaydayCalendarSerializer()));

        await scheduler.AddCalendar("payday", new PaydayCalendar { DayOfMonth = 15, Description = "no jobs on payday" });

        ICalendar? readBack = await scheduler.GetCalendar("payday");

        readBack.Should().BeOfType<PaydayCalendar>(
            "the registered serializer is what turns the stored blob back into the application's own type")
            .Which.DayOfMonth.Should().Be(15, "and its fields with it");

        readBack!.Description.Should().Be("no jobs on payday",
            "the description is written by the converter rather than by the serializer, so it round trips "
            + "for a custom calendar exactly as it does for a built-in one");

        await scheduler.Shutdown();
    }

    [Test]
    public async Task ACalendarWithNoRegisteredSerializerFailsTheStoreWriteNamingItsType()
    {
        IScheduler scheduler = await Scheduler(
            nameof(ACalendarWithNoRegisteredSerializerFailsTheStoreWriteNamingItsType),
            configure: null);

        Func<Task> act = async () => await scheduler.AddCalendar("payday", new PaydayCalendar { DayOfMonth = 15 });

        Exception failure = (await act.Should().ThrowAsync<JobPersistenceException>(
            "a calendar the serializer has never been told about cannot be written, and silence would "
            + "mean scheduling data that vanished")).Which;

        Messages(failure).Should().Contain(x => x.Contains(nameof(PaydayCalendar), StringComparison.Ordinal),
            "the type to write a CalendarSerializer<TCalendar> for is the one piece of information the "
            + "reader needs, so it has to be somewhere in the chain");

        await scheduler.Shutdown();
    }

    private static IEnumerable<string> Messages(Exception failure)
    {
        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            yield return current.Message;
        }
    }

    private async Task<IScheduler> Scheduler(string schedulerName, Action<SystemTextJsonSerializerRegistry>? configure)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = schedulerName;
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.UseSystemTextJsonSerializer(configure);
                store.ProvisionSchema();
            });
        });

        ServiceProvider container = services.BuildServiceProvider();
        return await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    /// <summary>
    /// The calendar an application writes: a <see cref="BaseCalendar" /> with one field of its own, so
    /// that a serializer has something to carry and getting it back is visible.
    /// </summary>
    public sealed class PaydayCalendar : BaseCalendar
    {
        public int DayOfMonth { get; set; }

        public override bool IsTimeIncluded(DateTimeOffset timeUtc)
        {
            return timeUtc.Day != DayOfMonth && base.IsTimeIncluded(timeUtc);
        }

        public override ICalendar Clone()
        {
            PaydayCalendar clone = new() { DayOfMonth = DayOfMonth };
            CloneFields(clone);
            return clone;
        }
    }

    /// <summary>
    /// And the serializer for it, which is the whole of what 4.x asks an implementor for.
    /// </summary>
    private sealed class PaydayCalendarSerializer : CalendarSerializer<PaydayCalendar>
    {
        public override string CalendarTypeName => "PaydayCalendar";

        protected override PaydayCalendar Create(JsonElement jsonElement, JsonSerializerOptions options) => new();

        protected override void SerializeFields(Utf8JsonWriter writer, PaydayCalendar calendar, JsonSerializerOptions options)
        {
            writer.WriteNumber("DayOfMonth", calendar.DayOfMonth);
        }

        protected override void DeserializeFields(PaydayCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
        {
            calendar.DayOfMonth = jsonElement.GetProperty("DayOfMonth").GetInt32();
        }
    }
}
