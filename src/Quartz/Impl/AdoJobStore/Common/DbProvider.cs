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

using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Data.Common;
using System.Reflection;
using System.Text;

using Quartz.Diagnostics;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// Concrete implementation of <see cref="IDbProvider" />.
/// </summary>
/// <author>Marko Lahma</author>
public class DbProvider : IDbProvider
{
    protected const string PropertyDbProvider = StdSchedulerFactory.PropertyDbProvider;
    protected const string DbProviderResourceName = "Quartz.Impl.AdoJobStore.Common.dbproviders.netstandard.properties";

    private readonly MethodInfo? commandBindByNamePropertySetter;
    private readonly ConstructorInfo connectionConstructor;
    private readonly ConstructorInfo commandConstructor;

    private static readonly List<DbMetadataFactory> dbMetadataFactories;
    // needs to allow concurrent threads to read and update, since field is static
    private static readonly ConcurrentDictionary<string, DbMetadata> dbMetadataLookup = new();

    /// <summary>
    /// Parse metadata once.
    /// </summary>
    static DbProvider()
    {
        var properties = StdSchedulerFactory.InitializeProperties(LogProvider.CreateLogger<StdSchedulerFactory>(), throwOnProblem: false);
        dbMetadataFactories = new List<DbMetadataFactory>
        {
            new ConfigurationBasedDbMetadataFactory(properties ?? new NameValueCollection(), PropertyDbProvider),
            new EmbeddedAssemblyResourceDbMetadataFactory(DbProviderResourceName, PropertyDbProvider)
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DbProvider"/> class.
    /// </summary>
    /// <param name="dbProviderName">Name of the db provider.</param>
    /// <param name="connectionString">The connection string.</param>
    public DbProvider(string dbProviderName, string connectionString)
    {
        ConnectionString = connectionString;
        Metadata = GetDbMetadata(dbProviderName);

        if (Metadata is null)
        {
            ThrowHelper.ThrowArgumentException($"Invalid DB provider name: {dbProviderName}{Environment.NewLine}{GenerateValidProviderNamesInfo()}");
        }

        // check if command supports direct setting of BindByName property, needed for Oracle Managed ODP diver at least
        var property = Metadata.CommandType?.GetProperty("BindByName", BindingFlags.Instance | BindingFlags.Public);
        if (property is not null && property.PropertyType == typeof(bool) && property.CanWrite)
        {
            commandBindByNamePropertySetter = property.GetSetMethod()!;
        }

        connectionConstructor = ObjectUtils.GetDefaultConstructor(Metadata.ConnectionType);
        commandConstructor = ObjectUtils.GetDefaultConstructor((Metadata.CommandType));
    }

    public void Initialize()
    {
        // do nothing, initialized in static constructor
    }

    ///<summary>
    /// Registers DB metadata information for given provider name.
    ///</summary>
    ///<param name="dbProviderName"></param>
    ///<param name="metadata"></param>
    public static void RegisterDbMetadata(string dbProviderName, DbMetadata metadata)
    {
        dbMetadataLookup[dbProviderName] = metadata;
    }

    private static DbMetadata GetDbMetadata(string providerName)
    {
        if (!dbMetadataLookup.TryGetValue(providerName, out var result))
        {
            foreach (DbMetadataFactory? dbMetadataFactory in dbMetadataFactories)
            {
                if (dbMetadataFactory.GetProviderNames().Contains(providerName))
                {
                    result = dbMetadataFactory.GetDbMetadata(providerName);
                    RegisterDbMetadata(providerName, result);
                    return result;
                }
            }
            ThrowHelper.ThrowArgumentOutOfRangeException(nameof(providerName), $"There is no metadata information for provider '{providerName}'");
        }

        return result;
    }

    /// <summary>
    /// Generates the valid provider names information.
    /// </summary>
    /// <returns></returns>
    protected static string GenerateValidProviderNamesInfo()
    {
        var providerNames = dbMetadataFactories
            .SelectMany(factory => factory.GetProviderNames())
            .Distinct()
            .OrderBy(name => name);

        StringBuilder sb = new StringBuilder("Valid DB Provider names are:").Append(Environment.NewLine);
        foreach (string providerName in providerNames)
        {
            sb.Append('\t').Append(providerName).Append(Environment.NewLine);
        }
        return sb.ToString();
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
    public string ConnectionString { get; set; }

    /// <inheritdoc />
    public DbMetadata Metadata { get; }

    /// <inheritdoc />
    public virtual void Shutdown()
    {
    }
}