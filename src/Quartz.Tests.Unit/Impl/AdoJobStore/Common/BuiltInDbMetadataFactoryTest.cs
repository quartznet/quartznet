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
    /// is how an application says "SQLite through System.Data.SQLite" or "MySQL through MySql.Data" — and a
    /// description of it has to be obtainable whether or not that assembly is on disk, because the point of
    /// the factory path is that Quartz never touches it.
    /// </summary>
    /// <remarks>
    /// MySqlConnector, Oracle's managed driver and FirebirdClient are not among them: Weasel.MySql,
    /// Weasel.Oracle and Weasel.Firebird depend on them, and this assembly tests the Quartz.Weasel packages
    /// built on those.
    /// </remarks>
    private static readonly string[] unreferencedDrivers = ["MySql", "SQLite", "SystemDataSqlClient"];

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
    /// A missing driver is the first error a new persistent store usually meets, so it names the package
    /// to add rather than the provider alone, and keeps the loader's exception for the details.
    /// </summary>
    [TestCase("MySql", "MySql.Data")]
    [TestCase("SQLite", "System.Data.SQLite.Core")]
    [TestCase("SystemDataSqlClient", "System.Data.SqlClient")]
    public void ADriverWhoseAssemblyIsMissingNamesThePackageToAdd(string providerName, string package)
    {
        Action resolve = () => DbMetadataResolver.BuiltIn().Resolve(providerName);

        resolve.Should().Throw<ArgumentException>()
            .WithMessage($"Provider '{providerName}' needs the {package} package: dotnet add package {package}.*")
            .WithInnerException<FileNotFoundException>();
    }

    /// <summary>
    /// Every driver Quartz describes has a package to name, or the message above would fall back to the
    /// provider name alone for it.
    /// </summary>
    [Test]
    public void EveryBuiltInDriverNamesItsPackage()
    {
        List<string> providerNames = new BuiltInDbMetadataFactory().GetProviderNames();

        providerNames.Should().AllSatisfy(name => BuiltInDbMetadataFactory.DriverPackage(name).Should().NotBeNullOrEmpty(
            $"a missing '{name}' driver is reported with the package to add"));
        BuiltInDbMetadataFactory.DriverPackage("Npgsql").Should().Be("Npgsql");
        BuiltInDbMetadataFactory.DriverPackage("OracleODPManaged").Should().Be("Oracle.ManagedDataAccess.Core",
            "the job-store page's table names the .NET Core package, not the .NET Framework one");
    }

    /// <summary>
    /// A failure that is not a missing assembly, or one of a provider with no package to name, keeps the
    /// old wording and adds what the loader said.
    /// </summary>
    [Test]
    public void AFailureThatIsNotAMissingDriverSaysWhatTheLoaderSaid()
    {
        BuiltInDbMetadataFactory.DescribeLoadFailure("Npgsql", new TypeLoadException("Could not load type 'NpgsqlTypes.NpgsqlDbType'."))
            .Should().Be("Error while reading metadata information for provider 'Npgsql': Could not load type 'NpgsqlTypes.NpgsqlDbType'.",
                "the package is there, so telling the reader to add it would send them the wrong way");

        BuiltInDbMetadataFactory.DescribeLoadFailure("Acme", new FileNotFoundException("Could not load file or assembly 'Acme.Data'."))
            .Should().Be("Error while reading metadata information for provider 'Acme': Could not load file or assembly 'Acme.Data'.",
                "Quartz knows no package for a provider it does not describe");
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
