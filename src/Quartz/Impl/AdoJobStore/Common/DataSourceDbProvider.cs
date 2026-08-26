using System.Data.Common;

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// Connects through a <see cref="DbDataSource"/> registered in the container rather than through a
/// connection string Quartz holds.
/// </summary>
/// <remarks>
/// <para>
/// The driver description is still needed, because it decides how parameters are named and typed, but the
/// connection itself comes from the data source. It is passed in rather than looked up, so this works the
/// same whether the driver is one Quartz ships a description for or one the application described.
/// </para>
/// <para>
/// Deliberately not a <see cref="DbProvider"/>: that class exists to construct the driver's connection
/// and command types, and running its constructor is what a trimmed application cannot survive — the
/// types were resolved from a name and the trimmer has removed what it could not see used. A data
/// source hands over the connection, so there is nothing here to construct and no type to name.
/// </para>
/// </remarks>
internal sealed class DataSourceDbProvider : IDbProvider
{
    private readonly DbDataSource source;

    public DataSourceDbProvider(DbMetadata metadata, DbDataSource source)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(source);

        Metadata = metadata;
        this.source = source;
    }

    /// <summary>
    /// The data source connections come from.
    /// </summary>
    /// <remarks>
    /// Read by the shared-database check, which uses the object itself as the identity of the database:
    /// a data source keeps its connection details to itself, so <see cref="ConnectionString"/>
    /// is empty here and cannot answer for it, while two schedulers pointed at one registered data source
    /// hold the same instance.
    /// </remarks>
    internal DbDataSource DataSource => source;

    /// <inheritdoc />
    public DbConnection CreateConnection()
    {
        return source.CreateConnection();
    }

    /// <inheritdoc />
    /// <remarks>
    /// A command that opens and closes a connection of its own, which is the only kind that makes sense
    /// with no unit of work in sight. The store's own statements go through
    /// <see cref="CreateCommand(DbConnection)"/> instead, for the reason that method gives.
    /// </remarks>
    public DbCommand CreateCommand()
    {
        DbCommand command = source.CreateCommand();
        Metadata.ApplyCommandSettings(command);
        return command;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Empty: a data source keeps its connection details to itself, and Quartz never sees them.
    /// </remarks>
    public string ConnectionString => "";

    /// <inheritdoc />
    public DbMetadata Metadata { get; }

    /// <inheritdoc />
    public void Shutdown()
    {
    }

    /// <summary>
    /// Mints a command on the connection the unit of work is running on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A data source configures the connections it hands out — <c>NpgsqlDataSource</c> attaches its type
    /// mappers, its logging and its composite type registrations — and a command reaches those through
    /// the connection it belongs to. A command constructed by reflection over
    /// <see cref="DbMetadata.CommandType"/>, or handed over by a
    /// <see cref="DbProviderFactory"/>, starts out attached to nothing, so it would be given the
    /// connection afterwards and miss whatever the data source configured before that point. Asking the
    /// connection for the command is what keeps the data source's configuration in play.
    /// </para>
    /// <para>
    /// Deliberately not <see cref="DbDataSource.CreateCommand"/>: those commands open and close a
    /// connection of their own, which cannot join the transaction Quartz's unit of work is running in.
    /// </para>
    /// </remarks>
    internal DbCommand CreateCommand(DbConnection connection)
    {
        DbCommand command = connection.CreateCommand();
        Metadata.ApplyCommandSettings(command);
        return command;
    }
}
