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
using System.Reflection;

using Quartz.Util;

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// Concrete implementation of <see cref="IDbProvider" />.
/// </summary>
/// <remarks>
/// <para>
/// A provider is described by its <see cref="DbMetadata"/>: the connection, command and parameter types
/// to instantiate, the parameter prefix, and so on. Descriptions come from the container — the drivers
/// Quartz ships descriptions for, plus anything the application registered — rather than from process-wide
/// state, so two containers in one process no longer have to agree on what a provider name means.
/// </para>
/// <para>
/// This is the provider that <em>constructs</em> the driver's objects, and so the one that needs their
/// types. When the description resolved them from a name — which is what naming a built-in driver does
/// — a trimmed application has no guarantee they survived, and the constructor below is where that
/// shows up. A provider that needs no type at all is the one built over a
/// <see cref="System.Data.Common.DbProviderFactory"/>, or the data source path.
/// </para>
/// </remarks>
/// <author>Marko Lahma</author>
public class DbProvider : IDbProvider
{
    private readonly ConstructorInvoker connectionConstructor;
    private readonly ConstructorInvoker commandConstructor;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbProvider"/> class, described by one of the drivers
    /// Quartz ships a description for.
    /// </summary>
    /// <remarks>
    /// This overload has no container to ask, so it sees only the built-in descriptions. A driver Quartz
    /// knows nothing about is described through the container instead — see the metadata callback on
    /// <c>UseGenericDatabase</c> — which is also how a built-in description is replaced.
    /// </remarks>
    /// <param name="dbProviderName">Name of the db provider.</param>
    /// <param name="connectionString">The connection string.</param>
    public DbProvider(string dbProviderName, string connectionString)
        : this(DbMetadataResolver.BuiltIn().Resolve(dbProviderName), connectionString)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DbProvider"/> class from an already-resolved
    /// description of the driver.
    /// </summary>
    /// <param name="metadata">The metadata describing the ADO.NET driver.</param>
    /// <param name="connectionString">The connection string.</param>
    public DbProvider(DbMetadata metadata, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        ConnectionString = connectionString;
        Metadata = metadata;

        // This is the provider that constructs the driver's own objects, so it is the one that cannot
        // work without their types. Said here, naming the way out, rather than as a null reference from
        // inside the reflection below.
        if (metadata.ConnectionType is null || metadata.CommandType is null)
        {
            Throw.ArgumentException(
                $"The description of '{metadata.ProductName ?? "the driver"}' names no "
                + $"{(metadata.ConnectionType is null ? nameof(DbMetadata.ConnectionType) : nameof(DbMetadata.CommandType))}, "
                + "so there is nothing for this provider to construct. Describe the driver's types, or reach the driver "
                + "without naming them - pass a DbProviderFactory to the Use<Db> overload that takes one, or register a "
                + "DbDataSource in the container.",
                nameof(metadata));
        }

        // Invokers rather than the ConstructorInfo itself: every command and every connection the store
        // opens goes through these, and ConstructorInfo.Invoke walks its argument array and re-checks the
        // signature on each call.
        connectionConstructor = ConstructorInvoker.Create(TypeActivator.GetDefaultConstructor(metadata.ConnectionType));
        commandConstructor = ConstructorInvoker.Create(TypeActivator.GetDefaultConstructor(metadata.CommandType));
    }

    /// <inheritdoc />
    public virtual DbCommand CreateCommand()
    {
        DbCommand command = (DbCommand) commandConstructor.Invoke();
        Metadata.ApplyCommandSettings(command);
        return command;
    }

    /// <inheritdoc />
    public virtual DbConnection CreateConnection()
    {
        DbConnection conn = (DbConnection) connectionConstructor.Invoke();
        conn.ConnectionString = ConnectionString;
        return conn;
    }

    /// <inheritdoc />
    public string ConnectionString { get; }

    /// <inheritdoc />
    public DbMetadata Metadata { get; }

    /// <inheritdoc />
    public virtual void Shutdown()
    {
    }
}