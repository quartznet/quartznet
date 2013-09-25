#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

using Oracle.ManagedDataAccess.Client;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore.Common
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    [Explicit("Run these by hand, need to have correct drivers installed")]
    public class DbMetadataTest
    {
        [Test]
        public void TestDbMetadataSqlServer20()
        {
            TestDbMetadata("SqlServer-20");
        }

        [Test]
        public void TestDbMetadataOracleClient20()
        {
            TestDbMetadata("OracleClient-20");
        }

        [Test]
        public void TestDbMetadataOracleODP20()
        {
            TestDbMetadata("OracleODP-20");
        }

        [Test]
        public void TestDbMetadataOracleODP1120()
        {
            TestDbMetadata("OracleODP-1123-20");
        }

        [Test]
        public void TestDbMetadataOracleODP1140()
        {
            TestDbMetadata("OracleODP-1123-40");
        }

        [Test]
        public void TestDbMetadataMySql50()
        {
            TestDbMetadata("MySql-50");
        }

        [Test]
        public void TestDbMetadataMySql51()
        {
            TestDbMetadata("MySql-51");
        }

        [Test]
        public void TestDbMetadataMySql10()
        {
            TestDbMetadata("MySql-10");
        }

        [Test]
        public void TestDbMetadataMySql109()
        {
            TestDbMetadata("MySql-109");
        }

        [Test]
        public void TestDbMetadataOracleODPManaged4012()
        {
            var provider = TestDbMetadata("OracleODPManaged-1211-40");
            var command = (OracleCommand) provider.CreateCommand();
            Assert.That(command.BindByName, Is.True, "bind by name should default to true");
        }

        private static DbProvider TestDbMetadata(string dbname)
        {
            DbProvider dbp = new DbProvider(dbname, "foo");
            DbMetadata md = dbp.Metadata;
            Assert.IsNotNull(md.AssemblyName);
            Assert.IsNotNull(md.BindByName);
            Assert.IsNotNull(md.CommandType);
            Assert.IsNotNull(md.ConnectionType);
            Assert.IsNotNull(md.ParameterType);
            Assert.IsNotNull(md.DbBinaryType);
            Assert.IsNotNull(md.ParameterDbTypeProperty);
            return dbp;
        }
    }
}
