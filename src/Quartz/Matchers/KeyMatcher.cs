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
/// Matches on the complete key being equal (both name and group).
/// </summary>
/// <seealso cref="Matchers.Key(JobKey)" />
/// <seealso cref="Matchers.Key(TriggerKey)" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public sealed class KeyMatcher<TKey> : IMatcher<TKey> where TKey : Key<TKey>
{
    internal KeyMatcher(TKey compareTo)
    {
        CompareToValue = compareTo;
    }

    public bool IsMatch(TKey key)
    {
        return CompareToValue.Equals(key);
    }

    public TKey CompareToValue { get; private set; } = null!;

    public override int GetHashCode()
    {
        const int Prime = 31;
        int result = 1;
        result = Prime * result + (CompareToValue is null ? 0 : CompareToValue.GetHashCode());
        return result;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }
        if (obj is null)
        {
            return false;
        }
        if (GetType() != obj.GetType())
        {
            return false;
        }
        KeyMatcher<TKey> other = (KeyMatcher<TKey>) obj;
        if (CompareToValue is null)
        {
            if (other.CompareToValue is not null)
            {
                return false;
            }
        }
        else if (!CompareToValue.Equals(other.CompareToValue))
        {
            return false;
        }
        return true;
    }
}