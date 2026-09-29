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

using System.Data.Common;

using JasperFx;

using Microsoft.Extensions.Logging;

using Quartz.Impl.AdoJobStore.Common;

using Weasel.Core.Migrations;

namespace Quartz.Weasel;

/// <summary>
/// What a dialect package tells the glue about the database it models.
/// </summary>
internal sealed class QuartzWeaselDialect
{
    /// <summary>The database, as a reader names it: <c>PostgreSQL</c>.</summary>
    public required string DatabaseName { get; init; }

    /// <summary>The extension that registers the dialect, for messages: <c>UseWeaselForPostgres</c>.</summary>
    public required string RegistrationMethod { get; init; }

    /// <summary>The store method that chooses the matching driver, for messages: <c>UsePostgres</c>.</summary>
    public required string StoreMethod { get; init; }

    /// <summary>
    /// The schema the tables are in when the table prefix names none, or empty when the dialect's database
    /// reads it from the connection: MySQL's connection database, Oracle's current schema.
    /// </summary>
    public required string DefaultSchema { get; init; }

    /// <summary>Whether a connection the store's provider creates is one this dialect can migrate.</summary>
    public required Func<DbConnection, bool> AcceptsConnection { get; init; }
}

/// <summary>
/// One scheduler's request to have its schema managed by Weasel, as the dialect package recorded it.
/// </summary>
internal sealed class QuartzWeaselRegistration
{
    /// <summary>The scheduler's registration key: <see langword="null" /> for the default scheduler.</summary>
    public required string? SchedulerKey { get; init; }

    public required QuartzWeaselDialect Dialect { get; init; }

    /// <summary>The <see cref="AutoCreate" /> the application chose, or <see langword="null" /> for none.</summary>
    public required AutoCreate? AutoCreate { get; init; }

    /// <summary>Builds the Weasel database once everything it needs has been resolved.</summary>
    public required Func<QuartzWeaselDatabaseContext, IQuartzWeaselDatabase> CreateDatabase { get; init; }
}

/// <summary>
/// Everything a dialect's database is built from, resolved out of the scheduler's own registrations.
/// </summary>
internal sealed class QuartzWeaselDatabaseContext
{
    /// <summary>The scheduler's name, which is also the database's Weasel identifier.</summary>
    public required string SchedulerName { get; init; }

    /// <summary>
    /// The schema the tables are in: the table prefix's, or the dialect's default — empty when the
    /// dialect's database reads it from the connection.
    /// </summary>
    public required string Schema { get; init; }

    /// <summary>The table prefix without its schema, spelled as the store's options spell it.</summary>
    public required string TablePrefix { get; init; }

    /// <summary>The store's own connection provider, so that Weasel reaches the database the store does.</summary>
    public required IDbProvider DbProvider { get; init; }

    /// <summary>
    /// The connection string of a connection the provider created, which is what describes the database:
    /// <see cref="IDbProvider.ConnectionString" /> is empty when connections come from a registered
    /// <c>DbDataSource</c>, and the connection it hands out is not.
    /// </summary>
    public required string ConnectionString { get; init; }

    public required IMigrationLogger MigrationLogger { get; init; }

    public required ILogger Logger { get; init; }

    /// <summary><c>quartz://scheduler/&lt;name&gt;</c>, what <c>db-patch -d</c> and friends match.</summary>
    public required Uri SubjectUri { get; init; }

    /// <summary>The <see cref="AutoCreate" /> the application chose, or <see langword="null" /> for none.</summary>
    public required AutoCreate? AutoCreate { get; init; }
}

/// <summary>
/// A dialect's Weasel database for one scheduler.
/// </summary>
/// <remarks>
/// Every implementation re-implements <see cref="IDatabase.ApplyAllConfiguredChangesToDatabaseAsync" />,
/// because that is the one member every apply path reaches — startup, <c>db-apply</c> and
/// <c>resources setup</c> — and Weasel's own implementation takes no lock on any of them.
/// </remarks>
internal interface IQuartzWeaselDatabase : IDatabase
{
    QuartzWeaselDatabaseContext Context { get; }

    /// <summary>
    /// What happens when the migration lock cannot be taken in time; Weasel's own setting, which the
    /// startup applier copies from the active JasperFx profile.
    /// </summary>
    ResourceMigrationFailureMode ResourceMigrationFailureMode { get; set; }
}
