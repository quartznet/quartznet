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

using System.Text;

namespace Quartz;

/// <summary>
/// Reports every schema violation found in an XML or JSON scheduling data file.
/// </summary>
/// <remarks>
/// A whole document is validated before any of it is applied, so this carries a list rather than
/// the first failure: <see cref="Message" /> is every message, one per line. Like everything else
/// Quartz throws, it is a <see cref="SchedulerException" />, so one catch block covers a scheduling
/// data file's failures whether they are schema violations or anything else.
/// </remarks>
/// <author> <a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a></author>
/// <author>Marko Lahma (.NET)</author>
public sealed class SchedulingDataValidationException : SchedulerException
{
    private readonly List<Exception> validationExceptions = [];

    /// <summary>
    /// Every violation found in the document.
    /// </summary>
    public IReadOnlyList<Exception> ValidationExceptions => validationExceptions;

    /// <summary>
    /// Returns the detail message string.
    /// </summary>
    public override string Message
    {
        get
        {
            if (validationExceptions.Count == 0)
            {
                return base.Message;
            }

            StringBuilder sb = new StringBuilder();

            foreach (Exception e in validationExceptions)
            {
                sb.AppendLine(e.Message);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Constructor for SchedulingDataValidationException.
    /// </summary>
    public SchedulingDataValidationException()
    {
    }

    /// <summary>
    /// Constructor for SchedulingDataValidationException.
    /// </summary>
    /// <param name="message">exception message.</param>
    public SchedulingDataValidationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Constructor for SchedulingDataValidationException.
    /// </summary>
    /// <param name="errors">collection of validation exceptions.</param>
    public SchedulingDataValidationException(IEnumerable<Exception> errors) : this()
    {
        validationExceptions.AddRange(errors);
    }
}