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

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Which failures the ADO job store retries, asserted against stand-ins for the exception shapes the
/// shipped drivers throw. The real ones cannot be constructed from outside their assemblies, and what
/// the detector reads of them is a property name and a number or a string either way.
/// </summary>
public class TransientErrorDetectorTest
{
    [Test]
    public void DriverSayingTransientIsBelieved()
    {
        TransientErrorDetector.IsTransient(new ProviderException { Transient = true }).Should().BeTrue();
    }

    [Test]
    public void DriverSayingNotTransientIsNotTheEndOfTheEnquiry()
    {
        // The old implementation returned the driver's verdict and stopped there, which meant every
        // check below it was dead for any exception deriving from DbException - all of them.
        var busy = new SqliteException { SqliteErrorCode = 5 };
        busy.IsTransient.Should().BeFalse("the stand-in reports what Microsoft.Data.Sqlite reports");

        TransientErrorDetector.IsTransient(busy).Should().BeTrue();
    }

    [Test]
    public void PermanentDriverFailureIsNotTransient()
    {
        TransientErrorDetector.IsTransient(new ProviderException()).Should().BeFalse();
    }

    [Test]
    public void NonDatabaseFailureIsNotTransient()
    {
        TransientErrorDetector.IsTransient(new InvalidOperationException("no")).Should().BeFalse();
    }

    [Test]
    public void TimeoutIsTransient()
    {
        TransientErrorDetector.IsTransient(new TimeoutException()).Should().BeTrue();
    }

    [TestCase(1205, TestName = "SqlServerDeadlockIsTransient")]
    [TestCase(40613, TestName = "SqlServerDatabaseUnavailableIsTransient")]
    [TestCase(49920, TestName = "SqlServerTooBusyIsTransient")]
    public void SqlServerErrorNumberIsTransient(int number)
    {
        TransientErrorDetector.IsTransient(new SqlServerException(number)).Should().BeTrue();
    }

    [Test]
    public void SqlServerConstraintViolationIsNotTransient()
    {
        // 2627 is a primary key violation: it will fail again on every retry.
        TransientErrorDetector.IsTransient(new SqlServerException(2627)).Should().BeFalse();
    }

    [Test]
    public void SqlServerErrorNumberIsFoundBesideOthers()
    {
        TransientErrorDetector.IsTransient(new SqlServerException(2627, 1205)).Should().BeTrue();
    }

    /// <summary>
    /// The error-number check recognises a shape, not a base class. A driver or a wrapper that reports
    /// SQL Server errors this way without deriving from <see cref="DbException" /> is recognised too,
    /// which is what the integration suite's simulator relies on.
    /// </summary>
    [Test]
    public void SqlServerErrorNumberIsFoundOnAnyExceptionThatCarriesIt()
    {
        TransientErrorDetector.IsTransient(new LooksLikeSqlServerException()).Should().BeTrue();
    }

    [TestCase(5, TestName = "SqliteBusyIsTransient")]
    [TestCase(6, TestName = "SqliteLockedIsTransient")]
    public void SqliteErrorCodeIsTransient(int code)
    {
        TransientErrorDetector.IsTransient(new SqliteException { SqliteErrorCode = code }).Should().BeTrue();
    }

    [Test]
    public void SqliteConstraintFailureIsNotTransient()
    {
        TransientErrorDetector.IsTransient(new SqliteException { SqliteErrorCode = 19 }).Should().BeFalse();
    }

    [TestCase(SQLiteResultCode.Busy, TestName = "LegacySqliteBusyIsTransient")]
    [TestCase(SQLiteResultCode.Locked, TestName = "LegacySqliteLockedIsTransient")]
    public void LegacySqliteResultCodeIsTransient(SQLiteResultCode code)
    {
        TransientErrorDetector.IsTransient(new SQLiteException { ResultCode = code }).Should().BeTrue();
    }

    [Test]
    public void LegacySqliteConstraintFailureIsNotTransient()
    {
        TransientErrorDetector.IsTransient(new SQLiteException { ResultCode = SQLiteResultCode.Constraint }).Should().BeFalse();
    }

    /// <summary>
    /// SQLSTATE class 40 is the standard's own "transaction rollback": the database abandoned the
    /// transaction for a reason the statements in it did not cause, and the prescribed answer is to run
    /// it again. It is provider-neutral, which is why it is read at all.
    /// </summary>
    [TestCase("40000", TestName = "RollbackWithNoSubclassIsTransient")]
    [TestCase("40001", TestName = "SerializationFailureIsTransient")]
    [TestCase("40003", TestName = "StatementCompletionUnknownIsTransient")]
    [TestCase("40P01", TestName = "PostgresDeadlockDetectedIsTransient")]
    public void TransactionRollbackSqlStateIsTransient(string sqlState)
    {
        TransientErrorDetector.IsTransient(new SqlStateException(sqlState)).Should().BeTrue(
            "SQLSTATE {0} is in class 40, transaction rollback, which is the standard saying to run the transaction again",
            sqlState);
    }

    [Test]
    public void DeferredConstraintViolationIsNotTransient()
    {
        TransientErrorDetector.IsTransient(new SqlStateException("40002")).Should().BeFalse(
            "40002 is the one member of class 40 that is a real error - an integrity constraint the commit found broken, which every retry will find broken too");
    }

    [Test]
    public void UniqueViolationIsNotTransient()
    {
        TransientErrorDetector.IsTransient(new SqlStateException("23505")).Should().BeFalse(
            "class 23 is an integrity-constraint violation, and only class 40 says anything about retrying");
    }

    [Test]
    public void DriverReportingNoSqlStateIsNotTransient()
    {
        TransientErrorDetector.IsTransient(new SqlStateException(null)).Should().BeFalse(
            "both SqlClients and every SQLite driver leave the state null, and they have to fall through to the checks written for them rather than being caught here");
    }

    /// <summary>
    /// Firebird is the reason this check exists, and it is also the driver that puts the state
    /// somewhere <see cref="DbException" /> does not look.
    /// </summary>
    [Test]
    public void FirebirdWriteConflictIsTransient()
    {
        var writeConflict = new FbException("40001");
        writeConflict.SqlState.Should().BeNull(
            "FbException declares a SQLSTATE of its own and never overrides the inherited SqlState, so the base class's null is what a caller reading it sees");
        writeConflict.IsTransient.Should().BeFalse(
            "FbException does not override IsTransient either, which is why the driver's own verdict was not enough");

        TransientErrorDetector.IsTransient(writeConflict).Should().BeTrue(
            "a write conflict between two Firebird transactions is a serialization failure, the textbook case for retrying");
    }

    [Test]
    public void FirebirdConstraintViolationIsNotTransient()
    {
        TransientErrorDetector.IsTransient(new FbException("23000")).Should().BeFalse(
            "reading Firebird's own spelling of the state must not turn every Firebird failure into a retry");
    }

    /// <summary>
    /// Firebird nests the <c>IscException</c> that carries the same property, and it derives from
    /// <see cref="Exception" /> rather than <see cref="DbException" />. The state is matched on shape,
    /// the way the SQL Server error numbers are, so it is found at either level.
    /// </summary>
    [Test]
    public void SqlStateIsFoundOnAnyExceptionThatCarriesIt()
    {
        TransientErrorDetector.IsTransient(new IscException("40001")).Should().BeTrue(
            "the property is what is recognised, not the base class");
    }

    [Test]
    public void TransactionRollbackIsFoundThroughAWrapper()
    {
        var wrapped = new JobPersistenceException("couldn't acquire next trigger", new SqlStateException("40001"));

        TransientErrorDetector.IsTransient(wrapped).Should().BeTrue(
            "the store wraps what it catches and the retry decision is taken on the wrapper, so a serialization failure has to be visible through it");
    }

    [Test]
    public void DeferredConstraintViolationStaysPermanentThroughAWrapper()
    {
        var wrapped = new JobPersistenceException("couldn't store trigger", new SqlStateException("40002"));

        TransientErrorDetector.IsTransient(wrapped).Should().BeFalse(
            "walking the chain must not widen class 40 to include the member that is excluded from it");
    }

    [Test]
    public void DriverSayingTransientWinsOverAnExcludedSqlState()
    {
        TransientErrorDetector.IsTransient(new SqlStateException("40002") { Transient = true }).Should().BeTrue(
            "the signals are inclusive - the first one saying transient wins - and a driver that has made up its own mind is believed whatever it reports beside it");
    }

    /// <summary>
    /// The store wraps nearly everything it catches, and the retry decisions are all taken on the
    /// wrapper, so a transient cause has to be visible through it.
    /// </summary>
    [Test]
    public void TransientCauseIsFoundThroughAWrapper()
    {
        var wrapped = new JobPersistenceException("couldn't store trigger", new SqlServerException(1205));

        TransientErrorDetector.IsTransient(wrapped).Should().BeTrue();
    }

    [Test]
    public void TransientCauseIsFoundThroughTwoWrappers()
    {
        var wrapped = new JobPersistenceException(
            "couldn't store trigger",
            new JobPersistenceException("couldn't commit", new ProviderException { Transient = true }));

        TransientErrorDetector.IsTransient(wrapped).Should().BeTrue();
    }

    [Test]
    public void PermanentCauseStaysPermanentThroughAWrapper()
    {
        var wrapped = new JobPersistenceException("couldn't store trigger", new SqlServerException(2627));

        TransientErrorDetector.IsTransient(wrapped).Should().BeFalse();
    }

    /// <summary>A driver exception that reports its own verdict and nothing else.</summary>
    private sealed class ProviderException : DbException
    {
        public bool Transient { get; init; }

        public override bool IsTransient => Transient;
    }

    /// <summary>
    /// Stands in for <c>Microsoft.Data.SqlClient.SqlException</c>: the numbers are on an
    /// <c>Errors</c> collection whose items carry a <c>Number</c>.
    /// </summary>
    private sealed class SqlServerException : DbException
    {
        public SqlServerException(params int[] numbers)
        {
            Errors = new SqlServerErrorCollection(numbers);
        }

        public SqlServerErrorCollection Errors { get; }
    }

    /// <summary>An exception carrying SQL Server errors without deriving from <see cref="DbException" />.</summary>
    private sealed class LooksLikeSqlServerException : Exception
    {
        public SqlServerErrorCollection Errors { get; } = new([49920]);
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

    /// <summary>
    /// Stands in for a driver that reports its SQLSTATE where <see cref="DbException" /> says to.
    /// Npgsql's <c>PostgresException</c>, MySqlConnector's and MySql.Data's <c>MySqlException</c> all
    /// override the property this way.
    /// </summary>
    private sealed class SqlStateException : DbException
    {
        public SqlStateException(string sqlState) => SqlState = sqlState;

        public override string SqlState { get; }

        public bool Transient { get; init; }

        public override bool IsTransient => Transient;
    }

    /// <summary>
    /// Stands in for <c>FirebirdSql.Data.FirebirdClient.FbException</c>, which declares a property
    /// named <c>SQLSTATE</c> and leaves the inherited <see cref="DbException.SqlState" /> alone.
    /// </summary>
    private sealed class FbException : DbException
    {
        public FbException(string sqlState) => SQLSTATE = sqlState;

        public string SQLSTATE { get; }
    }

    /// <summary>
    /// Stands in for <c>FirebirdSql.Data.Common.IscException</c>, the exception Firebird nests inside
    /// an <c>FbException</c> and reads the state off. It is not a <see cref="DbException" />.
    /// </summary>
    private sealed class IscException : Exception
    {
        public IscException(string sqlState) => SQLSTATE = sqlState;

        public string SQLSTATE { get; }
    }

    /// <summary>
    /// Stands in for <c>Microsoft.Data.Sqlite.SqliteException</c>, matched by type name and read
    /// through <c>SqliteErrorCode</c>.
    /// </summary>
    private sealed class SqliteException : DbException
    {
        public int SqliteErrorCode { get; init; }
    }

    /// <summary>
    /// Stands in for <c>System.Data.SQLite.SQLiteException</c>, which spells the same condition as a
    /// <c>ResultCode</c> enum.
    /// </summary>
    private sealed class SQLiteException : DbException
    {
        public SQLiteResultCode ResultCode { get; init; }
    }

    public enum SQLiteResultCode
    {
        Ok = 0,
        Busy = 5,
        Locked = 6,
        Constraint = 19
    }
}
