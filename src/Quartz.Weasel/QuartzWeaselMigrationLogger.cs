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

using Microsoft.Extensions.Logging;

using Weasel.Core.Migrations;

namespace Quartz.Weasel;

/// <summary>
/// Routes the DDL Weasel runs for one scheduler's schema into the application's log, and fails the
/// migration on the first statement that fails.
/// </summary>
/// <remarks>
/// Weasel hands a failed statement to <see cref="OnFailure" /> instead of throwing whenever the logger is
/// not its own console logger, taking that to mean the caller wants to decide. The decision here is to
/// stop: a migration that carries on past a failed <c>CREATE</c> leaves a schema the store's validation
/// then refuses with a message that names nothing the application wrote, which is what Wolverine's
/// logger learned the same way (JasperFx/wolverine GH-3997).
/// </remarks>
internal sealed class QuartzWeaselMigrationLogger : IMigrationLogger
{
    private readonly ILogger logger;
    private readonly string schedulerName;

    public QuartzWeaselMigrationLogger(ILogger logger, string schedulerName)
    {
        this.logger = logger;
        this.schedulerName = schedulerName;
    }

    public void SchemaChange(string sql) => logger.SchemaChange(schedulerName, sql.Trim());

    public void OnFailure(DbCommand command, Exception ex)
    {
        logger.SchemaChangeFailed(schedulerName, command.CommandText, ex);

        throw new SchedulerException(
            $"Weasel could not apply a change to the schema of scheduler '{schedulerName}'. The statement that failed"
            + $" was: {command.CommandText}",
            ex);
    }

    public void DestructiveChange(string description) => logger.DestructiveChange(schedulerName, description);

    public void WithheldDrop(string description) => logger.WithheldDrop(schedulerName, description);
}
