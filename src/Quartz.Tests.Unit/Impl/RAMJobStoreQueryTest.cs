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

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Jobs;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// Covers the listing and bulk-fetch part of the <see cref="IJobStore" /> contract, as implemented
/// by <see cref="RAMJobStore" />.
/// </summary>
public class RAMJobStoreQueryTest
{
    private static readonly DateTimeOffset startTime = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private RAMJobStore store;

    [SetUp]
    public async Task SetUp()
    {
        store = TestJobStores.Ram();
        await store.Initialize(TestJobStores.Identity());
        await store.SchedulerStarted();
    }

    [Test]
    public async Task QueryJobs_OrdersByGroupThenNameOrdinally()
    {
        await AddJob("j2", "alpha");
        await AddJob("j1", "alpha");
        await AddJob("j1", "Beta");
        await AddJob("j1", "DEFAULT");

        PagedResult<JobHeader> result = await store.QueryJobs(new JobQuery());

        result.Items.Select(x => x.Key).Should().Equal(
            [new JobKey("j1", "Beta"), new JobKey("j1", "DEFAULT"), new JobKey("j1", "alpha"), new JobKey("j2", "alpha")],
            "listings order by group and then name with ordinal comparison, so the default group is not sorted first the way Key.CompareTo would");
        result.HasMore.Should().BeFalse("the whole match set fit on the page");
        result.TotalCount.Should().BeNull("the total is only computed when the query asks for it");
    }

    [Test]
    public async Task QueryJobs_WithoutGroupMatcherSelectsEveryGroup()
    {
        await AddJob("j1", "g1");
        await AddJob("j1", "g2");

        PagedResult<JobHeader> result = await store.QueryJobs(new JobQuery { Group = null });

        result.Items.Should().HaveCount(2, "a null group matcher matches every group");
    }

    [Test]
    public async Task QueryJobs_TakesTheRequestedPage()
    {
        await StoreJobs("g", "j1", "j2", "j3", "j4", "j5");

        PagedResult<JobHeader> exactlyFull = await store.QueryJobs(new JobQuery { Take = 5 });
        exactlyFull.Items.Should().HaveCount(5);
        exactlyFull.HasMore.Should().BeFalse("a page that ends exactly at the last match has nothing beyond it");

        PagedResult<JobHeader> oneShort = await store.QueryJobs(new JobQuery { Take = 4 });
        oneShort.Items.Should().HaveCount(4);
        oneShort.HasMore.Should().BeTrue("one more match exists past the page");

        PagedResult<JobHeader> lastPage = await store.QueryJobs(new JobQuery { Skip = 3, Take = 2 });
        lastPage.Items.Select(x => x.Key.Name).Should().Equal(["j4", "j5"], "Skip is an offset into the ordering");
        lastPage.HasMore.Should().BeFalse();

        PagedResult<JobHeader> justPastTheEnd = await store.QueryJobs(new JobQuery { Skip = 5, Take = 2 });
        justPastTheEnd.Items.Should().BeEmpty();
        justPastTheEnd.HasMore.Should().BeFalse();

        PagedResult<JobHeader> farPastTheEnd = await store.QueryJobs(new JobQuery { Skip = 99, Take = 2 });
        farPastTheEnd.Items.Should().BeEmpty("skipping beyond the last match returns nothing");
        farPastTheEnd.HasMore.Should().BeFalse("there is nothing past a page that starts past the end");
    }

    [Test]
    public async Task QueryJobs_TakeZeroWithTotalCountIsACount()
    {
        await StoreJobs("g", "j1", "j2", "j3", "j4", "j5");

        PagedResult<JobHeader> result = await store.QueryJobs(new JobQuery { Take = 0, IncludeTotalCount = true });

        result.Items.Should().BeEmpty("Take of zero returns no items");
        result.TotalCount.Should().Be(5, "a count-only query still counts every match");
        result.HasMore.Should().BeTrue("every match lies beyond a page of zero items");
    }

    [Test]
    public async Task QueryJobs_TotalCountIgnoresPaging()
    {
        await StoreJobs("g", "j1", "j2", "j3", "j4", "j5");

        PagedResult<JobHeader> result = await store.QueryJobs(new JobQuery { Skip = 1, Take = 2, IncludeTotalCount = true });

        result.Items.Select(x => x.Key.Name).Should().Equal(["j2", "j3"]);
        result.TotalCount.Should().Be(5, "the total counts every match regardless of Skip and Take");
        result.HasMore.Should().BeTrue();
    }

    [Test]
    public async Task QueryJobs_TotalCountIsCountedWithinTheFilterOnly()
    {
        await StoreJobs("g1", "j1", "j2");
        await StoreJobs("g2", "j1");

        PagedResult<JobHeader> result = await store.QueryJobs(new JobQuery
        {
            Group = GroupMatcher<JobKey>.GroupEquals("g1"),
            IncludeTotalCount = true
        });

        result.TotalCount.Should().Be(2, "the total counts the matches of the query, not everything in the store");
    }

    [Test]
    public async Task QueryJobs_EqualityMatcherSelectsOneGroup()
    {
        await StoreJobs("reports", "daily", "weekly");
        await StoreJobs("reportsArchive", "old");

        PagedResult<JobHeader> result = await store.QueryJobs(new JobQuery { Group = GroupMatcher<JobKey>.GroupEquals("reports") });

        result.Items.Select(x => x.Key.Group).Should().AllBe("reports", "an equality matcher must not spill into groups that merely start with the value");
        result.Items.Should().HaveCount(2);
    }

    [Test]
    public async Task QueryJobs_StartsWithMatcherSelectsSeveralGroups()
    {
        await StoreJobs("reports", "daily");
        await StoreJobs("reportsArchive", "old");
        await StoreJobs("other", "x");

        PagedResult<JobHeader> result = await store.QueryJobs(new JobQuery { Group = GroupMatcher<JobKey>.GroupStartsWith("reports") });

        result.Items.Select(x => x.Key).Should().Equal(
            [new JobKey("daily", "reports"), new JobKey("old", "reportsArchive")],
            "a prefix matcher spans groups and the result stays ordered by group then name");
    }

    [Test]
    public async Task QueryJobs_HeaderCarriesTheStoredJobMetadata()
    {
        IJobDetail job = JobBuilder.Create()
            .OfType<StatefulTestJob>()
            .WithIdentity("fidelity", "g")
            .WithDescription("the one job")
            .RequestRecovery()
            .StoreDurably()
            .UsingJobData("secret", "value")
            .Build();

        await store.AddJob(job);

        PagedResult<JobHeader> result = await store.QueryJobs(new JobQuery());
        JobHeader header = result.Items.Single();

        header.Key.Should().Be(new JobKey("fidelity", "g"));
        header.Description.Should().Be("the one job");
        header.JobTypeName.Should().Be(
            typeof(StatefulTestJob).AssemblyQualifiedNameWithoutVersion(),
            "a listing reports the same job type string the ADO store persists");
        header.JobTypeName.Should().NotContain("Version=", "the persisted type name is version independent");
        header.Durable.Should().BeTrue();
        header.RequestsRecovery.Should().BeTrue();
        header.ConcurrentExecutionDisallowed.Should().BeTrue("the job type carries DisallowConcurrentExecution");
        header.PersistJobDataAfterExecution.Should().BeTrue("the job type carries PersistJobDataAfterExecution");
    }

    [Test]
    public void Headers_DoNotCarryJobData()
    {
        typeof(JobHeader).GetProperties().Select(x => x.PropertyType.Name).Should().NotContain(
            nameof(JobDataMap),
            "listing jobs must never load or deserialize job data");

        typeof(TriggerHeader).GetProperties().Select(x => x.PropertyType.Name).Should().NotContain(
            nameof(JobDataMap),
            "listing triggers must never load or deserialize job data");
    }

    [Test]
    public async Task QueryTriggers_OrdersByGroupThenNameOrdinally()
    {
        IJobDetail job = await AddJob("job", "g");
        await AddTrigger("t2", "alpha", job.Key);
        await AddTrigger("t1", "alpha", job.Key);
        await AddTrigger("t1", "Beta", job.Key);
        await AddTrigger("t1", "DEFAULT", job.Key);

        PagedResult<TriggerHeader> result = await store.QueryTriggers(new TriggerQuery());

        result.Items.Select(x => x.Key).Should().Equal(
            [new TriggerKey("t1", "Beta"), new TriggerKey("t1", "DEFAULT"), new TriggerKey("t1", "alpha"), new TriggerKey("t2", "alpha")],
            "listings order by group and then name with ordinal comparison");
    }

    [Test]
    public async Task QueryTriggers_ReportsAndFiltersExecutingConsistently()
    {
        IJobDetail job = await AddJob("job", "g");

        // Has to be firable now, unlike the far-future triggers the other listing tests use.
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("running", "g")
            .ForJob(job)
            .StartAt(d.AddSeconds(1))
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(5)).RepeatForever())
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        await store.AddTrigger(trigger);

        var acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        acquired.Should().HaveCount(1);
        (await store.TriggersFired(acquired)).Should().HaveCount(1);

        PagedResult<TriggerHeader> all = await store.QueryTriggers(new TriggerQuery());
        all.Items.Single().State.Should().Be(TriggerState.Executing,
            "a listing reports the same state GetTriggerState would");

        PagedResult<TriggerHeader> executing = await store.QueryTriggers(new TriggerQuery { State = TriggerState.Executing });
        executing.Items.Select(x => x.Key).Should().Equal([trigger.Key]);

        PagedResult<TriggerHeader> normal = await store.QueryTriggers(new TriggerQuery { State = TriggerState.Normal });
        normal.Items.Should().BeEmpty(
            "filtering by normal must not return a trigger the same listing reports as executing");
    }

    [Test]
    public async Task QueryTriggers_HeaderCarriesTheStoredTriggerMetadata()
    {
        IJobDetail job = await AddJob("job", "g");
        await store.AddCalendar("holidays", new HolidayCalendar());

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("nightly", "reports")
            .ForJob(job)
            .WithDescription("the nightly run")
            .StartAt(startTime)
            .EndAt(startTime.AddDays(30))
            .WithPriority(7)
            .WithExecutionGroup("batch")
            .WithCalendarName("holidays")
            .WithCronSchedule("0 0 12 * * ?")
            .Build();

        trigger.ComputeFirstFireTimeUtc(null);
        await store.AddTrigger(trigger);

        PagedResult<TriggerHeader> result = await store.QueryTriggers(new TriggerQuery());
        TriggerHeader header = result.Items.Single();

        header.Key.Should().Be(new TriggerKey("nightly", "reports"));
        header.JobKey.Should().Be(job.Key);
        header.Description.Should().Be("the nightly run");
        header.TriggerType.Should().Be("CRON", "the header uses the same discriminator the ADO store persists");
        header.State.Should().Be(TriggerState.Normal);
        header.StartTimeUtc.Should().Be(trigger.StartTimeUtc);
        header.EndTimeUtc.Should().Be(trigger.EndTimeUtc);
        header.NextFireTimeUtc.Should().Be(trigger.NextFireTimeUtc);
        header.PreviousFireTimeUtc.Should().BeNull("the trigger has not fired");
        header.CalendarName.Should().Be("holidays");
        header.Priority.Should().Be(7);
        header.ExecutionGroup.Should().Be("batch");
    }

    [Test]
    public async Task QueryTriggers_MapsTriggerTypesToTheAdoDiscriminators()
    {
        IJobDetail job = await AddJob("job", "g");

        await StoreBuiltTrigger(TriggerBuilder.Create().WithIdentity("a", "g").ForJob(job).StartAt(startTime).WithSimpleSchedule());
        await StoreBuiltTrigger(TriggerBuilder.Create().WithIdentity("b", "g").ForJob(job).StartAt(startTime).WithCronSchedule("0 0 12 * * ?"));
        await StoreBuiltTrigger(TriggerBuilder.Create().WithIdentity("c", "g").ForJob(job).StartAt(startTime).WithCalendarIntervalSchedule());
        await StoreBuiltTrigger(TriggerBuilder.Create().WithIdentity("d", "g").ForJob(job).StartAt(startTime).WithDailyTimeIntervalSchedule());

        TriggerWithAdditionalProperties custom = new TriggerWithAdditionalProperties
        {
            Key = new TriggerKey("e", "g"),
            JobKey = job.Key,
            StartTimeUtc = startTime
        };

        custom.ComputeFirstFireTimeUtc(null);
        await store.AddTrigger(custom);

        PagedResult<TriggerHeader> result = await store.QueryTriggers(new TriggerQuery());

        result.Items.Select(x => x.TriggerType).Should().Equal(
            ["SIMPLE", "CRON", "CAL_INT", "DAILY_I", "BLOB"],
            "the discriminators are the AdoConstants.TriggerType* values, and a trigger no persistence delegate handles is a blob");
    }

    [Test]
    public async Task QueryTriggers_FiltersByJob()
    {
        IJobDetail first = await AddJob("j1", "g");
        IJobDetail second = await AddJob("j2", "g");
        await AddTrigger("t1", "g", first.Key);
        await AddTrigger("t2", "g", second.Key);

        PagedResult<TriggerHeader> result = await store.QueryTriggers(new TriggerQuery { Job = second.Key });

        result.Items.Select(x => x.Key.Name).Should().Equal(["t2"], "the job filter selects only the triggers of that job");
    }

    [Test]
    public async Task QueryTriggers_FiltersByCalendarName()
    {
        IJobDetail job = await AddJob("job", "g");
        await store.AddCalendar("holidays", new HolidayCalendar());
        await AddTrigger("with", "g", job.Key, calendarName: "holidays");
        await AddTrigger("without", "g", job.Key);

        PagedResult<TriggerHeader> result = await store.QueryTriggers(new TriggerQuery { CalendarName = "holidays" });

        result.Items.Select(x => x.Key.Name).Should().Equal(["with"], "the calendar filter compares names ordinally");

        PagedResult<TriggerHeader> wrongCase = await store.QueryTriggers(new TriggerQuery { CalendarName = "HOLIDAYS" });

        wrongCase.Items.Should().BeEmpty("the calendar name comparison is ordinal, not case insensitive");
    }

    [Test]
    public async Task QueryTriggers_FiltersByState()
    {
        IJobDetail job = await AddJob("job", "g");
        IOperableTrigger paused = await AddTrigger("paused", "g", job.Key);
        await AddTrigger("running", "g", job.Key);

        await store.PauseTrigger(paused.Key);

        PagedResult<TriggerHeader> pausedOnly = await store.QueryTriggers(new TriggerQuery { State = TriggerState.Paused });
        pausedOnly.Items.Select(x => x.Key.Name).Should().Equal(["paused"], "the state filter reads the store's own trigger state");
        pausedOnly.Items.Single().State.Should().Be(TriggerState.Paused, "the reported state is the state that matched");

        PagedResult<TriggerHeader> normalOnly = await store.QueryTriggers(new TriggerQuery { State = TriggerState.Normal });
        normalOnly.Items.Select(x => x.Key.Name).Should().Equal(["running"]);

        PagedResult<TriggerHeader> errored = await store.QueryTriggers(new TriggerQuery { State = TriggerState.Error, IncludeTotalCount = true });
        errored.Items.Should().BeEmpty();
        errored.TotalCount.Should().Be(0, "counting failed triggers is a listing with a state filter");
    }

    [Test]
    public async Task QueryTriggers_CombinesFiltersWithAnd()
    {
        IJobDetail first = await AddJob("j1", "g");
        IJobDetail second = await AddJob("j2", "g");
        await store.AddCalendar("holidays", new HolidayCalendar());

        await AddTrigger("t1", "wanted", first.Key, calendarName: "holidays");
        await AddTrigger("t2", "wanted", second.Key, calendarName: "holidays");
        await AddTrigger("t3", "wanted", first.Key);
        await AddTrigger("t4", "other", first.Key, calendarName: "holidays");

        IOperableTrigger pausedOne = await AddTrigger("t5", "wanted", first.Key, calendarName: "holidays");
        await store.PauseTrigger(pausedOne.Key);

        PagedResult<TriggerHeader> result = await store.QueryTriggers(new TriggerQuery
        {
            Group = GroupMatcher<TriggerKey>.GroupEquals("wanted"),
            Job = first.Key,
            CalendarName = "holidays",
            State = TriggerState.Normal
        });

        result.Items.Select(x => x.Key.Name).Should().Equal(["t1"], "every filter has to hold at once");
    }

    [Test]
    public async Task QueryTriggers_PagesWithinAGroupMatcher()
    {
        IJobDetail job = await AddJob("job", "g");
        await AddTrigger("t1", "wanted", job.Key);
        await AddTrigger("t2", "wanted", job.Key);
        await AddTrigger("t3", "wanted", job.Key);
        await AddTrigger("t1", "other", job.Key);

        PagedResult<TriggerHeader> result = await store.QueryTriggers(new TriggerQuery
        {
            Group = GroupMatcher<TriggerKey>.GroupEquals("wanted"),
            Skip = 1,
            Take = 1,
            IncludeTotalCount = true
        });

        result.Items.Select(x => x.Key.Name).Should().Equal(["t2"]);
        result.HasMore.Should().BeTrue("t3 still matches beyond the page");
        result.TotalCount.Should().Be(3, "the total counts the matches of the group filter only");
    }

    [Test]
    public async Task QueryJobGroups_ListsGroupsThatHaveJobsWithTheirPausedFlag()
    {
        await StoreJobs("g1", "j1");
        await StoreJobs("g2", "j1");
        await StoreJobs("g3", "j1");
        await store.PauseJobGroups(GroupMatcher<JobKey>.GroupEquals("g2"));
        await store.PauseJobGroups(GroupMatcher<JobKey>.GroupEquals("ghost"));

        PagedResult<JobGroup> result = await store.QueryJobGroups(new JobGroupQuery());

        result.Items.Should().Equal(
            [new JobGroup("g1", false), new JobGroup("g2", true), new JobGroup("g3", false)],
            "an unfiltered listing reports the groups that currently have jobs, ordered by name, with their paused flag");
    }

    [Test]
    public async Task QueryJobGroups_PausedTrueIncludesAGroupWithNoJobs()
    {
        await StoreJobs("g1", "j1");
        await StoreJobs("g2", "j1");
        await store.PauseJobGroups(GroupMatcher<JobKey>.GroupEquals("g2"));
        await store.PauseJobGroups(GroupMatcher<JobKey>.GroupEquals("ghost"));

        PagedResult<JobGroup> result = await store.QueryJobGroups(new JobGroupQuery { Paused = true });

        result.Items.Should().Equal(
            [new JobGroup("g2", true), new JobGroup("ghost", true)],
            "a group stays paused while it holds no jobs, and the paused listing has to report it");
    }

    [Test]
    public async Task QueryJobGroups_PausedFalseExcludesPausedGroups()
    {
        await StoreJobs("g1", "j1");
        await StoreJobs("g2", "j1");
        await store.PauseJobGroups(GroupMatcher<JobKey>.GroupEquals("g2"));
        await store.PauseJobGroups(GroupMatcher<JobKey>.GroupEquals("ghost"));

        PagedResult<JobGroup> result = await store.QueryJobGroups(new JobGroupQuery { Paused = false, IncludeTotalCount = true });

        result.Items.Should().Equal([new JobGroup("g1", false)], "only groups that have jobs and are not paused match");
        result.TotalCount.Should().Be(1);
    }

    [Test]
    public async Task QueryTriggerGroups_ListsGroupsThatHaveTriggersWithTheirPausedFlag()
    {
        IJobDetail job = await AddJob("job", "g");
        await AddTrigger("t1", "g1", job.Key);
        await AddTrigger("t1", "g2", job.Key);
        await AddTrigger("t1", "g3", job.Key);
        await store.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("g2"));
        await store.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("ghost"));

        PagedResult<TriggerGroup> result = await store.QueryTriggerGroups(new TriggerGroupQuery());

        result.Items.Should().Equal(
            [new TriggerGroup("g1", false), new TriggerGroup("g2", true), new TriggerGroup("g3", false)],
            "an unfiltered listing reports the groups that currently have triggers, ordered by name, with their paused flag");

        PagedResult<TriggerGroup> page = await store.QueryTriggerGroups(new TriggerGroupQuery { Skip = 1, Take = 1, IncludeTotalCount = true });

        page.Items.Select(x => x.Name).Should().Equal(["g2"], "group listings page over the same ordering");
        page.HasMore.Should().BeTrue();
        page.TotalCount.Should().Be(3);
    }

    [Test]
    public async Task QueryTriggerGroups_PausedTrueIncludesAGroupWithNoTriggers()
    {
        IJobDetail job = await AddJob("job", "g");
        await AddTrigger("t1", "g1", job.Key);
        await AddTrigger("t1", "g2", job.Key);
        await store.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("g2"));
        await store.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("ghost"));

        PagedResult<TriggerGroup> result = await store.QueryTriggerGroups(new TriggerGroupQuery { Paused = true });

        result.Items.Should().Equal(
            [new TriggerGroup("g2", true), new TriggerGroup("ghost", true)],
            "the paused listing reports every paused group, including one that has no triggers");
    }

    [Test]
    public async Task QueryTriggerGroups_PausedFalseExcludesPausedGroups()
    {
        IJobDetail job = await AddJob("job", "g");
        await AddTrigger("t1", "g1", job.Key);
        await AddTrigger("t1", "g2", job.Key);
        await AddTrigger("t1", "g3", job.Key);
        await store.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("g2"));
        await store.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("ghost"));

        PagedResult<TriggerGroup> result = await store.QueryTriggerGroups(new TriggerGroupQuery { Paused = false });

        result.Items.Select(x => x.Name).Should().Equal(["g1", "g3"], "only groups that have triggers and are not paused match");
    }

    [Test]
    public async Task QueryCalendarNames_OrdersOrdinallyAndPages()
    {
        await store.AddCalendar("b", new HolidayCalendar());
        await store.AddCalendar("a", new HolidayCalendar());
        await store.AddCalendar("C", new HolidayCalendar());

        PagedResult<string> all = await store.QueryCalendarNames(new CalendarQuery());
        all.Items.Should().Equal(["C", "a", "b"], "calendar names are ordered ordinally, which puts upper case first");

        PagedResult<string> page = await store.QueryCalendarNames(new CalendarQuery { Skip = 1, Take = 1, IncludeTotalCount = true });
        page.Items.Should().Equal(["a"]);
        page.HasMore.Should().BeTrue();
        page.TotalCount.Should().Be(3);
    }

    [Test]
    public async Task QueryCalendarNames_NameMatcherSelectsAndStillOrders()
    {
        await store.AddCalendar("holiday-easter", new HolidayCalendar());
        await store.AddCalendar("holiday-xmas", new HolidayCalendar());
        await store.AddCalendar("workday", new HolidayCalendar());

        PagedResult<string> prefixed = await store.QueryCalendarNames(new CalendarQuery
        {
            Name = NameMatcher.NameStartsWith("holiday-")
        });
        prefixed.Items.Should().Equal(["holiday-easter", "holiday-xmas"]);

        PagedResult<string> contained = await store.QueryCalendarNames(new CalendarQuery
        {
            Name = NameMatcher.NameContains("day"),
            IncludeTotalCount = true
        });
        contained.Items.Should().Equal(["holiday-easter", "holiday-xmas", "workday"],
            "a filtered listing keeps the ordinal ordering an unfiltered one has");
        contained.TotalCount.Should().Be(3, "the total counts what the filter selects");

        PagedResult<string> exact = await store.QueryCalendarNames(new CalendarQuery
        {
            Name = NameMatcher.NameEquals("workday")
        });
        exact.Items.Should().Equal(["workday"]);

        PagedResult<string> none = await store.QueryCalendarNames(new CalendarQuery
        {
            Name = NameMatcher.NameEquals("nope")
        });
        none.Items.Should().BeEmpty();
        none.HasMore.Should().BeFalse();
    }

    [Test]
    public async Task GetJobDetails_SkipsMissingKeysAndDeduplicates()
    {
        IJobDetail first = await AddJob("j1", "g");
        IJobDetail second = await AddJob("j2", "g");

        List<IJobDetail> jobs = await store.GetJobs([second.Key, new JobKey("missing", "g"), second.Key, first.Key]);

        jobs.Select(x => x.Key).Should().Equal(
            [second.Key, first.Key],
            "the result follows the order of the keys asked for, keeps the first of a duplicate and simply omits what does not exist");
    }

    [Test]
    public async Task GetJobDetails_ReturnsCopiesOfTheStoredJobs()
    {
        IJobDetail job = await AddJob("j1", "g");

        List<IJobDetail> first = await store.GetJobs([job.Key]);
        List<IJobDetail> second = await store.GetJobs([job.Key]);

        first.Single().Should().NotBeSameAs(second.Single(), "callers must not get a handle on the store's own job detail");
        first.Single().Key.Should().Be(job.Key);
    }

    [Test]
    public async Task GetTriggers_SkipsMissingKeysAndDeduplicates()
    {
        IJobDetail job = await AddJob("job", "g");
        IOperableTrigger first = await AddTrigger("t1", "g", job.Key);
        IOperableTrigger second = await AddTrigger("t2", "g", job.Key);

        List<IOperableTrigger> triggers = await store.GetTriggers([second.Key, new TriggerKey("missing", "g"), second.Key, first.Key]);

        triggers.Select(x => x.Key).Should().Equal(
            [second.Key, first.Key],
            "the result follows the order of the keys asked for, keeps the first of a duplicate and simply omits what does not exist");
    }

    [Test]
    public async Task GetTriggers_ReturnsClonesThatCannotMutateTheStore()
    {
        IJobDetail job = await AddJob("job", "g");
        IOperableTrigger trigger = await AddTrigger("t1", "g", job.Key);

        List<IOperableTrigger> fetched = await store.GetTriggers([trigger.Key]);
        fetched.Single().Description = "mutated";

        List<IOperableTrigger> refetched = await store.GetTriggers([trigger.Key]);
        refetched.Single().Description.Should().BeNull("a bulk fetch hands out clones, so mutating one must not reach the store");

        PagedResult<TriggerHeader> listed = await store.QueryTriggers(new TriggerQuery());
        listed.Items.Single().Description.Should().BeNull("the listing reads the store's own trigger, which was never mutated");
    }

    [Test]
    public async Task BulkFetches_TolerateAnEmptyRequest()
    {
        List<IJobDetail> jobs = await store.GetJobs([]);
        List<IOperableTrigger> triggers = await store.GetTriggers([]);

        jobs.Should().BeEmpty();
        triggers.Should().BeEmpty();
    }

    [Test]
    public async Task QueryJobs_NameMatcherSelectsAcrossGroups()
    {
        await StoreJobs("g1", "report-daily", "report-weekly", "other");
        await StoreJobs("g2", "report-daily");

        PagedResult<JobHeader> result = await store.QueryJobs(new JobQuery { Name = NameMatcher<JobKey>.NameStartsWith("report") });

        result.Items.Select(x => x.Key).Should().Equal(
            [new JobKey("report-daily", "g1"), new JobKey("report-weekly", "g1"), new JobKey("report-daily", "g2")],
            "a name filter is independent of the group, and the result stays ordered by group then name");
    }

    [Test]
    public async Task QueryJobs_NameAndGroupFiltersCombineWithAnd()
    {
        await StoreJobs("g1", "report-daily", "other");
        await StoreJobs("g2", "report-daily");

        PagedResult<JobHeader> result = await store.QueryJobs(new JobQuery
        {
            Group = GroupMatcher<JobKey>.GroupEquals("g1"),
            Name = NameMatcher<JobKey>.NameEquals("report-daily"),
            IncludeTotalCount = true
        });

        result.Items.Select(x => x.Key).Should().Equal([new JobKey("report-daily", "g1")]);
        result.TotalCount.Should().Be(1, "the total counts the matches of both filters");
    }

    [Test]
    public async Task QueryTriggers_NameMatcherSelectsByTriggerName()
    {
        IJobDetail job = await AddJob("job", "g");
        await AddTrigger("nightly-a", "tg", job.Key);
        await AddTrigger("nightly-b", "tg", job.Key);
        await AddTrigger("hourly", "tg", job.Key);

        PagedResult<TriggerHeader> result = await store.QueryTriggers(new TriggerQuery { Name = NameMatcher<TriggerKey>.NameStartsWith("nightly") });

        result.Items.Select(x => x.Key.Name).Should().Equal(["nightly-a", "nightly-b"]);
    }

    [Test]
    public async Task QueryJobGroups_NameSelectsTheOneGroup()
    {
        await StoreJobs("g1", "j1");
        await StoreJobs("g2", "j1");
        await store.PauseJobGroups(GroupMatcher<JobKey>.GroupEquals("g2"));

        PagedResult<JobGroup> named = await store.QueryJobGroups(new JobGroupQuery { Name = NameMatcher.NameEquals("g2") });
        named.Items.Should().Equal([new JobGroup("g2", true)], "an exact name filter selects one group and no other");

        PagedResult<JobGroup> unpaused = await store.QueryJobGroups(new JobGroupQuery { Name = NameMatcher.NameEquals("g2"), Paused = false });
        unpaused.Items.Should().BeEmpty("the name and paused filters combine, and g2 is paused");

        PagedResult<JobGroup> missing = await store.QueryJobGroups(new JobGroupQuery { Name = NameMatcher.NameEquals("nope"), Paused = true });
        missing.Items.Should().BeEmpty();
    }

    [Test]
    public async Task QueryTriggerGroups_NameSelectsTheOneGroup()
    {
        IJobDetail job = await AddJob("job", "g");
        await AddTrigger("t1", "tg1", job.Key);
        await AddTrigger("t1", "tg2", job.Key);
        await store.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("tg2"));

        PagedResult<TriggerGroup> named = await store.QueryTriggerGroups(new TriggerGroupQuery { Name = NameMatcher.NameEquals("tg2") });
        named.Items.Should().Equal([new TriggerGroup("tg2", true)]);

        PagedResult<TriggerGroup> paused = await store.QueryTriggerGroups(new TriggerGroupQuery { Name = NameMatcher.NameEquals("tg1"), Paused = true });
        paused.Items.Should().BeEmpty("tg1 is not paused");
    }

    [Test]
    public async Task QueryJobGroups_NameMatchesByPatternAndNotOnlyByEquality()
    {
        await StoreJobs("reports-nightly", "j1");
        await StoreJobs("reports-hourly", "j2");
        await StoreJobs("imports", "j3");

        PagedResult<JobGroup> prefixed = await store.QueryJobGroups(new JobGroupQuery { Name = NameMatcher.NameStartsWith("reports-") });
        prefixed.Items.Select(x => x.Name).Should().Equal(["reports-hourly", "reports-nightly"],
            "a group's name filter is a matcher, so a tenant's or a subsystem's groups can be listed without reading the rest");

        PagedResult<JobGroup> contained = await store.QueryJobGroups(new JobGroupQuery { Name = NameMatcher.NameContains("port") });
        contained.Items.Select(x => x.Name).Should().Equal(["imports", "reports-hourly", "reports-nightly"]);

        PagedResult<JobGroup> suffixed = await store.QueryJobGroups(new JobGroupQuery { Name = NameMatcher.NameEndsWith("hourly") });
        suffixed.Items.Select(x => x.Name).Should().Equal(["reports-hourly"]);
    }

    [Test]
    public async Task QueryTriggerGroups_NameMatchesByPatternOverPausedGroupsToo()
    {
        IJobDetail job = await AddJob("job", "g");
        await AddTrigger("t1", "tenant-a", job.Key);
        await AddTrigger("t2", "tenant-b", job.Key);
        await AddTrigger("t3", "shared", job.Key);
        await store.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupStartsWith("tenant-"));

        PagedResult<TriggerGroup> prefixed = await store.QueryTriggerGroups(new TriggerGroupQuery { Name = NameMatcher.NameStartsWith("tenant-"), Paused = true });
        prefixed.Items.Select(x => x.Name).Should().Equal(["tenant-a", "tenant-b"],
            "the paused listing reads the paused groups and filters them by the same matcher the others use");

        PagedResult<TriggerGroup> unpaused = await store.QueryTriggerGroups(new TriggerGroupQuery { Name = NameMatcher.NameStartsWith("tenant-"), Paused = false });
        unpaused.Items.Should().BeEmpty("both tenant groups are paused");
    }

    private async ValueTask<IJobDetail> AddJob(string name, string group)
    {
        IJobDetail job = JobBuilder.Create()
            .OfType<NoOpJob>()
            .WithIdentity(name, group)
            .StoreDurably()
            .Build();

        await store.AddJob(job);
        return job;
    }

    private async ValueTask StoreJobs(string group, params string[] names)
    {
        foreach (string name in names)
        {
            await AddJob(name, group);
        }
    }

    private async ValueTask<IOperableTrigger> AddTrigger(string name, string group, JobKey jobKey, string calendarName = null)
    {
        return await StoreBuiltTrigger(TriggerBuilder.Create()
            .WithIdentity(name, group)
            .ForJob(jobKey)
            .StartAt(startTime)
            .WithCalendarName(calendarName)
            .WithCronSchedule("0 0 12 * * ?"));
    }

    private async ValueTask<IOperableTrigger> StoreBuiltTrigger(TriggerBuilder<IJob> builder)
    {
        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.ComputeFirstFireTimeUtc(null);
        await store.AddTrigger(trigger);
        return trigger;
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    private sealed class StatefulTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// A trigger no persistence delegate can handle, which the ADO store would therefore store as a blob.
    /// </summary>
    private sealed class TriggerWithAdditionalProperties : SimpleTriggerImpl
    {
        public override bool HasAdditionalProperties => true;
    }
}
