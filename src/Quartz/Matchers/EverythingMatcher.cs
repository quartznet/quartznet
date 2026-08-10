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

using Quartz.Util;

namespace Quartz;

/// <summary>
/// Matches every key.
/// </summary>
/// <seealso cref="Matchers.AllJobs" />
/// <seealso cref="Matchers.AllTriggers" />
/// <author>jhouse</author>
public sealed class EverythingMatcher<TKey> : IMatcher<TKey> where TKey : Key<TKey>
{
    private EverythingMatcher()
    {
    }

    /// <summary>
    /// Create an EverythingMatcher that matches every key.
    /// </summary>
    public static EverythingMatcher<TKey> All()
    {
        return new EverythingMatcher<TKey>();
    }

    public bool IsMatch(TKey key)
    {
        return true;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null)
        {
            return false;
        }

        return obj.GetType() == GetType();
    }

    public override int GetHashCode()
    {
        return GetType().Name.GetHashCode();
    }
}