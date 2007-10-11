#region License

/*
 * Copyright 2002-2004 the original author or authors.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#endregion

using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace Quartz.Util
{
	/// <summary>
	/// Utility methods that are used to convert objects from one type into another.
	/// </summary>
	/// <author>Aleksandar Seovic</author>
	public class ObjectUtils
	{
		/// <summary>
		/// Convert the value to the required <see cref="System.Type"/> (if necessary from a string).
		/// </summary>
		/// <param name="newValue">The proposed change value.</param>
		/// <param name="requiredType">
		/// The <see cref="System.Type"/> we must convert to.
		/// </param>
		/// <returns>The new value, possibly the result of type conversion.</returns>
		public static object ConvertValueIfNecessary(Type requiredType, object newValue)
		{
			if (newValue != null)
			{
				// if it is assignable, return the value right away
				if (IsAssignableFrom(newValue, requiredType))
				{
					return newValue;
				}

				
				// try to convert using type converter
			    TypeConverter typeConverter = TypeDescriptor.GetConverter(requiredType);
			    if (typeConverter.CanConvertFrom(newValue.GetType()))
			    {
				    newValue = typeConverter.ConvertFrom(null, CultureInfo.InvariantCulture, newValue);
			    }
			    if (requiredType == typeof(int) && newValue.GetType() == typeof(long))
			    {
				    // automatically doesn't work, try with converter
				    newValue = Convert.ToInt32(newValue);
			    }
			    else if (requiredType == typeof(short) && (newValue.GetType() == typeof(int) || newValue.GetType() == typeof(long)))
			    {
				    // automatically doesn't work, try with converter
				    newValue = Convert.ToInt16(newValue);
			    }
			    else if (requiredType == typeof(byte) && (newValue.GetType() == typeof(short) || newValue.GetType() == typeof(int) || newValue.GetType() == typeof(long)))
			    {
				    // automatically doesn't work, try with converter
				    newValue = Convert.ToByte(newValue);
			    }
                else if (newValue != null && requiredType == typeof(Type))
                {
                    Type t = Type.GetType(newValue.ToString());
                    if (t == null)
                    {
                        throw new ArgumentException("Unable to load type '" + newValue + "', incorrect type or missing assembly reference");
                    }
                    newValue = t;
                }
			}
			return newValue;
		}


		/// <summary>
		/// Determines whether value is assignable to required type.
		/// </summary>
		/// <param name="value">The value to check.</param>
		/// <param name="requiredType">Type of the required.</param>
		/// <returns>
		/// 	<c>true</c> if value can be assigned as given type; otherwise, <c>false</c>.
		/// </returns>
		private static bool IsAssignableFrom(object value, Type requiredType)
		{
			return requiredType.IsAssignableFrom(value.GetType());
		}
		
		/// <summary>
		/// Instantiates an instance of the type specified.
		/// </summary>
		/// <param name="t">The type to instantiate.</param>
		/// <returns></returns>
		public static object InstantiateType(Type t)
		{
			ConstructorInfo ci = t.GetConstructor(Type.EmptyTypes);
			if (ci == null)
			{
				throw new ArgumentException("Cannot instantiate type which has no empty constructor", t.Name);
			}
			return ci.Invoke(new object[0]);
		}

		
		/// <summary>
		/// Sets the object properties using reflection.
		/// </summary>
		/// <param name="obj">The object to set values to.</param>
		/// <param name="props">The properties to set to object.</param>
		public static void SetObjectProperties(object obj, NameValueCollection props)
		{
            // remove the type
			props.Remove("type");
			Type t = obj.GetType();

			foreach (string name in props.Keys)
			{
				string propertyName = name.Substring(0, 1).ToUpper(CultureInfo.InvariantCulture) + name.Substring(1);

				PropertyInfo pi = t.GetProperty(propertyName);

				try
				{
					if (pi == null)
					{
						throw new MethodAccessException(string.Format("No property '{0}'", propertyName));
					}
					
					MethodInfo mi = pi.GetSetMethod();

					object value = props[name];
					value = ConvertValueIfNecessary(mi.GetParameters()[0].ParameterType, value);

					mi.Invoke(obj, new object[] {value});

				}
				catch (Exception nfe)
				{
					throw new SchedulerConfigException(string.Format("Could not parse property '{0}' into correct data type: {1}", name, nfe.Message    ), nfe);
				}
			}
			
		}

	}

}
