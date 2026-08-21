using System.Data.Common;

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// Connects through a <see cref="DbDataSource"/> registered in the container rather than through a
/// connection string Quartz holds.
/// </summary>
/// <remarks>
/// The driver description is still needed, because it decides how parameters are named and typed, but the
/// connection itself comes from the data source. It is passed in rather than looked up, so this works the
/// same whether the driver is one Quartz ships a description for or one the application described.
/// </remarks>
internal sealed class DataSourceDbProvider : DbProvider
{
    private readonly DbDataSource source;

    public DataSourceDbProvider(DbMetadata metadata, DbDataSource source)
        : base(metadata, string.Empty)
    {
        this.source = source;
    }

    public override DbConnection CreateConnection()
    {
        return source.CreateConnection();
    }

    /// <summary>
    /// Mints a command on the connection the unit of work is running on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A data source configures the connections it hands out — <c>NpgsqlDataSource</c> attaches its type
    /// mappers, its logging and its composite type registrations — and a command reaches those through
    /// the connection it belongs to. A command constructed by reflection over
    /// <see cref="DbMetadata.CommandType"/> starts out attached to nothing, so it would be given the
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
        ApplyDriverCommandSettings(command);
        return command;
    }
}
