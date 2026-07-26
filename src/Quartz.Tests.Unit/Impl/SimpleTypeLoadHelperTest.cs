using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The 4.0 namespace renames are invisible to the compiler when a type is named by a configuration
/// string, so the fallback that keeps those strings resolving needs a test of its own.
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
    public void ShouldStillFailForATypeThatDoesNotExistUnderEitherName()
    {
        var act = () => loadHelper.LoadType("Quartz.Simpl.NoSuchThing, Quartz");

        act.Should().Throw<TypeLoadException>().WithMessage("*Quartz.Simpl.NoSuchThing*");
    }

    [Test]
    public void ShouldReturnNullForAnEmptyName()
    {
        loadHelper.LoadType("").Should().BeNull();
    }
}
