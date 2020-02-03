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

using NUnit.Framework;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore.Common
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class DbMetadataTest
    {
        [Test]
        public void TestDbMetadataSqlServer20()
        {
            TestDbMetadata(TestConstants.DefaultSqlServerProvider);
        }

        [Test]
        public void TestDbMetadataFirebird()
        {
            TestDbMetadata("Firebird", hashCustomBinaryType: false);
        }

        [Test]
        public void TestDbMetadataMySql()
        {
            TestDbMetadata("MySqlConnector");
        }

#if !NETCORE
        
        [Test]
        public void TestDbMetadataOracleODP()
        {
            TestDbMetadata("OracleODP");
        }

        [Test]
        public void TestDbMetadataOracleODPManaged()
        {
            var provider = TestDbMetadata("OracleODPManaged");
            var command = (Oracle.ManagedDataAccess.Client.OracleCommand) provider.CreateCommand();
            Assert.That(command.BindByName, Is.True, "bind by name should default to true");
        }
#endif

        private static DbProvider TestDbMetadata(string dbname, bool hashCustomBinaryType = true)
        {
            DbProvider dbp = new DbProvider(dbname, "foo");
            DbMetadata md = dbp.Metadata;
            Assert.IsNotNull(md.AssemblyName);
            Assert.IsNotNull(md.BindByName);
            Assert.IsNotNull(md.CommandType);
            Assert.IsNotNull(md.ConnectionType);
            Assert.IsNotNull(md.ParameterType);
            if (hashCustomBinaryType)
            {
                Assert.IsNotNull(md.DbBinaryType);
                Assert.IsNotNull(md.ParameterDbTypeProperty);
            }
            return dbp;
        }
    }
}