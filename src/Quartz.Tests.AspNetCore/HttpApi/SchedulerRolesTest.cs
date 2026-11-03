using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The policy <see cref="SchedulerRolesAuthorizationExtensions.RequireSchedulerRoles" /> builds: which role
/// grants which level, what an override may move, and what it refuses to build.
/// </summary>
/// <remarks>
/// Evaluated through ASP.NET Core's own <see cref="IAuthorizationService" /> with nothing registered beside
/// <c>AddAuthorization()</c>, which is the promise: the requirement is its own handler.
/// </remarks>
public class SchedulerRolesTest
{
    private const string Policy = "QuartzOperators";

    [TestCase(null, SchedulerAccess.View, false)]
    [TestCase("viewer", SchedulerAccess.View, true)]
    [TestCase("viewer", SchedulerAccess.Operate, false)]
    [TestCase("operator", SchedulerAccess.View, true)]
    [TestCase("operator", SchedulerAccess.Operate, true)]
    [TestCase("operator", SchedulerAccess.Administer, false)]
    [TestCase("admin", SchedulerAccess.Administer, true)]
    [TestCase("stranger", SchedulerAccess.View, false)]
    public async Task ARoleGrantsItsLevelAndEveryOneBelowIt(string? role, SchedulerAccess asked, bool granted)
    {
        IAuthorizationService authorization = Authorization(roles => { });

        AuthorizationResult result = await authorization.AuthorizeAsync(
            Principal(role), new SchedulerResource("acme") { Operation = "PauseJob", Access = asked }, Policy);

        result.Succeeded.Should().Be(granted);
    }

    /// <summary>
    /// A principal in several roles holds the highest of them.
    /// </summary>
    [Test]
    public async Task SeveralRolesGrantTheHighest()
    {
        IAuthorizationService authorization = Authorization(roles => { });

        AuthorizationResult result = await authorization.AuthorizeAsync(
            Principal("viewer", "admin"), new SchedulerResource("acme") { Operation = "Shutdown", Access = SchedulerAccess.Administer }, Policy);

        result.Succeeded.Should().BeTrue();
    }

    /// <summary>
    /// A resource that names no level is asked at the level of the operation it names, and one that names
    /// neither is a question about seeing the scheduler, which is View.
    /// </summary>
    [TestCase("Shutdown", false)]
    [TestCase("GetJobDetails", true)]
    [TestCase(null, true)]
    public async Task AResourceWithNoLevelIsAskedAtItsOperations(string? operation, bool viewerGranted)
    {
        IAuthorizationService authorization = Authorization(roles => { });

        AuthorizationResult result = await authorization.AuthorizeAsync(
            Principal("viewer"), new SchedulerResource("acme") { Operation = operation }, Policy);

        result.Succeeded.Should().Be(viewerGranted);
    }

    [Test]
    public async Task AnOverrideMovesAnOperationEitherWay()
    {
        IAuthorizationService authorization = Authorization(roles =>
        {
            roles.Operations["AddCalendar"] = SchedulerAccess.Operate;
            roles.Operations["Standby"] = SchedulerAccess.Administer;
        });

        (await authorization.AuthorizeAsync(Principal("operator"), Resource("AddCalendar", SchedulerAccess.Administer), Policy))
            .Succeeded.Should().BeTrue("the override lowers adding a calendar to Operate");
        (await authorization.AuthorizeAsync(Principal("operator"), Resource("Standby", SchedulerAccess.Operate), Policy))
            .Succeeded.Should().BeFalse("the override raises stand-by to Administer");
    }

    /// <summary>
    /// Job data keeps a trigger-now at Administer whatever the override for <c>TriggerJob</c> says.
    /// </summary>
    [Test]
    public async Task JobDataStaysAdministerWhateverTheOverride()
    {
        IAuthorizationService authorization = Authorization(roles => roles.Operations["TriggerJob"] = SchedulerAccess.Operate);

        (await authorization.AuthorizeAsync(Principal("operator"), Resource("TriggerJob", SchedulerAccess.Administer), Policy))
            .Succeeded.Should().BeFalse("a stored NativeJob runs the command its job data names");
        (await authorization.AuthorizeAsync(Principal("operator"), Resource("TriggerJob", SchedulerAccess.Operate), Policy))
            .Succeeded.Should().BeTrue();
    }

    /// <summary>
    /// Evaluated against anything but a <see cref="SchedulerResource" />, the policy grants nothing.
    /// </summary>
    [Test]
    public async Task AnotherResourceIsGrantedNothing()
    {
        IAuthorizationService authorization = Authorization(roles => { });

        (await authorization.AuthorizeAsync(Principal("admin"), resource: "acme", Policy)).Succeeded.Should().BeFalse();
    }

    [Test]
    public void ARoleListThatGrantsNobodyIsRefused()
    {
        Action build = () => new AuthorizationPolicyBuilder().RequireSchedulerRoles(_ => { });

        build.Should().Throw<ArgumentException>().WithMessage("Name at least one role*");
    }

    [Test]
    public void ABlankRoleIsRefused()
    {
        Action build = () => new AuthorizationPolicyBuilder().RequireSchedulerRoles(roles => roles.Operators.Add(" "));

        build.Should().Throw<ArgumentException>().WithMessage("Operators holds a blank role*");
    }

    [TestCase("PauseJobb", SchedulerAccess.Operate, "*no operation of the HTTP API*")]
    [TestCase("GetJobDetails", SchedulerAccess.Operate, "*is a read*")]
    [TestCase("PauseJob", SchedulerAccess.View, "*needs Operate or Administer*")]
    [TestCase("PauseJob", (SchedulerAccess) 7, "*needs Operate or Administer*")]
    public void AnOverrideThatCannotMeanAnythingIsRefused(string operation, SchedulerAccess access, string message)
    {
        Action build = () => new AuthorizationPolicyBuilder().RequireSchedulerRoles(roles =>
        {
            roles.Viewers.Add("viewer");
            roles.Operations[operation] = access;
        });

        build.Should().Throw<ArgumentException>().WithMessage(message);
    }

    /// <summary>
    /// The policy is a snapshot: a list changed after it was built changes nothing it decides.
    /// </summary>
    [Test]
    public async Task ThePolicyIsASnapshot()
    {
        SchedulerRoles? captured = null;
        IAuthorizationService authorization = Authorization(roles => captured = roles);

        // The options, and the policy with them, are built on first use.
        (await authorization.AuthorizeAsync(Principal("operator"), Resource("Shutdown", SchedulerAccess.Administer), Policy))
            .Succeeded.Should().BeFalse();

        captured!.Administrators.Add("operator");

        (await authorization.AuthorizeAsync(Principal("operator"), Resource("Shutdown", SchedulerAccess.Administer), Policy))
            .Succeeded.Should().BeFalse("the roles were read when the policy was built");
    }

    private static SchedulerResource Resource(string operation, SchedulerAccess access) => new("acme") { Operation = operation, Access = access };

    private static IAuthorizationService Authorization(Action<SchedulerRoles> configure)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddAuthorizationBuilder().AddPolicy(Policy, policy => policy.RequireSchedulerRoles(roles =>
        {
            roles.Viewers.Add("viewer");
            roles.Operators.Add("operator");
            roles.Administrators.Add("admin");
            configure(roles);
        }));

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Principal(params string?[] roles)
    {
        List<Claim> claims = [new Claim(ClaimTypes.Name, "caller")];
        claims.AddRange(roles.OfType<string>().Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
