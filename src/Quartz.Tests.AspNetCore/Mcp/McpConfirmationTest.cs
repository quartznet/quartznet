using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Mcp;

/// <summary>
/// M4: <c>reschedule</c> and <c>unschedule</c> change nothing without a confirmation token, and a token confirms the
/// one call it was previewed for — the same tool, scheduler, arguments and caller, the trigger in the state previewed,
/// once, before it expires — and is never logged.
/// </summary>
public sealed class McpConfirmationTest
{
    private McpTestHost? host;
    private FakeTimeProvider time = null!;
    private TriggerKey trigger = null!;

    [SetUp]
    public void SetUp()
    {
        // The protector's own expiry runs on the wall clock, so the fake starts there.
        time = new FakeTimeProvider(DateTimeOffset.UtcNow);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (host is not null)
        {
            await host.DisposeAsync();
            host = null;
        }
    }

    [Test]
    public async Task APreviewChangesNothingAndItsTokenAppliesTheSameCall()
    {
        McpClient admin = await Start();

        JsonElement preview = McpTestHost.Answer(await Reschedule(admin, "0 0 3 * * ?"));

        preview.GetProperty("applied").GetBoolean().Should().BeFalse();
        preview.GetProperty("preview").GetProperty("effects")[0].GetString().Should().Contain("→");
        preview.GetProperty("expiresAtUtc").GetDateTimeOffset().Should().Be(time.GetUtcNow() + TimeSpan.FromMinutes(5));
        (await CronOf()).Should().Be("0 0 2 * * ?", "a preview changes nothing");

        JsonElement applied = McpTestHost.Answer(await Reschedule(admin, "0 0 3 * * ?", Token(preview)));

        applied.GetProperty("applied").GetBoolean().Should().BeTrue();
        (await CronOf()).Should().Be("0 0 3 * * ?");
    }

    [Test]
    public async Task ATokenForOtherArgumentsIsRefused()
    {
        McpClient admin = await Start();
        string token = Token(McpTestHost.Answer(await Reschedule(admin, "0 0 3 * * ?")));

        JsonElement error = McpTestHost.Error(await Reschedule(admin, "0 0 4 * * ?", token));

        error.GetProperty("code").GetString().Should().Be("confirmation_refused");
        error.GetProperty("message").GetString().Should().Contain("arguments");
        (await CronOf()).Should().Be("0 0 2 * * ?");
    }

    [Test]
    public async Task ATokenForAnotherToolIsRefused()
    {
        McpClient admin = await Start();
        string token = Token(McpTestHost.Answer(await Unschedule(admin)));

        McpTestHost.Error(await Reschedule(admin, "0 0 3 * * ?", token)).GetProperty("message").GetString().Should().Contain("tool");
        (await host!.Scheduler.GetTrigger(trigger)).Should().NotBeNull();
    }

    [Test]
    public async Task ATokenForAnotherSchedulerIsRefused()
    {
        McpClient admin = await Start();

        // The same scheduler behind a target as well, so the trigger is there under both keys.
        host!.Services.GetRequiredService<Quartz.Configuration.SchedulerTargets>().Add(new Quartz.Configuration.SchedulerTarget { Name = "mirror", Origin = SchedulerOrigin.Remote });
        host.Services.GetRequiredService<ISchedulerRepository>().Bind(new FakeProxyScheduler(host.Scheduler, "mirror"), "mirror");

        string token = Token(McpTestHost.Answer(await Unschedule(admin)));
        JsonElement error = McpTestHost.Error(await Unschedule(admin, token, scheduler: "mirror/" + host.Scheduler.SchedulerName));

        error.GetProperty("message").GetString().Should().Contain("scheduler");
    }

    [Test]
    public async Task ATokenForAnotherCallerIsRefused()
    {
        // An operator may unschedule here, so the second caller is refused for being somebody else, not for the role.
        McpClient admin = await Start(roles => roles.Operations["UnscheduleJob"] = SchedulerAccess.Operate);
        McpClient @operator = await host!.Connect(RoleAuthenticationExtensions.Operator);
        string token = Token(McpTestHost.Answer(await Unschedule(admin)));

        McpTestHost.Error(await Unschedule(@operator, token)).GetProperty("message").GetString().Should().Contain("caller");
        (await host.Scheduler.GetTrigger(trigger)).Should().NotBeNull();
    }

    [Test]
    public async Task AnExpiredTokenIsRefused()
    {
        McpClient admin = await Start();
        string token = Token(McpTestHost.Answer(await Unschedule(admin)));

        time.Advance(TimeSpan.FromMinutes(6));

        McpTestHost.Error(await Unschedule(admin, token)).GetProperty("message").GetString().Should().Contain("expired or is not valid");
        host!.Logs.WithEventId(9502).Should().ContainSingle().Which.Properties["Reason"].Should().Be("expired or not valid");
    }

    [Test]
    public async Task ATamperedTokenIsRefused()
    {
        McpClient admin = await Start();
        string token = Token(McpTestHost.Answer(await Unschedule(admin)));
        char[] chars = token.ToCharArray();
        chars[chars.Length / 2] = chars[chars.Length / 2] == 'A' ? 'B' : 'A';

        McpTestHost.Error(await Unschedule(admin, new string(chars))).GetProperty("message").GetString().Should().Contain("expired or is not valid");
        (await host!.Scheduler.GetTrigger(trigger)).Should().NotBeNull();
    }

    [Test]
    public async Task ATriggerThatChangedSinceThePreviewIsNotChanged()
    {
        McpClient admin = await Start();
        string token = Token(McpTestHost.Answer(await Reschedule(admin, "0 0 3 * * ?")));

        await host!.Scheduler.PauseTrigger(trigger);

        McpTestHost.Error(await Reschedule(admin, "0 0 3 * * ?", token)).GetProperty("message").GetString().Should().Contain("changed since the preview");
        (await CronOf()).Should().Be("0 0 2 * * ?");
    }

    [Test]
    public async Task AReplayedConfirmationIsRefusedByItsNonceOrFindsNothingToChange()
    {
        McpClient admin = await Start();

        // Rescheduled to the schedule it has: the state the preview read is the state after it, so only the nonce
        // stands between the replay and a second change.
        string same = Token(McpTestHost.Answer(await Reschedule(admin, "0 0 2 * * ?")));
        McpTestHost.Answer(await Reschedule(admin, "0 0 2 * * ?", same)).GetProperty("applied").GetBoolean().Should().BeTrue();
        McpTestHost.Error(await Reschedule(admin, "0 0 2 * * ?", same)).GetProperty("message").GetString().Should().Contain("used already");

        string unschedule = Token(McpTestHost.Answer(await Unschedule(admin)));
        McpTestHost.Answer(await Unschedule(admin, unschedule)).GetProperty("applied").GetBoolean().Should().BeTrue();
        McpTestHost.Error(await Unschedule(admin, unschedule)).GetProperty("code").GetString().Should().Be("not_found",
            "an unschedule replayed on any instance finds no trigger");
    }

    [Test]
    public async Task TheTokenIsInNoLogRecordAtAnyLevel()
    {
        McpClient admin = await Start();
        string token = Token(McpTestHost.Answer(await Reschedule(admin, "0 0 3 * * ?")));
        await Reschedule(admin, "0 0 4 * * ?", token);
        await Reschedule(admin, "0 0 3 * * ?", token);

        host!.Logs.Entries.Should().NotBeEmpty();
        host.Logs.Entries.Should().NotContain(entry => entry.Message.Contains(token, StringComparison.Ordinal)
                                                       || entry.Properties.Values.Any(value => value != null && value.Contains(token, StringComparison.Ordinal)),
            "a confirmation token travels in the arguments, and no line carries an argument");
    }

    [Test]
    public async Task AnAnonymousCallerIsRefusedTheAdministerToolsWhateverThePolicySays()
    {
        host = await McpTestHost.Start(mcp: options => options.OperationAuthorizationPolicy = AllowEveryone.Policy, services: AllowEveryone.Add);
        (_, trigger) = await host.ScheduleJob();

        JsonElement error = McpTestHost.Error(await Unschedule(await host.Connect(role: null)));

        error.GetProperty("message").GetString().Should().Contain("identity");
    }

    [Test]
    public async Task TheUnschedulePreviewSaysANonDurableJobGoesWithItsLastTrigger()
    {
        McpClient admin = await Start();
        IJobDetail job = JobBuilder.Create<DummyJob>().WithIdentity("once", "adhoc").Build();
        ITrigger only = TriggerBuilder.Create().WithIdentity("once", "adhoc").ForJob(job).StartAt(DateTimeOffset.UtcNow.AddDays(1)).Build();
        await host!.Scheduler.ScheduleJob(job, only);

        JsonElement preview = McpTestHost.Answer(await McpTestHost.Call(admin, "unschedule", new { scheduler = host.Scheduler.SchedulerName, group = "adhoc", name = "once" }));

        preview.GetProperty("preview").GetProperty("effects").EnumerateArray().Select(static x => x.GetString())
            .Should().Contain(effect => effect!.Contains("not durable", StringComparison.Ordinal));
    }

    private async Task<McpClient> Start(Action<SchedulerRoles>? roles = null)
    {
        host = await McpTestHost.Start(roles: roles, services: services => services.AddSingleton<TimeProvider>(time));
        (_, trigger) = await host.ScheduleJob();
        return await host.Connect(RoleAuthenticationExtensions.Administrator);
    }

    private ValueTask<CallToolResult> Reschedule(McpClient client, string cron, string? token = null)
    {
        return McpTestHost.Call(client, "reschedule", token is null
            ? new Dictionary<string, object?> { ["scheduler"] = host!.Scheduler.SchedulerName, ["group"] = trigger.Group, ["name"] = trigger.Name, ["cronExpression"] = cron }
            : new Dictionary<string, object?> { ["scheduler"] = host!.Scheduler.SchedulerName, ["group"] = trigger.Group, ["name"] = trigger.Name, ["cronExpression"] = cron, ["confirmationToken"] = token });
    }

    private ValueTask<CallToolResult> Unschedule(McpClient client, string? token = null, string? scheduler = null)
    {
        Dictionary<string, object?> arguments = new()
        {
            ["scheduler"] = scheduler ?? host!.Scheduler.SchedulerName,
            ["group"] = trigger.Group,
            ["name"] = trigger.Name,
        };
        if (token is not null)
        {
            arguments["confirmationToken"] = token;
        }

        return McpTestHost.Call(client, "unschedule", arguments);
    }

    private static string Token(JsonElement preview) => preview.GetProperty("confirmationToken").GetString()!;

    private async Task<string?> CronOf()
    {
        return (await host!.Scheduler.GetTrigger(trigger) as ICronTrigger)?.CronExpressionString;
    }
}
