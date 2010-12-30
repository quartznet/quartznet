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

using System.Collections.Generic;

using Quartz.Impl.Matchers;

namespace Quartz
{
    /// <summary>
    /// Client programs may be interested in the 'listener' interfaces that are
    /// available from Quartz. The <code>{@link JobListener}</code> interface
    /// provides notifications of <code>Job</code> executions. The
    /// <code>{@link TriggerListener}</code> interface provides notifications of
    /// <code>Trigger</code> firings. The <code>{@link SchedulerListener}</code>
    /// interface provides notifications of <code>Scheduler</code> events and
    /// errors.  Listeners can be associated with local schedulers through the
    /// {@link ListenerManager} interface.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <author>jhouse</author>
    /// <since>2.0 - previously listeners were managed directly on the Scheduler interface.</since>
    public interface IListenerManager
    {
        /// <summary>
        /// Add the given <code>{@link JobListener}</code> to the <code>Scheduler</code>,
        /// and register it to receive events for Jobs that are matched by ANY of the
        /// given Matchers.
        /// </summary>
        /// <remarks>
        /// If no matchers are provided, the <code>EverythingMatcher</code> will be used.
        /// </remarks>
        /// <seealso cref="IMatcher{T}" />
        /// <seealso cref="EverythingMatcher{T}" />
        void AddJobListener(IJobListener jobListener, params IMatcher<JobKey>[] matchers);

        /// <summary>
        /// Add the given <code>{@link JobListener}</code> to the <code>Scheduler</code>,
        /// and register it to receive events for Jobs that are matched by ANY of the
        /// given Matchers.
        /// </summary>
        /// <remarks>
        /// If no matchers are provided, the <code>EverythingMatcher</code> will be used.
        /// </remarks>
        /// <seealso cref="IMatcher{T}" />
        /// <seealso cref="EverythingMatcher{T}" />
        void AddJobListener(IJobListener jobListener, IList<IMatcher<JobKey>> matchers);

        /// <summary>
        /// Add the given Matcher to the set of matchers for which the listener
        /// will receive events if ANY of the matchers match.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="listenerName">the name of the listener to add the matcher to</param>
        /// <param name="matcher">the additional matcher to apply for selecting events</param>
        /// <returns>true if the identified listener was found and updated</returns>
        bool AddJobListenerMatcher(string listenerName, IMatcher<JobKey> matcher);

        /// <summary>
        /// Remove the given Matcher to the set of matchers for which the listener
        /// will receive events if ANY of the matchers match.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="listenerName">the name of the listener to add the matcher to</param>
        /// <param name="matcher">the additional matcher to apply for selecting events</param>
        /// <returns>true if the given matcher was found and removed from the listener's list of matchers</returns>
        bool RemoveJobListenerMatcher(string listenerName, IMatcher<JobKey> matcher);

        /// <summary>
        /// Set the set of Matchers for which the listener
        /// will receive events if ANY of the matchers match.
        /// </summary>
        /// <remarks>
        /// <p>Removes any existing matchers for the identified listener!</p>
        /// </remarks>
        /// <param name="listenerName">the name of the listener to add the matcher to</param>
        /// <param name="matchers">the matchers to apply for selecting events</param>
        /// <returns>true if the given matcher was found and removed from the listener's list of matchers</returns>
        bool SetJobListenerMatchers(string listenerName, IList<IMatcher<JobKey>> matchers);

        /// <summary>
        /// Get the set of Matchers for which the listener
        /// will receive events if ANY of the matchers match.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="listenerName">the name of the listener to add the matcher to</param>
        /// <returns>the matchers registered for selecting events for the identified listener</returns>
        IList<IMatcher<JobKey>> GetJobListenerMatchers(string listenerName);

        /// <summary>
        /// Remove the identified <code>{@link JobListener}</code> from the <code>Scheduler</code>.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>true if the identified listener was found in the list, and removed.</returns>
        bool RemoveJobListener(string name);

        /// <summary>
        /// Get a List containing all of the <code>{@link JobListener}</code>s in
        /// the <code>Scheduler</code>.
        /// </summary>
        IList<IJobListener> GetJobListeners();

        /// <summary>
        /// Get the <code>{@link JobListener}</code> that has the given name.
        /// </summary>
        IJobListener GetJobListener(string name);

        /// <summary>
        /// Add the given <code>{@link TriggerListener}</code> to the <code>Scheduler</code>,
        /// and register it to receive events for Triggers that are matched by ANY of the
        /// given Matchers.
        /// </summary>
        /// <remarks>
        /// If no matcher is provided, the <code>EverythingMatcher</code> will be used.
        /// </remarks>
        /// <seealso cref="IMatcher{T}" />
        /// <seealso cref="EverythingMatcher{T}" />
        void AddTriggerListener(ITriggerListener triggerListener, params IMatcher<TriggerKey>[] matchers);

        /// <summary>
        /// Add the given <code>{@link TriggerListener}</code> to the <code>Scheduler</code>,
        /// and register it to receive events for Triggers that are matched by ANY of the
        /// given Matchers.
        /// </summary>
        /// <remarks>
        /// If no matcher is provided, the <code>EverythingMatcher</code> will be used.
        /// </remarks>
        /// <seealso cref="IMatcher{T}" />
        /// <seealso cref="EverythingMatcher{T}" />
        void AddTriggerListener(ITriggerListener triggerListener, IList<IMatcher<TriggerKey>> matchers);

        /// <summary>
        /// Add the given Matcher to the set of matchers for which the listener
        /// will receive events if ANY of the matchers match.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="listenerName">the name of the listener to add the matcher to</param>
        /// <param name="matcher">the additional matcher to apply for selecting events</param>
        /// <returns>true if the identified listener was found and updated</returns>
        bool AddTriggerListenerMatcher(string listenerName, IMatcher<TriggerKey> matcher);

        /// <summary>
        /// Remove the given Matcher to the set of matchers for which the listener
        /// will receive events if ANY of the matchers match.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="listenerName">the name of the listener to add the matcher to</param>
        /// <param name="matcher">the additional matcher to apply for selecting events</param>
        /// <returns>true if the given matcher was found and removed from the listener's list of matchers</returns>
        bool RemoveTriggerListenerMatcher(string listenerName, IMatcher<TriggerKey> matcher);

        /// <summary>
        /// Set the set of Matchers for which the listener
        /// will receive events if ANY of the matchers match.
        /// </summary>
        /// <remarks>
        /// <p>Removes any existing matchers for the identified listener!</p>
        /// </remarks>
        /// <param name="listenerName">the name of the listener to add the matcher to</param>
        /// <param name="matchers">the matchers to apply for selecting events</param>
        /// <returns>true if the given matcher was found and removed from the listener's list of matchers</returns>
        bool SetTriggerListenerMatchers(string listenerName, IList<IMatcher<TriggerKey>> matchers);

        /// <summary>
        /// Get the set of Matchers for which the listener
        /// will receive events if ANY of the matchers match.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="listenerName">the name of the listener to add the matcher to</param>
        /// <returns>the matchers registered for selecting events for the identified listener</returns>
        IList<IMatcher<TriggerKey>> GetTriggerListenerMatchers(string listenerName);

        /// <summary>
        /// Remove the identified <code>{@link TriggerListener}</code> from the <code>Scheduler</code>.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>true if the identified listener was found in the list, and</returns>
        /// removed.
        bool RemoveTriggerListener(string name);

        /// <summary>
        /// Get a List containing all of the <code>{@link TriggerListener}</code>s
        /// in the <code>Scheduler</code>.
        /// </summary>
        IList<ITriggerListener> GetTriggerListeners();

        /// <summary>
        /// Get the <code>{@link TriggerListener}</code> that has the given name.
        /// </summary>
        ITriggerListener GetTriggerListener(string name);

        /// <summary>
        /// Register the given <code>{@link SchedulerListener}</code> with the
        /// <code>Scheduler</code>.
        /// </summary>
        void AddSchedulerListener(ISchedulerListener schedulerListener);

        /// <summary>
        /// Remove the given <code>{@link SchedulerListener}</code> from the
        /// <code>Scheduler</code>.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>true if the identified listener was found in the list, and removed.</returns>
        bool RemoveSchedulerListener(ISchedulerListener schedulerListener);

        /// <summary>
        /// Get a List containing all of the <code>{@link SchedulerListener}</code>s
        /// registered with the <code>Scheduler</code>.
        /// </summary>
        IList<ISchedulerListener> GetSchedulerListeners();
    }
}