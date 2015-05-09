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
using System.Globalization;
using System.Reflection;

using Common.Logging;

using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Simpl
{
	/// <summary> 
	/// A JobFactory that instantiates the Job instance (using the default no-arg
	/// constructor, or more specifically: <see cref="ObjectUtils.InstantiateType{T}" />), and
	/// then attempts to set all values from the <see cref="IJobExecutionContext" /> and
	/// the <see cref="IJobExecutionContext" />'s merged <see cref="JobDataMap" /> onto 
	/// properties of the job.
	/// </summary>
    /// <remarks>   
    /// Set the WarnIfPropertyNotFound property to true if you'd like noisy logging in
    /// the case of values in the <see cref="JobDataMap" /> not mapping to properties on your job
    /// class. This may be useful for troubleshooting typos of property names, etc.
    /// but very noisy if you regularly (and purposely) have extra things in your
    ///  <see cref="JobDataMap" />.
    /// Also of possible interest is the ThrowIfPropertyNotFound property which
    /// will throw exceptions on unmatched JobDataMap keys.
    /// </remarks>
 	/// <seealso cref="IJobFactory" />
	/// <seealso cref="SimpleJobFactory" />
	/// <seealso cref="SchedulerContext"/>
	/// <seealso cref="IJobExecutionContext.MergedJobDataMap" />
	/// <seealso cref="WarnIfPropertyNotFound" />
	/// <seealso cref="ThrowIfPropertyNotFound" />
	/// <author>James Houser</author>
	/// <author>Marko Lahma (.NET)</author>
	public class PropertySettingJobFactory : SimpleJobFactory
	{
	    private static readonly ILog log = LogManager.GetLogger(typeof(PropertySettingJobFactory));

	    /// <summary> 
	    /// Whether the JobInstantiation should fail and throw and exception if
	    /// a key (name) and value (type) found in the JobDataMap does not 
	    /// correspond to a property setter on the Job class.
	    /// </summary>
	    public virtual bool ThrowIfPropertyNotFound { get; set; }

	    /// <summary> 
	    /// Get or set whether a warning should be logged if
	    /// a key (name) and value (type) found in the JobDataMap does not 
	    /// correspond to a property setter on the Job class.
	    /// </summary>
	    public virtual bool WarnIfPropertyNotFound { get; set; }

	    /// <summary>
	    /// Called by the scheduler at the time of the trigger firing, in order to
	    /// produce a <see cref="IJob" /> instance on which to call Execute.
	    /// </summary>
	    /// <remarks>
	    /// <para>
	    /// It should be extremely rare for this method to throw an exception -
	    /// basically only the case where there is no way at all to instantiate
	    /// and prepare the Job for execution.  When the exception is thrown, the
	    /// Scheduler will move all triggers associated with the Job into the
	    /// <see cref="TriggerState.Error" /> state, which will require human
	    /// intervention (e.g. an application restart after fixing whatever
	    /// configuration problem led to the issue with instantiating the Job).
	    /// </para>
	    /// </remarks>
	    /// <param name="bundle">The TriggerFiredBundle from which the <see cref="IJobDetail" />
	    ///   and other info relating to the trigger firing can be obtained.</param>
	    /// <param name="scheduler"></param>
	    /// <returns>the newly instantiated Job</returns>
	    /// <throws>  SchedulerException if there is a problem instantiating the Job. </throws>
	    public override IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
		{
			IJob job = base.NewJob(bundle, scheduler);

			JobDataMap jobDataMap = new JobDataMap();
            jobDataMap.PutAll(scheduler.Context);
			jobDataMap.PutAll(bundle.JobDetail.JobDataMap);
			jobDataMap.PutAll(bundle.Trigger.JobDataMap);

			SetObjectProperties(job, jobDataMap);

			return job;
		}

        /// <summary>
        /// Sets the object properties.
        /// </summary>
        /// <param name="obj">The object to set properties to.</param>
        /// <param name="data">The data to set.</param>
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
						HandleError(string.Format(CultureInfo.InvariantCulture, "No property on Job class {0} for property '{1}'", obj.GetType(), name));
						continue;
					}

					paramType = prop.PropertyType;

					if (o == null && (paramType.IsPrimitive || paramType.IsEnum))
					{
						// cannot set null to these
						HandleError(string.Format(CultureInfo.InvariantCulture, "Cannot set null to property on Job class {0} for property '{1}'", obj.GetType(), name));
					}
					if (paramType == typeof(char) && o is string && ((string) o).Length != 1)
					{
						// handle special case
						HandleError(string.Format(CultureInfo.InvariantCulture, "Cannot set empty string to char property on Job class {0} for property '{1}'", obj.GetType(), name));
					}

                    object goodValue = paramType == typeof (TimeSpan)
										   ? ObjectUtils.GetTimeSpanValueForProperty(prop, o)
										   : ConvertValueIfNecessary(paramType, o);

					prop.GetSetMethod().Invoke(obj, new object[] {goodValue});
				}
				catch (FormatException nfe)
				{
					HandleError(
							string.Format(CultureInfo.InvariantCulture, "The setter on Job class {0} for property '{1}' expects a {2} but was given {3}", obj.GetType(), name, paramType, o), nfe);
				}
				catch (MethodAccessException)
				{
                    HandleError(string.Format(CultureInfo.InvariantCulture, "The setter on Job class {0} for property '{1}' expects a {2} but was given a {3}", obj.GetType(), name, paramType, o.GetType()));
				}
				catch (ArgumentException e)
				{
					HandleError(
							string.Format(CultureInfo.InvariantCulture, "The setter on Job class {0} for property '{1}' expects a {2} but was given {3}", obj.GetType(), name, paramType, o.GetType()), e);
				}
				catch (UnauthorizedAccessException e)
				{
					HandleError(
							string.Format(CultureInfo.InvariantCulture, "The setter on Job class {0} for property '{1}' could not be accessed.", obj.GetType(), name), e);
				}
				catch (TargetInvocationException e)
				{
					HandleError(
							string.Format(CultureInfo.InvariantCulture, "The setter on Job class {0} for property '{1}' could not be accessed.", obj.GetType(), name), e);
				}
                catch (Exception e)
                {
                    HandleError(
                            string.Format(CultureInfo.InvariantCulture, "The setter on Job class {0} for property '{1}' threw exception when processing.", obj.GetType(), name), e);
                }
			}
		}

	    protected virtual object ConvertValueIfNecessary(Type requiredType, object newValue)
	    {
	        return ObjectUtils.ConvertValueIfNecessary(requiredType, newValue);
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
					log.Warn(message);
				}
				else
				{
					log.Warn(message, e);
				}
			}
		}
	}
}
