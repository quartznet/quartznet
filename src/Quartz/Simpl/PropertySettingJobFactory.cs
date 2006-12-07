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
using System;
using log4net;
using Quartz.Spi;

namespace Quartz.Simpl
{
	/// <summary> A JobFactory that instantiates the Job instance (using the default no-arg
	/// constructor, or more specifically: <code>class.newInstance()</code>), and
	/// then attempts to set all values in the <code>JobExecutionContext</code>'s
	/// <code>JobDataMap</code> onto bean properties of the <code>Job</code>.
	/// 
	/// </summary>
	/// <seealso cref="IJobFactory">
	/// </seealso>
	/// <seealso cref="SimpleJobFactory">
	/// </seealso>
	/// <seealso cref="JobExecutionContext.MergedJobDataMap">
	/// </seealso>
	/// <seealso cref="WarnIfPropertyNotFound">
	/// </seealso>
	/// <seealso cref="ThrowIfPropertyNotFound">
	/// 
	/// </seealso>
	/// <author>James Houser</author>
	public class PropertySettingJobFactory : SimpleJobFactory
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof(PropertySettingJobFactory));

		/// <summary> 
		/// Whether the JobInstantiation should fail and throw and exception if
		/// a key (name) and value (type) found in the JobDataMap does not 
		/// correspond to a proptery setter on the Job class.
		/// </summary>
		public virtual bool ThrowIfPropertyNotFound
		{
			get { return throwIfNotFound; }

			set { throwIfNotFound = value; }
		}

		/// <summary> 
		/// Get or set whether a warning should be logged if
		/// a key (name) and value (type) found in the JobDataMap does not 
		/// correspond to a proptery setter on the Job class.
		/// </summary>
		public virtual bool WarnIfPropertyNotFound
		{
			get { return warnIfNotFound; }

			set { warnIfNotFound = value; }
		}

		private bool warnIfNotFound = true;
		private bool throwIfNotFound = false;

		public override IJob NewJob(TriggerFiredBundle bundle)
		{
			IJob job = base.NewJob(bundle);

			JobDataMap jobDataMap = new JobDataMap();
			jobDataMap.PutAll(bundle.JobDetail.JobDataMap);
			jobDataMap.PutAll(bundle.Trigger.JobDataMap);

			setBeanProps(job, jobDataMap);

			return job;
		}

		protected internal virtual void setBeanProps(object obj, JobDataMap data)
		{
			/* TODO
			//UPGRADE_ISSUE: Interface 'java.beans.BeanInfo' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javabeansBeanInfo_3"'
			BeanInfo bi = null;
			try
			{
				//UPGRADE_ISSUE: Method 'java.beans.Introspector.getBeanInfo' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javabeansIntrospector_3"'
				bi = Introspector.getBeanInfo(obj.GetType());
			}
				//UPGRADE_ISSUE: Class 'java.beans.IntrospectionException' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javabeansIntrospectionException_3"'
			catch (IntrospectionException e1)
			{
				if (ThrowIfPropertyNotFound)
				{
					throw new SchedulerException("Unable to introspect Job class.", e1);
				}
				if (WarnIfPropertyNotFound)
				{
					Log.Warn("Unable to introspect Job class.", e1);
				}
			}

			//UPGRADE_ISSUE: Method 'java.beans.BeanInfo.getPropertyDescriptors' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javabeansBeanInfo_3"'
			PropertyDescriptor[] propDescs = bi.getPropertyDescriptors();

			IEnumerator keys = data.KeySet().GetEnumerator();
			//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
			while (keys.MoveNext())
			{
				//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
				String name = (String) keys.Current;
				String c = name.Substring(0, (1) - (0)).ToUpper(new CultureInfo("en-US"));
				String methName = "set" + c + name.Substring(1);

				MethodInfo setMeth = getSetMethod(methName, propDescs);

				Type paramType = null;
				object o = null;

				try
				{
					if (setMeth == null)
					{
						if (ThrowIfPropertyNotFound)
						{
							throw new SchedulerException("No setter on Job class " + obj.GetType() + " for property '" + name + "'");
						}
						if (WarnIfPropertyNotFound)
						{
							Log.Warn("No setter on Job class " + obj.GetType() + " for property '" + name + "'");
						}
						continue;
					}

					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.reflect.Method.getParameterTypes' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					paramType = setMeth.GetParameters()[0];
					o = data[name];

					if (paramType.Equals(typeof (int)))
					{
						if (o is Int32)
						{
							setMeth.Invoke(obj, new object[] {o});
						}
						else if (o is String)
						{
							setMeth.Invoke(obj, new object[] {data.GetIntegerFromString(name)});
						}
					}
					else if (paramType.Equals(typeof (long)))
					{
						if (o is Int64)
						{
							setMeth.Invoke(obj, new object[] {o});
						}
						else if (o is String)
						{
							setMeth.Invoke(obj, new object[] {data.GetLongFromString(name)});
						}
					}
					else if (paramType.Equals(typeof (float)))
					{
						if (o is Single)
						{
							setMeth.Invoke(obj, new object[] {o});
						}
						else if (o is String)
						{
							setMeth.Invoke(obj, new object[] {data.GetFloatFromString(name)});
						}
					}
					else if (paramType.Equals(typeof (double)))
					{
						if (o is Double)
						{
							setMeth.Invoke(obj, new object[] {o});
						}
						else if (o is String)
						{
							setMeth.Invoke(obj, new object[] {data.GetDoubleFromString(name)});
						}
					}
					else if (paramType.Equals(typeof (bool)))
					{
						if (o is Boolean)
						{
							setMeth.Invoke(obj, new object[] {o});
						}
						else if (o is String)
						{
							setMeth.Invoke(obj, new object[] {data.GetBooleanFromString(name)});
						}
					}
					else if (paramType.Equals(typeof (String)))
					{
						if (o is String)
						{
							setMeth.Invoke(obj, new object[] {o});
						}
					}
					else
					{
						if (paramType.IsAssignableFrom(o.GetType()))
						{
							setMeth.Invoke(obj, (object[]) new object[] {o});
						}
						else
						{
							throw new MethodAccessException();
						}
					}
				}
				catch (FormatException nfe)
				{
					if (ThrowIfPropertyNotFound)
					{
						//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.object.toString' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
						throw new SchedulerException(
							"The setter on Job class " + obj.GetType() + " for property '" + name + "' expects a " + paramType +
							" but was given " + o, nfe);
					}
					if (WarnIfPropertyNotFound)
					{
						//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.object.toString' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
						Log.Warn(
							"The setter on Job class " + obj.GetType() + " for property '" + name + "' expects a " + paramType +
							" but was given " + o, nfe);
					}
					continue;
				}
				catch (MethodAccessException)
				{
					if (ThrowIfPropertyNotFound)
					{
						throw new SchedulerException("The setter on Job class " + obj.GetType() + " for property '" + name +
						                             "' expects a " + paramType + " but was given " + o.GetType());
					}
					if (WarnIfPropertyNotFound)
					{
						Log.Warn("The setter on Job class " + obj.GetType() + " for property '" + name + "' expects a " + paramType +
						         " but was given a " + o.GetType());
					}
					continue;
				}
				catch (ArgumentException e)
				{
					if (ThrowIfPropertyNotFound)
					{
						throw new SchedulerException(
							"The setter on Job class " + obj.GetType() + " for property '" + name + "' expects a " + paramType +
							" but was given " + o.GetType(), e);
					}
					if (WarnIfPropertyNotFound)
					{
						Log.Warn(
							"The setter on Job class " + obj.GetType() + " for property '" + name + "' expects a " + paramType +
							" but was given a " + o.GetType(), e);
					}
					continue;
				}
				catch (UnauthorizedAccessException e)
				{
					if (ThrowIfPropertyNotFound)
					{
						throw new SchedulerException(
							"The setter on Job class " + obj.GetType() + " for property '" + name + "' could not be accessed.", e);
					}
					if (WarnIfPropertyNotFound)
					{
						Log.Warn(
							"The setter on Job class " + obj.GetType() + " for property '" + name + "' expects a " + paramType +
							"' could not be accessed.", e);
					}
					continue;
				}
				catch (TargetInvocationException e)
				{
					if (ThrowIfPropertyNotFound)
					{
						throw new SchedulerException(
							"The setter on Job class " + obj.GetType() + " for property '" + name + "' could not be accessed.", e);
					}
					if (WarnIfPropertyNotFound)
					{
						Log.Warn(
							"The setter on Job class " + obj.GetType() + " for property '" + name + "' expects a " + paramType +
							"' could not be accessed.", e);
					}
					continue;
				}
			}
			*/
		}

/*
		private MethodInfo getSetMethod(String name, PropertyDescriptor[] props)
		{
			for (int i = 0; i < props.Length; i++)
			{
				//UPGRADE_ISSUE: Method 'java.beans.PropertyDescriptor.getWriteMethod' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javabeansPropertyDescriptorgetWriteMethod_3"'
				MethodInfo wMeth = null; // TODO props[i].getWriteMethod();

				if (wMeth == null)
				{
					continue;
				}

				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.reflect.Method.getParameterTypes' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				if (wMeth.GetParameters().Length != 1)
				{
					continue;
				}

				if (wMeth.Name.Equals(name))
				{
					return wMeth;
				}
			}

			return null;
		}
*/
	}
}