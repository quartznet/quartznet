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
/// A provider is described by its <see cref="DbMetadata"/>: the connection, command and parameter types
/// to instantiate, the parameter prefix, and so on. Descriptions come from the container — the drivers
/// Quartz ships descriptions for, plus anything the application registered — rather than from process-wide
/// state, so two containers in one process no longer have to agree on what a provider name means.
/// </remarks>
/// <author>Marko Lahma</author>
public class DbProvider : IDbProvider
{
    private readonly MethodInfo? commandBindByNamePropertySetter;
    private readonly ConstructorInfo connectionConstructor;
    private readonly ConstructorInfo commandConstructor;

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

        // check if command supports direct setting of BindByName property, needed for Oracle Managed ODP diver at least
        var property = Metadata.CommandType?.GetProperty("BindByName", BindingFlags.Instance | BindingFlags.Public);
        if (property is not null && property.PropertyType == typeof(bool) && property.CanWrite)
        {
            commandBindByNamePropertySetter = property.GetSetMethod()!;
        }

        connectionConstructor = ObjectUtils.GetDefaultConstructor(Metadata.ConnectionType);
        commandConstructor = ObjectUtils.GetDefaultConstructor((Metadata.CommandType));
    }

    /// <inheritdoc />
    public virtual DbCommand CreateCommand()
    {
        DbCommand command = (DbCommand) commandConstructor.Invoke([]);
        commandBindByNamePropertySetter?.Invoke(command, [Metadata.BindByName]);
        return command;
    }

    /// <inheritdoc />
    public virtual DbConnection CreateConnection()
    {
        DbConnection conn = (DbConnection) connectionConstructor.Invoke([]);
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