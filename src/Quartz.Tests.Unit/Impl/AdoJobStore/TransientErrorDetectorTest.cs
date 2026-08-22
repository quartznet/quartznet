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
/// the detector reads of them is a property name and a number either way.
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
