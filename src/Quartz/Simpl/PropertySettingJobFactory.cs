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

using System.Reflection;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Simpl;

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
    private readonly ILogger<PropertySettingJobFactory> logger;

    public PropertySettingJobFactory()
    {
        logger = LogProvider.CreateLogger<PropertySettingJobFactory>();
    }

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
        IJob job = InstantiateJob(bundle, scheduler);

        var jobDataMap = BuildJobDataMap(bundle, scheduler);

        if (jobDataMap.Count > 0)
        {
            SetObjectProperties(job, jobDataMap);
        }

        return job;
    }

    protected virtual JobDataMap BuildJobDataMap(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        var capacity = scheduler.Context.Count + bundle.JobDetail.JobDataMap.Count + bundle.Trigger.JobDataMap.Count;
        JobDataMap jobDataMap = new JobDataMap(capacity);
        if (capacity == 0)
        {
            return jobDataMap;
        }

        jobDataMap.PutAll(scheduler.Context);
        jobDataMap.PutAll(bundle.JobDetail.JobDataMap);
        jobDataMap.PutAll(bundle.Trigger.JobDataMap);
        return jobDataMap;
    }

    protected virtual IJob InstantiateJob(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        return base.NewJob(bundle, scheduler);
    }

    /// <summary>
    /// Sets the object properties.
    /// </summary>
    /// <param name="obj">The object to set properties to.</param>
    /// <param name="data">The data to set.</param>
    public virtual void SetObjectProperties(object obj, JobDataMap data)
    {
        if (obj is IJobWrapper jobWrapper)
        {
            SetObjectProperties(jobWrapper.Target, data);
        }
        else
        {
            foreach (string name in data.Keys)
            {
                SetObjectProperty(obj, name, data[name]);
            }
        }
    }

    /// <summary>
    /// Sets specific property to object, handles conversion and error conditions.
    /// </summary>
    /// <param name="job">Job instance to set property value to.</param>
    /// <param name="name">Property name to set.</param>
    /// <param name="value">Value to set.</param>
    protected virtual void SetObjectProperty(object job, string name, object? value)
    {
        string propName = name;
        if (!char.IsUpper(name[0]))
        {
            var c = char.ToUpper(name[0]);
            propName = c + name.Substring(1);
        }

        var o = value;
        var prop = job.GetType().GetProperty(propName);

        Type? paramType = null;
        try
        {
            if (prop is null)
            {
                HandleError($"No property on Job class {job.GetType()} for property '{name}'");
                return;
            }

            paramType = prop.PropertyType;

            if (o is null && (paramType.IsPrimitive || paramType.IsEnum))
            {
                // cannot set null to these
                HandleError($"Cannot set null to property on Job class {job.GetType()} for property '{name}'");
            }

            if (paramType == typeof(char) && o is string s && s.Length != 1)
            {
                // handle special case
                HandleError($"Cannot set empty string to char property on Job class {job.GetType()} for property '{name}'");
            }

            var goodValue = paramType == typeof(TimeSpan)
                ? ObjectUtils.GetTimeSpanValueForProperty(prop, o)
                : ConvertValueIfNecessary(paramType, o);

            prop.GetSetMethod()!.Invoke(job, new[] { goodValue });
        }
        catch (FormatException nfe)
        {
            HandleError($"The setter on Job class {job.GetType()} for property '{name}' expects a {paramType} but was given {o}", nfe);
        }
        catch (MethodAccessException)
        {
            HandleError($"The setter on Job class {job.GetType()} for property '{name}' expects a {paramType} but was given a {o?.GetType()}");
        }
        catch (ArgumentException e)
        {
            HandleError($"The setter on Job class {job.GetType()} for property '{name}' expects a {paramType} but was given {o?.GetType()}", e);
        }
        catch (UnauthorizedAccessException e)
        {
            HandleError($"The setter on Job class {job.GetType()} for property '{name}' could not be accessed.", e);
        }
        catch (TargetInvocationException e)
        {
            HandleError($"The setter on Job class {job.GetType()} for property '{name}' could not be accessed.", e);
        }
        catch (Exception e)
        {
            HandleError($"The setter on Job class {job.GetType()} for property '{name}' threw exception when processing.", e);
        }
    }

    protected virtual object? ConvertValueIfNecessary(Type requiredType, object? newValue)
    {
        return ObjectUtils.ConvertValueIfNecessary(requiredType, newValue);
    }

    private void HandleError(string message, Exception? e = null)
    {
        if (ThrowIfPropertyNotFound)
        {
            ThrowHelper.ThrowSchedulerException(message, e);
        }

        if (WarnIfPropertyNotFound)
        {
#pragma warning disable CA2254
            if (e is null)
            {
                logger.LogWarning(message);
            }
            else
            {
                logger.LogWarning(e, message);
            }
#pragma warning restore CA2254
        }
    }
}