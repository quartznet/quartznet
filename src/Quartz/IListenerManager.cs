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

using Quartz.Impl.Matchers;

namespace Quartz;

/// <summary>
/// Client programs may be interested in the 'listener' interfaces that are
/// available from Quartz. The <see cref="IJobListener" /> interface
/// provides notifications of Job executions. The
/// <see cref="ITriggerListener" /> interface provides notifications of
/// <see cref="ITrigger" /> firings. The <see cref="ISchedulerListener" />
/// interface provides notifications of scheduler events and
/// errors.  Listeners can be associated with local schedulers through the
/// <see cref="IListenerManager" /> interface.
/// </summary>
/// <remarks>
/// </remarks>
/// <author>jhouse</author>
/// <since>2.0 - previously listeners were managed directly on the Scheduler interface.</since>
public interface IListenerManager
{
    /// <summary>
    /// Add the given <see cref="IJobListener" /> to the <see cref="IScheduler" />,
    /// and register it to receive events for Jobs that are matched by ANY of the
    /// given Matchers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If no matchers are provided, the <see cref="IJobListener" /> will receive all events.
    /// </para>
    /// <para>
    /// If a <see cref="IJobListener" /> with the same name is already registered, that listener
    /// and the associated matchers will be replaced.
    /// </para>
    /// </remarks>
    /// <seealso cref="IMatcher{T}" />
    /// <seealso cref="EverythingMatcher{T}" />
    void AddJobListener(IJobListener jobListener, params IMatcher<JobKey>[] matchers);

    /// <summary>
    /// Add the given <see cref="IJobListener" /> to the <see cref="IScheduler" />,
    /// and register it to receive events for Jobs that are matched by ANY of the
    /// given Matchers.
    /// </summary>
    /// <remarks>
    /// If no matchers are provided, the <see cref="EverythingMatcher{TKey}" /> will be used.
    /// </remarks>
    /// <seealso cref="IMatcher{T}" />
    /// <seealso cref="EverythingMatcher{T}" />
    void AddJobListener(IJobListener jobListener, IReadOnlyCollection<IMatcher<JobKey>> matchers);

    /// <summary>
    /// Add the given Matcher to the set of matchers for which the listener
    /// will receive events if ANY of the matchers match.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="listenerName">the name of the listener to add the matcher to</param>
    /// <param name="matcher">the additional matcher to apply for selecting events</param>
    /// <returns>
    /// <see langword="true"/> if the identified listener was found and the matcher was associated;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="listenerName"/> is <see langword="null"/>.</exception>
    bool AddJobListenerMatcher(string listenerName, IMatcher<JobKey> matcher);

    /// <summary>
    /// Removes the given <see cref="IMatcher{JobKey}"/> from the set of matchers for which the listener
    /// will receive events if ANY of the matchers match.
    /// </summary>
    /// <param name="listenerName">The name of the listener to remove the matcher for.</param>
    /// <param name="matcher">The matcher to remove.</param>
    /// <returns>
    /// <see langword="true"/> if the given matcher was found and removed from the listener's list of matchers;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="listenerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="matcher"/> is <see langword="null"/>.</exception>
    bool RemoveJobListenerMatcher(string listenerName, IMatcher<JobKey> matcher);

    /// <summary>
    /// Sets the matchers for which the listener will receive events if ANY of the matchers match.
    /// </summary>
    /// <param name="listenerName">The name of the listener to set the matchers for.</param>
    /// <param name="matchers">The matchers to set. When empty, the <see cref="IJobListener"/> will receive all events.</param>
    /// <returns>
    /// <see langword="true"/> if the specified listener exists and the matchers were set; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="listenerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="matchers"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This removes any existing matchers for the identified listener!
    /// </remarks>
    bool SetJobListenerMatchers(string listenerName, IReadOnlyCollection<IMatcher<JobKey>> matchers);

    /// <summary>
    /// Get the set of <see cref="IMatcher{JobKey}"/> instances that were registered for the specified
    /// listener.
    /// </summary>
    /// <param name="listenerName">The name of the listener to add the matcher to</param>
    /// <returns>
    /// The matchers registered for selecting events for the identified listener, or <see langword="null"/> if
    /// no matcher where registered for the <see cref="IJobListener"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="listenerName"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A <see cref="IJobListener"/> will receive a given event if no matchers were registered for the
    /// listener or the event matches ANY of the matchers that were registered for that
    /// <see cref="IJobListener"/>.
    /// </remarks>
    IReadOnlyCollection<IMatcher<JobKey>>? GetJobListenerMatchers(string listenerName);

    /// <summary>
    /// Remove the identified <see cref="IJobListener" /> from the <see cref="IScheduler" />.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the identified listener was found in the list, and removed;
    /// otherwise, <see langword="true"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    bool RemoveJobListener(string name);

    /// <summary>
    /// Gets all of the <see cref="IJobListener" />s in the <see cref="IScheduler" />.
    /// </summary>
    /// <returns>
    /// A shallow copy of all <see cref="IJobListener" /> instances that are registered.
    /// </returns>
    IJobListener[] GetJobListeners();

    /// <summary>
    /// Get the <see cref="IJobListener" /> that has the given name.
    /// </summary>
    /// <param name="name">The name of the <see cref="IJobListener" /> to retrieve.</param>
    /// <returns>
    /// The <see cref="IJobListener" /> with the specified name.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">No <see cref="IJobListener"/> was found with the specified name.</exception>
    IJobListener GetJobListener(string name);

    /// <summary>
    /// Add the given <see cref="ITriggerListener" /> to the <see cref="IScheduler" />,
    /// and register it to receive events for Triggers that are matched by ANY of the
    /// given Matchers.
    /// </summary>
    /// <remarks>
    /// If no matcher is provided, the <see cref="EverythingMatcher{TKey}" /> will be used.
    /// </remarks>
    /// <seealso cref="IMatcher{T}" />
    /// <seealso cref="EverythingMatcher{T}" />
    void AddTriggerListener(ITriggerListener triggerListener, params IMatcher<TriggerKey>[] matchers);

    /// <summary>
    /// Add the given <see cref="ITriggerListener" /> to the <see cref="IScheduler" />,
    /// and register it to receive events for Triggers that are matched by ANY of the
    /// given Matchers.
    /// </summary>
    /// <remarks>
    /// If no matcher is provided, the <see cref="EverythingMatcher{TKey}" /> will be used.
    /// </remarks>
    /// <seealso cref="IMatcher{T}" />
    /// <seealso cref="EverythingMatcher{T}" />
    void AddTriggerListener(ITriggerListener triggerListener, IReadOnlyCollection<IMatcher<TriggerKey>> matchers);

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
    /// <para>Removes any existing matchers for the identified listener!</para>
    /// </remarks>
    /// <param name="listenerName">the name of the listener to add the matcher to</param>
    /// <param name="matchers">the matchers to apply for selecting events</param>
    /// <returns>true if the given matcher was found and removed from the listener's list of matchers</returns>
    bool SetTriggerListenerMatchers(string listenerName, IReadOnlyCollection<IMatcher<TriggerKey>> matchers);

    /// <summary>
    /// Get the set of Matchers for which the listener
    /// will receive events if ANY of the matchers match.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="listenerName">the name of the listener to add the matcher to</param>
    /// <returns>the matchers registered for selecting events for the identified listener</returns>
    IReadOnlyCollection<IMatcher<TriggerKey>>? GetTriggerListenerMatchers(string listenerName);

    /// <summary>
    /// Removes the identified <see cref="ITriggerListener" /> from the <see cref="IScheduler" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>true if the identified listener was found in the list, and</returns>
    /// removed.
    bool RemoveTriggerListener(string name);

    /// <summary>
    /// Gets all of the <see cref="ITriggerListener" /> instances in the <see cref="IScheduler" />.
    /// </summary>
    /// <returns>
    /// A shallow copy of all <see cref="ITriggerListener" /> instances that are registered.
    /// </returns>
    ITriggerListener[] GetTriggerListeners();

    /// <summary>
    /// Get the <see cref="ITriggerListener" /> that has the given name.
    /// </summary>
    ITriggerListener GetTriggerListener(string name);

    /// <summary>
    /// Register the given <see cref="ISchedulerListener" /> with the
    ///<see cref="IScheduler" />.
    /// </summary>
    void AddSchedulerListener(ISchedulerListener schedulerListener);

    /// <summary>
    /// Remove the given <see cref="ISchedulerListener" /> from the
    ///<see cref="IScheduler" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>true if the identified listener was found in the list, and removed.</returns>
    bool RemoveSchedulerListener(ISchedulerListener schedulerListener);

    /// <summary>
    /// Get a List containing all of the <see cref="ISchedulerListener" />s
    /// registered with the <see cref="IScheduler" />.
    /// </summary>
    IReadOnlyCollection<ISchedulerListener> GetSchedulerListeners();
}