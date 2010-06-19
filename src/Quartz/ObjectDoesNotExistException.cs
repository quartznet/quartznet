#region License
/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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

namespace Quartz
{
    /// <summary>
    /// An exception that is thrown to indicate that an attempt to retrieve or update an
    /// object (i.e. <see cref="JobDetail" />, <see cref="Trigger" />
    /// or <see cref="ICalendar" /> in a <see cref="IScheduler" />
    /// failed, because one with the given identifier does not exists.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class ObjectDoesNotExistException : JobPersistenceException
    {
        /// <summary>
        /// Create a <code>ObjectDoesNotExistException</code> with the given message.
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="cause"></param>
        public ObjectDoesNotExistException(string msg, Exception cause) : base(msg, cause)
        {
        }


        /// <summary>
        /// Create a <see cref="ObjectDoesNotExistException" /> and auto-generate a
        /// message using the name/group from the given <see cref="JobDetail" />.
        /// </summary>
        /// <param name="offendingJob"></param>
        public ObjectDoesNotExistException(JobDetail offendingJob)
            : base(string.Format("Job with identifier of name: '{0}' and group: '{1}', does not exist.", offendingJob.Name, offendingJob.Group))
        {
            
        }

        /// <summary>
        /// Create a <see cref="ObjectAlreadyExistsException" /> and auto-generate a
        /// message using the name/group from the given <see cref="Trigger" />.
        /// </summary>
        /// <param name="offendingTrigger">Offending trigger</param>
        public ObjectDoesNotExistException(Trigger offendingTrigger)
            : base(string.Format("Trigger with identifier of name: '{0}' and group: '{1}', does not exist.", offendingTrigger.Name, offendingTrigger.Group))
        {
        }
    }
}