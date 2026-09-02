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

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;

using Microsoft.Extensions.Logging;

using Quartz.Configuration;
using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Impl;

/// <summary>
/// A JobFactory that instantiates the Job instance (using the default no-arg
/// constructor, or more specifically: <see cref="TypeActivator.Instantiate{T}" />), and
/// then attempts to set all values from the <see cref="IJobExecutionContext" />'s merged
/// <see cref="JobDataMap" /> onto properties of the job.
/// </summary>
/// <remarks>
/// By default an entry in the <see cref="JobDataMap" /> that does not map to a property on your job
/// class is ignored, because a map is allowed to carry values the job reads for itself. Set
/// <see cref="PropertyMismatchBehavior" /> to <see cref="Quartz.Impl.PropertyMismatchBehavior.Warn" />
/// to log those — useful for troubleshooting a misspelled property name, noisy if you regularly (and
/// purposely) have extra things in your map — or to
/// <see cref="Quartz.Impl.PropertyMismatchBehavior.Throw" /> to fail the instantiation outright.
/// </remarks>
/// <seealso cref="IJobFactory" />
/// <seealso cref="SimpleJobFactory" />
/// <seealso cref="SchedulerContext"/>
/// <seealso cref="IJobExecutionContext.MergedJobDataMap" />
/// <seealso cref="PropertyMismatchBehavior" />
/// <author>James Houser</author>
/// <author>Marko Lahma (.NET)</author>
public class PropertySettingJobFactory : SimpleJobFactory
{
    private readonly ILogger<PropertySettingJobFactory> logger;

    /// <inheritdoc cref="SimpleJobFactory(ILoggerFactory)" />
    public PropertySettingJobFactory(ILoggerFactory? loggerFactory = null) : base(loggerFactory)
    {
        logger = loggerFactory?.CreateLogger<PropertySettingJobFactory>()
            ?? LogProvider.CreateLogger<PropertySettingJobFactory>();
    }

    /// <summary>
    /// What happens when a key (name) and value (type) found in the <see cref="JobDataMap" /> does not
    /// correspond to a property setter on the job class. Defaults to
    /// <see cref="PropertyMismatchBehavior.Ignore" />.
    /// </summary>
    public virtual PropertyMismatchBehavior PropertyMismatchBehavior { get; set; }

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
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>the newly instantiated Job</returns>
    /// <remarks>
    /// Deliberately not an <c>async</c> method. An async state machine restores the caller's
    /// <see cref="System.Threading.ExecutionContext" /> when its synchronous part returns, which would
    /// discard any <see cref="System.Threading.AsyncLocal{T}" /> a factory sets while building the job —
    /// the ambient context that <see cref="MicrosoftDependencyInjectionJobFactory.ConfigureScope" />
    /// exists to establish, and that has to survive into <see cref="IJob.Execute" /> (#1528).
    /// </remarks>
    /// <exception cref="SchedulerException">
    /// The job could not be instantiated, or a value in the merged job data map does not match a
    /// settable property of the job and <see cref="PropertyMismatchBehavior" /> says to throw.
    /// </exception>
    public override ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        var creatingScope = CreateJobInstance(bundle, scheduler, cancellationToken);

        if (!creatingScope.IsCompletedSuccessfully)
        {
            return AwaitScope(this, creatingScope, bundle, scheduler, cancellationToken);
        }

        var scope = creatingScope.Result;

        try
        {
            ApplyProperties(scope, bundle, scheduler);
        }
        catch (Exception e)
        {
            // The job exists by now, and whatever the factory allocated to produce it is in the
            // scope's state. ReturnJob is not called when CreateJob throws, so hand it back here
            // rather than leaking it on every fire of a job whose data map does not match.
            return ReturnAndRethrow(this, scope, e, cancellationToken);
        }

        return new ValueTask<JobScope>(scope);

        static async ValueTask<JobScope> AwaitScope(
            PropertySettingJobFactory factory,
            ValueTask<JobScope> creatingScope,
            TriggerFiredBundle bundle,
            IScheduler scheduler,
            CancellationToken cancellationToken)
        {
            var scope = await creatingScope.ConfigureAwait(false);

            try
            {
                factory.ApplyProperties(scope, bundle, scheduler);
            }
            catch (Exception e)
            {
                return await ReturnAndRethrow(factory, scope, e, cancellationToken).ConfigureAwait(false);
            }

            return scope;
        }

        static async ValueTask<JobScope> ReturnAndRethrow(
            PropertySettingJobFactory factory,
            JobScope scope,
            Exception failure,
            CancellationToken cancellationToken)
        {
            try
            {
                await factory.ReturnJob(scope, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // Never let cleanup replace the failure that caused it.
                factory.logger.JobReturnAfterFailedCreationFailed(e);
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
            return default;
        }
    }

    private void ApplyProperties(JobScope scope, TriggerFiredBundle bundle, IScheduler scheduler)
    {
        var jobDataMap = BuildJobDataMap(bundle, scheduler);

        if (jobDataMap.Count > 0)
        {
            SetObjectProperties(scope.Job, jobDataMap);
        }
    }

    /// <summary>
    /// Builds the map whose entries are applied to the job's properties: the trigger's data merged
    /// over the job's.
    /// </summary>
    /// <remarks>
    /// Until 4.0 the scheduler context was merged in as well, underneath both. That injected every
    /// context entry into every fire — including the service-provider entry the DI integration seeds,
    /// which no job has a property for, so with <see cref="Quartz.Impl.PropertyMismatchBehavior.Throw" /> every
    /// container-hosted fire failed. A job that wants a context value reads
    /// <c>context.Scheduler.Context</c> in <see cref="IJob.Execute" />; a factory that wants the old
    /// behavior overrides this method, which is handed the scheduler for exactly that reason.
    /// </remarks>
    protected virtual JobDataMap BuildJobDataMap(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        var capacity = bundle.JobDetail.JobDataMap.Count + bundle.Trigger.JobDataMap.Count;
        JobDataMap jobDataMap = new JobDataMap(capacity);
        if (capacity == 0)
        {
            return jobDataMap;
        }

        foreach (var pair in bundle.JobDetail.JobDataMap)
        {
            jobDataMap[pair.Key] = pair.Value;
        }
        foreach (var pair in bundle.Trigger.JobDataMap)
        {
            jobDataMap[pair.Key] = pair.Value;
        }
        return jobDataMap;
    }

    /// <summary>
    /// Produces the job instance, before any <see cref="JobDataMap" /> properties are applied to it.
    /// </summary>
    /// <remarks>
    /// This is the extension point for derived factories that need to change how the job is built —
    /// resolving it from a container, for example — without overriding <see cref="CreateJob" /> and
    /// reimplementing the property setting this class exists to provide.
    /// <para>
    /// It returns a <see cref="ValueTask{TResult}" /> so an override <i>can</i> await, but prefer to
    /// keep the synchronous path synchronous: an <c>async</c> override puts the work inside a state
    /// machine, which restores the caller's <see cref="System.Threading.ExecutionContext" /> when its
    /// synchronous part returns and so discards any <see cref="System.Threading.AsyncLocal{T}" /> the
    /// override set. Ambient context established here has to survive into <see cref="IJob.Execute" />
    /// (#1528). Return a completed <see cref="ValueTask{TResult}" /> when nothing needs awaiting.
    /// </para>
    /// </remarks>
    /// <param name="bundle">The TriggerFiredBundle from which the <see cref="IJobDetail" />
    ///   and other info relating to the trigger firing can be obtained.</param>
    /// <param name="scheduler">a handle to the scheduler that is about to execute the job</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected virtual ValueTask<JobScope> CreateJobInstance(
        TriggerFiredBundle bundle,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<JobScope>(new JobScope(InstantiateJobCore(bundle)));
    }

    /// <summary>
    /// Sets the object properties.
    /// </summary>
    /// <param name="obj">The object to set properties to.</param>
    /// <param name="data">The data to set.</param>
    public virtual void SetObjectProperties(object obj, JobDataMap data)
    {
        foreach (string name in data.Keys)
        {
            SetObjectProperty(obj, name, data[name]);
        }
    }

    /// <summary>
    /// Sets specific property to object, handles conversion and error conditions.
    /// </summary>
    /// <param name="job">Job instance to set property value to.</param>
    /// <param name="name">Property name to set.</param>
    /// <param name="value">Value to set.</param>
    /// <remarks>
    /// The property is found on the instance's own type rather than on the job detail's declared one,
    /// because a factory is allowed to hand back something else — <c>AddJobType&lt;TJob, TImpl&gt;</c>
    /// registers exactly that — and the data belongs to whatever was built. Both of those types are
    /// annotated where they enter Quartz, so their public properties survive trimming; the analyzer
    /// cannot see the link because the instance arrives here as an <see cref="object" />. A job factory
    /// written from scratch that returns a type nothing else references is the one case this does not
    /// cover, and such a factory has to root its own jobs.
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The job instance is of the job detail's declared type or of a registered implementation of it, and both are annotated where they enter Quartz. See the remarks.")]
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
                ? ValueConverter.GetTimeSpanValueForProperty(prop, o)
                : ConvertValueIfNecessary(paramType, o);

            prop.GetSetMethod()!.Invoke(job, [goodValue]);
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

    /// <summary>
    /// Coerces a <see cref="JobDataMap" /> value into the type the job's property takes.
    /// </summary>
    /// <remarks>
    /// The seam a derived factory overrides to convert a value some other way. It is deliberately the
    /// only thing between the map and the property, so an override sees every value the factory binds.
    /// </remarks>
    protected virtual object? ConvertValueIfNecessary(Type requiredType, object? newValue)
    {
        return ValueConverter.ConvertValueIfNecessary(requiredType, newValue);
    }

    private void HandleError(string message, Exception? e = null)
    {
        if (PropertyMismatchBehavior == PropertyMismatchBehavior.Throw)
        {
            Throw.SchedulerException(message, e);
        }

        if (PropertyMismatchBehavior == PropertyMismatchBehavior.Warn)
        {
            logger.JobPropertyNotSet(message, e);
        }
    }
}