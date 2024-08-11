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

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore.Common;

/// <author>Marko Lahma (.NET)</author>
[TestFixture]
public class DbMetadataTest
{
    [Test]
    [Category("db-sqlserver")]
    public void TestDbMetadataSqlServer20()
    {
        TestDbMetadata(TestConstants.DefaultSqlServerProvider);
    }

    [Test]
    [Category("db-firebird")]
    public void TestDbMetadataFirebird()
    {
        TestDbMetadata("Firebird", hashCustomBinaryType: false);
    }

    [Test]
    [Category("db-mysql")]
    public void TestDbMetadataMySql()
    {
        TestDbMetadata("MySqlConnector");
    }

    private static DbProvider TestDbMetadata(string dbname, bool hashCustomBinaryType = true)
    {
        DbProvider dbp = new DbProvider(dbname, "foo");
        DbMetadata md = dbp.Metadata;
        Assert.That(md.AssemblyName, Is.Not.Null);
        Assert.That(md.CommandType, Is.Not.Null);
        Assert.That(md.ConnectionType, Is.Not.Null);
        Assert.That(md.ParameterType, Is.Not.Null);
        if (hashCustomBinaryType)
        {
            Assert.That(md.DbBinaryType, Is.Not.Null);
            Assert.That(md.ParameterDbTypeProperty, Is.Not.Null);
        }
        return dbp;
    }
}