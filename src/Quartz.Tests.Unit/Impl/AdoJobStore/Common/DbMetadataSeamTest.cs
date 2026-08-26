#nullable enable

using System.Data;
using System.Data.Common;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore.Common;

/// <summary>
/// The two typed seams on a driver description, and what happens when a description names no type at
/// all.
/// </summary>
/// <remarks>
/// Quartz reaches a driver's own command and parameter types by reflection — <c>BindByName</c> on the
/// command, and the property <c>ParameterDbTypePropertyName</c> names on the parameter — because it
/// references no driver assembly and so cannot name them. An application does reference one, so it can
/// say the same things with a lambda; that is what these seams are, and they are what makes a
/// description that names no type able to describe a driver completely.
/// </remarks>
public sealed class DbMetadataSeamTest
{
    private static readonly DbMetadata NamedDriver = new()
    {
        ProductName = "Fake",
        ConnectionType = typeof(FakeConnection),
        CommandType = typeof(FakeCommand),
        ParameterType = typeof(FakeParameter),
        ParameterDbType = typeof(DbType),
        ParameterDbTypePropertyName = nameof(FakeParameter.DbType),
        DbBinaryTypeName = nameof(DbType.Binary),
        ParameterNamePrefix = "@",
        BindByName = true,
    };

    /// <summary>The same driver as a description behind a factory or a data source sees it.</summary>
    private static readonly DbMetadata TypeFreeDriver = new()
    {
        ProductName = "Fake",
        ParameterNamePrefix = "@",
        BindByName = true,
    };

    [Test]
    public void ConfigureCommand_IsAppliedToEveryCommand()
    {
        DbMetadata metadata = TypeFreeDriver with { ConfigureCommand = command => ((FakeCommand) command).BindByName = false };

        using FakeCommand command = new();
        metadata.ApplyCommandSettings(command);

        command.BindByName.Should().BeFalse(
            "a description naming no command type has nothing to look BindByName up on, so the seam is the "
            + "only way it can be set at all");
    }

    [Test]
    public void ConfigureCommand_WinsOverTheReflectiveBindByNameProbe()
    {
        DbMetadata metadata = NamedDriver with
        {
            BindByName = true,
            ConfigureCommand = command => ((FakeCommand) command).BindByName = false,
        };

        using FakeCommand command = new();
        metadata.ApplyCommandSettings(command);

        command.BindByName.Should().BeFalse(
            "the description said what to do, so the probe that guesses at it must not run afterwards and "
            + "undo it");
    }

    [Test]
    public void WithNoSeam_TheReflectiveBindByNameProbeStillRuns()
    {
        using FakeCommand command = new();
        (NamedDriver with { BindByName = false }).ApplyCommandSettings(command);

        command.BindByName.Should().BeFalse(
            "the driver named its command type, so BindByName is set the way it always was - the seam adds "
            + "a route rather than replacing one");
    }

    [Test]
    public void ConfigureBinaryParameter_IsAppliedToABinaryParameter()
    {
        int configured = 0;
        DbMetadata metadata = TypeFreeDriver with
        {
            ConfigureBinaryParameter = parameter =>
            {
                configured++;
                parameter.Size = -1;
            },
        };

        FakeParameter parameter = new();
        metadata.ApplyParameterType(parameter, metadata.BinaryParameterType);

        configured.Should().Be(1);
        parameter.Size.Should().Be(-1,
            "Oracle's blob column is the reason the seam exists: DbType.Binary means OracleDbType.Raw there, "
            + "and a job data map over two kilobytes will not fit in one");
    }

    [Test]
    public void WithNoSeamAndNoParameterType_ABinaryParameterIsBoundAsDbTypeBinary()
    {
        FakeParameter parameter = new();
        TypeFreeDriver.ApplyParameterType(parameter, TypeFreeDriver.BinaryParameterType);

        parameter.DbType.Should().Be(DbType.Binary,
            "a driver that ships a DbProviderFactory maps DbType itself, so the framework's own spelling of "
            + "'this is a blob' is enough");
    }

    [Test]
    public void ADescriptionThatNamesTheParameterTypeStillWritesTheDescribedProperty()
    {
        FakeParameter parameter = new();
        NamedDriver.ApplyParameterType(parameter, NamedDriver.BinaryParameterType);

        parameter.DbType.Should().Be(DbType.Binary);
        NamedDriver.ParameterDbTypeProperty.Should().NotBeNull(
            "the described property is what the name path has always written, and it is still what it uses");
    }

    /// <summary>
    /// <see cref="DbProvider" /> is the one provider that constructs the driver's own objects, so it is
    /// the one that cannot work without their types. It says so where the description arrives.
    /// </summary>
    [Test]
    public void DbProvider_RefusesADescriptionThatNamesNoConnectionType()
    {
        Action build = () => new DbProvider(TypeFreeDriver, "irrelevant");

        build.Should().Throw<ArgumentException>()
            .WithMessage("*ConnectionType*")
            .WithMessage("*DbProviderFactory*",
                "the way out belongs in the message: this description is exactly the one a factory or a data "
                + "source is for");
    }

    [Test]
    public void AParameterTypeWithNowhereToWriteItIsReported()
    {
        FakeParameter parameter = new();
        Action bind = () => TypeFreeDriver.ApplyParameterType(parameter, SqlDbTypeLookalike.VarBinary);

        bind.Should().Throw<InvalidOperationException>()
            .WithMessage("*ParameterType*",
                "a driver-specific enum needs the driver's own property to write it to, and silently dropping "
                + "it would bind a blob as whatever the value inferred");
    }

    /// <summary>
    /// Stands in for a driver's own parameter type enum — <c>SqlDbType</c>, <c>NpgsqlDbType</c> — which
    /// only means anything on the property that driver's parameter declares for it.
    /// </summary>
    private enum SqlDbTypeLookalike
    {
        VarBinary,
    }
}
