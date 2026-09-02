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
    /// <param name="message">What went wrong.</param>
    public SchedulerException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SchedulerException"/> class.
    /// </summary>
    /// <param name="innerException">The cause, whose message becomes this exception's.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerException" /> is <see langword="null" />.</exception>
    public SchedulerException(Exception innerException) : base(MessageOf(innerException), innerException)
    {
    }

    /// <summary>
    /// The cause's message, checked first: the base constructor call would otherwise dereference a null
    /// cause and answer a <see cref="NullReferenceException" /> from the base type of every exception
    /// Quartz raises.
    /// </summary>
    private static string MessageOf(Exception innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
        return innerException.Message;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SchedulerException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public SchedulerException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates and returns a string representation of the current exception.
    /// </summary>
    /// <returns>
    /// A string representation of the current exception, with the cause appended when there is one.
    /// </returns>
    public override string ToString()
    {
        if (InnerException is null)
        {
            return base.ToString();
        }
        return $"{base.ToString()} [See nested exception: {InnerException}]";
    }
}