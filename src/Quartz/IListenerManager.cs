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

namespace Quartz
{
/**
 * Client programs may be interested in the 'listener' interfaces that are
 * available from Quartz. The <code>{@link JobListener}</code> interface
 * provides notifications of <code>Job</code> executions. The 
 * <code>{@link TriggerListener}</code> interface provides notifications of 
 * <code>Trigger</code> firings. The <code>{@link SchedulerListener}</code> 
 * interface provides notifications of <code>Scheduler</code> events and 
 * errors.  Listeners can be associated with local schedulers through the 
 * {@link ListenerManager} interface.  
 * 
 * @author jhouse
 * @since 2.0 - previously listeners were managed directly on the Scheduler interface.
 */

    public interface IListenerManager
    {
        /**
     * Add the given <code>{@link JobListener}</code> to the <code>Scheduler</code>,
     * and register it to receive events for Jobs that are matched by ANY of the
     * given Matchers.
     * 
     * If no matchers are provided, the <code>EverythingMatcher</code> will be used.
     * 
     * @see Matcher
     * @see EverythingMatcher
     */
        void AddJobListener(IJobListener jobListener, params IMatcher<JobKey>[] matchers);

        /**
     * Add the given <code>{@link JobListener}</code> to the <code>Scheduler</code>,
     * and register it to receive events for Jobs that are matched by ANY of the
     * given Matchers.
     * 
     * If no matchers are provided, the <code>EverythingMatcher</code> will be used.
     * 
     * @see Matcher
     * @see EverythingMatcher
     */
        void AddJobListener(IJobListener jobListener, IList<IMatcher<JobKey>> matchers);

        /**
     * Add the given Matcher to the set of matchers for which the listener
     * will receive events if ANY of the matchers match.
     *  
     * @param listenerName the name of the listener to add the matcher to
     * @param matcher the additional matcher to apply for selecting events
     * @return true if the identified listener was found and updated
     * @throws SchedulerException
     */
        bool AddJobListenerMatcher(string listenerName, IMatcher<JobKey> matcher);

        /**
     * Remove the given Matcher to the set of matchers for which the listener
     * will receive events if ANY of the matchers match.
     *  
     * @param listenerName the name of the listener to add the matcher to
     * @param matcher the additional matcher to apply for selecting events
     * @return true if the given matcher was found and removed from the listener's list of matchers
     * @throws SchedulerException
     */
        bool RemoveJobListenerMatcher(string listenerName, IMatcher<JobKey> matcher);

        /**
     * Set the set of Matchers for which the listener
     * will receive events if ANY of the matchers match.
     * 
     * <p>Removes any existing matchers for the identified listener!</p>
     *  
     * @param listenerName the name of the listener to add the matcher to
     * @param matchers the matchers to apply for selecting events
     * @return true if the given matcher was found and removed from the listener's list of matchers
     * @throws SchedulerException
     */
        bool SetJobListenerMatchers(string listenerName, IList<IMatcher<JobKey>> matchers);

        /**
     * Get the set of Matchers for which the listener
     * will receive events if ANY of the matchers match.
     * 
     *  
     * @param listenerName the name of the listener to add the matcher to
     * @return the matchers registered for selecting events for the identified listener
     * @throws SchedulerException
     */
        IList<IMatcher<JobKey>> GetJobListenerMatchers(string listenerName);

        /**
     * Remove the identified <code>{@link JobListener}</code> from the <code>Scheduler</code>.
     * 
     * @return true if the identified listener was found in the list, and
     *         removed.
     */
        bool RemoveJobListener(string name);

        /**
     * Get a List containing all of the <code>{@link JobListener}</code>s in
     * the <code>Scheduler</code>.
     */
        IList<IJobListener> GetJobListeners();

        /**
     * Get the <code>{@link JobListener}</code> that has the given name.
     */
        IJobListener GetJobListener(string name);

        /**
     * Add the given <code>{@link TriggerListener}</code> to the <code>Scheduler</code>,
     * and register it to receive events for Triggers that are matched by ANY of the
     * given Matchers.
     * 
     * If no matcher is provided, the <code>EverythingMatcher</code> will be used.
     * 
     * @see Matcher
     * @see EverythingMatcher
     */
        void AddTriggerListener(ITriggerListener triggerListener, params IMatcher<TriggerKey>[] matchers);

        /**
     * Add the given <code>{@link TriggerListener}</code> to the <code>Scheduler</code>,
     * and register it to receive events for Triggers that are matched by ANY of the
     * given Matchers.
     * 
     * If no matcher is provided, the <code>EverythingMatcher</code> will be used.
     * 
     * @see Matcher
     * @see EverythingMatcher
     */
        void AddTriggerListener(ITriggerListener triggerListener, IList<IMatcher<TriggerKey>> matchers);

        /**
     * Add the given Matcher to the set of matchers for which the listener
     * will receive events if ANY of the matchers match.
     *  
     * @param listenerName the name of the listener to add the matcher to
     * @param matcher the additional matcher to apply for selecting events
     * @return true if the identified listener was found and updated
     * @throws SchedulerException
     */
        bool AddTriggerListenerMatcher(string listenerName, IMatcher<TriggerKey> matcher);

        /**
     * Remove the given Matcher to the set of matchers for which the listener
     * will receive events if ANY of the matchers match.
     *  
     * @param listenerName the name of the listener to add the matcher to
     * @param matcher the additional matcher to apply for selecting events
     * @return true if the given matcher was found and removed from the listener's list of matchers
     * @throws SchedulerException
     */
        bool RemoveTriggerListenerMatcher(string listenerName, IMatcher<TriggerKey> matcher);

        /**
     * Set the set of Matchers for which the listener
     * will receive events if ANY of the matchers match.
     * 
     * <p>Removes any existing matchers for the identified listener!</p>
     *  
     * @param listenerName the name of the listener to add the matcher to
     * @param matchers the matchers to apply for selecting events
     * @return true if the given matcher was found and removed from the listener's list of matchers
     * @throws SchedulerException
     */
        bool SetTriggerListenerMatchers(string listenerName, IList<IMatcher<TriggerKey>> matchers);

        /**
     * Get the set of Matchers for which the listener
     * will receive events if ANY of the matchers match.
     * 
     *  
     * @param listenerName the name of the listener to add the matcher to
     * @return the matchers registered for selecting events for the identified listener
     * @throws SchedulerException
     */
        IList<IMatcher<TriggerKey>> GetTriggerListenerMatchers(string listenerName);

        /**
     * Remove the identified <code>{@link TriggerListener}</code> from the <code>Scheduler</code>.
     * 
     * @return true if the identified listener was found in the list, and
     *         removed.
     */
        bool RemoveTriggerListener(string name);

        /**
     * Get a List containing all of the <code>{@link TriggerListener}</code>s 
     * in the <code>Scheduler</code>.
     */
        IList<ITriggerListener> GetTriggerListeners();

        /**
     * Get the <code>{@link TriggerListener}</code> that has the given name.
     */
        ITriggerListener GetTriggerListener(string name);

        /**
     * Register the given <code>{@link SchedulerListener}</code> with the
     * <code>Scheduler</code>.
     */
        void AddSchedulerListener(ISchedulerListener schedulerListener);

        /**
     * Remove the given <code>{@link SchedulerListener}</code> from the
     * <code>Scheduler</code>.
     * 
     * @return true if the identified listener was found in the list, and
     *         removed.
     */
        bool RemoveSchedulerListener(ISchedulerListener schedulerListener);

        /**
     * Get a List containing all of the <code>{@link SchedulerListener}</code>s
     * registered with the <code>Scheduler</code>.
     */
        IList<ISchedulerListener> GetSchedulerListeners();
    }
}