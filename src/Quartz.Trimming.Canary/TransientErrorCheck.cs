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

using Microsoft.Data.Sqlite;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Trimming.Canary;

/// <summary>
/// The store's verdict on a SQLite error worth retrying, asked out of a trimmed or natively compiled
/// publish.
/// </summary>
/// <remarks>
/// <para>
/// Whether the store retries is <c>TransientErrorDetector</c>'s call, and for SQLite it reads a property
/// by name: <see cref="SqliteException" /> reports busy and locked only through <c>SqliteErrorCode</c>,
/// and leaves <see cref="System.Data.Common.DbException.IsTransient" /> false. A publish that trims the
/// property makes every busy database a failure the store gives up on, and says nothing. Quartz names the
/// property with <c>[DynamicDependency]</c> so that it survives; this check is what shows it does.
/// </para>
/// <para>
/// The detector is internal and is asked directly, through the canary's <c>InternalsVisibleTo</c>: the
/// store reaches it only on a database too busy to answer, which a canary cannot arrange without racing
/// a lock. Nothing here reads <c>SqliteErrorCode</c> itself, which would keep the property for the wrong
/// reason.
/// </para>
/// </remarks>
internal static class TransientErrorCheck
{
    // SQLite's own result codes, which Microsoft.Data.Sqlite reports unchanged.
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteConstraint = 19;

    /// <summary>
    /// Runs the check, returning <see langword="null" /> when it passed and a message when it did not.
    /// </summary>
    public static string? Run()
    {
        try
        {
            List<string> failures = [];

            foreach ((string condition, int code) in new[] { ("busy", SqliteBusy), ("locked", SqliteLocked) })
            {
                // Wrapped the way the store wraps what it catches, since the wrapper is what it asks about.
                JobPersistenceException wrapped = new("couldn't acquire next trigger", new SqliteException($"database is {condition}", code));
                if (!TransientErrorDetector.IsTransient(wrapped))
                {
                    failures.Add($"a SqliteException with SqliteErrorCode {code} ({condition}) is not transient, so the store would give up on a database that is only {condition}");
                }
            }

            if (TransientErrorDetector.IsTransient(new SqliteException("constraint failed", SqliteConstraint)))
            {
                failures.Add($"a SqliteException with SqliteErrorCode {SqliteConstraint} (constraint) is transient, so the store would retry an error that fails the same way every time");
            }

            if (failures.Count > 0)
            {
                return "FAIL transient-errors: " + string.Join("; ", failures) + ".";
            }

            Console.WriteLine("PASS transient-errors");
            return null;
        }
        catch (Exception e)
        {
            return $"FAIL transient-errors: {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e}";
        }
    }
}
