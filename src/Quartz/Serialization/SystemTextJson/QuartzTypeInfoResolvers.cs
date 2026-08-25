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

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Quartz.Serialization.SystemTextJson;

/// <summary>
/// The resolver chain both of Quartz's JSON formats are built out of: the generated contract in front,
/// the scheduler's own registry behind it, and reflection last where reflection still exists.
/// </summary>
/// <remarks>
/// The store format and the HTTP wire format have the same shape and the same open half — the values
/// inside a <see cref="JobDataMap" />, which the application chose and no generated contract can name.
/// They are assembled here once so that the two cannot drift, and so that the argument for naming
/// <see cref="DefaultJsonTypeInfoResolver" /> is written down in one place.
/// </remarks>
internal static class QuartzTypeInfoResolvers
{
    /// <summary>
    /// Puts <paramref name="contract" /> and the scheduler's registry in front of
    /// <paramref name="options" />'s resolver chain, leaving the chain ending in reflection.
    /// </summary>
    /// <param name="options">The options being taught the chain.</param>
    /// <param name="contract">The generated metadata for the format's closed shapes.</param>
    /// <param name="registry">
    /// The scheduler's registry, which answers for the trigger and calendar types registered with it and
    /// carries whatever resolvers the application handed in.
    /// </param>
    /// <remarks>
    /// <para>
    /// Asking twice leaves the chain as asking once does. On the server the options belong to the whole
    /// container, and every <c>AddQuartzHttpApi</c> call wants the same contract in front of them.
    /// </para>
    /// <para>
    /// Options carrying no resolver of their own fall back to reflection lazily, but only for as long as
    /// the chain stays empty — and putting anything in it ends that. So the fallback has to be named
    /// here, or the values inside a <see cref="JobDataMap" />, whose types no contract can know, would
    /// stop resolving at all.
    /// </para>
    /// </remarks>
    public static void UseQuartzContract(
        this JsonSerializerOptions options,
        IJsonTypeInfoResolver contract,
        SystemTextJsonSerializerRegistry registry)
    {
        IList<IJsonTypeInfoResolver> chain = options.TypeInfoResolverChain;
        if (chain.Contains(contract))
        {
            return;
        }

        if (chain.Count == 0)
        {
            DefaultJsonTypeInfoResolver? reflection = Reflection();
            if (reflection is not null)
            {
                chain.Add(reflection);
            }
        }

        int index = 0;
        chain.Insert(index++, contract);
        foreach (IJsonTypeInfoResolver resolver in registry.TypeInfoResolvers)
        {
            chain.Insert(index++, resolver);
        }
    }

    /// <summary>
    /// The reflection-based resolver the open half needs, or <see langword="null" /> where
    /// reflection-based serialization is switched off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chain has to end in reflection because both formats do: a <see cref="JobDataMap" /> holds
    /// whatever the application put in it, and no generated metadata can describe that. The same guard
    /// <c>Microsoft.AspNetCore.Http.Json.JsonOptions</c> builds its own default resolver behind is what
    /// makes naming <see cref="DefaultJsonTypeInfoResolver" /> here safe: a trimmed publish sets
    /// <c>System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault</c> to false — the SDK does it by
    /// default, as the trim canary's runtimeconfig shows — so the trimmer substitutes the property,
    /// drops this branch and never sees the resolver. What such an application is left with is the
    /// generated contract plus whatever it registered itself, which is enough for a job data map holding
    /// the types Quartz's own accessors name.
    /// </para>
    /// <para>
    /// A native AOT publish is the same publish: it implies <c>PublishTrimmed</c>, so it sets the same
    /// switch to false and ILCompiler substitutes the same property. That is why the AOT warning is
    /// answered here rather than recorded — the resolver this branch would construct does not exist in
    /// an AOT application to need constructing.
    /// </para>
    /// <para>
    /// The suppressions therefore hide nothing an application is not told. An application whose job data
    /// holds a type of its own, published trimmed, gets a <c>NotSupportedException</c> naming that type
    /// on the first write rather than a silently wrong payload, and
    /// <see cref="SystemTextJsonSerializerRegistry.AddTypeInfoResolver" /> is how it answers for it.
    /// </para>
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Guarded by IsReflectionEnabledByDefault, which a trimmed publish substitutes away along with this branch. See the remarks.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Guarded by IsReflectionEnabledByDefault, which an AOT publish substitutes away along with this branch. See the remarks.")]
    public static DefaultJsonTypeInfoResolver? Reflection()
    {
        return JsonSerializer.IsReflectionEnabledByDefault ? new DefaultJsonTypeInfoResolver() : null;
    }
}
