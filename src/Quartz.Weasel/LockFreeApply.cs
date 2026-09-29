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

using Weasel.Core;

namespace Quartz.Weasel;

/// <summary>
/// The apply of a dialect that takes no global lock, which answers a failure as a lost race: by reading the
/// schema again, the way <c>ProvisionSchema()</c> answers one.
/// </summary>
/// <remarks>
/// Whichever process loses a statement to another fails it — an <c>ALTER TABLE … ADD COLUMN</c> on SQLite,
/// any unguarded DDL on Oracle — while the other has made, or is making, that very change. So a failure is
/// held up against the schema: nothing left to do means another process finished it (event 10009);
/// anything left is applied again after a pause (event 10008), until the attempts run out and the last
/// failure stands.
/// </remarks>
internal static class LockFreeApply
{
    /// <param name="database">The database applied to, which also reads the schema again.</param>
    /// <param name="apply">One apply, without a lock: Weasel's own.</param>
    /// <param name="attempts">How many applies are made before a failure stands.</param>
    /// <param name="retryDelay">The pause between reading the schema again and the next apply.</param>
    /// <param name="timeProvider">The clock the pause is measured on.</param>
    /// <param name="cancellationToken">Stops the apply; a cancelled apply is never retried.</param>
    public static async Task<SchemaPatchDifference> ApplyAsync(
        IQuartzWeaselDatabase database,
        Func<CancellationToken, Task<SchemaPatchDifference>> apply,
        int attempts,
        TimeSpan retryDelay,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        QuartzWeaselDatabaseContext context = database.Context;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await apply(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (attempt < attempts && !cancellationToken.IsCancellationRequested)
            {
                context.Logger.SchemaApplyRetrying(context.SchedulerName, database.Describe().DatabaseUri(), attempt, attempts, e);
            }

            SchemaMigration after = await database.CreateMigrationAsync(cancellationToken).ConfigureAwait(false);
            if (after.Difference == SchemaPatchDifference.None)
            {
                context.Logger.SchemaAppliedByAnotherProcess(context.SchedulerName, database.Describe().DatabaseUri());
                return SchemaPatchDifference.None;
            }

            await Task.Delay(retryDelay, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
