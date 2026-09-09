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

namespace Quartz;

/// <summary>
/// A SQLite file database a fixture owns outright: a path of its own under the temporary directory, a
/// connection string that does not pool, and the file deleted again when the fixture is done with it.
/// </summary>
/// <remarks>
/// <para>
/// Pooling being off is the whole of why this type exists. Every fixture that owned a SQLite file used
/// to end its teardown with <c>SqliteConnection.ClearAllPools</c>, and that call is process-global:
/// <c>Quartz.Tests.Unit</c> declares <c>[assembly: Parallelizable(ParallelScope.Fixtures)]</c>, so one
/// fixture's teardown ran it while other fixtures were in the middle of their tests. Microsoft.Data.Sqlite
/// hands out the underlying handle from its pool rather than a copy, so clearing the pool disposes a
/// connection somebody else is still using — <c>ObjectDisposedException: SQLitePCL.sqlite3</c> raised
/// from inside a setup that was doing nothing wrong, seen twice during the 4.1 campaign and never twice
/// in a row (#3755).
/// </para>
/// <para>
/// An unpooled connection closes its handle when it is disposed, so there is no pool to clear and the
/// file is deletable as soon as the last connection has gone. <c>SqlitePoolClearingTest</c> is what
/// keeps the global call from coming back.
/// </para>
/// <para>
/// Compiled into <c>Quartz.Tests.AspNetCore</c> and <c>Quartz.Tests.Integration</c> as well, because
/// the fixtures that needed this are spread across all three projects and one of them running the
/// global call would be enough.
/// </para>
/// </remarks>
internal sealed class SqliteTestDatabase : IDisposable
{
    /// <summary>
    /// How many times the file is asked to go away before the fixture gives up on it.
    /// </summary>
    /// <remarks>
    /// On Windows a handle closed a microsecond ago can still fail the delete, so a single attempt
    /// leaves stray files behind. Twenty of them at fifty milliseconds is a second of patience, which
    /// is far more than the wait has ever needed.
    /// </remarks>
    private const int DeleteAttempts = 20;

    private static readonly TimeSpan deleteRetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Names a database no other fixture and no other run can be holding.
    /// </summary>
    /// <param name="name">
    /// What the fixture is about, so that a file left behind by a killed run says where it came from.
    /// </param>
    public SqliteTestDatabase(string name)
    {
        DatabaseFile = Path.Combine(Path.GetTempPath(), $"quartz-{name}-{Guid.NewGuid():N}.db");
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseFile,
            Pooling = false,
        }.ToString();
    }

    /// <summary>
    /// Where the database is. SQLite creates it on the first connection, so nothing has to exist yet.
    /// </summary>
    public string DatabaseFile { get; }

    /// <summary>
    /// What to hand <c>UseSqlite</c>, a <c>DbProvider</c>, or a connection opened by the test itself.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Deletes the file, giving the last connection a moment to let go of it.
    /// </summary>
    /// <remarks>
    /// A file that will not go away is scratch space in the temporary directory rather than a failure,
    /// so this gives up quietly rather than failing a test that has already said what it had to say.
    /// </remarks>
    public void Dispose()
    {
        for (int attempt = 0; attempt < DeleteAttempts && File.Exists(DatabaseFile); attempt++)
        {
            try
            {
                File.Delete(DatabaseFile);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(deleteRetryDelay);
            }
        }
    }
}
