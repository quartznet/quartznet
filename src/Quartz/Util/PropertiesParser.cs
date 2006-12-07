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
		public virtual NameValueCollection UnderlyingProperties
		{
			get { return props; }
		}

		internal NameValueCollection props = null;

		public PropertiesParser(NameValueCollection props)
		{
			this.props = props;
		}

		public virtual string GetStringProperty(string name)
		{
			string val = props.Get(name);
			if (val == null)
			{
				return null;
			}
			return val.Trim();
		}

		public virtual string GetStringProperty(string name, string def)
		{
			string val = props[name] == null ? def : props[name];
			if (val == null)
			{
				return def;
			}
			val = val.Trim();
			if (val.Length == 0)
			{
				return def;
			}
			return val;
		}

		public virtual string[] GetStringArrayProperty(string name)
		{
			return GetStringArrayProperty(name, null);
		}

		public virtual string[] GetStringArrayProperty(string name, string[] def)
		{
			string vals = GetStringProperty(name);
			if (vals == null)
			{
				return def;
			}

			if (vals != null && !vals.Trim().Equals(""))
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
					return def;
				}
			}

			return def;
		}

		public virtual bool GetBooleanProperty(string name)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return false;
			}

			return val.ToUpper().Equals("TRUE");
		}

		public virtual bool GetBooleanProperty(string name, bool def)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return def;
			}

			return val.ToUpper().Equals("TRUE");
		}

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

		public virtual sbyte GetByteProperty(string name, sbyte def)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return def;
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

		public virtual char GetCharProperty(string name, char def)
		{
			string param = GetStringProperty(name);
			if (param == null)
			{
				return def;
			}

			if (param.Length == 0)
			{
				return def;
			}

			return param[0];
		}

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

		public virtual double GetDoubleProperty(string name, double def)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return def;
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

		public virtual float GetFloatProperty(string name, float def)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return def;
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

		public virtual int GetIntProperty(string name, int def)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return def;
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

		public virtual int[] GetIntArrayProperty(string name)
		{
			return GetIntArrayProperty(name, null);
		}

		public virtual int[] GetIntArrayProperty(string name, int[] def)
		{
			string vals = GetStringProperty(name);
			if (vals == null)
			{
				return def;
			}

			if (vals != null && !vals.Trim().Equals(""))
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
					return def;
				}
			}

			return def;
		}

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

		public virtual short GetShortProperty(string name, short def)
		{
			string val = GetStringProperty(name);
			if (val == null)
			{
				return def;
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

		public virtual string[] GetPropertyGroups(string prefix)
		{
			Hashtable groups = new Hashtable(10);

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

		public virtual NameValueCollection GetPropertyGroup(string prefix)
		{
			return GetPropertyGroup(prefix, false);
		}

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