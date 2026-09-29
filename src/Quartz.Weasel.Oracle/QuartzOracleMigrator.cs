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

using Oracle.ManagedDataAccess.Client;

using Weasel.Oracle;

using DbCommandBuilder = Weasel.Core.DbCommandBuilder;

namespace Quartz.Weasel.Oracle;

/// <summary>
/// Weasel's Oracle migrator, refusing destructive changes, with every introspection command reading a
/// <c>LONG</c> whole.
/// </summary>
/// <remarks>
/// <para>
/// Weasel reads the column behind a descending index key out of <c>ALL_IND_EXPRESSIONS.COLUMN_EXPRESSION</c>,
/// a <c>LONG</c>, and ODP.NET hands a <c>LONG</c> back empty unless the command says how much of it to
/// fetch. Weasel's table query says so on the command it builds, but a comparison splits its queries into
/// one command per statement, and in Weasel 9.36.0 the split commands do not carry the setting. The
/// descending <c>PRIORITY</c> of <c>IDX_QRTZ_T_NFT_ST</c> then reads back as Oracle's hidden
/// <c>SYS_NC…$</c> column, and every apply drops and recreates the index.
/// </para>
/// <para>
/// Reading a table on its own (<c>Table.FetchExistingAsync</c>) is not affected, which is why the index
/// reads back correctly there.
/// </para>
/// </remarks>
internal sealed class QuartzOracleMigrator : OracleMigrator
{
    public QuartzOracleMigrator()
    {
        RefuseDestructiveChanges = true;
    }

    public override DbCommandBuilder CreateCommandBuilder(DbConnection conn) => new LongFetchingCommandBuilder();

    /// <summary>Weasel's splitting builder, with <c>InitialLONGFetchSize</c> set on each command it splits off.</summary>
    internal sealed class LongFetchingCommandBuilder : OracleDbCommandBuilder
    {
        public override IReadOnlyList<DbCommand> CompileCommands()
        {
            IReadOnlyList<DbCommand> commands = base.CompileCommands();

            foreach (OracleCommand command in commands.OfType<OracleCommand>())
            {
                // -1 is "all of it".
                command.InitialLONGFetchSize = -1;
            }

            return commands;
        }
    }
}
