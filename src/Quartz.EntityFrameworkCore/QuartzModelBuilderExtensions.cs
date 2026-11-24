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

using Microsoft.EntityFrameworkCore;

using Quartz.EntityFrameworkCore;

namespace Quartz;

/// <summary>
/// Adds the ADO.NET job store's tables to an Entity Framework Core model.
/// </summary>
public static class QuartzModelBuilderExtensions
{
    /// <summary>
    /// Adds the job store's tables to this model, so that <c>dotnet ef migrations add</c> creates them, and after
    /// a Quartz upgrade adds what the release added.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call it from <c>OnModelCreating</c> with <c>Database.ProviderName</c>. The provider decides the dialect:
    /// PostgreSQL (Npgsql), SQL Server, MySQL (Oracle's <c>MySql.EntityFrameworkCore</c> or Pomelo), SQLite, Oracle
    /// and Firebird.
    /// The tables are the ones <c>SchemaProvisioning.CreateIfMissing</c> creates, generated from the same model.
    /// </para>
    /// <para>
    /// The migration that creates the tables ends with <c>migrationBuilder.CompleteQuartzSchema()</c>, which
    /// adds the part of the schema an EF Core model cannot say.
    /// </para>
    /// <para>
    /// The tables are the job store's: nothing in the model is meant to be queried or written through EF Core.
    /// </para>
    /// </remarks>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="providerName">The EF Core provider's name: <c>Database.ProviderName</c>.</param>
    /// <param name="configure">
    /// Sets the table prefix and the schema, which must name the job store's tables, and on Firebird the longest
    /// name the database takes.
    /// </param>
    /// <returns>The same model builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">No provider is configured.</exception>
    /// <exception cref="NotSupportedException">The provider is not one Quartz has a model for.</exception>
    /// <exception cref="ArgumentException">
    /// The prefix holds what an unquoted identifier cannot or makes a name too long for the database, the schema
    /// is set for a database without schemas, or the identifier length is set for a database other than Firebird
    /// or outside 31–63.
    /// </exception>
    public static ModelBuilder AddQuartzTables(this ModelBuilder modelBuilder, string? providerName, Action<QuartzTablesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        QuartzDialect dialect = QuartzDialect.ForProvider(providerName);
        QuartzModel.Add(modelBuilder, dialect, QuartzTableNaming.Create(dialect, configure));

        return modelBuilder;
    }
}
