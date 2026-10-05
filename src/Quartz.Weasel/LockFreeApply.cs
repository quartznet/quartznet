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

using Weasel.Core;

namespace Quartz.Weasel;

/// <summary>
/// The apply of a dialect that takes no global lock, which answers a lost race the way
/// <c>ProvisionSchema()</c> answers one: by reading the schema again.
/// </summary>
/// <remarks>
/// <para>
/// Whichever process loses a statement to another fails it — an <c>ALTER TABLE … ADD COLUMN</c> on SQLite,
/// any unguarded DDL on Oracle — while the other has made, or is making, that very change. The dialect tells
/// such a failure apart by the provider's error, and it is then held up against the schema: nothing left to
/// do means another process finished it (event 10009); anything left is applied again after a pause
/// (event 10008), until the attempts run out.
/// </para>
/// <para>
/// Any other failure — a refused privilege, a rebuild Weasel rejects, a server out of reach — is not retried.
/// Whichever way the apply fails, it fails with a <see cref="SchedulerException" /> that names the scheduler
/// and the database, and carries the failure that ended it as the inner exception. A cancelled apply is
/// neither retried nor wrapped.
/// </para>
/// </remarks>
internal static class LockFreeApply
{
    /// <param name="database">The database applied to, which also reads the schema again.</param>
    /// <param name="apply">One apply, without a lock: Weasel's own.</param>
    /// <param name="isLostRace">
    /// Whether the provider's error is one a statement raises when another process made the same change
    /// first: the dialect's own classifier.
    /// </param>
    /// <param name="attempts">How many applies are made before a lost race stands.</param>
    /// <param name="retryDelay">The pause between reading the schema again and the next apply.</param>
    /// <param name="timeProvider">The clock the pause is measured on.</param>
    /// <param name="cancellationToken">Stops the apply; a cancelled apply is never retried.</param>
    public static async Task<SchemaPatchDifference> ApplyAsync(
        IQuartzWeaselDatabase database,
        Func<CancellationToken, Task<SchemaPatchDifference>> apply,
        Func<DbException, bool> isLostRace,
        int attempts,
        TimeSpan retryDelay,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        QuartzWeaselDatabaseContext context = database.Context;

        for (int attempt = 1; ; attempt++)
        {
            Exception failure;
            try
            {
                return await apply(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                failure = e;
            }

            if (ProviderError(failure) is not { } error || !isLostRace(error))
            {
                throw Failed(database, failure, "the failure is not one another process applying the same schema causes, so it was not retried");
            }

            Uri uri = database.Describe().DatabaseUri();
            context.Logger.SchemaApplyRetrying(context.SchedulerName, uri, attempt, attempts, failure);

            SchemaMigration after;
            try
            {
                after = await database.CreateMigrationAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                throw Failed(database, e, "reading the schema again, after an apply that lost a race, failed");
            }

            if (after.Difference == SchemaPatchDifference.None)
            {
                context.Logger.SchemaAppliedByAnotherProcess(context.SchedulerName, uri);
                return SchemaPatchDifference.None;
            }

            if (attempt >= attempts)
            {
                throw Failed(database, failure, $"it lost a race on each of {attempts} attempts, and the schema still differs from the model");
            }

            await Task.Delay(retryDelay, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The provider's own error behind a failure: the exception itself, or the first one it wraps — Weasel
    /// and <see cref="QuartzWeaselMigrationLogger" /> each wrap a failed statement's.
    /// </summary>
    internal static DbException? ProviderError(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException error)
            {
                return error;
            }
        }

        return null;
    }

    private static SchedulerException Failed(IQuartzWeaselDatabase database, Exception failure, string reason) =>
        new($"Weasel could not apply the schema of scheduler '{database.Context.SchedulerName}' to {database.Describe().DatabaseUri()}: {reason}.", failure);
}
