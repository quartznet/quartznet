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
using System.Collections.Specialized;

namespace Quartz.Util
{
	/// <summary>
	/// This is an utility class used to parse the properties.
	/// </summary>
	/// <author> James House</author>
	public class PropertiesParser
	{
        /// <summary>
        /// Gets the underlying properties.
        /// </summary>
        /// <value>The underlying properties.</value>
		public virtual NameValueCollection UnderlyingProperties
		{
			get { return props; }
		}

		internal NameValueCollection props = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="PropertiesParser"/> class.
        /// </summary>
        /// <param name="props">The props.</param>
		public PropertiesParser(NameValueCollection props)
		{
			this.props = props;
		}

        /// <summary>
        /// Gets the string property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
		public virtual string GetStringProperty(string name)
		{
			string val = props.Get(name);
			if (val == null)
			{
				return null;
			}
			return val.Trim();
		}

        /// <summary>
        /// Gets the string property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="defaultValue">The default value.</param>
        /// <returns></returns>
		public virtual string GetStringProperty(string name, string defaultValue)
		{
			string val = props[name] == null ? defaultValue : props[name];
			if (val == null)
			{
				return defaultValue;
			}
			val = val.Trim();
			if (val.Length == 0)
			{
				return defaultValue;
			}
			return val;
		}

        /// <summary>
        /// Gets the string array property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
		public virtual string[] GetStringArrayProperty(string name)
		{
			return GetStringArrayProperty(name, null);
		}

        /// <summary>
        /// Gets the string array property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="defaultValue">The default value.</param>
        /// <returns></returns>
		public virtual string[] GetStringArrayProperty(string name, string[] defaultValue)
		{
			string vals = GetStringProperty(name);
			if (vals == null)
			{
				return defaultValue;
			}

			if (!vals.Trim().Equals(""))
			{
				string[] stok = vals.Split(',');
				ArrayList strs = ArrayList.Synchronized(new ArrayList(10));
				try
				{
					foreach (string s in stok)
					{
						strs.Add(s);
					}
					string[] outStrs = new string[strs.Count];
					for (int i = 0; i < strs.Count; i++)
					{
						outStrs[i] = ((string) strs[i]);
					}
					return outStrs;
				}
				catch (Exception)
				{
					return defaultValue;
				}
			}

			return defaultValue;
		}

        /// <summary>
        /// Gets the boolean property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
		public virtual bool GetBooleanProperty(string name)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return false;
			}

			return val.ToUpper().Equals("TRUE");
		}

        /// <summary>
        /// Gets the boolean property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="defaultValue">if set to <c>true</c> [defaultValue].</param>
        /// <returns></returns>
		public virtual bool GetBooleanProperty(string name, bool defaultValue)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return defaultValue;
			}

			return val.ToUpper().Equals("TRUE");
		}

        /// <summary>
        /// Gets the byte property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
		public virtual sbyte GetByteProperty(string name)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				throw new FormatException(" null string");
			}

			try
			{
				return SByte.Parse(val);
			}
			catch (FormatException)
			{
				throw new FormatException(" '" + val + "'");
			}
		}

        /// <summary>
        /// Gets the byte property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="defaultValue">The default value.</param>
        /// <returns></returns>
		public virtual sbyte GetByteProperty(string name, sbyte defaultValue)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
			    return defaultValue;
			}

			try
			{
				return SByte.Parse(val);
			}
			catch (FormatException)
			{
				throw new FormatException(" '" + val + "'");
			}
		}

        /// <summary>
        /// Gets the char property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
		public virtual char GetCharProperty(string name)
		{
			string param = GetStringProperty(name);
			if (param == null)
			{
				return '\x0000';
			}

			if (param.Length == 0)
			{
				return '\x0000';
			}

			return param[0];
		}

        /// <summary>
        /// Gets the char property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="defaultValue">The default value.</param>
        /// <returns></returns>
		public virtual char GetCharProperty(string name, char defaultValue)
		{
			string param = GetStringProperty(name);
			if (param == null)
			{
				return defaultValue;
			}

			if (param.Length == 0)
			{
				return defaultValue;
			}

			return param[0];
		}

        /// <summary>
        /// Gets the double property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
		public virtual double GetDoubleProperty(string name)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				throw new FormatException(" null string");
			}

			try
			{
				return Double.Parse(val);
			}
			catch (FormatException)
			{
				throw new FormatException(" '" + val + "'");
			}
		}

        /// <summary>
        /// Gets the double property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="defaultValue">The default value.</param>
        /// <returns></returns>
		public virtual double GetDoubleProperty(string name, double defaultValue)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return defaultValue;
			}

			try
			{
				return Double.Parse(val);
			}
			catch (FormatException)
			{
				throw new FormatException(" '" + val + "'");
			}
		}

        /// <summary>
        /// Gets the float property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
		public virtual float GetFloatProperty(string name)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				throw new FormatException(" null string");
			}

			try
			{
				return Single.Parse(val);
			}
			catch (FormatException)
			{
				throw new FormatException(" '" + val + "'");
			}
		}

        /// <summary>
        /// Gets the float property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="defaultValue">The default value.</param>
        /// <returns></returns>
		public virtual float GetFloatProperty(string name, float defaultValue)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return defaultValue;
			}

			try
			{
				return Single.Parse(val);
			}
			catch (FormatException)
			{
				throw new FormatException(" '" + val + "'");
			}
		}

        /// <summary>
        /// Gets the int property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
		public virtual int GetIntProperty(string name)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				throw new FormatException(" null string");
			}

			try
			{
				return Int32.Parse(val);
			}
			catch (FormatException)
			{
				throw new FormatException(" '" + val + "'");
			}
		}

        /// <summary>
        /// Gets the int property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="defaultValue">The default value.</param>
        /// <returns></returns>
		public virtual int GetIntProperty(string name, int defaultValue)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return defaultValue;
			}

			try
			{
				return Int32.Parse(val);
			}
			catch (FormatException)
			{
				throw new FormatException(" '" + val + "'");
			}
		}

        /// <summary>
        /// Gets the int array property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
		public virtual int[] GetIntArrayProperty(string name)
		{
			return GetIntArrayProperty(name, null);
		}

        /// <summary>
        /// Gets the int array property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="defaultValue">The default value.</param>
        /// <returns></returns>
		public virtual int[] GetIntArrayProperty(string name, int[] defaultValue)
		{
			string vals = GetStringProperty(name);
			if (vals == null)
			{
				return defaultValue;
			}

			if (!vals.Trim().Equals(""))
			{
				string[] stok = vals.Split(',');
				ArrayList ints = ArrayList.Synchronized(new ArrayList(10));
				try
				{
					foreach (string s in stok)
					{
						try
						{
							ints.Add(Int32.Parse(s));
						}
						catch (FormatException)
						{
							throw new FormatException(" '" + vals + "'");
						}
					}
					int[] outInts = new int[ints.Count];
					for (int i = 0; i < ints.Count; i++)
					{
						outInts[i] = ((Int32) ints[i]);
					}
					return outInts;
				}
				catch (Exception)
				{
					return defaultValue;
				}
			}

			return defaultValue;
		}

        /// <summary>
        /// Gets the long property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
		public virtual long GetLongProperty(string name)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				throw new FormatException(" null string");
			}

			try
			{
				return Int64.Parse(val);
			}
			catch (FormatException)
			{
				throw new FormatException(" '" + val + "'");
			}
		}

        /// <summary>
        /// Gets the long property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="def">The def.</param>
        /// <returns></returns>
		public virtual long GetLongProperty(string name, long def)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return def;
			}

			try
			{
				return Int64.Parse(val);
			}
			catch (FormatException)
			{
				throw new FormatException(" '" + val + "'");
			}
		}

        /// <summary>
        /// Gets the short property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
		public virtual short GetShortProperty(string name)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				throw new FormatException(" null string");
			}

			try
			{
				return Int16.Parse(val);
			}
			catch (FormatException)
			{
				throw new FormatException(" '" + val + "'");
			}
		}

        /// <summary>
        /// Gets the short property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="defaultValue">The default value.</param>
        /// <returns></returns>
		public virtual short GetShortProperty(string name, short defaultValue)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return defaultValue;
			}

			try
			{
				return Int16.Parse(val);
			}
			catch (FormatException)
			{
				throw new FormatException(" '" + val + "'");
			}
		}

        /// <summary>
        /// Gets the property groups.
        /// </summary>
        /// <param name="prefix">The prefix.</param>
        /// <returns></returns>
		public virtual string[] GetPropertyGroups(string prefix)
		{
			IDictionary groups = new Hashtable(10);

			if (!prefix.EndsWith("."))
			{
				prefix += ".";
			}

			foreach (string key in props.Keys)
			{
				if (key.StartsWith(prefix))
				{
					string groupName = key.Substring(prefix.Length, (key.IndexOf('.', prefix.Length)) - (prefix.Length));
					groups[groupName] = groupName;
				}
			}

			ArrayList a = new ArrayList(groups.Values);
			return (string[]) a.ToArray(typeof (string));
		}

        /// <summary>
        /// Gets the property group.
        /// </summary>
        /// <param name="prefix">The prefix.</param>
        /// <returns></returns>
		public virtual NameValueCollection GetPropertyGroup(string prefix)
		{
			return GetPropertyGroup(prefix, false);
		}

        /// <summary>
        /// Gets the property group.
        /// </summary>
        /// <param name="prefix">The prefix.</param>
        /// <param name="stripPrefix">if set to <c>true</c> [strip prefix].</param>
        /// <returns></returns>
		public virtual NameValueCollection GetPropertyGroup(string prefix, bool stripPrefix)
		{
			NameValueCollection group = new NameValueCollection();

			if (!prefix.EndsWith("."))
			{
				prefix += ".";
			}

			foreach (string key in props.Keys)
			{
				if (key.StartsWith(prefix))
				{
					if (stripPrefix)
					{
						group[key.Substring(prefix.Length)] = props.Get(key);
					}
					else
					{
						group[key] = props.Get(key);
					}
				}
			}

			return group;
		}
	}
}