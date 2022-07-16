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
                Assert.That(ex.Message, Does.Contain("There is no metadata information for provider 'FooBar'"));
            }
        }

        [Test]
        public void TestValidProviderSqlServer()
        {
            DbProvider provider = new DbProvider(TestConstants.DefaultSqlServerProvider, "foo");
            Assert.IsNotNull(provider.ConnectionString);
            Assert.IsNotNull(provider.Metadata);
        }
    }
}
