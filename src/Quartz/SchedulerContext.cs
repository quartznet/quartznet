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
using Quartz.Util;

namespace Quartz
{
	/// <summary>
	/// Holds context/environment data that can be made available to Jobs as they
	/// are executed. 
	/// </summary>
	/// <seealso cref="IScheduler.Context" />
	/// <author>James House</author>
	[Serializable]
	public class SchedulerContext : DirtyFlagMap
	{
		/// <summary> <p>
		/// Tell the <see cref="SchedulerContext" /> that it should allow non-
		/// <see cref="Serializable" /> data.
		/// </p>
		/// 
		/// <p>
		/// Future versions of Quartz may make distinctions on how it propogates
		/// data in the SchedulerContext between instances of proxies to a single
		/// scheduler instance - i.e. if Quartz is being used via RMI.
		/// </p>
		/// </summary>
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

		/// <summary> <p>
		/// Create an empty <see cref="JobDataMap" />.
		/// </p>
		/// </summary>
		public SchedulerContext() : base(15)
		{
		}

		/// <summary> <p>
		/// Create a <see cref="JobDataMap" /> with the given data.
		/// </p>
		/// </summary>
		public SchedulerContext(IDictionary map) : this()
		{
			PutAll(map);
		}


		/// <summary>
		/// Determines whether this context contains transient data.
		/// </summary>
		/// <returns>
		/// 	<c>true</c> if instance contains transient data; otherwise, <c>false</c>.
		/// </returns>
		public virtual bool ContainsTransientData()
		{
			if (!AllowsTransientData)
			{
				// short circuit...
				return false;
			}

			foreach (object key in Keys)
			{
				object o = base[key];
				if (!(o is ISerializable))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary> <p>
		/// Nulls-out any data values that are non-Serializable.
		/// </p>
		/// </summary>
		public virtual void RemoveTransientData()
		{
			if (!AllowsTransientData)
			{
				// short circuit...
				return;
			}
			foreach (object key in Keys)
			{
				object o = base[key];
				if (!(o is ISerializable))
				{
					Remove(key);
				}
			}
		}

		/// <summary> <p>
		/// Adds the name-value pairs in the given <see cref="Map" /> to the <see cref="SchedulerContext" />.
		/// </p>
		/// 
		/// <p>
		/// All keys must be <see cref="String" />s.
		/// </p>
		/// </summary>
		public override void PutAll(IDictionary map)
		{
			foreach (DictionaryEntry entry in map)
			{
				Put(entry.Key, entry.Value);
				// will throw IllegalArgumentException if value not serializable
			}
		}

		/// <summary> <p>
		/// Adds the given <see cref="int" /> value to the <see cref="SchedulerContext" />.
		/// </p>
		/// </summary>
		public virtual void Put(string key, int value_Renamed)
		{
			base.Put(key, value_Renamed);
		}

		/// <summary> <p>
		/// Adds the given <see cref="long" /> value to the <see cref="SchedulerContext" />.
		/// </p>
		/// </summary>
		public virtual void Put(string key, long value_Renamed)
		{
			base.Put(key, value_Renamed);
		}

		/// <summary> <p>
		/// Adds the given <see cref="float" /> value to the <see cref="SchedulerContext" />.
		/// </p>
		/// </summary>
		public virtual void Put(string key, float value_Renamed)
		{
			base.Put(key, value_Renamed);
		}

		/// <summary> <p>
		/// Adds the given <see cref="double" /> value to the <see cref="SchedulerContext" />.
		/// </p>
		/// </summary>
		public virtual void Put(string key, double value_Renamed)
		{
			base.Put(key, value_Renamed);
		}

		/// <summary> <p>
		/// Adds the given <see cref="boolean" /> value to the <see cref="SchedulerContext" />.
		/// </p>
		/// </summary>
		public virtual void Put(string key, bool value_Renamed)
		{
			base.Put(key, value_Renamed);
		}

		/// <summary> <p>
		/// Adds the given <see cref="char" /> value to the <see cref="SchedulerContext" />.
		/// </p>
		/// </summary>
		public virtual void Put(string key, char value_Renamed)
		{
			base.Put(key, value_Renamed);
		}

		/// <summary> <p>
		/// Adds the given <see cref="String" /> value to the <see cref="SchedulerContext" />.
		/// </p>
		/// </summary>
		public virtual void Put(string key, string value_Renamed)
		{
			base.Put(key, value_Renamed);
		}

		/// <summary> <p>
		/// Adds the given <see cref="object" /> value to the <see cref="SchedulerContext" />.
		/// </p>
		/// </summary>
		public override object Put(object key, object value_Renamed)
		{
			if (!(key is string))
			{
				throw new ArgumentException("Keys in map must be Strings.");
			}

			return base.Put(key, value_Renamed);
		}

		/// <summary> <p>
		/// Retrieve the identified <see cref="int" /> value from the <see cref="SchedulerContext" />.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  ClassCastException </throws>
		/// <summary>           if the identified object is not an Integer.
		/// </summary>
		public virtual int GetInt(string key)
		{
			object obj = this[key];

			try
			{
				return ((Int32) obj);
			}
			catch (Exception)
			{
				throw new InvalidCastException("Identified object is not an Integer.");
			}
		}

		/// <summary> <p>
		/// Retrieve the identified <see cref="long" /> value from the <see cref="SchedulerContext" />.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  ClassCastException </throws>
		/// <summary>           if the identified object is not a Long.
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

		/// <summary> <p>
		/// Retrieve the identified <see cref="float" /> value from the <see cref="SchedulerContext" />.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  ClassCastException </throws>
		/// <summary>           if the identified object is not a Float.
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

		/// <summary> <p>
		/// Retrieve the identified <see cref="double" /> value from the <see cref="SchedulerContext" />.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  ClassCastException </throws>
		/// <summary>           if the identified object is not a Double.
		/// </summary>
		public virtual double GetDouble(string key)
		{
			object obj = this[key];

			try
			{
				return (double) obj;
			}
			catch (Exception)
			{
				throw new InvalidCastException("Identified object is not a Double.");
			}
		}

		/// <summary> <p>
		/// Retrieve the identified <see cref="boolean" /> value from the <see cref="SchedulerContext" />.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  ClassCastException </throws>
		/// <summary>           if the identified object is not a Boolean.
		/// </summary>
		public virtual bool GetBoolean(string key)
		{
			object obj = this[key];

			try
			{
				return ((Boolean) obj);
			}
			catch (Exception)
			{
				throw new InvalidCastException("Identified object is not a Boolean.");
			}
		}

		/// <summary> <p>
		/// Retrieve the identified <see cref="char" /> value from the <see cref="SchedulerContext" />.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  ClassCastException </throws>
		/// <summary>           if the identified object is not a Character.
		/// </summary>
		public virtual char GetChar(string key)
		{
			object obj = this[key];

			try
			{
				return (char) obj;
			}
			catch (Exception)
			{
				throw new InvalidCastException("Identified object is not a Character.");
			}
		}

		/// <summary> <p>
		/// Retrieve the identified <see cref="String" /> value from the <see cref="SchedulerContext" />.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  ClassCastException </throws>
		/// <summary>           if the identified object is not a String.
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
	}
}