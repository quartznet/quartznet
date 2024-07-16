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

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Spi;

namespace Quartz.Simpl;

/// <summary>
/// Helper base class for host name lookup requiring instance id generators.
/// </summary>
/// <author>Marko Lahma</author>
public abstract class HostNameBasedIdGenerator : IInstanceIdGenerator
{
    protected const int IdMaxLength = 50;

    private readonly ILogger<HostNameBasedIdGenerator> logger;

    protected HostNameBasedIdGenerator()
    {
        logger = LogProvider.CreateLogger<HostNameBasedIdGenerator>();
    }

    /// <summary>
    /// Generate the instance id for a <see cref="IScheduler" />
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns> The clusterwide unique instance id.
    /// </returns>
    public abstract ValueTask<string?> GenerateInstanceId(CancellationToken cancellationToken = default);

    protected async ValueTask<string?> GetHostName(
        int maxLength,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var hostAddress = await GetHostAddress(cancellationToken).ConfigureAwait(false);
            string hostName = hostAddress.HostName;
            if (hostName is not null && hostName.Length > maxLength)
            {
                string newName = hostName.Substring(0, maxLength);
                logger.LogInformation("Host name '{HostName}' was too long, shortened to '{Newname}'", hostName, newName);
                hostName = newName;
            }
            return hostName;
        }
        catch (Exception e)
        {
            ThrowHelper.ThrowSchedulerException("Couldn't get host name!", e);
            return null;
        }
    }

    protected virtual ValueTask<IPHostEntry> GetHostAddress(
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<IPHostEntry>(new IPHostEntry
        {
            HostName = Dns.GetHostName()
        });
    }
}