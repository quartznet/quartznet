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

namespace Quartz.Serialization.SystemTextJson;

/// <summary>
/// The <see cref="JsonSerializerOptions" /> members that <c>net10.0</c> lacks. Each call site is written against the
/// <c>net11.0</c> API (#4341), where CA2263 asks for the generic form.
/// </summary>
/// <remarks>
/// <para>
/// Both assets carry these members, with no <c>#if</c>. Eight shipped packages see Quartz's internals
/// (<c>AssemblyInfoExtras.cs</c>), compile against the <c>net10.0</c> asset, and in a <c>net11.0</c> application
/// run on the <c>net11.0</c> one. A member that only the <c>net10.0</c> asset had would compile in one of them and
/// throw <see cref="MissingMethodException" /> on .NET 11 alone.
/// </para>
/// <para>
/// A real member always binds before an extension member, so the <c>net11.0</c> build of Quartz calls the BCL's
/// own. Only a body differs between the assets, never whether the member exists. On <c>net11.0</c> the body
/// forwards to the BCL, because CA2263 refuses the non-generic call there. When <c>net10.0</c> is dropped, delete
/// this file; no call site changes.
/// </para>
/// </remarks>
internal static class JsonSerializerOptionsPolyfills
{
    extension(JsonSerializerOptions options)
    {
        // JsonSerializerOptions.GetTypeInfo<T>() arrived in .NET 11, as this same cast of the metadata for typeof(T).
        public JsonTypeInfo<T> GetTypeInfo<T>() =>
#if NET11_0_OR_GREATER
            options.GetTypeInfo<T>();
#else
            (JsonTypeInfo<T>) options.GetTypeInfo(typeof(T));
#endif
    }
}
