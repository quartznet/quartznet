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
using System.Collections;
using System.Data.Common;

using Microsoft.Data.Sqlite;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Which failures say that a table does not exist: each dialect's own signal, and nothing else a statement can fail
/// with, so a store never takes a lost connection for a database nobody migrated.
/// </summary>
public class UndefinedTableDetectorTest
{
    [TestCase("42P01", TestName = "PostgresUndefinedTableIsMissing")]
    [TestCase("42S02", TestName = "MySqlNoSuchTableIsMissing")]
    public void ASqlStateForAMissingTableIsMissing(string sqlState)
    {
        UndefinedTableDetector.IsUndefinedTable(new SqlStateException(sqlState)).Should().BeTrue();
    }

    [TestCase("08006", TestName = "AConnectionFailureIsNotAMissingTable")]
    [TestCase("42501", TestName = "ARefusedPermissionIsNotAMissingTable")]
    [TestCase("40001", TestName = "ASerializationFailureIsNotAMissingTable")]
    [TestCase("57P01", TestName = "AnAdministratorShutdownIsNotAMissingTable")]
    public void AnyOtherSqlStateIsNot(string sqlState)
    {
        UndefinedTableDetector.IsUndefinedTable(new SqlStateException(sqlState)).Should().BeFalse(
            "only the dialect's own words for a missing table may hide a column and name a script");
    }

    /// <summary>
    /// Firebird's "Table unknown" is a code among the several its exception carries, under the generic Dynamic SQL
    /// Error whose state is the one it reports.
    /// </summary>
    [Test]
    public void FirebirdsTableUnknownIsMissing()
    {
        UndefinedTableDetector.IsUndefinedTable(new FbException("42000", 335544569, 335544436, 335544580, 335544634)).Should().BeTrue(
            "335544580 is isc_dsql_relation_err, Table unknown");
        UndefinedTableDetector.IsUndefinedTable(new FbException("42000", 335544569, 335544436, 335544578)).Should().BeFalse(
            "335544578 is a column that does not exist, not a table");
        UndefinedTableDetector.IsUndefinedTable(new FbException("08006", 335544721)).Should().BeFalse("335544721 is a network error");
    }

    [Test]
    public void SqlServersInvalidObjectNameIsMissing()
    {
        UndefinedTableDetector.IsUndefinedTable(new SqlServerException(208)).Should().BeTrue("208 is Invalid object name");
        UndefinedTableDetector.IsUndefinedTable(new SqlServerException(4060)).Should().BeFalse("4060 is a database the login cannot open");
        UndefinedTableDetector.IsUndefinedTable(new SqlServerException(1205)).Should().BeFalse("1205 is a deadlock victim");
    }

    [Test]
    public void OraclesTableOrViewDoesNotExistIsMissing()
    {
        UndefinedTableDetector.IsUndefinedTable(new OracleException("ORA-00942: table or view does not exist")).Should().BeTrue();
        UndefinedTableDetector.IsUndefinedTable(new OracleException("ORA-03113: end-of-file on communication channel")).Should().BeFalse(
            "a connection the server dropped is a failed read");
    }

    [Test]
    public void SqlitesNoSuchTableIsMissing()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM QRTZ_TRIGGER_STATUS WHERE 1 = 0";

        SqliteException missing = FluentActions.Invoking(() => command.ExecuteReader()).Should().Throw<SqliteException>().Which;

        UndefinedTableDetector.IsUndefinedTable(missing).Should().BeTrue("the real driver's words for a missing table");
        UndefinedTableDetector.IsUndefinedTable(new SqliteException("database is locked", 5)).Should().BeFalse();
    }

    [Test]
    public void AMissingTableIsFoundUnderTheStoresWrapping()
    {
        UndefinedTableDetector.IsUndefinedTable(new JobPersistenceException("couldn't read", new SqlStateException("42P01"))).Should().BeTrue();
        UndefinedTableDetector.IsUndefinedTable(new JobPersistenceException("couldn't read", new SqlStateException("08006"))).Should().BeFalse();
    }

    [Test]
    public void AFailureThatIsNoDatabasesIsNot()
    {
        UndefinedTableDetector.IsUndefinedTable(new TimeoutException()).Should().BeFalse();
        UndefinedTableDetector.IsUndefinedTable(new InvalidOperationException("no such table, in an application's own words")).Should().BeFalse(
            "SQLite's words count only on a database exception");
    }

    /// <summary>Stands in for a driver that reports its SQLSTATE where <see cref="DbException" /> says to.</summary>
    private sealed class SqlStateException : DbException
    {
        public SqlStateException(string sqlState) => SqlState = sqlState;

        public override string SqlState { get; }
    }

    /// <summary>
    /// Stands in for <c>FbException</c>, which declares a <c>SQLSTATE</c> of its own and its codes on an <c>Errors</c>
    /// collection whose items carry a <c>Number</c>.
    /// </summary>
    private sealed class FbException : DbException
    {
        public FbException(string sqlState, params int[] numbers)
        {
            SQLSTATE = sqlState;
            Errors = new SqlServerErrorCollection(numbers);
        }

        public string SQLSTATE { get; }

        public SqlServerErrorCollection Errors { get; }
    }

    /// <summary>Stands in for <c>OracleException</c>, whose message starts with the error's code.</summary>
    private sealed class OracleException : DbException
    {
        public OracleException(string message) : base(message)
        {
        }
    }

    /// <summary>Stands in for <c>SqlException</c>: the numbers are on an <c>Errors</c> collection.</summary>
    private sealed class SqlServerException : DbException
    {
        public SqlServerException(params int[] numbers) => Errors = new SqlServerErrorCollection(numbers);

        public SqlServerErrorCollection Errors { get; }
    }

    private sealed class SqlServerErrorCollection : IEnumerable
    {
        private readonly int[] numbers;

        public SqlServerErrorCollection(int[] numbers) => this.numbers = numbers;

        public IEnumerator GetEnumerator()
        {
            foreach (int number in numbers)
            {
                yield return new SqlServerError(number);
            }
        }
    }

    private sealed class SqlServerError
    {
        public SqlServerError(int number) => Number = number;

        public int Number { get; }
    }
}
