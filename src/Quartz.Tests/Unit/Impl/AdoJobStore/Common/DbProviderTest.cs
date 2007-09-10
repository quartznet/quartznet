
using System;

using MbUnit.Framework;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore.Common
{
    [TestFixture]
    public class DbProviderTest
    {
        [Test]
        public void TestInvalidProviderName()
        {
            try
            {
                DbProvider provider = new DbProvider("FooBar", "");
                Assert.Fail();
            }
            catch (Exception ex)
            {
                Assert.IsTrue(ex.Message.IndexOf("Invalid DB provider name") > -1);
            }
        }

        [Test]
        public void TestValidProviderSqlServer11()
        {
            DbProvider provider = new DbProvider("SqlServer-11", "foo");
            Assert.IsNotNull(provider.ConnectionString);
            Assert.IsNotNull(provider.Metadata);
        }
    }
}
