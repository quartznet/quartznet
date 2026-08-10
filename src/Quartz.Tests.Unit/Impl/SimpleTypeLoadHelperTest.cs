using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Jobs;
using Quartz.Listeners;
using Quartz.Plugins.History;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The 4.0 namespace and type renames are invisible to the compiler when a type is named by a
/// configuration string, so the fallback that keeps those strings resolving needs a test of its own.
/// </summary>
public class SimpleTypeLoadHelperTest
{
    private ITypeLoadHelper loadHelper;

    [SetUp]
    public void SetUp()
    {
        loadHelper = new SimpleTypeLoadHelper();
    }

    [Test]
    public void ShouldLoadTypeByItsCurrentName()
    {
        loadHelper.LoadType("Quartz.Impl.DefaultThreadPool, Quartz").Should().Be<DefaultThreadPool>();
    }

    [Test]
    public void ShouldLoadTypeNamedByItsPre40Namespace()
    {
        loadHelper.LoadType("Quartz.Simpl.DefaultThreadPool, Quartz").Should().Be<DefaultThreadPool>(
            "configuration naming the old Quartz.Simpl namespace has to keep working");
    }

    [Test]
    public void ShouldLoadSpiTypeNamedByItsPre40Namespace()
    {
        loadHelper.LoadType("Quartz.Spi.IJobStore, Quartz").Should().Be<IJobStore>(
            "configuration naming the old Quartz.Spi namespace has to keep working");
    }

    [Test]
    public void ShouldLoadBuiltInJobNamedByItsPre40Namespace()
    {
        loadHelper.LoadType("Quartz.Job.NoOpJob, Quartz.Jobs").Should().Be<NoOpJob>(
            "a stored JOB_CLASS_NAME naming the old Quartz.Job namespace has to keep firing");
    }

    [Test]
    public void ShouldLoadBuiltInJobNamedByItsPre40NamespaceAndAssembly()
    {
        loadHelper.LoadType("Quartz.Job.NoOpJob, Quartz").Should().Be<NoOpJob>(
            "a 3.x name carries both the old namespace and the pre-split assembly, and the fallbacks must compose");
    }

    [Test]
    public void ShouldLoadPluginNamedByItsPre40Namespace()
    {
        loadHelper.LoadType("Quartz.Plugin.History.LoggingJobHistoryPlugin, Quartz.Plugins")
            .Should().Be<LoggingJobHistoryPlugin>(
                "quartz.plugin.<name>.type naming the old singular namespace has to keep working");
    }

    [Test]
    public void ShouldLoadListenerNamedByItsPre40Namespace()
    {
        loadHelper.LoadType("Quartz.Listener.BroadcastJobListener, Quartz")
            .Should().Be<BroadcastJobListener>(
                "quartz.jobListener.<name>.type naming the old singular namespace has to keep working");
    }

    [Test]
    public void ShouldLoadTypeNamedByAMergedAssembly()
    {
        loadHelper.LoadType("Quartz.Impl.MicrosoftDependencyInjectionJobFactory, Quartz.Extensions.DependencyInjection")
            .Should().Be<MicrosoftDependencyInjectionJobFactory>(
                "configuration naming an assembly that merged into the core package has to keep working");
    }

    [Test]
    public void ShouldLoadTypeNamedByBothItsPre40NamespaceAndAMergedAssembly()
    {
        loadHelper.LoadType("Quartz.Simpl.MicrosoftDependencyInjectionJobFactory, Quartz.Extensions.DependencyInjection")
            .Should().Be<MicrosoftDependencyInjectionJobFactory>(
                "a 3.x configuration string carries both the old namespace and the old assembly, and the fallbacks must compose");
    }

    [Test]
    public void ShouldLoadSerializerNamedByItsPre40NamespaceAndAssembly()
    {
        loadHelper.LoadType("Quartz.Simpl.SystemTextJsonObjectSerializer, Quartz.Serialization.SystemTextJson")
            .Should().Be<SystemTextJsonObjectSerializer>(
                "the System.Text.Json serializer merged into the core package in 4.0");
    }

    [Test]
    public void ShouldLoadTheJobStoreNamedByItsPre40TypeName()
    {
        loadHelper.LoadType("Quartz.Impl.AdoJobStore.JobStoreTX, Quartz")
            .Should().Be<LocalTransactionJobStore>(
                "quartz.jobStore.type is the one type name almost every persistent configuration spells out");
    }

    [Test]
    public void ShouldLoadTheContainerManagedJobStoreNamedByItsPre40TypeName()
    {
        loadHelper.LoadType("Quartz.Impl.AdoJobStore.JobStoreCMT, Quartz")
            .Should().Be<ExternalTransactionJobStore>(
                "quartz.jobStore.type is the one type name almost every persistent configuration spells out");
    }

    [Test]
    public void ShouldLoadTheJobStoreNamedByItsPre40TypeNameWithoutAnAssembly()
    {
        loadHelper.LoadType("Quartz.Impl.AdoJobStore.JobStoreTX")
            .Should().Be<LocalTransactionJobStore>(
                "the assembly is optional in a configured type name when the type lives in the calling assembly's dependencies");
    }

    [Test]
    public void ShouldNotRewriteATypeWhoseNameMerelyStartsWithARenamedOne()
    {
        var act = () => loadHelper.LoadType("Quartz.Impl.AdoJobStore.JobStoreTXExtras, Quartz");

        act.Should().Throw<TypeLoadException>().WithMessage("*JobStoreTXExtras*",
            "the rename applies to the whole type name, not to anything that happens to begin with it");
    }

    [Test]
    public void ShouldStillFailForATypeThatDoesNotExistUnderEitherName()
    {
        var act = () => loadHelper.LoadType("Quartz.Simpl.NoSuchThing, Quartz");

        act.Should().Throw<TypeLoadException>().WithMessage("*Quartz.Simpl.NoSuchThing*");
    }

    [Test]
    public void ShouldStillFailForATypeThatDoesNotExistInAMergedAssemblyEither()
    {
        var act = () => loadHelper.LoadType("Quartz.Simpl.NoSuchThing, Quartz.Extensions.Hosting");

        act.Should().Throw<TypeLoadException>().WithMessage("*Quartz.Simpl.NoSuchThing*");
    }

    [Test]
    public void ShouldReturnNullForAnEmptyName()
    {
        loadHelper.LoadType("").Should().BeNull();
    }
}
