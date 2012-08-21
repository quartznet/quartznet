#region License

/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
        public override bool Equals(Object obj)
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
        /// All keys must be <see cref="String" />s, and all values must be serializable.
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
        /// Adds the given <see cref="String" /> value to the <see cref="IJob" />'s
        /// data map.
        /// </summary>
        public virtual void Put(string key, string value)
        {
            base.Put(key, value);
        }

        /// <summary> 
        /// Retrieve the identified <see cref="int" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual int GetInt(string key)
        {
            object obj = this[key];

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
            object obj = this[key];

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
            object obj = this[key];

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
            object obj = this[key];

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
            object obj = this[key];

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
            object obj = this[key];

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
        /// Retrieve the identified <see cref="String" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public virtual string GetString(string key)
        {
            object obj = this[key];

            try
            {
                return (string) obj;
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
            object obj = this[key];

            try
            {
                return Convert.ToDateTime(obj, CultureInfo.InvariantCulture);
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
            object obj = this[key];
            return (DateTimeOffset) obj;
        }

        /// <summary>
        /// Retrieve the identified <see cref="TimeSpan" /> value from the <see cref="JobDataMap" />. 
        /// </summary>
        public virtual TimeSpan GetTimeSpan(string key)
        {
            object obj = this[key];
            return (TimeSpan) obj;
        }
    }
}