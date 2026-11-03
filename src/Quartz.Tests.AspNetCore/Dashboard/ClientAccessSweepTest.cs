using System.Security.Claims;

using FakeItEasy;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Dashboard.Support;

using Routes = Quartz.HttpApiContract.SchedulerRoutes;
using WireRoute = Quartz.HttpApiContract.WireRoute;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// Every member of the dashboard's client that changes something, called by a visitor of each operator
/// role: allowed at the level its HTTP API route needs and refused below it — before anything reaches the
/// scheduler, so a refused call is never sent to a scheduler in another process.
/// </summary>
/// <remarks>
/// The policy is a real one, built with <c>RequireSchedulerRoles</c> and evaluated by ASP.NET Core's own
/// <see cref="IAuthorizationService" />, and the visitor is a principal with a role claim: the promise is
/// that an application names three roles and Quartz does the rest. The scheduler is a fake, which records
/// whether a call got as far as it.
/// </remarks>
public class ClientAccessSweepTest
{
    private const string SchedulerName = "acme";
    private const string Policy = "QuartzOperators";

    private static readonly JobKeyDto Job = new("reports", "nightly");
    private static readonly TriggerKeyDto Trigger = new("reports", "nightly");

    /// <summary>
    /// Every mutating member, the route it is, and whether the call carries job data.
    /// </summary>
    private static readonly (string Member, WireRoute Route, bool Data, Func<InProcessQuartzApiClient, Task> Call)[] Operations =
    [
        ("Start", Routes.Start, false, async client => await client.Start(SchedulerName)),
        ("Standby", Routes.Standby, false, async client => await client.Standby(SchedulerName)),
        ("Shutdown", Routes.Shutdown, false, async client => await client.Shutdown(SchedulerName)),
        ("PauseAll", Routes.PauseAll, false, async client => await client.PauseAll(SchedulerName)),
        ("PauseAllWith", Routes.PauseAll, false, async client => await client.PauseAllWith(SchedulerName, new PauseDetails { Reason = "maintenance" })),
        ("ResumeAll", Routes.ResumeAll, false, async client => await client.ResumeAll(SchedulerName)),
        ("PauseJob", Routes.PauseJob, false, async client => await client.PauseJob(SchedulerName, Job)),
        ("PauseJobWith", Routes.PauseJob, false, async client => await client.PauseJobWith(SchedulerName, Job, new PauseDetails { Reason = "maintenance" })),
        ("ResumeJob", Routes.ResumeJob, false, async client => await client.ResumeJob(SchedulerName, Job)),
        ("TriggerJob", Routes.TriggerJob, false, async client => await client.TriggerJob(SchedulerName, Job)),
        ("TriggerJob, empty map", Routes.TriggerJob, false, async client => await client.TriggerJob(SchedulerName, Job, new JobDataMap())),
        ("TriggerJob, with data", Routes.TriggerJob, true, async client => await client.TriggerJob(SchedulerName, Job, new JobDataMap { ["command"] = "rm" })),
        ("Interrupt", Routes.InterruptJob, false, async client => await client.Interrupt(SchedulerName, Job)),
        ("InterruptFireInstance", Routes.InterruptJobInstance, false, async client => await client.InterruptFireInstance(SchedulerName, "fire-1")),
        ("DeleteJob", Routes.DeleteJob, false, async client => await client.DeleteJob(SchedulerName, Job)),
        ("AddJob", Routes.AddJob, false, async client => await client.AddJob(SchedulerName, new AddJobRequest(NewJob(), Replace: true, StoreNonDurableWhileAwaitingScheduling: null))),
        ("PauseTrigger", Routes.PauseTrigger, false, async client => await client.PauseTrigger(SchedulerName, Trigger)),
        ("PauseTriggerWith", Routes.PauseTrigger, false, async client => await client.PauseTriggerWith(SchedulerName, Trigger, new PauseDetails { Reason = "maintenance" })),
        ("ResumeTrigger", Routes.ResumeTrigger, false, async client => await client.ResumeTrigger(SchedulerName, Trigger)),
        ("ResetTriggerFromErrorState", Routes.ResetTriggerFromErrorState, false, async client => await client.ResetTriggerFromErrorState(SchedulerName, Trigger)),
        ("UpdateTriggerDetails", Routes.UpdateTriggerDetails, false, async client => await client.UpdateTriggerDetails(SchedulerName, Trigger, new TriggerDetailsUpdate().WithDescription("edited"))),
        ("PauseTriggers", Routes.PauseTriggerKeys, false, async client => await client.PauseTriggers(SchedulerName, [Trigger])),
        ("PauseTriggersWith", Routes.PauseTriggerKeys, false, async client => await client.PauseTriggersWith(SchedulerName, [Trigger], new PauseDetails { Reason = "maintenance" })),
        ("PauseJobsWith", Routes.PauseJobKeys, false, async client => await client.PauseJobsWith(SchedulerName, [Job], details: null)),
        ("ResumeTriggers", Routes.ResumeTriggerKeys, false, async client => await client.ResumeTriggers(SchedulerName, [Trigger])),
        ("UnscheduleJobs", Routes.UnscheduleJobs, false, async client => await client.UnscheduleJobs(SchedulerName, [Trigger])),
        ("Backfill", Routes.BackfillTrigger, false, async client => await client.Backfill(SchedulerName, Trigger, Hour, Hour.AddHours(1))),
        ("ScheduleJob", Routes.ScheduleJob, false, async client => await client.ScheduleJob(SchedulerName, new ScheduleJobRequest(NewTrigger(), NewJob()))),
        ("UnscheduleJob", Routes.UnscheduleJob, false, async client => await client.UnscheduleJob(SchedulerName, Trigger)),
        ("RescheduleJob", Routes.RescheduleJob, false, async client => await client.RescheduleJob(SchedulerName, Trigger, new RescheduleRequest(NewTrigger()))),
        ("AddCalendar", Routes.AddCalendar, false, async client => await client.AddCalendar(SchedulerName, new AddCalendarRequest("holidays", new BaseCalendar(), Replace: true, UpdateTriggers: false))),
        ("DeleteCalendar", Routes.DeleteCalendar, false, async client => await client.DeleteCalendar(SchedulerName, "holidays")),
    ];

    private static readonly DateTimeOffset Hour = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The sweep is every member the client has: one added later and left out of the table would be a
    /// member nobody checked.
    /// </summary>
    [Test]
    public void TheSweepNamesEveryMutatingRouteTheDashboardCalls()
    {
        Operations.Select(operation => operation.Route).Distinct().Should().HaveCount(26,
            "the client reaches twenty-six of the thirty-nine mutating routes; the others are group and bulk forms, clearing and the execution limits, which it does not call");
        Operations.Should().OnlyContain(operation => operation.Route.Mutates);
    }

    [TestCase(null)]
    [TestCase(SchedulerAccess.View)]
    [TestCase(SchedulerAccess.Operate)]
    [TestCase(SchedulerAccess.Administer)]
    public async Task EveryOperationIsAllowedAtItsLevelAndRefusedBelowIt(SchedulerAccess? role)
    {
        List<string> wrong = [];
        foreach ((string member, WireRoute route, bool data, Func<InProcessQuartzApiClient, Task> call) in Operations)
        {
            IScheduler scheduler = FakeScheduler();
            InProcessQuartzApiClient client = ClientFor(scheduler, role, policy: Policy);

            SchedulerAccess required = Routes.RequiredAccess(route, data);
            bool expected = role >= required;

            bool refused = await IsRefused(call, client);
            if (refused == expected)
            {
                wrong.Add($"{member} needs {required}; a visitor at {role?.ToString() ?? "no role"} was {(refused ? "refused" : "allowed")}");
            }

            if (refused && ReachedTheScheduler(scheduler) is { } reached)
            {
                wrong.Add($"{member} was refused after {reached} reached the scheduler");
            }
        }

        wrong.Should().BeEmpty();
    }

    /// <summary>
    /// The first test: with no operation policy every member is what it was in 4.7 — nothing refused for
    /// a role, whoever the visitor is.
    /// </summary>
    [Test]
    public async Task WithoutAnOperationPolicyNothingIsRefusedForARole()
    {
        foreach ((string member, _, _, Func<InProcessQuartzApiClient, Task> call) in Operations)
        {
            InProcessQuartzApiClient client = ClientFor(FakeScheduler(), role: null, policy: null);

            (await IsRefused(call, client)).Should().BeFalse($"{member} is refused only by an operation policy, and none is set");
        }
    }

    /// <summary>
    /// <see cref="QuartzDashboardOptions.ReadOnly" /> stays the ceiling, refused as before 4.8 with the same
    /// exception, ahead of the role: an administrator of a read-only dashboard reads.
    /// </summary>
    [Test]
    public async Task ReadOnlyIsRefusedFirstAndAsBefore()
    {
        IScheduler scheduler = FakeScheduler();
        InProcessQuartzApiClient client = ClientFor(scheduler, SchedulerAccess.Administer, Policy, readOnly: true);

        Func<Task> delete = async () => await client.DeleteJob(SchedulerName, Job);

        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("Quartz dashboard is configured as read-only.");
    }

    /// <summary>
    /// The refusal names the operation and the level, and nothing about what the visitor holds.
    /// </summary>
    [Test]
    public async Task TheRefusalNamesTheOperationAndTheLevel()
    {
        InProcessQuartzApiClient client = ClientFor(FakeScheduler(), SchedulerAccess.Operate, Policy);

        Func<Task> trigger = async () => await client.TriggerJob(SchedulerName, Job, new JobDataMap { ["command"] = "rm" });

        (await trigger.Should().ThrowAsync<UnauthorizedAccessException>())
            .Which.Message.Should().Be("Not authorized for TriggerJob with job data on scheduler 'acme': it needs Administer.");
    }

    /// <summary>
    /// A visitor in no role may not read either: under an operation policy, seeing a scheduler is View.
    /// </summary>
    [Test]
    public async Task AVisitorInNoRoleCannotRead()
    {
        InProcessQuartzApiClient client = ClientFor(FakeScheduler(), role: null, Policy);

        Func<Task> read = async () => await client.GetScheduler(SchedulerName);

        await read.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("Not authorized for scheduler 'acme'.");
    }

    private static async Task<bool> IsRefused(Func<InProcessQuartzApiClient, Task> call, InProcessQuartzApiClient client)
    {
        try
        {
            await call(client);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // The role refused: the operation, or — for a visitor in no role — seeing the scheduler at all.
            return true;
        }
        catch (Exception)
        {
            // The fake answers with defaults some members cannot use; what matters is that the call got
            // past the role, which a refusal would not have.
            return false;
        }
    }

    /// <summary>
    /// The first call to the scheduler that did work rather than read a property, or null for none.
    /// </summary>
    private static string? ReachedTheScheduler(IScheduler scheduler)
    {
        return Fake.GetCalls(scheduler)
            .Select(call => call.Method)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .FirstOrDefault();
    }

    private static IScheduler FakeScheduler()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(SchedulerName);
        return scheduler;
    }

    private static InProcessQuartzApiClient ClientFor(IScheduler scheduler, SchedulerAccess? role, string? policy, bool readOnly = false)
    {
        SchedulerRepository repository = new();
        repository.Bind(scheduler);

        IOptions<QuartzDashboardOptions> options = Options.Create(new QuartzDashboardOptions
        {
            OperationAuthorizationPolicy = policy,
            ReadOnly = readOnly,
        });

        ServiceCollection services = new();
        services.AddLogging();
        services.AddAuthorizationBuilder().AddPolicy(Policy, builder => builder.RequireSchedulerRoles(roles =>
        {
            roles.Viewers.Add("quartz-viewer");
            roles.Operators.Add("quartz-operator");
            roles.Administrators.Add("quartz-admin");
        }));
        IAuthorizationService authorizationService = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();

        TestAuthenticationStateProvider visitor = new() { User = Principal(role) };

        return new InProcessQuartzApiClient(
            repository,
            A.Fake<ISchedulerRegistry>(),
            options,
            A.Fake<IExecutionHistoryStore>(),
            new ServiceCollection().BuildServiceProvider(),
            new SchedulerAuthorization(options, authorizationService, visitor),
            new AttachedStores(new SchedulerWindowRegistry(), new SchedulerTargets()));
    }

    private static ClaimsPrincipal Principal(SchedulerAccess? role)
    {
        List<Claim> claims = [new Claim(ClaimTypes.Name, "visitor")];
        string? roleName = role switch
        {
            SchedulerAccess.View => "quartz-viewer",
            SchedulerAccess.Operate => "quartz-operator",
            SchedulerAccess.Administer => "quartz-admin",
            _ => null,
        };

        if (roleName is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, roleName));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static JobDetailDto NewJob()
    {
        return new JobDetailDto("nightly", "reports", "Quartz.Tests.AspNetCore.Support.DummyJob", null, Durable: true,
            RequestsRecovery: false, ConcurrentExecutionDisallowed: null, PersistJobDataAfterExecution: null, new JobDataMap());
    }

    private static ITrigger NewTrigger()
    {
        return TriggerBuilder.Create().WithIdentity("nightly", "reports").ForJob("nightly", "reports").StartAt(Hour).Build();
    }
}
