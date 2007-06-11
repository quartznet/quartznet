/* 
* Copyright 2004-2005 OpenSymphony 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/
using System;
using System.Collections;
using System.Runtime.Serialization;

using Quartz.Collection;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz
{
	/// <summary>
	/// Holds state information for <see cref="IJob" /> instances.
	/// </summary>
	/// <remarks>
	/// <see cref="JobDataMap" /> instances are stored once when the <see cref="IJob" />
	/// is added to a scheduler. They are also re-persisted after every execution of
	/// <see cref="IStatefulJob" /> instances.
	/// <p>
	/// <see cref="JobDataMap" /> instances can also be stored with a 
	/// <see cref="Trigger" />.  This can be useful in the case where you have a Job
	/// that is stored in the scheduler for regular/repeated use by multiple 
	/// Triggers, yet with each independent triggering, you want to supply the
	/// Job with different data inputs.  
	/// </p>
	/// <p>
	/// The <see cref="JobExecutionContext" /> passed to a Job at execution time 
	/// also contains a convenience <see cref="JobDataMap" /> that is the result
	/// of merging the contents of the trigger's JobDataMap (if any) over the
	/// Job's JobDataMap (if any).  
	/// </p>
    /// </remarks>
	/// <seealso cref="IJob" />
	/// <seealso cref="IStatefulJob" />
	/// <seealso cref="Trigger" />
	/// <seealso cref="JobExecutionContext" />
	/// <author>James House</author>
	[Serializable]
	public class JobDataMap : DirtyFlagMap
	{
		/// <summary> 
		/// Tell the <see cref="JobDataMap" /> that it should allow non-serializable data.
        /// </summary>
        /// <remarks>
		/// If the <see cref="JobDataMap" /> does contain non-Serializable
		/// objects, and it belongs to a non-volatile <see cref="IJob" /> that is
		/// stored in a <see cref="IJobStore" /> that supports persistence, then
		/// those elements will be nulled-out during persistence.
        /// </remarks>
		public virtual bool AllowsTransientData
		{
			get { return allowsTransientData; }

			set
			{
				if (ContainsTransientData() && !value)
				{
					throw new SystemException("Cannot set property 'allowsTransientData' to 'false' " +
					                          "when data map contains non-serializable objects.");
				}

				allowsTransientData = value;
			}
		}

		private bool allowsTransientData = false;

		/// <summary>
		/// Create an empty <see cref="JobDataMap" />.
		/// </summary>
		public JobDataMap() : base(15)
		{
		}

		/// <summary> 
		/// Create a <see cref="JobDataMap" /> with the given data.
		/// </summary>
		public JobDataMap(IDictionary map) : this()
		{
			PutAll(map);
		}

		/// <summary>
		/// Determines whether [contains transient data].
		/// </summary>
		/// <returns>
		/// 	<c>true</c> if [contains transient data]; otherwise, <c>false</c>.
		/// </returns>
		public virtual bool ContainsTransientData()
		{
			if (!AllowsTransientData)
			{
				// short circuit...
				return false;
			}

			string[] keys = GetKeys();

			for (int i = 0; i < keys.Length; i++)
			{
				object o = base[keys[i]];
				if (!(o is ISerializable))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary> 
		/// Nulls-out any data values that are non-Serializable.
		/// </summary>
		public virtual void RemoveTransientData()
		{
			if (!AllowsTransientData)
			{
				// short circuit...
				return;
			}

			string[] keys = GetKeys();

			for (int i = 0; i < keys.Length; i++)
			{
				object o = base[keys[i]];
				if (!(o is ISerializable))
				{
					Remove(keys[i]);
				}
			}
		}

		/// <summary>
		/// Adds the name-value pairs in the given <see cref="IDictionary" /> to the <see cref="JobDataMap" />.
		/// <p>
		/// All keys must be <see cref="String" />s, and all values must be serializable.
		/// </p>
		/// </summary>
		public override void PutAll(IDictionary map)
		{
			IEnumerator itr = new HashSet(map.Keys).GetEnumerator();
			while (itr.MoveNext())
			{
				object key = itr.Current;
				object val = map[key];

				Put(key, val);
				// will throw ArgumentException if value not serilizable
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
		/// Adds the given <see cref="bool" /> value as a string version to the
		/// <see cref="IJob" />'s data map.
		/// </summary>
		public virtual void PutAsString(string key, bool value)
		{
			string strValue = value.ToString();
			base.Put(key, strValue);
		}


		/// <summary>
		/// Adds the given <see cref="char" /> value as a string version to the
		/// <see cref="IJob" />'s data map.
		/// </summary>
		public virtual void PutAsString(string key, char value)
		{
			string strValue = value.ToString();
			base.Put(key, strValue);
		}

		/// <summary>
		/// Adds the given <see cref="double" /> value as a string version to the
		/// <see cref="IJob" />'s data map.
		/// </summary>
		public virtual void PutAsString(string key, double value)
		{
			string strValue = value.ToString();
			base.Put(key, strValue);
		}


		/// <summary>
		/// Adds the given <see cref="float" /> value as a string version to the
		/// <see cref="IJob" />'s data map.
		/// </summary>
		public virtual void PutAsString(string key, float value)
		{
			string strValue = value.ToString();
			base.Put(key, strValue);
		}


		/// <summary>
		/// Adds the given <see cref="int" /> value as a string version to the
		/// <see cref="IJob" />'s data map.
		/// </summary>
		public virtual void PutAsString(string key, int value)
		{
			string strValue = value.ToString();
			base.Put(key, strValue);
		}


		/// <summary>
		/// Adds the given <see cref="long" /> value as a string version to the
		/// <see cref="IJob" />'s data map.
		/// </summary>
		public virtual void PutAsString(string key, long value)
		{
			string strValue = value.ToString();
			base.Put(key, strValue);
		}

		/// <summary>
		/// Adds the given serializable object value to the <see cref="JobDataMap" />.
		/// </summary>
		public override object Put(object key, object value)
		{
			if (!(key is string))
			{
				throw new ArgumentException("Keys in map must be Strings.");
			}
			return base.Put(key, value);
		}

		/// <summary> 
		/// Retrieve the identified <see cref="int" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual int GetInt(string key)
		{
			object obj = this[key];

			try
			{
				return (int) obj;
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
				return (long) obj;
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
				return (float) obj;
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
				return ((double) obj);
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
				return ((bool) obj);
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
				return ((char) obj);
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
		/// Retrieve the identified <see cref="int" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual int GetIntFromString(string key)
		{
			object obj = this[key];

			return Int32.Parse((string) obj);
		}

		/// <summary>
		/// Retrieve the identified <see cref="int" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual long GetIntValue(string key)
		{
			object obj = this[key];

			if (obj is string)
			{
				return GetIntFromString(key);
			}
			else
			{
				return GetInt(key);
			}
		}

		/// <summary>
		/// Retrieve the identified <see cref="int" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual Int32 GetIntegerFromString(string key)
		{
			object obj = this[key];

			return Int32.Parse((string) obj);
		}

		/// <summary>
		/// Retrieve the identified <see cref="bool" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual bool GetBooleanValueFromString(string key)
		{
			object obj = this[key];

			return ((string) obj).ToUpper().Equals("TRUE");
		}

		/// <summary>
		/// Retrieve the identified <see cref="bool" /> value from the 
		/// <see cref="JobDataMap" />.
		/// </summary>
		public virtual bool GetBooleanValue(string key)
		{
			object obj = this[key];

			if (obj is string)
			{
				return GetBooleanValueFromString(key);
			}
			else
			{
				return GetBoolean(key);
			}
		}

		/// <summary>
		/// Retrieve the identified <see cref="Boolean" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual Boolean GetBooleanFromString(string key)
		{
			object obj = this[key];

			return ((string) obj).ToUpper().Equals("TRUE");
		}

		/// <summary>
		/// Retrieve the identified <see cref="char" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual char GetCharFromString(string key)
		{
			object obj = this[key];

			return ((string) obj)[0];
		}
		
		/// <summary>
		/// Retrieve the identified <see cref="double" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual double GetDoubleValueFromString(string key)
		{
			object obj = this[key];
			return Double.Parse((string) obj);
		}

		/// <summary>
		/// Retrieve the identified <see cref="double" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual double GetDoubleValue(string key)
		{
			object obj = this[key];

			if (obj is string)
			{
				return GetDoubleValueFromString(key);
			}
			else
			{
				return GetDouble(key);
			}
		}

		/// <summary>
		/// Retrieve the identified <see cref="Double" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual Double GetDoubleFromString(string key)
		{
			object obj = this[key];
			return Double.Parse((string) obj);
		}

		/// <summary>
		/// Retrieve the identified <see cref="float" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual float GetFloatValueFromString(string key)
		{
			object obj = this[key];
			return Single.Parse((string) obj);
		}

		/// <summary>
		/// Retrieve the identified <see cref="float" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual float GetFloatValue(string key)
		{
			object obj = this[key];

			if (obj is string)
			{
				return GetFloatValueFromString(key);
			}
			else
			{
				return GetFloat(key);
			}
		}

		/// <summary>
		/// Retrieve the identified <see cref="float" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual Single GetFloatFromString(string key)
		{
			object obj = this[key];
			return Single.Parse((string) obj);
		}

		/// <summary>
		/// Retrieve the identified <see cref="long" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual long GetLongValueFromString(string key)
		{
			object obj = this[key];
			return Int64.Parse((string) obj);
		}

		/// <summary>
		/// Retrieve the identified <see cref="long" /> value from the <see cref="JobDataMap" />.
		/// </summary>
		public virtual long GetLongValue(string key)
		{
			object obj = this[key];

			if (obj is string)
			{
				return GetLongValueFromString(key);
			}
			else
			{
				return GetLong(key);
			}
		}


		/// <summary>
		/// Gets the keys.
		/// </summary>
		/// <returns></returns>
		public virtual string[] GetKeys()
		{
			return (string[]) new ArrayList(KeySet()).ToArray(typeof (string));
		}

		/// <summary>
		/// Gets the date time.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns></returns>
		public DateTime GetDateTime(string key)
		{
			object obj = this[key];

			try
			{
				return ((DateTime) obj);
			}
			catch (Exception)
			{
				throw new InvalidCastException("Identified object is not a DateTime.");
			}
		}

        /// <summary>
        /// Gets the value behind the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns></returns>
	    public object Get(object key)
	    {
	        return this[key];
	    }
	}
}