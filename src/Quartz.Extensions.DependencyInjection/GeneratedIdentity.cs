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

namespace Quartz;

/// <summary>
/// A key generated for a job or trigger whose registration gave it no identity.
/// </summary>
/// <remarks>
/// Recorded while the builder still knows no key was given, because a generated key cannot be told from
/// a chosen one afterwards: a job may be named with a GUID on purpose.
/// </remarks>
internal sealed class GeneratedIdentity
{
    public GeneratedIdentity(string registration, string kind, string key)
    {
        Registration = registration;
        Kind = kind;
        Key = key;
    }

    /// <summary>
    /// The call that registered it, naming the job type, or the job's key when the call names no type.
    /// </summary>
    public string Registration { get; }

    /// <summary>
    /// <c>job</c> or <c>trigger</c>.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// The generated key.
    /// </summary>
    public string Key { get; }
}
