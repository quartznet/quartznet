#region License

/*
 * Copyright 2009- Marko Lahma
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Data.Common;

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// Connects through the <see cref="DbProviderFactory"/> an ADO.NET driver ships, rather than through
/// types named in the driver description.
/// </summary>
/// <remarks>
/// <para>
/// Every ADO.NET provider exposes a singleton factory — <c>SqlClientFactory.Instance</c>,
/// <c>NpgsqlFactory.Instance</c>, <c>SqliteFactory.Instance</c> — and a factory hands back an instance
/// of every type the store uses: <see cref="DbProviderFactory.CreateConnection"/> for the connection,
/// the connection for the command, the command for its parameters. Nothing is resolved by name and
/// nothing is constructed by reflection, which is what a trimmed or ahead-of-time-compiled application
/// needs: the trimmer cannot see through <c>Type.GetType("Npgsql.NpgsqlConnection, Npgsql")</c>, and
/// removes the constructor behind it.
/// </para>
/// <para>
/// The driver description is still needed, because it decides how parameters are named, but it names no
/// type. A driver that needs a command or a binary parameter configured in a way Quartz cannot name
/// says so with <see cref="DbMetadata.ConfigureCommand"/> and
/// <see cref="DbMetadata.ConfigureBinaryParameter"/> — the application references the driver, so it can
/// name what Quartz cannot.
/// </para>
/// <para>
/// Public, unlike its data source sibling, because an application can hold a factory that no
/// <c>Use&lt;Db&gt;</c> overload knows about — one built by
/// <see cref="DbProviderFactories.GetFactory(string)"/>, or a driver's own subclass — and
/// <c>UseConnectionProvider</c> is where that goes.
/// </para>
/// </remarks>
public sealed class ProviderFactoryDbProvider : IDbProvider
{
    private readonly DbProviderFactory factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderFactoryDbProvider"/> class.
    /// </summary>
    /// <param name="metadata">
    /// The driver description. Only what the description says about parameter naming and the two typed
    /// seams is read; the types on it, if any, are ignored, because nothing here constructs one.
    /// </param>
    /// <param name="factory">The driver's factory, normally its <c>Instance</c> singleton.</param>
    /// <param name="connectionString">The connection string every connection is opened with.</param>
    public ProviderFactoryDbProvider(DbMetadata metadata, DbProviderFactory factory, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(connectionString);

        Metadata = metadata;
        this.factory = factory;
        ConnectionString = connectionString;
    }

    /// <inheritdoc />
    public DbConnection CreateConnection()
    {
        DbConnection connection = factory.CreateConnection()
            ?? Throw.ArgumentException<DbConnection>(
                $"{factory.GetType().FullName}.CreateConnection() returned null, so this factory cannot reach a database.",
                nameof(factory));

        connection.ConnectionString = ConnectionString;
        return connection;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A command with no connection of its own, for a caller that has none either — the store's own
    /// statements go through <see cref="CreateCommand(DbConnection)"/> instead.
    /// </remarks>
    public DbCommand CreateCommand()
    {
        DbCommand command = factory.CreateCommand()
            ?? Throw.ArgumentException<DbCommand>(
                $"{factory.GetType().FullName}.CreateCommand() returned null, so this factory cannot issue a statement.",
                nameof(factory));

        Metadata.ApplyCommandSettings(command);
        return command;
    }

    /// <summary>
    /// Mints a command on the connection the unit of work is running on.
    /// </summary>
    /// <remarks>
    /// The same reasoning as on the data source path, and the same answer: a command from
    /// <see cref="DbProviderFactory.CreateCommand"/> starts out attached to nothing and would be given
    /// the connection afterwards, missing whatever the connection was configured with before that point.
    /// Asking the connection is one call either way.
    /// </remarks>
    internal DbCommand CreateCommand(DbConnection connection)
    {
        DbCommand command = connection.CreateCommand();
        Metadata.ApplyCommandSettings(command);
        return command;
    }

    /// <summary>
    /// The type of connection the factory hands out, worked out by asking it for one.
    /// </summary>
    /// <remarks>
    /// Read by the two checks that used to read <see cref="DbMetadata.ConnectionType"/> — see
    /// <see cref="DbProviderConnections"/>. The connection is never opened and is not given a connection
    /// string, so this asks nothing of the database and cannot fail on a connection string the driver
    /// dislikes.
    /// </remarks>
    internal Type? ConnectionType => connectionType ??= SampleConnectionType();

    private Type? connectionType;

    private Type? SampleConnectionType()
    {
        using DbConnection? connection = factory.CreateConnection();
        return connection?.GetType();
    }

    /// <inheritdoc />
    public string ConnectionString { get; }

    /// <inheritdoc />
    public DbMetadata Metadata { get; }

    /// <inheritdoc />
    public void Shutdown()
    {
    }
}
