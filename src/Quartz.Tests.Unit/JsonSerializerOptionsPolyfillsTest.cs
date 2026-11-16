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

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Quartz.Serialization.SystemTextJson;

namespace Quartz.Tests.Unit;

/// <summary>
/// The <c>net10.0</c> backfills exist in both of <c>Quartz</c>'s assets and answer as the BCL does (#4341).
/// </summary>
/// <remarks>
/// Eight shipped packages see Quartz's internals, compile against the <c>net10.0</c> asset and run on the
/// <c>net11.0</c> one, so an internal member that only one asset had would throw <see cref="MissingMethodException" />
/// on .NET 11 alone. This project is compiled against each asset in turn: the <c>.NET 11</c> leg builds it for
/// <c>net11.0</c>, the other legs for <c>net10.0</c>. Each call below names the extension's container, so it binds
/// to Quartz's member and not to the BCL's, and a member missing from either asset fails that build.
/// </remarks>
public class JsonSerializerOptionsPolyfillsTest
{
    [Test]
    public void GetTypeInfoOfTIsTheMetadataForTypeofT()
    {
        JsonSerializerOptions options = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        options.MakeReadOnly();

        JsonTypeInfo<string> text = JsonSerializerOptionsPolyfills.GetTypeInfo<string>(options);
        JsonTypeInfo<object> anything = JsonSerializerOptionsPolyfills.GetTypeInfo<object>(options);

        text.Should().BeSameAs(options.GetTypeInfo(typeof(string)),
            "options that are read-only cache their metadata, so the generic form and typeof(T) answer one instance");
        anything.Should().BeSameAs(options.GetTypeInfo(typeof(object)));
    }
}
