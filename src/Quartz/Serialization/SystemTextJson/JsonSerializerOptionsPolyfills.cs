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
/// The <see cref="JsonSerializerOptions" /> members that the <c>net10.0</c> build of Quartz lacks. Each call site is
/// written against the <c>net11.0</c> API (#4341), where CA2263 asks for the generic form.
/// </summary>
/// <remarks>
/// A real member always binds before an extension member, so the <c>net11.0</c> build calls the BCL's. When
/// <c>net10.0</c> is dropped, delete this file; no call site changes.
/// </remarks>
internal static class JsonSerializerOptionsPolyfills
{
    extension(JsonSerializerOptions options)
    {
#if !NET11_0_OR_GREATER
        // JsonSerializerOptions.GetTypeInfo<T>() arrived in .NET 11, as this same cast of the metadata for typeof(T).
        public JsonTypeInfo<T> GetTypeInfo<T>() => (JsonTypeInfo<T>) options.GetTypeInfo(typeof(T));
#endif
    }
}
