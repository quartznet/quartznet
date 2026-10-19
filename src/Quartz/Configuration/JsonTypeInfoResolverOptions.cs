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

using System.Text.Json.Serialization.Metadata;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Serialization.SystemTextJson;

namespace Quartz.Configuration;

/// <summary>
/// The metadata one scheduler's builder was handed through
/// <see cref="SystemTextJsonConfigurationExtensions.AddJsonTypeInfoResolver" />, kept as that scheduler's
/// named options.
/// </summary>
/// <remarks>
/// Options rather than a registration of their own, because options are already named per scheduler and
/// keep the order their <c>Configure</c> calls were made in, which is the order the resolvers are asked in.
/// Internal: the public surface is the builder method, and the list is only ever read here.
/// </remarks>
internal sealed class JsonTypeInfoResolverOptions
{
    /// <summary>
    /// The resolvers, in the order they were added.
    /// </summary>
    public List<IJsonTypeInfoResolver> Resolvers { get; } = [];

    /// <summary>
    /// The registry a scheduler's own System.Text.Json serializers read: <paramref name="registry" />,
    /// with the resolvers this scheduler's builder was handed asked after the registry's own.
    /// </summary>
    /// <param name="provider">A provider that can resolve options.</param>
    /// <param name="schedulerName">The scheduler's options name; empty for the default scheduler.</param>
    /// <param name="registry">The registry the serializer would read without them.</param>
    public static SystemTextJsonSerializerRegistry Apply(
        IServiceProvider provider,
        string schedulerName,
        SystemTextJsonSerializerRegistry registry)
    {
        JsonTypeInfoResolverOptions options = provider.GetRequiredService<IOptionsMonitor<JsonTypeInfoResolverOptions>>().Get(schedulerName);
        return registry.WithResolvers(options.Resolvers);
    }
}
