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
/// An exception that is thrown to indicate that cancellation failed.
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public sealed class UnableToInterruptJobException : SchedulerException
{
    /// <summary>
    /// Create a <see cref="UnableToInterruptJobException" /> with the given message.
    /// </summary>
    public UnableToInterruptJobException(string message) : base(message)
    {
    }

    /// <summary>
    /// Create a <see cref="UnableToInterruptJobException" /> with the given cause.
    /// </summary>
    public UnableToInterruptJobException(Exception innerException) : base(innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnableToInterruptJobException"/> class.
    /// </summary>
    /// <param name="info">The <see cref="SerializationInfo"></see> that holds the serialized object data about the exception being thrown.</param>
    /// <param name="context">The <see cref="System.Runtime.Serialization.StreamingContext"></see> that contains contextual information about the source or destination.</param>
    /// <exception cref="System.Runtime.Serialization.SerializationException">The class name is null or <see cref="System.Exception.HResult"></see> is zero (0). </exception>
    /// <exception cref="System.ArgumentNullException">The info parameter is null. </exception>
    public UnableToInterruptJobException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}