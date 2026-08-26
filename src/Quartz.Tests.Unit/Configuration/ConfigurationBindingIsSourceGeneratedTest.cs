#nullable enable

using System.Globalization;
using System.Reflection;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Holds the one thing <c>EnableConfigurationBindingGenerator</c> cannot say for itself: that the
/// generator actually intercepted the binder calls, and that it bound every member of every options
/// type it reached.
/// </summary>
/// <remarks>
/// <para>
/// The property is a request, not a result. An interceptor that declines to intercept leaves the
/// reflection binder in the compiled assembly, and every test in this project passes either way — the
/// reflection binder is perfectly correct in a process that has a JIT. What it is not is
/// ahead-of-time-safe: <c>Quartz.Trimming.Canary</c> built against it and published natively comes up
/// with three of its eight configured values at their defaults and no error anywhere, which is the
/// failure shape <see cref="ConfigurationIsNeverSilentlyDroppedTest"/> guards elsewhere — configuration
/// accepted without complaint and then discarded.
/// </para>
/// <para>
/// Two halves, because neither is sufficient. <see cref="TheBinderCallsInQuartzTypedOptionsAreIntercepted"/>
/// reads the interception out of the built assembly's metadata, which is the only place the fact is
/// recorded. Everything below it binds a section and reads the value back, which is what a reader
/// actually cares about — and, unlike the metadata check, it fails when the generator quietly declines
/// to bind one member of a type it did generate a binder for. That is not hypothetical: the generator
/// skips a member whose type configuration cannot express, and says nothing about it.
/// </para>
/// <para>
/// The samples are derived from a fresh instance's own value, so a sample can never coincide with the
/// default it is meant to displace — an assertion that passed because the value was already what it was
/// going to be would prove nothing.
/// </para>
/// </remarks>
public class ConfigurationBindingIsSourceGeneratedTest
{
    /// <summary>
    /// The six calls the generator has to intercept, which are all of the binder calls Quartz makes.
    /// </summary>
    private const int InterceptedBinderCalls = 6;

    /// <summary>
    /// The file those calls are written in. The generator intercepts call sites in the project being
    /// compiled, so the fact that these are Quartz's own is what makes the fix reach consumers who set
    /// nothing.
    /// </summary>
    private const string InterceptedFile = "QuartzTypedOptions.cs";

    /// <summary>
    /// Members the binder cannot reach, each with the reason it is out of reach. A new entry needs a
    /// reason of the same kind: a value no configuration source can produce, which is therefore
    /// documented as settable from code only.
    /// </summary>
    /// <remarks>
    /// Ratified by <c>OptionsConventionTest</c>, whose S6 rule classifies a delegate as a scalar that is
    /// "replaced, never edited" and so allows one on an options type. A member that is <em>not</em> a
    /// delegate and cannot bind does not belong here: move it off the options type, or split the
    /// binding, rather than excusing it.
    /// </remarks>
    private static readonly Dictionary<string, string> codeOnlyMembers = new(StringComparer.Ordinal)
    {
        ["Quartz.DataSourceOptions.DataSourceFactory"] =
            "a Func<IServiceProvider, DbDataSource> is a value only code can supply, and the member says so"
    };

    [Test]
    public void TheBinderCallsInQuartzTypedOptionsAreIntercepted()
    {
        List<CustomAttributeData> interceptions = Interceptions();

        interceptions.Should().HaveCount(InterceptedBinderCalls,
            "every Configure<TOptions>(name, section) call in QuartzTypedOptions has to be intercepted — "
            + "one that is not leaves the reflection binder in place for that options type, which no "
            + "diagnostic reports");

        foreach (CustomAttributeData interception in interceptions)
        {
            FileOf(interception).Should().Be(InterceptedFile,
                "the generator only intercepts call sites in the project it runs in, so an interception "
                + "pointing anywhere else means the count above is being made up by some other file");
        }
    }

    [Test]
    public void TheGeneratedBinderKnowsEveryOptionsTypeTheQuartzSectionBinds()
    {
        Type[] bound =
        [
            typeof(QuartzSchedulerOptions),
            typeof(ThreadPoolOptions),
            typeof(InMemoryJobStoreOptions),
            typeof(AdoJobStoreOptions),
            typeof(ClusteringOptions),
            typeof(DataSourceOptions)
        ];

        List<Type> generated = GeneratedBinder()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name == "BindCore")
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.ParameterType.IsByRef)
            .Select(parameter => parameter.ParameterType.GetElementType()!)
            .ToList();

        generated.Should().Contain(bound,
            "each of these binds from its own section, and a type the generator wrote no binder for is "
            + "one the reflection binder is still doing at runtime");
    }

    [Test]
    public void TheSchedulerSectionBindsEveryMemberOfQuartzSchedulerOptions()
    {
        AssertEveryMemberBinds<QuartzSchedulerOptions>(QuartzTypedOptions.SchedulerSection);
    }

    [Test]
    public void TheThreadPoolSectionBindsEveryMemberOfThreadPoolOptions()
    {
        AssertEveryMemberBinds<ThreadPoolOptions>(QuartzTypedOptions.ThreadPoolSection);
    }

    [Test]
    public void TheJobStoreSectionBindsEveryMemberOfInMemoryJobStoreOptions()
    {
        AssertEveryMemberBinds<InMemoryJobStoreOptions>(QuartzTypedOptions.JobStoreSection);
    }

    [Test]
    public void TheJobStoreSectionBindsEveryMemberOfAdoJobStoreOptions()
    {
        AssertEveryMemberBinds<AdoJobStoreOptions>(QuartzTypedOptions.JobStoreSection);
    }

    [Test]
    public void TheClusteringSubSectionBindsEveryMemberOfClusteringOptions()
    {
        AssertEveryMemberBinds<ClusteringOptions>(
            $"{QuartzTypedOptions.JobStoreSection}:{QuartzTypedOptions.ClusteringSection}");
    }

    [Test]
    public void EachDataSourceChildBindsEveryMemberOfDataSourceOptions()
    {
        // The one call site that binds a section per child rather than a section per scheduler, and the
        // one whose options are named after something other than the scheduler.
        AssertEveryMemberBinds<DataSourceOptions>($"{QuartzTypedOptions.DataSourceSection}:reporting", "reporting");
    }

    [Test]
    public void EveryCodeOnlyMemberIsStillEarningItsPlace()
    {
        foreach (string member in codeOnlyMembers.Keys)
        {
            PropertyInfo? property = BoundOptionsTypes()
                .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                .SingleOrDefault(candidate => Describe(candidate) == member);

            property.Should().NotBeNull($"the list names {member}, which no longer exists");
            SampleFor(property!, Activator.CreateInstance(property!.DeclaringType!)!).Should().BeNull(
                $"{member} is bindable now, so excusing it hides a member the binder does write");
        }
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Fills every member of <typeparamref name="TOptions"/> from configuration and reads each one back
    /// off the instance the container hands its own components.
    /// </summary>
    private static void AssertEveryMemberBinds<TOptions>(string sectionPath, string optionsName = "")
        where TOptions : class, new()
    {
        TOptions probe = new();
        Dictionary<string, string?> values = new(StringComparer.Ordinal);
        Dictionary<PropertyInfo, object?> expected = new();
        List<string> unbindable = [];

        foreach (PropertyInfo property in Settable(typeof(TOptions)))
        {
            if (IsStringDictionary(property.PropertyType))
            {
                // Bound into rather than assigned, which is the code path the reflection binder needed
                // RequiresDynamicCode for.
                values[$"Quartz:{sectionPath}:{property.Name}:canary"] = "bound";
                expected[property] = "bound";
                continue;
            }

            if (SampleFor(property, probe) is not { } sample)
            {
                unbindable.Add($"{Describe(property)} is a {property.PropertyType.Name}");
                continue;
            }

            values[$"Quartz:{sectionPath}:{property.Name}"] = sample;
            expected[property] = Parse(property.PropertyType, sample);
        }

        unbindable.Should().BeEquivalentTo(
            codeOnlyMembers
                .Where(entry => entry.Key.StartsWith(typeof(TOptions).FullName + ".", StringComparison.Ordinal))
                .Select(entry => UnbindableDescription(entry.Key)),
            "the generator drops a member whose type configuration cannot express and says nothing about "
            + "it, so a new one is a setting that binds from nowhere — move it off the options type, or "
            + "split the binding, or say here why no configuration source could ever produce it");

        ServiceCollection services = new();
        services.BindQuartzOptions(Section(values));

        using ServiceProvider container = services.BuildServiceProvider();
        TOptions bound = container.GetRequiredService<IOptionsMonitor<TOptions>>().Get(optionsName);

        List<string> dropped = [];
        foreach ((PropertyInfo property, object? want) in expected)
        {
            object? got = IsStringDictionary(property.PropertyType)
                ? ((IReadOnlyDictionary<string, string>) property.GetValue(bound)!).GetValueOrDefault("canary")
                : property.GetValue(bound);

            if (!Equals(got, want))
            {
                dropped.Add($"{Describe(property)} bound as '{got ?? "null"}', wanted '{want ?? "null"}'");
            }
        }

        dropped.Should().BeEmpty(
            "every one of these was written into the configuration section the options type binds from, "
            + "so a value still at its default is one the binder passed over");
    }

    private static IConfiguration Section(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Quartz");
    }

    private static IEnumerable<Type> BoundOptionsTypes()
    {
        yield return typeof(QuartzSchedulerOptions);
        yield return typeof(ThreadPoolOptions);
        yield return typeof(InMemoryJobStoreOptions);
        yield return typeof(AdoJobStoreOptions);
        yield return typeof(ClusteringOptions);
        yield return typeof(DataSourceOptions);
    }

    private static IEnumerable<PropertyInfo> Settable(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.GetMethod is { IsPublic: true })
        .OrderBy(property => property.Name, StringComparer.Ordinal);

    /// <summary>
    /// A value for the member that differs from the one a fresh instance already has, or
    /// <see langword="null" /> when configuration cannot express the member's type at all.
    /// </summary>
    private static string? SampleFor(PropertyInfo property, object probe)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        object? current = property.GetValue(probe);

        if (type == typeof(string) || type == typeof(object))
        {
            return $"bound-{property.Name}";
        }

        if (type == typeof(bool))
        {
            return current is true ? "false" : "true";
        }

        if (type == typeof(int))
        {
            return ((current as int? ?? 0) + 7).ToString(CultureInfo.InvariantCulture);
        }

        if (type == typeof(TimeSpan))
        {
            return ((current as TimeSpan? ?? TimeSpan.Zero) + TimeSpan.FromSeconds(7)).ToString("c", CultureInfo.InvariantCulture);
        }

        if (type.IsEnum)
        {
            return Enum.GetNames(type).Last(name => !Equals(Enum.Parse(type, name), current));
        }

        return null;
    }

    private static object? Parse(Type propertyType, string sample)
    {
        Type type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (type == typeof(string) || type == typeof(object))
        {
            return sample;
        }

        if (type == typeof(bool))
        {
            return bool.Parse(sample);
        }

        if (type == typeof(int))
        {
            return int.Parse(sample, CultureInfo.InvariantCulture);
        }

        if (type == typeof(TimeSpan))
        {
            return TimeSpan.Parse(sample, CultureInfo.InvariantCulture);
        }

        return Enum.Parse(type, sample);
    }

    private static bool IsStringDictionary(Type type) =>
        type.IsGenericType
        && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
        && type.GetGenericArguments()[0] == typeof(string)
        && type.GetGenericArguments()[1] == typeof(string);

    /// <summary>
    /// The generated binder, which the compiler emits into Quartz itself as a file-local type — so its
    /// metadata name carries a hash of the generated file's own name and cannot be written down.
    /// </summary>
    private static Type GeneratedBinder()
    {
        Type[] candidates = typeof(IScheduler).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "Microsoft.Extensions.Configuration.Binder.SourceGeneration")
            .Where(type => type.Name.EndsWith("BindingExtensions", StringComparison.Ordinal))
            .ToArray();

        candidates.Should().ContainSingle(
            "the configuration binding generator emits exactly one binder per compilation, and none at "
            + "all when EnableConfigurationBindingGenerator is off in src/Quartz/Quartz.csproj");

        return candidates[0];
    }

    private static List<CustomAttributeData> Interceptions() => GeneratedBinder()
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .SelectMany(method => method.GetCustomAttributesData())
        .Where(attribute => attribute.AttributeType.Name.EndsWith("InterceptsLocationAttribute", StringComparison.Ordinal))
        .ToList();

    /// <summary>
    /// The source file an interception points at.
    /// </summary>
    /// <remarks>
    /// Roslyn's version 1 location is base64 of a 16-byte content checksum, a 4-byte position and the
    /// display file name. Nothing else decodes it, so a version this does not know about fails loudly
    /// rather than being read as if it were version 1.
    /// </remarks>
    private static string FileOf(CustomAttributeData interception)
    {
        interception.ConstructorArguments[0].Value.Should().Be(1,
            "this decodes Roslyn's version 1 interceptable location; a new version needs a new decoder here");

        byte[] data = Convert.FromBase64String((string) interception.ConstructorArguments[1].Value!);
        data.Length.Should().BeGreaterThan(20, "a version 1 location is a checksum, a position and a file name");

        return Encoding.UTF8.GetString(data, 20, data.Length - 20);
    }

    private static string UnbindableDescription(string member)
    {
        string[] parts = member.Split('.');
        Type type = BoundOptionsTypes().Single(candidate => candidate.Name == parts[^2]);
        PropertyInfo property = type.GetProperty(parts[^1])!;
        return $"{member} is a {property.PropertyType.Name}";
    }

    private static string Describe(PropertyInfo property) => $"{property.DeclaringType!.FullName}.{property.Name}";
}
