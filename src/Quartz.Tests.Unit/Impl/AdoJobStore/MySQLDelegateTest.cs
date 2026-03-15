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

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Unit tests for MySQLDelegate to verify query optimizations.
/// </summary>
public class MySQLDelegateTest
{
    [Test]
    public void GetSelectNextTriggerToAcquireSql_ShouldContainForceIndexHint()
    {
        // Arrange
        TestMySQLDelegate mySqlDelegate = new TestMySQLDelegate();
        int maxCount = 10;

        // Act
        string sql = mySqlDelegate.GetSelectNextTriggerToAcquireSqlPublic(maxCount);

        // Assert
        Assert.That(sql, Does.Contain("FORCE INDEX (IDX_QRTZ_T_NFT_ST)"), 
            "Query should use FORCE INDEX hint for performance optimization");
        Assert.That(sql, Does.Contain($"LIMIT {maxCount}"), 
            "Query should include LIMIT clause");
        Assert.That(sql, Does.Contain("ORDER BY"), 
            "Query should include ORDER BY clause");
    }

    [Test]
    public void GetSelectNextMisfiredTriggersInStateToAcquireSql_ShouldContainForceIndexHint()
    {
        // Arrange
        TestMySQLDelegate mySqlDelegate = new TestMySQLDelegate();
        int count = 20;

        // Act
        string sql = mySqlDelegate.GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(count);

        // Assert
        Assert.That(sql, Does.Contain("FORCE INDEX (IDX_QRTZ_T_NFT_ST_MISFIRE)"), 
            "Query should use FORCE INDEX hint for performance optimization");
        Assert.That(sql, Does.Contain($"LIMIT {count}"), 
            "Query should include LIMIT clause");
        Assert.That(sql, Does.Contain("ORDER BY"), 
            "Query should include ORDER BY clause");
    }

    [Test]
    public void GetSelectNextMisfiredTriggersInStateToAcquireSql_WithMinusOne_ShouldUseFallbackQuery()
    {
        // Arrange
        TestMySQLDelegate mySqlDelegate = new TestMySQLDelegate();

        // Act
        string sql = mySqlDelegate.GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(-1);

        // Assert
        Assert.That(sql, Does.Not.Contain("LIMIT"), 
            "Query should not include LIMIT clause when count is -1");
    }

    [Test]
    public void GetSelectNextTriggerToAcquireSql_ShouldIncludeAllRequiredColumns()
    {
        // Arrange
        TestMySQLDelegate mySqlDelegate = new TestMySQLDelegate();

        // Act
        string sql = mySqlDelegate.GetSelectNextTriggerToAcquireSqlPublic(10);

        // Assert
        Assert.That(sql, Does.Contain("TRIGGER_NAME"), 
            "Query should select TRIGGER_NAME");
        Assert.That(sql, Does.Contain("TRIGGER_GROUP"), 
            "Query should select TRIGGER_GROUP");
        Assert.That(sql, Does.Contain("JOB_CLASS_NAME"), 
            "Query should select JOB_CLASS_NAME");
    }

    [Test]
    public void GetSelectNextMisfiredTriggersInStateToAcquireSql_ShouldIncludeRequiredColumns()
    {
        // Arrange
        TestMySQLDelegate mySqlDelegate = new TestMySQLDelegate();

        // Act
        string sql = mySqlDelegate.GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(10);

        // Assert
        Assert.That(sql, Does.Contain("TRIGGER_NAME"), 
            "Query should select TRIGGER_NAME");
        Assert.That(sql, Does.Contain("TRIGGER_GROUP"), 
            "Query should select TRIGGER_GROUP");
    }

    /// <summary>
    /// Test subclass to expose protected methods for testing.
    /// </summary>
    private class TestMySQLDelegate : MySQLDelegate
    {
        public string GetSelectNextTriggerToAcquireSqlPublic(int maxCount)
        {
            return GetSelectNextTriggerToAcquireSql(maxCount);
        }

        public string GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(int count)
        {
            return GetSelectNextMisfiredTriggersInStateToAcquireSql(count);
        }
    }
}
