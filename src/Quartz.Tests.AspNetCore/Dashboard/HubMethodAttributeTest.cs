using System.Reflection;

using Microsoft.AspNetCore.SignalR;

using Quartz.Dashboard.Hubs;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The dashboard's hubs declare their methods with no attribute on them, not even the one
/// <see langword="async" /> leaves.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET Core 11 reads every custom attribute on a hub method as authorization metadata, and a method with
/// any at all, but none that says who may call it, is held to the host's <c>FallbackPolicy</c>. The hub's
/// <c>AllowAnonymous</c> does not reach it: that is endpoint metadata, and a hub method is authorized against
/// the connection's principal alone (dotnet/aspnetcore#69624). Under a fail-closed policy an <see langword="async" />
/// <c>Register</c> refused every agent the hub's token check had let in (#4234).
/// </para>
/// <para>
/// A method with no attribute is not authorized at all, on 10 and on 11, which is what both hubs had before
/// 11: the agent hub checks its token itself, and the live-events hub's join asks the scheduler's policy.
/// </para>
/// </remarks>
public class HubMethodAttributeTest
{
    /// <summary>
    /// Every hub the dashboard assembly declares, found rather than listed, so a hub added later is held to
    /// the same rule.
    /// </summary>
    private static IEnumerable<Type> DashboardHubs()
    {
        return typeof(QuartzDashboardHub).Assembly.GetTypes()
            .Where(x => x is { IsClass: true, IsAbstract: false } && typeof(Hub).IsAssignableFrom(x))
            .OrderBy(x => x.FullName, StringComparer.Ordinal);
    }

    [Test]
    public void TheScanFindsBothHubs()
    {
        DashboardHubs().Should().Contain([typeof(DashboardAgentHub), typeof(QuartzDashboardHub)],
            "a scan that finds nothing would pass the rule vacuously");
    }

    [TestCaseSource(nameof(DashboardHubs))]
    public void NoHubMethodCarriesAnAttribute(Type hub)
    {
        List<MethodInfo> methods = HubMethods(hub);

        methods.Should().NotBeEmpty("the methods are found as SignalR finds them, and a hub has some");
        methods.Where(x => x.GetCustomAttributes(inherit: true).Length > 0)
            .Select(x => $"{x.Name}: {string.Join(", ", x.GetCustomAttributes(inherit: true).Select(y => y.GetType().Name))}")
            .Should().BeEmpty("ASP.NET Core 11 holds a hub method with any attribute to the FallbackPolicy, whatever the hub's endpoint allows");
    }

    /// <summary>
    /// The methods SignalR binds on a hub: public instance methods, not property accessors, and not those
    /// <see cref="Hub" /> or <see cref="object" /> declare or that implement an interface.
    /// </summary>
    private static List<MethodInfo> HubMethods(Type hub)
    {
        HashSet<MethodInfo> interfaceMethods = [.. hub.GetInterfaces().SelectMany(x => hub.GetInterfaceMap(x).TargetMethods)];

        return
        [
            .. hub.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => !x.IsSpecialName && !interfaceMethods.Contains(x))
                .Where(x =>
                {
                    Type declaring = x.GetBaseDefinition().DeclaringType!;
                    Type definition = declaring.IsGenericType ? declaring.GetGenericTypeDefinition() : declaring;
                    return definition != typeof(object) && definition != typeof(Hub) && definition != typeof(Hub<>);
                }),
        ];
    }
}
