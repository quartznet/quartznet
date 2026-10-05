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

namespace Quartz.Impl;

/// <summary>
/// A host sent a name for an enum this client has no member for, where the contract has nothing to read
/// it as instead.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="JsonException" />, so a caller that catches what a malformed body raises catches this too,
/// and System.Text.Json adds the path of the member it was reading. It is its own type so that the reader of
/// a listing can tell it from every other failure: an item that carries an unknown name is left out of the
/// listing, while a body that is malformed still fails the call, as it always did.
/// </para>
/// <para>
/// Outside a listing nothing catches it. A single read of something this client cannot name — one
/// trigger's state, the execution limits — fails, and the message says why.
/// </para>
/// </remarks>
internal sealed class UnknownWireNameException : JsonException
{
    /// <param name="enumType">The enum the name was read as.</param>
    /// <param name="name">The name the host sent.</param>
    public UnknownWireNameException(Type enumType, string name)
        : base($"The host sent '{name}' as a {enumType.Name}, which this version of Quartz.HttpClient does not know. Upgrade the client to read it.")
    {
        EnumType = enumType;
        Name = name;
    }

    /// <summary>
    /// The enum the name was read as.
    /// </summary>
    public Type EnumType { get; }

    /// <summary>
    /// The name the host sent.
    /// </summary>
    public string Name { get; }
}
