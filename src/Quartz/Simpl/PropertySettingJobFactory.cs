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
using System.Globalization;
using System.Reflection;

using Common.Logging;

using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Simpl
{
	/// <summary> 
	/// A JobFactory that instantiates the Job instance (using the default no-arg
	/// constructor, or more specifically: <see cref="Activator.CreateInstance(Type)" />), and
	/// then attempts to set all values in the <see cref="JobExecutionContext" />'s
	/// <see cref="JobDataMap" /> onto bean properties of the <see cref="IJob" />.
	/// </summary>
	/// <seealso cref="IJobFactory" />
	/// <seealso cref="SimpleJobFactory" />
	/// <seealso cref="JobExecutionContext.MergedJobDataMap" />
	/// <seealso cref="WarnIfPropertyNotFound" />
	/// <seealso cref="ThrowIfPropertyNotFound" />
	/// <author>James Houser</author>
	public class PropertySettingJobFactory : SimpleJobFactory
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (PropertySettingJobFactory));

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

		/// <summary>
		/// Called by the scheduler at the time of the trigger firing, in order to
		/// produce a <see cref="IJob" /> instance on which to call Execute.
		/// <p>
		/// It should be extremely rare for this method to throw an exception -
		/// basically only the the case where there is no way at all to instantiate
		/// and prepare the Job for execution.  When the exception is thrown, the
		/// Scheduler will move all triggers associated with the Job into the
		/// <see cref="Trigger.STATE_ERROR" /> state, which will require human
		/// intervention (e.g. an application restart after fixing whatever
		/// configuration problem led to the issue wih instantiating the Job.
		/// </p>
		/// </summary>
		/// <param name="bundle">The TriggerFiredBundle from which the <see cref="JobDetail" />
		/// and other info relating to the trigger firing can be obtained.</param>
		/// <returns>the newly instantiated Job</returns>
		/// <throws>  SchedulerException if there is a problem instantiating the Job. </throws>
		public override IJob NewJob(TriggerFiredBundle bundle)
		{
			IJob job = base.NewJob(bundle);

			JobDataMap jobDataMap = new JobDataMap();
			jobDataMap.PutAll(bundle.JobDetail.JobDataMap);
			jobDataMap.PutAll(bundle.Trigger.JobDataMap);

			SetObjectProperties(job, jobDataMap);

			return job;
		}

		public virtual void SetObjectProperties(object obj, JobDataMap data)
		{
			Type paramType = null;

			foreach (string name in data.Keys)
			{
				string c = name.Substring(0, 1).ToUpper(CultureInfo.InvariantCulture);
				string propName = c + name.Substring(1);

				object o = data[name];
				PropertyInfo prop = obj.GetType().GetProperty(propName);

				try
				{
					if (prop == null)
					{
						HandleError("No property on Job class " + obj.GetType() + " for property '" + name + "'");
						continue;
					}

					paramType = prop.PropertyType;

					if (o == null && (paramType.IsPrimitive || paramType.IsEnum))
					{
						// cannot set null to these
						HandleError("Cannot set null to property on Job class " + obj.GetType() + " for property '" + name + "'");
					}
					if (paramType == typeof(char) && o!= null && o is string && ((string) o).Length != 1)
					{
						// handle special case
						HandleError("Cannot set empty string to char property on Job class " + obj.GetType() + " for property '" + name + "'");
					}
					
					object goodValue = ObjectUtils.ConvertValueIfNecessary(paramType, o);
					prop.GetSetMethod().Invoke(obj, new object[] {goodValue});
				}
				catch (FormatException nfe)
				{
					HandleError(
							"The setter on Job class " + obj.GetType() + " for property '" + name + "' expects a " + paramType +
							" but was given " + o, nfe);

					continue;
				}
				catch (MethodAccessException)
				{
					HandleError("The setter on Job class " + obj.GetType() + " for property '" + name + "' expects a " + paramType +
						         " but was given a " + o.GetType());

					continue;
				}
				catch (ArgumentException e)
				{
					HandleError(
							"The setter on Job class " + obj.GetType() + " for property '" + name + "' expects a " + paramType +
							" but was given " + o.GetType(), e);

					continue;
				}
				catch (UnauthorizedAccessException e)
				{
					HandleError(
							"The setter on Job class " + obj.GetType() + " for property '" + name + "' could not be accessed.", e);
					continue;
				}
				catch (TargetInvocationException e)
				{
					HandleError(
							"The setter on Job class " + obj.GetType() + " for property '" + name + "' could not be accessed.", e);
					
					continue;
				}
			}
		}

		private void HandleError(string message)
		{
			HandleError(message, null);
		}

		private void HandleError(string message, Exception e)
		{
			if (ThrowIfPropertyNotFound)
			{
				throw new SchedulerException(message, e);
			}

			if (WarnIfPropertyNotFound)
			{
				if (e == null)
				{
					Log.Warn(message);
				}
				else
				{
					Log.Warn(message, e);
				}
			}
		}
	}
}