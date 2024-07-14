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

namespace Quartz.Impl.Matchers;

/// <summary>
/// An abstract base class for some types of matchers.
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public abstract class StringMatcher<TKey> : IMatcher<TKey> where TKey : Key<TKey>
{
    protected StringMatcher(string compareTo, StringOperator compareWith)
    {
        if (compareTo is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(compareTo), "CompareTo value cannot be null!");
        }
        CompareToValue = compareTo;
        CompareWithOperator = compareWith;
    }

    protected abstract string GetValue(TKey key);

    public bool IsMatch(TKey key)
    {
        return CompareWithOperator.Evaluate(GetValue(key), CompareToValue);
    }

    public override int GetHashCode()
    {
        const int Prime = 31;
        int result = 1;
        result = Prime * result + (CompareToValue?.GetHashCode() ?? 0);
        result = Prime * result + CompareWithOperator.GetHashCode();
        return result;
    }

    public override bool Equals(object? obj)
    {
        if (this == obj)
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
        StringMatcher<TKey> other = (StringMatcher<TKey>) obj;
        if (CompareToValue is null)
        {
            if (other.CompareToValue is not null)
            {
                return false;
            }
        }
        else if (CompareToValue != other.CompareToValue)
        {
            return false;
        }
        if (!CompareWithOperator.Equals(other.CompareWithOperator))
        {
            return false;
        }
        return true;
    }

    public string CompareToValue { get; }
    public StringOperator CompareWithOperator { get; }

    public override string ToString()
    {
        return $"{CompareWithOperator}({CompareToValue})";
    }
}