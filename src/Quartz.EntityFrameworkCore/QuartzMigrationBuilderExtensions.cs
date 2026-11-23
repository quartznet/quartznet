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

using Microsoft.EntityFrameworkCore.Migrations;

using Quartz.EntityFrameworkCore;

namespace Quartz;

/// <summary>
/// The parts of the job store's schema an Entity Framework Core model cannot say, for a migration to add.
/// </summary>
public static class QuartzMigrationBuilderExtensions
{
    /// <summary>
    /// Adds what the model of <c>AddQuartzTables</c> cannot say. Call it at the end of <c>Up</c> in the
    /// migration that creates Quartz's tables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On SQLite it creates the four delete triggers that clear a trigger's detail rows on a connection that
    /// does not enforce foreign keys. On MySQL it creates the acquisition index again with its descending
    /// <c>PRIORITY</c> column, which Oracle's provider leaves out when it creates the index. On PostgreSQL and
    /// SQL Server it adds nothing.
    /// </para>
    /// <para>
    /// It is safe to run again: the triggers are created only where they are missing, and the index is
    /// replaced by the same index. A SQLite migration that rebuilds <c>QRTZ_TRIGGERS</c> drops its triggers,
    /// and ends with this call to put them back.
    /// </para>
    /// </remarks>
    /// <param name="migrationBuilder">The migration being built.</param>
    /// <param name="configure">The table prefix and the schema, as <c>AddQuartzTables</c> was given them.</param>
    /// <returns>The same migration builder, for chaining.</returns>
    /// <exception cref="NotSupportedException">The migration's provider is not one Quartz has a model for.</exception>
    /// <exception cref="ArgumentException">
    /// The prefix carries a schema or quotes, or the schema is set for a database without schemas.
    /// </exception>
    public static MigrationBuilder CompleteQuartzSchema(this MigrationBuilder migrationBuilder, Action<QuartzTablesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        QuartzDialect dialect = QuartzDialect.ForProvider(migrationBuilder.ActiveProvider);
        QuartzSchemaCompletion.Add(migrationBuilder, dialect, QuartzTableNaming.Create(dialect, configure));

        return migrationBuilder;
    }

    /// <summary>
    /// Brings a schema <c>AppAny.Quartz.EntityFrameworkCore.Migrations</c> 0.6.3 created to the one
    /// <c>AddQuartzTables</c> models. It is the body of <c>Up</c> in the one migration that switches packages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It repairs what that schema has wrong: the three execution-history indexes it never created, everywhere;
    /// on SQL Server, the <c>NOT NULL</c> on <c>QRTZ_SIMPROP_TRIGGERS.STR_PROP_1</c> to <c>STR_PROP_3</c>,
    /// which Quartz writes <c>NULL</c> to, and the 150-character <c>QRTZ_FIRED_TRIGGERS.INSTANCE_NAME</c>.
    /// </para>
    /// <para>
    /// It converges what is cheap to change and never narrows a column: on SQL Server and MySQL,
    /// <c>QRTZ_FIRED_TRIGGERS.IS_NONCONCURRENT</c> becomes nullable; on SQL Server,
    /// <c>QRTZ_TRIGGERS.MISFIRE_INSTR</c> becomes <c>int</c>; on PostgreSQL and SQL Server, the foreign keys
    /// take the job store's delete rules. What differs in spelling only, or would narrow a column, is left as it
    /// is. Then it does what <see cref="CompleteQuartzSchema" /> does.
    /// </para>
    /// <para>
    /// Scaffold the migration after replacing the AppAny model with <c>AddQuartzTables</c>, given the prefix and
    /// the schema the AppAny model used, and replace the body EF Core wrote with this call. The snapshot then
    /// holds this package's model, and every later migration only adds.
    /// </para>
    /// </remarks>
    /// <param name="migrationBuilder">The migration being built.</param>
    /// <param name="configure">The table prefix and the schema, as <c>AddQuartzTables</c> is given them.</param>
    /// <returns>The same migration builder, for chaining.</returns>
    /// <exception cref="NotSupportedException">The migration's provider is not one Quartz has a model for.</exception>
    /// <exception cref="ArgumentException">
    /// The prefix carries a schema or quotes, or the schema is set for a database without schemas.
    /// </exception>
    public static MigrationBuilder MoveQuartzTablesFromAppAny(this MigrationBuilder migrationBuilder, Action<QuartzTablesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        QuartzDialect dialect = QuartzDialect.ForProvider(migrationBuilder.ActiveProvider);
        QuartzTableNaming naming = QuartzTableNaming.Create(dialect, configure);

        AppAnyMove.Add(migrationBuilder, dialect, naming);
        QuartzSchemaCompletion.Add(migrationBuilder, dialect, naming);

        return migrationBuilder;
    }
}
