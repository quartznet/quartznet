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

/*
* Previously Copyright (c) 2001-2004 James House
*/
using System;
using System.Collections;

namespace Quartz
{
    /// <summary>
    /// Conveys the detail properties of a given <see cref="IJob" /> instance.
    /// <p>
    /// Quartz does not store an actual instance of a <see cref="IJob" /> type, but
    /// instead allows you to define an instance of one, through the use of a <see cref="JobDetail" />.
    /// </p>
    /// 
    /// <p>
    /// <see cref="IJob" />s have a name and group associated with them, which
    /// should uniquely identify them within a single <see cref="IScheduler" />.
    /// </p>
    /// 
    /// <p>
    /// <see cref="Trigger" /> s are the 'mechanism' by which <see cref="IJob" /> s
    /// are scheduled. Many <see cref="Trigger" /> s can point to the same <see cref="IJob" />,
    /// but a single <see cref="Trigger" /> can only point to one <see cref="IJob" />.
    /// </p>
    /// </summary>
    /// <seealso cref="IJob" />
    /// <seealso cref="IStatefulJob"/>
    /// <seealso cref="JobDataMap"/>
    /// <seealso cref="Trigger"/>
    /// <author>James House</author>
    /// <author>Sharada Jambula</author>
    [Serializable]
    public class JobDetail : ICloneable
    {
        private string name;
        private string group = Scheduler_Fields.DEFAULT_GROUP;
        private string description;
        private Type jobType;
        private JobDataMap jobDataMap;
        private bool volatility = false;
        private bool durability = false;
        private bool shouldRecover = false;

        private ArrayList jobListeners = new ArrayList(2);

        /// <summary>
        /// Get or sets the name of this <see cref="IJob" />.
        /// </summary>
        /// <exception cref="ArgumentException"> 
        /// if name is null or empty.
        /// </exception>
        public string Name
        {
            get { return name; }

            set
            {
                if (value == null || value.Trim().Length == 0)
                {
                    throw new ArgumentException("Job name cannot be empty.");
                }

                name = value;
            }
        }

        /// <summary>
        /// Get or sets the group of this <see cref="IJob" />. 
        /// If <see langword="null" />, Scheduler.DEFAULT_GROUP will be used.
        /// </summary>
        /// <exception cref="ArgumentException"> 
        /// If the group is an empty string.
        /// </exception>
        public string Group
        {
            get { return group; }

            set
            {
                if (value != null && value.Trim().Length == 0)
                {
                    throw new ArgumentException("Group name cannot be empty.");
                }

                if (value == null)
                {
                    value = Scheduler_Fields.DEFAULT_GROUP;
                }

                group = value;
            }
        }

        /// <summary> 
        /// Returns the 'full name' of the <see cref="Trigger" /> in the format
        /// "group.name".
        /// </summary>
        public virtual string FullName
        {
            get { return group + "." + name; }
        }

        /// <summary>
        /// Get or set the description given to the <see cref="IJob" /> instance by its
        /// creator (if any).
        /// <p>
        /// May be useful
        /// for remembering/displaying the purpose of the job, though the
        /// description has no meaning to Quartz.
        /// </p>
        /// </summary>
        public virtual string Description
        {
            get { return description; }
            set { description = value; }
        }

        /// <summary>
        /// Get or sets the instance of <see cref="IJob" /> that will be executed.
        /// </summary>
        /// <exception cref="ArgumentException"> 
        /// if jobType is null or the class is not a <see cref="IJob" />.
        /// </exception>
        public Type JobType
        {
            get { return jobType; }

            set
            {
                if (value == null)
                {
                    throw new ArgumentException("Job class cannot be null.");
                }

                if (!typeof (IJob).IsAssignableFrom(value))
                {
                    throw new ArgumentException("Job class must implement the Job interface.");
                }

                jobType = value;
            }
        }

        /// <summary>
        /// Get or set the <see cref="JobDataMap" /> that is associated with the <see cref="IJob" />.
        /// </summary>
        public virtual JobDataMap JobDataMap
        {
            get
            {
                if (jobDataMap == null)
                {
                    jobDataMap = new JobDataMap();
                }
                return jobDataMap;
            }

            set { jobDataMap = value; }
        }

        /// <summary>
        /// Set whether or not the <see cref="IJob" /> should be persisted in the
        /// <see cref="IJobStore" /> for re-use after program
        /// restarts.
        /// <p>
        /// If not explicitly set, the default value is <see langword="false" />.
        /// </p>
        /// </summary>
        public bool Volatility
        {
            set { volatility = value; }
        }

        /// <summary>
        /// Set whether or not the <see cref="IJob" /> should remain stored after it
        /// is orphaned (no <see cref="Trigger" />s point to it).
        /// <p>
        /// If not explicitly set, the default value is <see langword="false" />.
        /// </p>
        /// </summary>
        public bool Durability
        {
            set { durability = value; }
        }

        /// <summary>
        /// Set whether or not the the <see cref="IScheduler" /> should re-Execute
        /// the <see cref="IJob" /> if a 'recovery' or 'fail-over' situation is
        /// encountered.
        /// <p>
        /// If not explicitly set, the default value is <see langword="false" />.
        /// </p>
        /// </summary>
        /// <seealso cref="JobExecutionContext.Recovering" />
        public bool RequestsRecovery
        {
            set { shouldRecover = value; }
        }

        /// <summary>
        /// Whether or not the <see cref="IJob" /> should not be persisted in the
        /// <see cref="IJobStore" /> for re-use after program
        /// restarts.
        /// <p>
        /// If not explicitly set, the default value is <see langword="false" />.
        /// </p>
        /// </summary>
        /// <returns> <see langword="true" /> if the <see cref="IJob" /> should be garbage
        /// collected along with the <see cref="IScheduler" />.
        /// </returns>
        public virtual bool Volatile
        {
            get { return volatility; }
        }

        /// <summary>
        /// Whether or not the <see cref="IJob" /> should remain stored after it is
        /// orphaned (no <see cref="Trigger" />s point to it).
        /// <p>
        /// If not explicitly set, the default value is <see langword="false" />.
        /// </p>
        /// </summary>
        /// <returns> 
        /// <see langword="true" /> if the Job should remain persisted after
        /// being orphaned.
        /// </returns>
        public virtual bool Durable
        {
            get { return durability; }
        }

        /// <summary>
        /// Whether or not the <see cref="IJob" /> implements the interface <see cref="IStatefulJob" />.
        /// </summary>
        public virtual bool Stateful
        {
            get
            {
                if (jobType == null)
                {
                    return false;
                }

                return (typeof (IStatefulJob).IsAssignableFrom(jobType));
            }
        }

        /// <summary>
        /// Returns an array of <see cref="String" /> s containing the names of all
        /// <see cref="IJobListener" /> s assigned to the <see cref="IJob" />,
        /// in the order in which they should be notified.
        /// </summary>
        public virtual string[] JobListenerNames
        {
            get { return (string[]) jobListeners.ToArray(typeof (string)); }
        }


        /// <summary>
        /// Create a <see cref="JobDetail" /> with no specified name or group, and
        /// the default settings of all the other properties.
        /// <p>
        /// Note that the {@link #setName(String)},{@link #setGroup(String)}and
        /// {@link #setJobClass(Class)}methods must be called before the job can be
        /// placed into a {@link Scheduler}
        /// </p>
        /// </summary>
        public JobDetail()
        {
            // do nothing...
        }

        /// <summary>
        /// Create a <see cref="JobDetail" /> with the given name, and group, and
        /// the default settings of all the other properties.
        /// If <see langword="null" />, Scheduler.DEFAULT_GROUP will be used.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// If name is null or empty, or the group is an empty string.
        /// </exception>
        public JobDetail(string name, string group, Type jobClass)
        {
            Name = name;
            Group = group;
            JobType = jobClass;
        }

        /// <summary>
        /// Create a <see cref="JobDetail" /> with the given name, and group, and
        /// the given settings of all the other properties.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">if <see langword="null" />, Scheduler.DEFAULT_GROUP will be used.</param>
        /// <param name="jobType">Type of the job.</param>
        /// <param name="volatility">if set to <c>true</c> [volatility].</param>
        /// <param name="durability">if set to <c>true</c> [durability].</param>
        /// <param name="recover">if set to <c>true</c> [recover].</param>
        /// <exception cref="ArgumentException"> ArgumentException
        /// if nameis null or empty, or the group is an empty string.
        /// </exception>
        public JobDetail(string name, string group, Type jobType, bool volatility, bool durability, bool recover)
        {
            Name = name;
            Group = group;
            JobType = jobType;
            Volatility = volatility;
            Durability = durability;
            RequestsRecovery = recover;
        }

        /// <summary> 
        /// Validates whether the properties of the <see cref="JobDetail" /> are
        /// valid for submission into a <see cref="IScheduler" />.
        /// </summary>
        public virtual void Validate()
        {
            if (name == null)
            {
                throw new SchedulerException("Job's name cannot be null", SchedulerException.ERR_CLIENT_ERROR);
            }

            if (group == null)
            {
                throw new SchedulerException("Job's group cannot be null", SchedulerException.ERR_CLIENT_ERROR);
            }

            if (jobType == null)
            {
                throw new SchedulerException("Job's class cannot be null", SchedulerException.ERR_CLIENT_ERROR);
            }
        }

        /// <summary> <p>
        /// Instructs the <see cref="IScheduler" /> whether or not the <see cref="IJob" />
        /// should be re-executed if a 'recovery' or 'fail-over' situation is
        /// encountered.
        /// </p>
        /// 
        /// <p>
        /// If not explicitly set, the default value is <see langword="false" />.
        /// </p>
        /// 
        /// </summary>
        /// <seealso cref="JobExecutionContext.Recovering">
        /// </seealso>
        public virtual bool requestsRecovery()
        {
            return shouldRecover;
        }

        /// <summary>
        /// Add the specified name of a <see cref="JobListener" /> to the
        /// end of the <see cref="IJob" />'s list of listeners.
        /// </summary>
        public virtual void AddJobListener(string listenerName)
        {
            jobListeners.Add(listenerName);
        }

        /// <summary> <p>
        /// Remove the specified name of a <see cref="JobListener" /> from
        /// the <see cref="IJob" />'s list of listeners.
        /// </p>
        /// 
        /// </summary>
        /// <returns> true if the given name was found in the list, and removed
        /// </returns>
        public virtual bool RemoveJobListener(string listenerName)
        {
            for (int i = 0; i < jobListeners.Count; i++)
            {
                IJobListener listener = (IJobListener) jobListeners[i];
                if (listener.Name == listenerName)
                {
                    jobListeners.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Return a simple string representation of this object.
        /// </summary>
        public override string ToString()
        {
            return
                string.Format(
                    "JobDetail '{0}':  jobType: '{1} isStateful: {2} isVolatile: {3} isDurable: {4} requestsRecovers: {5",
                    FullName, ((JobType == null) ? null : JobType.FullName), Stateful, Volatile, Durable,
                    requestsRecovery());
        }

        /// <summary>
        /// Creates a new object that is a copy of the current instance.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public virtual object Clone()
        {
            JobDetail copy;
            try
            {
                copy = (JobDetail) MemberwiseClone();
                copy.jobListeners = (ArrayList) jobListeners.Clone();
                if (jobDataMap != null)
                {
                    copy.jobDataMap = (JobDataMap) jobDataMap.Clone();
                }
            }
            catch (Exception)
            {
                throw new Exception("Not Cloneable.");
            }

            return copy;
        }
    }
}