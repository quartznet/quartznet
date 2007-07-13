using System.Collections;

using NUnit.Framework;

using Quartz.Collection;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class CollectionUtilTest
    {

        public void TestRemoveNonExistingElement()
        {
            ArrayList items = new ArrayList();
            items.Add("bar");
            bool removed = CollectionUtil.Remove(items, "foo");

            Assert.IsFalse(removed, "Collection did loose element when it shouldn't have");
        }

        public void TestRemoveExistingElement()
        {
            ArrayList items = new ArrayList();
            items.Add("foo");
            bool removed = CollectionUtil.Remove(items, "foo");

            Assert.IsTrue(removed, "Collection did'nt loose element when it should have");
        }


        public void TestRemoveFromNullCollection()
        {
            try
            {
                CollectionUtil.Remove(null, "foo");
                Assert.Fail("Removing from null collection succeeded");
            }
            catch
            {
                // ok
            }
        }

    }

}
