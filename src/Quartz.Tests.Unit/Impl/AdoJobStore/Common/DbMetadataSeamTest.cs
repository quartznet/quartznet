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
    /// The half-configured description an application writing its own <see cref="DbMetadata" /> lands
    /// on: it named a binary type and forgot to say which property carries it. The property used to be
    /// a non-nullable <c>string</c> initialised to <c>null!</c>, so the failure was
    /// <c>Type.GetProperty(null)</c> — "Value cannot be null. (Parameter 'name')" — two lines from the
    /// deliberate message its neighbours raise.
    /// </summary>
    [Test]
    public void ABinaryTypeWithNoPropertyToWriteItToIsReportedLikeItsNeighbours()
    {
        DbMetadata forgotten = NamedDriver with { ParameterDbTypePropertyName = null };

        FakeParameter parameter = new();
        Action bind = () => forgotten.ApplyParameterType(parameter, forgotten.BinaryParameterType);

        bind.Should().Throw<ArgumentException>()
            .WithMessage("*Fake*", "which provider is half-configured is the first thing to say")
            .WithMessage("*DbBinaryTypeName*", "and which setting made the other one required")
            .WithMessage("*ParameterDbTypePropertyName*", "and which one is missing");
    }

    /// <summary>
    /// A driver that names no large-text type binds the captured log as the string it is: every driver
    /// but the managed Oracle one takes a string into its large text column.
    /// </summary>
    [Test]
    public void WithNoLargeTextTypeTheLogIsBoundAsItIs()
    {
        NamedDriver.LargeTextParameterType.Should().BeNull(
            "nothing is said about a parameter whose driver needs nothing said, so no parameter type is written");
        TypeFreeDriver.LargeTextParameterType.Should().BeNull();
    }

    [Test]
    public void ANamedLargeTextTypeIsWrittenThroughTheDescribedProperty()
    {
        DbMetadata metadata = NamedDriver with { DbLargeTextTypeName = nameof(DbType.String) };

        FakeParameter parameter = new();
        metadata.ApplyParameterType(parameter, metadata.LargeTextParameterType!);

        metadata.LargeTextParameterType.Should().Be(DbType.String);
        parameter.DbType.Should().Be(DbType.String,
            "the name is resolved against ParameterDbType and written where a binary type is, which is how "
            + "OracleDbType.Clob reaches an OracleParameter without Quartz naming the driver");
    }

    /// <summary>
    /// The large-text type is a member of the driver's own enum, so it belongs with the types, as the
    /// binary type does: the half of a description a factory or a data source reads names none.
    /// </summary>
    /// <remarks>
    /// The full Oracle description, which names <c>Clob</c>, needs <c>Oracle.ManagedDataAccess</c> loaded;
    /// the Oracle integration leg is what binds a log through it.
    /// </remarks>
    [Test]
    public void NoTypeFreeDescriptionNamesALargeTextType()
    {
        BuiltInDbMetadataFactory factory = new();

        foreach (string provider in factory.GetProviderNames())
        {
            DbMetadata facts = factory.GetTypeFreeDbMetadata(provider);

            facts.DbLargeTextTypeName.Should().BeNull(
                $"{provider}'s type-free description has no ParameterDbType to resolve an enum member against");
            facts.LargeTextParameterType.Should().BeNull($"and so binds a string as it is");
        }
    }

    [Test]
    public void ConfigureLargeTextParameter_IsAppliedToALargeTextParameterOnly()
    {
        int configured = 0;
        DbMetadata metadata = TypeFreeDriver with
        {
            ConfigureLargeTextParameter = parameter =>
            {
                configured++;
                parameter.Size = -1;
            },
        };

        FakeParameter parameter = new();
        metadata.ApplyParameterType(parameter, metadata.LargeTextParameterType!);

        configured.Should().Be(1, "a description reached through a factory says what a Clob is with the seam");
        parameter.Size.Should().Be(-1);

        FakeParameter binary = new();
        metadata.ApplyParameterType(binary, metadata.BinaryParameterType);

        configured.Should().Be(1, "the large-text seam is for the large-text parameter and no other");
        binary.DbType.Should().Be(DbType.Binary);
    }

    [Test]
    public void ConfigureLargeTextParameter_WinsOverTheNamedType()
    {
        bool configured = false;
        DbMetadata metadata = NamedDriver with
        {
            DbLargeTextTypeName = nameof(DbType.String),
            ConfigureLargeTextParameter = _ => configured = true,
        };

        FakeParameter parameter = new() { DbType = DbType.Int32 };
        metadata.ApplyParameterType(parameter, metadata.LargeTextParameterType!);

        configured.Should().BeTrue("set, the seam wins, exactly as ConfigureBinaryParameter does");
        parameter.DbType.Should().Be(DbType.Int32, "and the reflective setter does not run after it");
    }

    [Test]
    public void ALargeTextSeamHandedAParameterThatIsNotADbParameterSaysSo()
    {
        DbMetadata metadata = TypeFreeDriver with { ConfigureLargeTextParameter = _ => { } };

        Action bind = () => metadata.ApplyParameterType(new PlainParameter(), metadata.LargeTextParameterType!);

        bind.Should().Throw<InvalidOperationException>().WithMessage("*ConfigureLargeTextParameter*");
    }

    [Test]
    public void ALargeTextTypeWithNoPropertyToWriteItToIsReported()
    {
        DbMetadata forgotten = TypeFreeDriver with
        {
            ParameterType = typeof(FakeParameter),
            ParameterDbType = typeof(DbType),
            DbLargeTextTypeName = nameof(DbType.String),
        };

        Action resolve = () => _ = forgotten.LargeTextParameterType;

        resolve.Should().Throw<ArgumentException>()
            .WithMessage("*DbLargeTextTypeName*", "the setting that made the property required is the one named")
            .WithMessage("*ParameterDbTypePropertyName*");
    }

    /// <summary>
    /// An <see cref="IDbDataParameter" /> that is not a <see cref="DbParameter" />, which no driver hands
    /// out but a caller of the binder could.
    /// </summary>
    private sealed class PlainParameter : IDbDataParameter
    {
        public DbType DbType { get; set; }

        public ParameterDirection Direction { get; set; }

        public bool IsNullable => true;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string ParameterName { get; set; } = "";

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string SourceColumn { get; set; } = "";

        public DataRowVersion SourceVersion { get; set; }

        public object? Value { get; set; }

        public byte Precision { get; set; }

        public byte Scale { get; set; }

        public int Size { get; set; }
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
