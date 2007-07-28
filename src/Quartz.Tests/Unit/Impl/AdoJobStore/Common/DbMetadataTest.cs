using NUnit.Framework;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore.Common
{
    [TestFixture]
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
        }
    }
}
