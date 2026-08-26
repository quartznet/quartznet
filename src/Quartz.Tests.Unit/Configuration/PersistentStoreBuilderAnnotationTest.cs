#nullable enable

using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Which database registrations admit to naming a type, and where that admission stops.
/// </summary>
/// <remarks>
/// <see cref="RequiresUnreferencedCodeAttribute"/> is viral: a method that calls one has to carry it
/// too. That is what makes it the right thing to say here and the wrong thing to say one layer up —
/// it belongs on the overload that chooses a driver by name, inside the application's
/// <c>UsePersistentStore</c> callback, and it must not reach <c>AddQuartz</c>, which every application
/// calls including ones on the in-memory store.
/// </remarks>
public sealed class PersistentStoreBuilderAnnotationTest
{
    private static IEnumerable<MethodInfo> DatabaseRegistrations() => typeof(PersistentStoreBuilderExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(method => method.Name.StartsWith("Use", StringComparison.Ordinal))
        .OrderBy(method => method.Name, StringComparer.Ordinal);

    private static bool NamesTheDriver(MethodInfo method) =>
        !method.GetParameters().Any(parameter => parameter.ParameterType == typeof(DbProviderFactory))
        && !method.GetParameters().Any(parameter => parameter.ParameterType == typeof(Func<Quartz.Impl.AdoJobStore.Common.DbMetadata>));

    [Test]
    public void EveryRegistrationThatChoosesADriverByNameSaysSo()
    {
        List<string> unannotated = DatabaseRegistrations()
            .Where(NamesTheDriver)
            .Where(method => method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>() is null)
            .Select(Describe)
            .ToList();

        unannotated.Should().BeEmpty(
            "an overload that resolves the driver's types from strings cannot promise a trimmed application "
            + "anything, and saying so is what points the caller at the factory overload beside it");
    }

    [Test]
    public void NoRegistrationThatTakesAFactoryCarriesTheWarning()
    {
        List<string> annotated = DatabaseRegistrations()
            .Where(method => !NamesTheDriver(method))
            .Where(method => method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>() is not null)
            .Select(Describe)
            .ToList();

        annotated.Should().BeEmpty(
            "the factory overloads name nothing, and a warning on them would leave a trimmed application "
            + "with nowhere to go");
    }

    [Test]
    public void TheWarningPointsAtBothWaysOut()
    {
        MethodInfo useSqlServer = DatabaseRegistrations().First(method =>
            method.Name == nameof(PersistentStoreBuilderExtensions.UseSqlServer) && NamesTheDriver(method));

        string message = useSqlServer.GetCustomAttribute<RequiresUnreferencedCodeAttribute>()!.Message;

        message.Should().Contain("DbProviderFactory").And.Contain("DbDataSource");
    }

    /// <summary>
    /// The boundary the annotation must not cross. <c>AddQuartz</c> is what every application calls, and
    /// a warning here would tell an application on the in-memory store that Quartz cannot be trimmed.
    /// </summary>
    [Test]
    public void AddQuartzCarriesNoWarning()
    {
        List<string> annotated = typeof(QuartzServiceCollectionExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>() is not null)
            .Select(Describe)
            .ToList();

        annotated.Should().BeEmpty(
            "registering a scheduler says nothing about which store it ends up with, so the warning has to "
            + "stop at the registration that does");
    }

    private static string Describe(MethodInfo method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name))})";
}
