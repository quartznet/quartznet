using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Extensions.Redis;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Jobs;
using Quartz.Listeners;
using Quartz.Plugins.History;
using Quartz.Plugins.Xml;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The 4.0 namespace and type renames are invisible to the compiler when a type is named by a
/// configuration string, so the fallback that keeps those strings resolving needs a test of its own.
/// </summary>
public class SimpleTypeLoaderTest
{
    private ITypeLoader typeLoader;

    [SetUp]
    public void SetUp()
    {
        typeLoader = new SimpleTypeLoader();
    }

    [Test]
    public void ShouldLoadTypeByItsCurrentName()
    {
        typeLoader.LoadType("Quartz.Impl.DefaultThreadPool, Quartz").Should().Be<DefaultThreadPool>();
    }

    [Test]
    public void ShouldLoadTypeNamedByItsPre40Namespace()
    {
        typeLoader.LoadType("Quartz.Simpl.DefaultThreadPool, Quartz").Should().Be<DefaultThreadPool>(
            "configuration naming the old Quartz.Simpl namespace has to keep working");
    }

    [Test]
    public void ShouldLoadSpiTypeNamedByItsPre40Namespace()
    {
        typeLoader.LoadType("Quartz.Spi.IJobStore, Quartz").Should().Be<IJobStore>(
            "configuration naming the old Quartz.Spi namespace has to keep working");
    }

    [Test]
    public void ShouldLoadBuiltInJobNamedByItsPre40Namespace()
    {
        typeLoader.LoadType("Quartz.Job.NoOpJob, Quartz.Jobs").Should().Be<NoOpJob>(
            "a stored JOB_CLASS_NAME naming the old Quartz.Job namespace has to keep firing");
    }

    [Test]
    public void ShouldLoadBuiltInJobNamedByItsPre40NamespaceAndAssembly()
    {
        typeLoader.LoadType("Quartz.Job.NoOpJob, Quartz").Should().Be<NoOpJob>(
            "a 3.x name carries both the old namespace and the pre-split assembly, and the fallbacks must compose");
    }

    [Test]
    public void ShouldLoadPluginNamedByItsPre40Namespace()
    {
        typeLoader.LoadType("Quartz.Plugin.History.LoggingJobHistoryPlugin, Quartz.Plugins")
            .Should().Be<LoggingJobHistoryPlugin>(
                "quartz.plugin.<name>.type naming the old singular namespace has to keep working");
    }

    [Test]
    public void ShouldLoadListenerNamedByItsPre40Namespace()
    {
        typeLoader.LoadType("Quartz.Listener.JobChainingJobListener, Quartz")
            .Should().Be<JobChainingJobListener>(
                "quartz.jobListener.<name>.type naming the old singular namespace has to keep working");
    }

    [Test]
    public void ShouldLoadTheXmlPluginNamedByItsPre40Casing()
    {
        typeLoader.LoadType("Quartz.Plugin.Xml.XMLSchedulingDataProcessorPlugin, Quartz.Plugins")
            .Should().Be<XmlSchedulingDataProcessorPlugin>(
                "the namespace and the type were both respelled, and the two fallbacks must compose");
    }

    [Test]
    public void ShouldLoadTheRedisLockHandlerNamedByItsPre40Namespace()
    {
        typeLoader.LoadType("Quartz.Impl.Redis.RedisSemaphore, Quartz.Extensions.Redis")
            .Should().Be<RedisLockHandler>(
                "quartz.jobStore.lockHandler.type naming the old namespace and the old type name has to keep working");
    }

    [Test]
    public void ShouldLoadTheRedisLockHandlerNamedByItsAlphaTypeName()
    {
        typeLoader.LoadType("Quartz.Extensions.Redis.RedisSemaphore, Quartz.Extensions.Redis")
            .Should().Be<RedisLockHandler>(
                "the 4.0 alphas shipped the handler under the namespace it has now and the name it no longer has");
    }

    [Test]
    public void ShouldLoadTypeNamedByAMergedAssembly()
    {
        typeLoader.LoadType("Quartz.Impl.MicrosoftDependencyInjectionJobFactory, Quartz.Extensions.DependencyInjection")
            .Should().Be<MicrosoftDependencyInjectionJobFactory>(
                "configuration naming an assembly that merged into the core package has to keep working");
    }

    [Test]
    public void ShouldLoadTypeNamedByBothItsPre40NamespaceAndAMergedAssembly()
    {
        typeLoader.LoadType("Quartz.Simpl.MicrosoftDependencyInjectionJobFactory, Quartz.Extensions.DependencyInjection")
            .Should().Be<MicrosoftDependencyInjectionJobFactory>(
                "a 3.x configuration string carries both the old namespace and the old assembly, and the fallbacks must compose");
    }

    [Test]
    public void ShouldLoadSerializerNamedByItsPre40NamespaceAndAssembly()
    {
        typeLoader.LoadType("Quartz.Simpl.SystemTextJsonObjectSerializer, Quartz.Serialization.SystemTextJson")
            .Should().Be<SystemTextJsonObjectSerializer>(
                "the System.Text.Json serializer merged into the core package in 4.0");
    }

    [Test]
    public void ShouldLoadTheJobStoreNamedByItsPre40TypeName()
    {
        typeLoader.LoadType("Quartz.Impl.AdoJobStore.JobStoreTX, Quartz")
            .Should().Be<LocalTransactionJobStore>(
                "quartz.jobStore.type is the one type name almost every persistent configuration spells out");
    }

    [Test]
    public void ShouldLoadTheContainerManagedJobStoreNamedByItsPre40TypeName()
    {
        typeLoader.LoadType("Quartz.Impl.AdoJobStore.JobStoreCMT, Quartz")
            .Should().Be<ExternalTransactionJobStore>(
                "quartz.jobStore.type is the one type name almost every persistent configuration spells out");
    }

    [Test]
    public void ShouldLoadTheJobStoreNamedByItsPre40TypeNameWithoutAnAssembly()
    {
        typeLoader.LoadType("Quartz.Impl.AdoJobStore.JobStoreTX")
            .Should().Be<LocalTransactionJobStore>(
                "the assembly is optional in a configured type name when the type lives in the calling assembly's dependencies");
    }

    [Test]
    [TestCase("Quartz.Impl.AdoJobStore.StdRowLockSemaphore", typeof(SelectForUpdateLockHandler))]
    [TestCase("Quartz.Impl.AdoJobStore.UpdateLockRowSemaphore", typeof(UpdateRowLockHandler))]
    [TestCase("Quartz.Impl.AdoJobStore.UpdateLockRowSemaphoreMOT", typeof(SqlServerMemoryOptimizedUpdateRowLockHandler))]
    [TestCase("Quartz.Impl.AdoJobStore.PostgreSQLRowLockSemaphore", typeof(PostgreSqlSelectForUpdateLockHandler))]
    [TestCase("Quartz.Impl.AdoJobStore.SimpleSemaphore", typeof(InProcessLockHandler))]
    public void ShouldLoadALockHandlerNamedByItsPre40TypeName(string configured, Type expected)
    {
        typeLoader.LoadType(configured)
            .Should().Be(expected,
                "quartz.jobStore.lockHandler.type spells these as strings, so the 3.x names must keep resolving");
    }

    /// <summary>
    /// The lock handlers were renamed twice: once out of 3.x's three spellings of the same idea, and
    /// again when "semaphore" gave way to "lock handler". A configuration written against a 4.0 alpha
    /// names a type that only ever existed in those alphas, and it has to keep resolving too.
    /// </summary>
    [Test]
    [TestCase("Quartz.Impl.AdoJobStore.SelectForUpdateSemaphore", typeof(SelectForUpdateLockHandler))]
    [TestCase("Quartz.Impl.AdoJobStore.UpdateRowSemaphore", typeof(UpdateRowLockHandler))]
    [TestCase("Quartz.Impl.AdoJobStore.SqlServerMemoryOptimizedUpdateRowSemaphore", typeof(SqlServerMemoryOptimizedUpdateRowLockHandler))]
    [TestCase("Quartz.Impl.AdoJobStore.PostgreSqlSelectForUpdateSemaphore", typeof(PostgreSqlSelectForUpdateLockHandler))]
    [TestCase("Quartz.Impl.AdoJobStore.SimpleSemaphore", typeof(InProcessLockHandler))]
    [TestCase("Quartz.Impl.AdoJobStore.SQLiteSemaphore", typeof(SqliteLockHandler))]
    public void ShouldLoadALockHandlerNamedByIts40AlphaTypeName(string configured, Type expected)
    {
        typeLoader.LoadType(configured)
            .Should().Be(expected,
                "the alphas are what an early adopter's configuration file was written against");
    }

    [Test]
    public void ShouldNotRewriteATypeWhoseNameMerelyStartsWithARenamedOne()
    {
        var act = () => typeLoader.LoadType("Quartz.Impl.AdoJobStore.JobStoreTXExtras, Quartz");

        act.Should().Throw<TypeLoadException>().WithMessage("*JobStoreTXExtras*",
            "the rename applies to the whole type name, not to anything that happens to begin with it");
    }

    [Test]
    public void ShouldStillFailForATypeThatDoesNotExistUnderEitherName()
    {
        var act = () => typeLoader.LoadType("Quartz.Simpl.NoSuchThing, Quartz");

        act.Should().Throw<TypeLoadException>().WithMessage("*Quartz.Simpl.NoSuchThing*");
    }

    [Test]
    public void ShouldStillFailForATypeThatDoesNotExistInAMergedAssemblyEither()
    {
        var act = () => typeLoader.LoadType("Quartz.Simpl.NoSuchThing, Quartz.Extensions.Hosting");

        act.Should().Throw<TypeLoadException>().WithMessage("*Quartz.Simpl.NoSuchThing*");
    }

    [Test]
    public void ShouldReturnNullForAnEmptyName()
    {
        typeLoader.LoadType("").Should().BeNull();
    }

    // -------------------------------------------------------------------------------------------------
    // Aliases the application declares, which ride the same table mechanism as the renames above.
    // -------------------------------------------------------------------------------------------------

    [Test]
    public void ShouldResolveANameThroughADeclaredAlias()
    {
        ITypeLoader loader = WithAliases(options => options.Map("Acme.Jobs.NightlyReport, Acme.Jobs", typeof(NoOpJob)));

        loader.LoadType("Acme.Jobs.NightlyReport, Acme.Jobs").Should().Be<NoOpJob>(
            "a declared rename is what keeps a stored JOB_CLASS_NAME firing after the type moved");
    }

    [Test]
    public void ShouldResolveANameThroughAnAliasThatNamesNoAssembly()
    {
        ITypeLoader loader = WithAliases(options => options.Map("Acme.Jobs.NightlyReport", typeof(NoOpJob)));

        loader.LoadType("Acme.Jobs.NightlyReport, Acme.Jobs").Should().Be<NoOpJob>(
            "an alias matches the part of the name before the assembly, so one entry covers however the "
            + "assembly was spelled beside it");
    }

    [Test]
    public void ShouldKeepTheStoredAssemblyWhenTheAliasTargetNamesNone()
    {
        ITypeLoader loader = WithAliases(options => options.Aliases["Quartz.Jobs.NightlyReport"] = "Quartz.Jobs.NoOpJob");

        loader.LoadType("Quartz.Jobs.NightlyReport, Quartz.Jobs").Should().Be<NoOpJob>(
            "a target that names no assembly is a rename within the assembly the name already carried");
    }

    [Test]
    public void ShouldNotClaimANameThatMerelyStartsWithADeclaredAlias()
    {
        ITypeLoader loader = WithAliases(options => options.Map("Acme.Jobs.Nightly", typeof(NoOpJob)));

        var act = () => loader.LoadType("Acme.Jobs.NightlyReport, Acme.Jobs");

        act.Should().Throw<TypeLoadException>().WithMessage("*Acme.Jobs.NightlyReport*",
            "an alias applies to a whole type name, exactly as Quartz's own rename table does");
    }

    [Test]
    public void ShouldPreferADeclaredAliasOverANameThatStillResolves()
    {
        ITypeLoader loader = WithAliases(options => options.Map("Quartz.Jobs.NoOpJob, Quartz.Jobs", typeof(DirectoryScanJob)));

        loader.LoadType("Quartz.Jobs.NoOpJob, Quartz.Jobs").Should().Be<DirectoryScanJob>(
            "an alias states what a stored name means now, so it holds through the window in which the "
            + "old type is still deployed beside the new one");
    }

    [Test]
    public void ShouldFallBackToTheNameAsGivenWhenAnAliasTargetCannotBeLoaded()
    {
        ITypeLoader loader = WithAliases(options => options.Aliases["Quartz.Impl.DefaultThreadPool"] = "Acme.Nope, Acme");

        loader.LoadType("Quartz.Impl.DefaultThreadPool, Quartz").Should().Be<DefaultThreadPool>(
            "an alias that resolves to nothing is refused at startup, so a loader built without validation "
            + "carries on rather than losing a name that was never broken");
    }

    [Test]
    public void ShouldIgnoreABlankAlias()
    {
        ITypeLoader loader = WithAliases(options =>
        {
            options.Aliases[" "] = "Quartz.Jobs.NoOpJob, Quartz.Jobs";
            options.Aliases["Acme.Jobs.NightlyReport"] = "";
        });

        loader.LoadType("Quartz.Impl.DefaultThreadPool, Quartz").Should().Be<DefaultThreadPool>(
            "a blank alias would prefix-match every name there is; it is refused at startup, and ignored "
            + "by a loader that was built without validation");
    }

    [Test]
    public void ShouldStillApplyTheBuiltInRenamesBesideADeclaredAlias()
    {
        ITypeLoader loader = WithAliases(options => options.Map("Acme.Jobs.NightlyReport, Acme.Jobs", typeof(NoOpJob)));

        loader.LoadType("Quartz.Simpl.DefaultThreadPool, Quartz").Should().Be<DefaultThreadPool>(
            "declaring a rename of your own does not displace Quartz's own 3.x table");
        loader.LoadType("Quartz.Impl.AdoJobStore.JobStoreTX, Quartz").Should().Be<LocalTransactionJobStore>(
            "nor the type renames in it, which keep quartz.jobStore.type resolving");
    }

    private static ITypeLoader WithAliases(Action<TypeLoaderOptions> configure)
    {
        TypeLoaderOptions options = new();
        configure(options);
        return new SimpleTypeLoader(logger: null, Options.Create(options));
    }
}
