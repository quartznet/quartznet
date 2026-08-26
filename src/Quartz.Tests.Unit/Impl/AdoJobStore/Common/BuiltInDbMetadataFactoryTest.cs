#nullable enable

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore.Common;

/// <summary>
/// The two halves of a shipped driver description: what is true of the driver, and the types it is
/// reached through.
/// </summary>
public sealed class BuiltInDbMetadataFactoryTest
{
    /// <summary>
    /// The drivers this test assembly does not reference, and so cannot load a type out of. Naming one
    /// is how an application says "SQLite through System.Data.SQLite" or "Oracle" — and a description of
    /// it has to be obtainable whether or not that assembly is on disk, because the point of the factory
    /// path is that Quartz never touches it.
    /// </summary>
    private static readonly string[] unreferencedDrivers = ["MySql", "MySqlConnector", "SQLite", "Firebird", "OracleODPManaged", "SystemDataSqlClient"];

    [TestCaseSource(nameof(unreferencedDrivers))]
    public void ADriverThisApplicationDoesNotReferenceStillDescribesItself(string providerName)
    {
        DbMetadata metadata = DbMetadataResolver.BuiltIn().ResolveWithoutTypes(providerName);

        metadata.ParameterNamePrefix.Should().NotBeNullOrEmpty(
            "how a driver spells a parameter is true of it whether or not its assembly is anywhere near "
            + "this process, and it is the whole of what a provider built over a DbProviderFactory needs");

        metadata.ConnectionType.Should().BeNull();
        metadata.CommandType.Should().BeNull();
        metadata.ParameterType.Should().BeNull();
        metadata.ParameterDbType.Should().BeNull();
        metadata.ExceptionType.Should().BeNull();
        metadata.DbBinaryTypeName.Should().BeNull(
            "the binary type is named as a value of the driver's own parameter type enum, so it belongs "
            + "with the types rather than with the facts");
    }

    /// <summary>
    /// The other half of the same pair: asking for the full description of a driver that is not here
    /// fails, and fails naming the provider.
    /// </summary>
    [TestCaseSource(nameof(unreferencedDrivers))]
    public void TheSameDriverCannotBeDescribedInFullWithoutItsAssembly(string providerName)
    {
        Action resolve = () => DbMetadataResolver.BuiltIn().Resolve(providerName);

        resolve.Should().Throw<ArgumentException>().WithMessage($"*'{providerName}'*");
    }

    /// <summary>
    /// The halves have to agree, or configuring a driver through its factory would spell parameters
    /// differently from configuring the same driver by name.
    /// </summary>
    [TestCase("SqlServer", "@")]
    [TestCase("MicrosoftDataSqlClient", "@")]
    [TestCase("Npgsql", ":")]
    [TestCase("SQLite-Microsoft", "@")]
    public void TheTypeFreeHalfSaysTheSameThingsAsTheFullDescription(string providerName, string parameterNamePrefix)
    {
        DbMetadata full = DbMetadataResolver.BuiltIn().Resolve(providerName);
        DbMetadata typeFree = DbMetadataResolver.BuiltIn().ResolveWithoutTypes(providerName);

        typeFree.ParameterNamePrefix.Should().Be(parameterNamePrefix).And.Be(full.ParameterNamePrefix);
        typeFree.BindByName.Should().Be(full.BindByName);
        typeFree.UseParameterNamePrefixInParameterCollection.Should().Be(full.UseParameterNamePrefixInParameterCollection);
        typeFree.ProductName.Should().Be(full.ProductName);
        typeFree.AssemblyName.Should().Be(full.AssemblyName);
    }

    [Test]
    public void ANameNothingDescribesIsReportedWithTheOnesThatAre()
    {
        Action resolve = () => DbMetadataResolver.BuiltIn().ResolveWithoutTypes("NoSuchDriver");

        resolve.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Valid DB Provider names are*");
    }
}
