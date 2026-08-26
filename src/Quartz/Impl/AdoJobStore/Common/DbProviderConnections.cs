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

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// What kind of connection a provider produces, for the two checks that need to know.
/// </summary>
/// <remarks>
/// <para>
/// Both are diagnostics rather than mechanics: refusing an enlisted connection that came from another
/// driver, which would otherwise fail as a cast error deep inside the first statement, and warning
/// about a SQL Server connection paired with a delegate that speaks generic SQL. Both used to read
/// <see cref="DbMetadata.ConnectionType"/>, which a description reached through a
/// <see cref="System.Data.Common.DbProviderFactory"/> or a
/// <see cref="System.Data.Common.DbDataSource"/> does not have.
/// </para>
/// <para>
/// The answer is to ask the provider rather than the description: a provider that was handed a factory
/// or a data source can make one connection and look at what it got. That costs one allocation once —
/// the connection is never opened, and no connection string is put on it — and it makes both checks
/// work for a registration that names no type, which is the registration a trimmed application makes.
/// </para>
/// </remarks>
internal static class DbProviderConnections
{
    /// <summary>
    /// The type of connection <paramref name="provider"/> hands out, or <see langword="null"/> when
    /// nothing can say — a provider Quartz did not write, described by nothing.
    /// </summary>
    internal static Type? ExpectedConnectionType(this IDbProvider provider)
    {
        if (provider.Metadata.ConnectionType is { } described)
        {
            return described;
        }

        return provider switch
        {
            ProviderFactoryDbProvider factoryProvider => factoryProvider.ConnectionType,
            DataSourceDbProvider dataSourceProvider => dataSourceProvider.ConnectionType,
            _ => null,
        };
    }
}
