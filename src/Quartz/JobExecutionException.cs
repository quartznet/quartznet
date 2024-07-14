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

using System.Runtime.Serialization;

namespace Quartz;

/// <summary>
/// An exception that can be thrown by a <see cref="IJob" />
/// to indicate to the Quartz <see cref="IScheduler" /> that an error
/// occurred while executing, and whether or not the <see cref="IJob" /> requests
/// to be re-fired immediately (using the same <see cref="IJobExecutionContext" />),
/// or whether it wants to be unscheduled.
/// </summary>
/// <remarks>
/// Note that if the flag for 'refire immediately' is set, the flags for
/// unscheduling the Job are ignored.
/// </remarks>
/// <seealso cref="IJob" />
/// <seealso cref="IJobExecutionContext" />
/// <seealso cref="SchedulerException" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public sealed class JobExecutionException : SchedulerException
{
    /// <summary>
    /// Create a JobExecutionException, with the 're-fire immediately' flag set
    /// to <see langword="false" />.
    /// </summary>
    public JobExecutionException()
    {
    }

    /// <summary>
    /// Create a JobExecutionException, with the given cause.
    /// </summary>
    /// <param name="innerException">The cause.</param>
    public JobExecutionException(Exception innerException) : base(innerException)
    {
    }

    /// <summary>
    /// Create a JobExecutionException, with the given message.
    /// </summary>
    public JobExecutionException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JobExecutionException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The original cause.</param>
    public JobExecutionException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Create a JobExecutionException with the 're-fire immediately' flag set
    /// to the given value.
    /// </summary>
    public JobExecutionException(bool refireImmediately)
    {
        RefireImmediately = refireImmediately;
    }

    /// <summary>
    /// Create a JobExecutionException with the given underlying exception, and
    /// the 're-fire immediately' flag set to the given value.
    /// </summary>
    public JobExecutionException(Exception innerException, bool refireImmediately) : base(innerException)
    {
        RefireImmediately = refireImmediately;
    }

    /// <summary>
    /// Create a JobExecutionException with the given message, and underlying
    /// exception, and the 're-fire immediately' flag set to the given value.
    /// </summary>
    public JobExecutionException(string message, Exception innerException, bool refireImmediately) : base(message, innerException)
    {
        RefireImmediately = refireImmediately;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JobExecutionException"/> class.
    /// </summary>
    /// <param name="info">The <see cref="SerializationInfo"></see> that holds the serialized object data about the exception being thrown.</param>
    /// <param name="context">The <see cref="System.Runtime.Serialization.StreamingContext"></see> that contains contextual information about the source or destination.</param>
    /// <exception cref="System.Runtime.Serialization.SerializationException">The class name is null or <see cref="System.Exception.HResult"></see> is zero (0). </exception>
    /// <exception cref="System.ArgumentNullException">The info parameter is null. </exception>
    private JobExecutionException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }

    /// <summary>
    /// Gets or sets a value indicating whether to unschedule firing trigger.
    /// </summary>
    /// <value>
    /// 	<c>true</c> if firing trigger should be unscheduled; otherwise, <c>false</c>.
    /// </value>
    public bool UnscheduleFiringTrigger { set; get; }

    /// <summary>
    /// Gets or sets a value indicating whether to unschedule all triggers.
    /// </summary>
    /// <value>
    /// 	<c>true</c> if all triggers should be unscheduled; otherwise, <c>false</c>.
    /// </value>
    public bool UnscheduleAllTriggers { set; get; }


    /// <summary>
    /// Gets or sets a value indicating whether to refire immediately.
    /// </summary>
    /// <value><c>true</c> if to refire immediately; otherwise, <c>false</c>.</value>
    public bool RefireImmediately { get; set; }

    /// <summary>
    /// Creates and returns a string representation of the current exception.
    /// </summary>
    /// <returns>
    /// A string representation of the current exception.
    /// </returns>
    /// <PermissionSet><IPermission class="System.Security.Permissions.FileIOPermission, mscorlib, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" version="1" PathDiscovery="*AllFiles*"/></PermissionSet>
    public override string ToString()
        => $"Parameters: refire = {RefireImmediately}, unscheduleFiringTrigger = {UnscheduleFiringTrigger}, unscheduleAllTriggers = {UnscheduleAllTriggers} {Environment.NewLine} {base.ToString()}";
}