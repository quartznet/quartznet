using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The drift guard between <c>AddQuartzSharedServices</c> and what a scheduler built at runtime is
/// handed from the application.
/// </summary>
/// <remarks>
/// A generation's container is seeded with the application's shared services rather than registering
/// its own, and the list of them is written by hand — there is no way to derive it, because "shared"
/// is a statement about meaning rather than about a registration's shape. So the failure mode is that
/// a shared service is added to <c>AddQuartzSharedServices</c> and nobody remembers this list, and a
/// tenant quietly gets a second copy of something there is supposed to be one of. That failure does not
/// show up as an exception; it shows up as a tenant the dashboard cannot see, or measurements nobody is
/// collecting.
/// <para>
/// So every service type the shared registration produces must be classified: either forwarded, or
/// excused here with the reason it is not. A new one fails this test until somebody decides which it
/// is.
/// </para>
/// </remarks>
public sealed class SharedServiceForwardingTest
{
    /// <summary>
    /// The service types a generation's container deliberately registers for itself, and why.
    /// </summary>
    private static readonly Dictionary<Type, string> excused = new()
    {
        [typeof(SchedulerNameRegistry)] =
            "written while a collection is being registered into, and a generation's collection holds "
            + "exactly one scheduler - the application's list of names is not that",

        [typeof(ISchedulerRegistry)] =
            "answers 'what schedulers are there', which a container holding one tenant cannot; removed "
            + "from the generation's collection so the question falls through to the application's",

        [typeof(Microsoft.Extensions.Options.IValidateOptions<>)] =
            "a generation validates its own scheduler's options, and the validators are stateless - "
            + "sharing them would buy nothing and tie a tenant's validation to the application's",

        [typeof(Microsoft.Extensions.Options.IConfigureOptions<>)] =
            "configuration is per collection by definition: what the application configured is the "
            + "application's schedulers, and the recipe configures the tenant's",

        [typeof(Microsoft.Extensions.Options.IStartupValidator)] =
            "what ValidateOnStart registers, and a host is what runs it - a generation is built after "
            + "the host started, so its options are validated on the first read of them instead, which "
            + "is inside the Add call the caller is awaiting",

        [typeof(Microsoft.Extensions.Options.IOptions<>)] = OptionsFramework,
        [typeof(Microsoft.Extensions.Options.IOptionsSnapshot<>)] = OptionsFramework,
        [typeof(Microsoft.Extensions.Options.IOptionsMonitor<>)] = OptionsFramework,
        [typeof(Microsoft.Extensions.Options.IOptionsFactory<>)] = OptionsFramework,
        [typeof(Microsoft.Extensions.Options.IOptionsMonitorCache<>)] = OptionsFramework,

        [typeof(Microsoft.Extensions.Logging.ILogger<>)] = Logging,
        [typeof(Microsoft.Extensions.Logging.ILoggerFactory)] = Logging,
        [typeof(Microsoft.Extensions.Logging.Configuration.ILoggerProviderConfigurationFactory)] = Logging,
        [typeof(Microsoft.Extensions.Logging.Configuration.ILoggerProviderConfiguration<>)] = Logging,
    };

    private const string OptionsFramework =
        "the options framework's own plumbing, which every container that reads options has; a "
        + "generation reads its own options through its own";

    private const string Logging =
        "AddLogging()'s registrations, which follow the ILoggerFactory that is forwarded - the factory "
        + "is what decides where a log line goes, and it is the application's";

    [Test]
    public void EverySharedServiceIsEitherForwardedOrExcused()
    {
        ServiceCollection services = new();
        services.AddQuartzSharedServices();

        List<string> unclassified = [];
        foreach (ServiceDescriptor descriptor in services)
        {
            if (IsClassified(descriptor.ServiceType))
            {
                continue;
            }

            unclassified.Add(descriptor.ServiceType.FullName!);
        }

        unclassified.Should().BeEmpty(
            "a service AddQuartzSharedServices registers is either one thing per container - in which "
            + "case SchedulerGeneration.ForwardedServiceTypes has to carry it to a tenant - or one thing "
            + "per scheduler, in which case say so here. Silently getting a second copy is the failure "
            + "this test exists to prevent, and it has no symptom until production");
    }

    [Test]
    public void EveryForwardedServiceIsOneTheSharedRegistrationActuallyMakes()
    {
        ServiceCollection services = new();
        services.AddQuartzSharedServices();

        HashSet<Type> registered = [];
        foreach (ServiceDescriptor descriptor in services)
        {
            registered.Add(descriptor.ServiceType);
        }

        SchedulerGeneration.ForwardedServiceTypes.Should().OnlyContain(x => registered.Contains(x),
            "the list drifts in both directions: a service that stopped being registered here is one a "
            + "generation is still trying to carry across, which is a forward of nothing at all");
    }

    [Test]
    public void TheExcusedListSaysWhy()
    {
        excused.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.Value),
            "an excuse without a reason is a service somebody classified in a hurry, and the next reader "
            + "has no way to tell whether it was thought about");
    }

    private static bool IsClassified(Type serviceType)
    {
        if (SchedulerGeneration.ForwardedServiceTypes.Contains(serviceType) || excused.ContainsKey(serviceType))
        {
            return true;
        }

        return serviceType.IsConstructedGenericType
            && (SchedulerGeneration.ForwardedServiceTypes.Contains(serviceType.GetGenericTypeDefinition())
                || excused.ContainsKey(serviceType.GetGenericTypeDefinition()));
    }
}
