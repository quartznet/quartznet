#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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

#nullable enable

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Weasel.Core.Migrations;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// A container with one scheduler whose SQLite store hands its schema to Weasel, and the Weasel database
/// that registration produces — reached the way <c>db-apply</c> reaches it, through
/// <see cref="IDatabaseSource" />.
/// </summary>
internal sealed class WeaselSqliteContainer : IAsyncDisposable
{
    private WeaselSqliteContainer(ServiceProvider services, IDatabase database)
    {
        Services = services;
        Database = database;
    }

    public ServiceProvider Services { get; }

    public IDatabase Database { get; }

    public static async Task<WeaselSqliteContainer> CreateAsync(
        string connectionString,
        string schedulerName,
        string tablePrefix = "QRTZ_",
        Action<SqliteWeaselOptions>? configure = null)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ConfigureStore(options => options.TablePrefix = tablePrefix);
                store.UseWeaselForSqlite(configure);
            });
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IReadOnlyList<IDatabase> databases = await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();

        return new WeaselSqliteContainer(provider, databases.Should().ContainSingle().Subject);
    }

    public ValueTask DisposeAsync() => Services.DisposeAsync();
}
