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

using System.Net;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Simpl;

[TestFixture]
public class SimpleInstanceIdGeneratorTest
{
    private IInstanceIdGenerator generator;

    [SetUp]
    public void SetUp()
    {
        generator = new TestInstanceIdGenerator();
    }

    [Test]
    public async Task IdShouldNotExceed50Chars()
    {
        string instanceId = await generator.GenerateInstanceId();
        Assert.That(instanceId, Has.Length.LessThanOrEqualTo(50));
    }

    private class TestInstanceIdGenerator : HostNameBasedIdGenerator
    {
        // assume ticks to be at most 20 chars long
        private const int HostNameMaxLength = IdMaxLength - 20;


        public override async ValueTask<string> GenerateInstanceId(CancellationToken cancellationToken = default)
        {
            var hostName = await GetHostName(HostNameMaxLength, cancellationToken).ConfigureAwait(false);
            return hostName + TimeProvider.System.GetTimestamp();
        }

        protected override ValueTask<IPHostEntry> GetHostAddress(
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<IPHostEntry>(new IPHostEntry
            {
                HostName = "my-windows-machine-with-long-name.at.azurewebsites.net"
            });
        }
    }
}