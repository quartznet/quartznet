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

using System.Collections;
using System.Globalization;
using System.Runtime.Serialization;

using Quartz.Util;

namespace Quartz;

/// <summary>
/// Holds state information for <see cref="IJob" /> instances.
/// </summary>
/// <remarks>
/// <see cref="JobDataMap" /> instances are stored once when the <see cref="IJob" />
/// is added to a scheduler. They are also re-persisted after every execution of
/// instances that have <see cref="PersistJobDataAfterExecutionAttribute" /> present.
/// <para>
/// <see cref="JobDataMap" /> instances can also be stored with a
/// <see cref="ITrigger" />.  This can be useful in the case where you have a Job
/// that is stored in the scheduler for regular/repeated use by multiple
/// Triggers, yet with each independent triggering, you want to supply the
/// Job with different data inputs.
/// </para>
/// <para>
/// The <see cref="IJobExecutionContext" /> passed to a Job at execution time
/// also contains a convenience <see cref="JobDataMap" /> that is the result
/// of merging the contents of the trigger's JobDataMap (if any) over the
/// Job's JobDataMap (if any).
/// </para>
/// <para>
/// Update since 2.4.2 - We keep an dirty flag for this map so that whenever you modify(add/delete) any of the entries,
/// it will set to "true". However if you create new instance using an existing map with constructor, then
/// the dirty flag will NOT be set to "true" until you modify the instance.
/// </para>
/// </remarks>
/// <seealso cref="IJob" />
/// <seealso cref="PersistJobDataAfterExecutionAttribute" />
/// <seealso cref="ITrigger" />
/// <seealso cref="IJobExecutionContext" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public sealed class JobDataMap : StringKeyDirtyFlagMap
{
    /// <summary>
    /// Create an empty <see cref="JobDataMap" />.
    /// </summary>
    public JobDataMap() : this(0)
    {
    }

    /// <summary>
    /// Create <see cref="JobDataMap" /> with initial capacity.
    /// </summary>
    public JobDataMap(int initialCapacity) : base(initialCapacity)
    {
    }

    /// <summary>
    /// Create a <see cref="JobDataMap" /> with the given data.
    /// </summary>
    public JobDataMap(IDictionary<string, object?> map) : this(map.Count)
    {
        PutAll(map);

        // When constructing a new data map from another existing map, we should NOT mark dirty flag as true
        // Use case: loading JobDataMap from DB
        ClearDirtyFlag();
    }

    /// <summary>
    /// Create a <see cref="JobDataMap" /> with the given data.
    /// </summary>
    public JobDataMap(IDictionary map) : this(map.Count)
    {
        bool clearDirtyFlag = true;
        foreach (DictionaryEntry entry in map)
        {
            if (SchedulerConstants.ForceJobDataMapDirty.Equals(entry.Key))
            {
                clearDirtyFlag = false;
            }
            else
            {
                this[(string) entry.Key] = entry.Value!;
            }
        }

        if (clearDirtyFlag)
        {
            // When constructing a new data map from another existing map, we should NOT mark dirty flag as true
            // Use case: loading JobDataMap from DB
            ClearDirtyFlag();
        }
    }

    /// <summary>
    /// Serialization constructor.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="context"></param>
    private JobDataMap(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }

    /// <summary>
    /// Adds the given value as a string version using the default ToString operation.
    /// </summary>
    public void PutAsString<T>(string key, T value) where T : IConvertible
    {
        Put(key, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Adds the given <see cref="DateTimeOffset" /> value as a string version to the
    /// <see cref="IJob" />'s data map.
    /// </summary>
    public void PutAsString(string key, DateTimeOffset value)
    {
        string strValue = value.ToString(CultureInfo.InvariantCulture);
        Put(key, strValue);
    }

    /// <summary>
    /// Adds the given <see cref="TimeSpan" /> value as a string version to the
    /// <see cref="IJob" />'s data map.
    /// </summary>
    public void PutAsString(string key, TimeSpan value)
    {
        string strValue = value.ToString();
        Put(key, strValue);
    }

    /// <summary>
    /// Adds the given <see cref="Guid" /> value as a string version to the
    /// <see cref="IJob" />'s data map. The hyphens are omitted from the  <see cref="Guid" />.
    /// </summary>
    public void PutAsString(string key, Guid value)
    {
        string strValue = value.ToString("N");
        Put(key, strValue);
    }

    /// <summary>
    /// Adds the given <see cref="Guid" /> value as a string version to the
    /// <see cref="IJob" />'s data map. The hyphens are omitted from the  <see cref="Guid" />.
    /// </summary>
    public void PutAsString(string key, Guid? value)
    {
        string? strValue = value?.ToString("N");
        Put(key, strValue!);
    }

    internal override DirtyFlagMap<string, object> Clone()
    {
        return new JobDataMap(WrappedMap);
    }
}