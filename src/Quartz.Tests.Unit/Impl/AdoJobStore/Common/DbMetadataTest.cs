using NUnit.Framework;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore.Common
{
    [TestFixture]
    [Ignore("Run these by hand, need to have correct drivers installed")]
    public class DbMetadataTest
    {
        [Test]
        public void TestDbMetadataSqlServer11()
        {
            TestDbMetadata("SqlServer-11");
        }

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

        private static void TestDbMetadata(string dbname)
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
        }
    }
}
