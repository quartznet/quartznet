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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace Quartz.Util
{
    /// <summary>
    /// An implementation of <see cref="IDictionary" /> that wraps another <see cref="IDictionary" />
    /// and flags itself 'dirty' when it is modified, enforces that all keys are
    /// strings.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class StringKeyDirtyFlagMap : DirtyFlagMap<string, object>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StringKeyDirtyFlagMap"/> class.
        /// </summary>
        public StringKeyDirtyFlagMap()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StringKeyDirtyFlagMap"/> class.
        /// </summary>
        /// <param name="initialCapacity">The initial capacity.</param>
        public StringKeyDirtyFlagMap(int initialCapacity) : base(initialCapacity)
        {
        }

        // TODO (NetCore Port) - When serialized in an object collection, Json.Net deserializes all integer types as longs and all real number types
        //                       as doubles. If needed, we could do some 'fix-ups' here if a different default was preferable (return numeric types as the
        //                       smallest type they fit in, for example). For now, let's use the default Json.Net behavior and re-evaluate later if any
        //                       cleanup is needed here.
        //[OnDeserialized]
        //private void CleanupDeserializedMap(StreamingContext ctx)
        //{
        //    foreach (var key in GetKeys())
        //    {
        //        var val = this[key];
        //        if (val is long)
        //        {
        //            long longVal = (long)val;
        //            if (longVal <= int.MaxValue && longVal >= int.MinValue)
        //            {
        //                Put(key, (int)longVal);
        //                continue;
        //            }
        //        }
        //    }
        //}

        /// <summary>
        /// Serialization constructor.
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected StringKeyDirtyFlagMap(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        /// <summary>
        /// Determines whether the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <param name="obj">The <see cref="T:System.Object"/> to compare with the current <see cref="T:System.Object"/>.</param>
        /// <returns>
        /// 	<see langword="true"/> if the specified <see cref="T:System.Object"/> is equal to the
        /// current <see cref="T:System.Object"/>; otherwise, <see langword="false"/>.
        /// </returns>
        public override bool Equals(object? obj)
        {
            return base.Equals(obj);
        }

        /// <summary>
        /// Serves as a hash function for a particular type, suitable
        /// for use in hashing algorithms and data structures like a hash table.
        /// </summary>
        /// <returns>
        /// A hash code for the current <see cref="T:System.Object"/>.
        /// </returns>
        public override int GetHashCode()
        {
            return WrappedMap.GetHashCode();
        }

        /// <summary>
        /// Gets the keys.
        /// </summary>
        /// <returns></returns>
        public virtual IList<string> GetKeys()
        {
            return new List<string>(KeySet());
        }

        /// <summary>
        /// Adds the name-value pairs in the given <see cref="IDictionary" /> to the <see cref="JobDataMap" />.
        /// <para>
        /// All keys must be <see cref="string" />s, and all values must be serializable.
        /// </para>
        /// </summary>
        public override void PutAll(IDictionary<string, object> map)
        {
            foreach (KeyValuePair<string, object> pair in map)
            {
                Put(pair.Key, pair.Value);
                // will throw ArgumentException if value not serializable
            }
        }

        /// <summary>
        /// Adds the given <see cref="int" /> value to the <see cref="IJob" />'s
        /// data map.
        /// </summary>
        public virtual void Put(string key, int value)
        {
            base.Put(key, value);
        }

        /// <summary>
        /// Adds the given <see cref="long" /> value to the <see cref="IJob" />'s
        /// data map.
        /// </summary>
        public virtual void Put(string key, long value)
        {
            base.Put(key, value);
        }

        /// <summary>
        /// Adds the given <see cref="float" /> value to the <see cref="IJob" />'s
        /// data map.
        /// </summary>
        public virtual void Put(string key, float value)
        {
            base.Put(key, value);
        }

        /// <summary>
        /// Adds the given <see cref="double" /> value to the <see cref="IJob" />'s
        /// data map.
        /// </summary>
        public virtual void Put(string key, double value)
        {
            base.Put(key, value);
        }

        /// <summary>
        /// Adds the given <see cref="bool" /> value to the <see cref="IJob" />'s
        /// data map.
        /// </summary>
        public virtual void Put(string key, bool value)
        {
            base.Put(key, value);
        }

        /// <summary>
        /// Adds the given <see cref="char" /> value to the <see cref="IJob" />'s
        /// data map.
        /// </summary>
        public virtual void Put(string key, char value)
        {
            base.Put(key, value);
        }

        /// <summary>
        /// Adds the given <see cref="string" /> value to the <see cref="IJob" />'s
        /// data map.
        /// </summary>
        public virtual void Put(string key, string? value)
        {
            base.Put(key, value!);
        }

        /// <summary>
        /// Adds the given <see cref="Guid" /> value to the <see cref="IJob" />'s
        /// data map.
        /// </summary>
        public virtual void Put(string key, Guid value)
        {
            base.Put(key, value);
        }

        /// <summary>
        /// Retrieve the identified <see cref="int" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual int GetInt(string key)
        {
            var obj = this[key];

            try
            {
                return Convert.ToInt32(obj);
            }
            catch (Exception)
            {
                throw new InvalidCastException("Identified object is not an Integer.");
            }
        }

        /// <summary>
        /// Retrieve the identified <see cref="long" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual long GetLong(string key)
        {
            var obj = this[key];

            try
            {
                return Convert.ToInt64(obj);
            }
            catch (Exception)
            {
                throw new InvalidCastException("Identified object is not a Long.");
            }
        }

        /// <summary>
        /// Retrieve the identified <see cref="float" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual float GetFloat(string key)
        {
            var obj = this[key];

            try
            {
                return Convert.ToSingle(obj);
            }
            catch (Exception)
            {
                throw new InvalidCastException("Identified object is not a Float.");
            }
        }

        /// <summary>
        /// Retrieve the identified <see cref="double" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual double GetDouble(string key)
        {
            var obj = this[key];

            try
            {
                return Convert.ToDouble(obj);
            }
            catch (Exception)
            {
                throw new InvalidCastException("Identified object is not a Double.");
            }
        }

        /// <summary>
        /// Retrieve the identified <see cref="bool" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual bool GetBoolean(string key)
        {
            var obj = this[key];

            try
            {
                return Convert.ToBoolean(obj);
            }
            catch (Exception)
            {
                throw new InvalidCastException("Identified object is not a Boolean.");
            }
        }

        /// <summary>
        /// Retrieve the identified <see cref="char" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual char GetChar(string key)
        {
            var obj = this[key];

            try
            {
                return Convert.ToChar(obj);
            }
            catch (Exception)
            {
                throw new InvalidCastException("Identified object is not a Character.");
            }
        }

        /// <summary>
        /// Retrieve the identified <see cref="string" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual string? GetString(string key)
        {
            var obj = this[key];

            try
            {
                return (string?) obj;
            }
            catch (Exception)
            {
                throw new InvalidCastException("Identified object is not a String.");
            }
        }

        /// <summary>
        /// Retrieve the identified <see cref="DateTime" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual DateTime GetDateTime(string key)
        {
            var obj = this[key];

            try
            {
                return
                    obj is DateTimeOffset dto
                        ? dto.DateTime
                        : Convert.ToDateTime(obj, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                throw new InvalidCastException("Identified object is not a DateTime.");
            }
        }

        /// <summary>
        /// Retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual DateTimeOffset GetDateTimeOffset(string key)
        {
            var obj = this[key];
            return (DateTimeOffset) obj!;
        }

        /// <summary>
        /// Retrieve the identified <see cref="TimeSpan" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual TimeSpan GetTimeSpan(string key)
        {
            var obj = this[key];
            return (TimeSpan) obj!;
        }

        /// <summary>
        /// Retrieve the identified <see cref="Guid" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual Guid GetGuid(string key)
        {
            var obj = this[key];
            return (Guid) obj!;
        }

        /// <summary>
        /// Retrieve the identified <see cref="Guid" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual Guid? GetNullableGuid(string key)
        {
            var obj = this[key];
            return (Guid?) obj;
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="int" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual bool TryGetInt(string key, out int value)
        {
            try
            {
                value = GetInt(key);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="bool" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual bool TryGetBoolean(string key, out bool value)
        {
            try
            {
                value = GetBoolean(key);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="double" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual bool TryGetDouble(string key, out double value)
        {
            try
            {
                value = GetDouble(key);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="float" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual bool TryGetFloat(string key, out float value)
        {
            try
            {
                value = GetFloat(key);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="long" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual bool TryGetLong(string key, out long value)
        {
            try
            {
                value = GetLong(key);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="DateTime" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual bool TryGetDateTime(string key, out DateTime value)
        {
            try
            {
                value = GetDateTime(key);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual bool TryGetDateTimeOffset(string key, out DateTimeOffset value)
        {
            try
            {
                value = GetDateTimeOffset(key);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="TimeSpan" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual bool TryGetTimeSpan(string key, out TimeSpan value)
        {
            try
            {
                value = GetTimeSpan(key);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="Guid" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual bool TryGetGuid(string key, out Guid value)
        {
            try
            {
                value = GetGuid(key);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Try to retrieve the identified Nullable <see cref="Guid" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual bool TryGetNullableGuid(string key, out Guid? value)
        {
            try
            {
                value = GetNullableGuid(key);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="char" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual bool TryGetChar(string key, out char value)
        {
            try
            {
                value = GetChar(key);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }
    }
}
