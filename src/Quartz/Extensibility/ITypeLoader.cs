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

namespace Quartz.Extensibility;

/// <summary>
/// An interface for classes wishing to provide the service of loading classes
/// and resources within the scheduler...
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface ITypeLoader
{
    /// <summary>
    /// Return the type with the given name.
    /// </summary>
    /// <remarks>
    /// An implementation that cannot resolve the name must <b>throw</b> — <see cref="TypeLoadException" />
    /// is what the built-in helper raises — rather than returning <see langword="null" />. Quartz calls
    /// this when it already knows a type is required, so a null would only surface later as a failure
    /// with nothing left to point at. <see langword="null" /> is reserved for a null or empty name.
    /// </remarks>
    /// <param name="name">The assembly-qualified type name to load.</param>
    /// <exception cref="TypeLoadException">The name could not be resolved to a type.</exception>
    Type? LoadType(string name);
}