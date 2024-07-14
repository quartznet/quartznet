#pragma warning disable SYSLIB0051 // 'Exception.Exception(SerializationInfo, StreamingContext)' is obsolete

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
/// Base class for exceptions thrown by the Quartz <see cref="IScheduler" />.
/// </summary>
/// <remarks>
/// SchedulerExceptions may contain a reference to another
/// <see cref="Exception" />, which was the underlying cause of the SchedulerException.
/// </remarks>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public class SchedulerException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SchedulerException"/> class.
    /// </summary>
    public SchedulerException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SchedulerException"/> class.
    /// </summary>
    public SchedulerException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SchedulerException"/> class.
    /// </summary>
    /// <param name="info">The <see cref="SerializationInfo"></see> that holds the serialized object data about the exception being thrown.</param>
    /// <param name="context">The <see cref="System.Runtime.Serialization.StreamingContext"></see> that contains contextual information about the source or destination.</param>
    /// <exception cref="System.Runtime.Serialization.SerializationException">The class name is null or <see cref="System.Exception.HResult"></see> is zero (0). </exception>
    /// <exception cref="System.ArgumentNullException">The info parameter is null. </exception>
    protected SchedulerException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SchedulerException"/> class.
    /// </summary>
    /// <param name="innerException">The cause.</param>
    public SchedulerException(Exception innerException) : base(innerException.Message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SchedulerException"/> class.
    /// </summary>
    /// <param name="message">The MSG.</param>
    /// <param name="innerException">The cause.</param>
    public SchedulerException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates and returns a string representation of the current exception.
    /// </summary>
    /// <returns>
    /// A string representation of the current exception.
    /// </returns>
    /// <PermissionSet><IPermission class="System.Security.Permissions.FileIOPermission, mscorlib, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" version="1" PathDiscovery="*AllFiles*"/></PermissionSet>
    public override string ToString()
    {
        if (InnerException is null)
        {
            return base.ToString();
        }
        return $"{base.ToString()} [See nested exception: {InnerException}]";
    }
}