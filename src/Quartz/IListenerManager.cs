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
/// All three kinds of listener are managed the same way: a listener is identified by its name,
/// registering one under a name that is already taken replaces it, and it is removed by that
/// same name.
/// <para>
/// The matchers that decide which jobs and triggers a listener hears about are part of that
/// registration, because registration is the moment anyone knows them. A listener that has to hear
/// about something else is registered again under the same name, with the matchers it needs.
/// </para>
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
    /// <para>
    /// The listener's shape is checked as it is added. Every member of <see cref="IJobListener" /> has a
    /// default implementation, so a public method carrying a notification's name but not its signature
    /// still compiles — it just stops implementing anything, and the default runs in its place with
    /// nothing to say the method is dead. Such a listener is refused rather than attached and never
    /// called.
    /// </para>
    /// </remarks>
    /// <exception cref="SchedulerConfigException">
    /// <paramref name="jobListener" /> has a public method with an <see cref="IJobListener" /> member's
    /// name but not its signature, so that member is not implemented.
    /// </exception>
    /// <seealso cref="IMatcher{T}" />
    /// <seealso cref="EverythingMatcher{T}" />
    void AddJobListener(IJobListener jobListener, params IReadOnlyCollection<IMatcher<JobKey>> matchers);

    /// <summary>
    /// Remove the identified <see cref="IJobListener" /> from the <see cref="IScheduler" />.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the identified listener was found in the list, and removed;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    bool RemoveJobListener(string name);

    /// <summary>
    /// Gets all of the <see cref="IJobListener" />s in the <see cref="IScheduler" />.
    /// </summary>
    /// <returns>
    /// A shallow copy of all <see cref="IJobListener" /> instances that are registered.
    /// </returns>
    IReadOnlyList<IJobListener> GetJobListeners();

    /// <summary>
    /// Get the <see cref="IJobListener" /> that has the given name.
    /// </summary>
    /// <param name="name">The name of the <see cref="IJobListener" /> to retrieve.</param>
    /// <returns>
    /// The <see cref="IJobListener" /> registered under the name, or <see langword="null"/> when
    /// there is none.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    IJobListener? GetJobListener(string name);

    /// <summary>
    /// Add the given <see cref="ITriggerListener" /> to the <see cref="IScheduler" />,
    /// and register it to receive events for Triggers that are matched by ANY of the
    /// given Matchers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If no matchers are provided, the <see cref="ITriggerListener" /> will receive all events.
    /// </para>
    /// <para>
    /// If a <see cref="ITriggerListener" /> with the same name is already registered, that listener
    /// and the associated matchers will be replaced.
    /// </para>
    /// <para>
    /// The listener's shape is checked as it is added. Every member of <see cref="ITriggerListener" /> has
    /// a default implementation, so a public method carrying a notification's name but not its signature
    /// still compiles — it just stops implementing anything, and the default runs in its place with
    /// nothing to say the method is dead. Such a listener is refused rather than attached and never
    /// called.
    /// </para>
    /// </remarks>
    /// <exception cref="SchedulerConfigException">
    /// <paramref name="triggerListener" /> has a public method with an <see cref="ITriggerListener" />
    /// member's name but not its signature, so that member is not implemented.
    /// </exception>
    /// <seealso cref="IMatcher{T}" />
    /// <seealso cref="EverythingMatcher{T}" />
    void AddTriggerListener(ITriggerListener triggerListener, params IReadOnlyCollection<IMatcher<TriggerKey>> matchers);

    /// <summary>
    /// Removes the identified <see cref="ITriggerListener" /> from the <see cref="IScheduler" />.
    /// </summary>
    /// <returns>true if the identified listener was found in the list, and removed.</returns>
    bool RemoveTriggerListener(string name);

    /// <summary>
    /// Gets all of the <see cref="ITriggerListener" /> instances in the <see cref="IScheduler" />.
    /// </summary>
    /// <returns>
    /// A shallow copy of all <see cref="ITriggerListener" /> instances that are registered.
    /// </returns>
    IReadOnlyList<ITriggerListener> GetTriggerListeners();

    /// <summary>
    /// Get the <see cref="ITriggerListener" /> registered under the given name, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    ITriggerListener? GetTriggerListener(string name);

    /// <summary>
    /// Register the given <see cref="ISchedulerListener" /> with the
    /// <see cref="IScheduler" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If a <see cref="ISchedulerListener" /> with the same <see cref="ISchedulerListener.Name" />
    /// is already registered, that listener will be replaced.
    /// </para>
    /// <para>
    /// The listener's shape is checked as it is added. Every member of <see cref="ISchedulerListener" />
    /// has a default implementation, so a public method carrying a notification's name but not its
    /// signature still compiles — it just stops implementing anything, and the default runs in its place
    /// with nothing to say the method is dead. Such a listener is refused rather than attached and never
    /// called.
    /// </para>
    /// </remarks>
    /// <exception cref="SchedulerConfigException">
    /// <paramref name="schedulerListener" /> has a public method with an <see cref="ISchedulerListener" />
    /// member's name but not its signature, so that member is not implemented.
    /// </exception>
    void AddSchedulerListener(ISchedulerListener schedulerListener);

    /// <summary>
    /// Remove the identified <see cref="ISchedulerListener" /> from the
    /// <see cref="IScheduler" />.
    /// </summary>
    /// <param name="name">The <see cref="ISchedulerListener.Name" /> of the listener to remove.</param>
    /// <returns>true if the identified listener was found in the list, and removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    bool RemoveSchedulerListener(string name);

    /// <summary>
    /// Gets all of the <see cref="ISchedulerListener" /> instances in the <see cref="IScheduler" />.
    /// </summary>
    /// <returns>
    /// A shallow copy of all <see cref="ISchedulerListener" /> instances that are registered.
    /// </returns>
    IReadOnlyList<ISchedulerListener> GetSchedulerListeners();

    /// <summary>
    /// Get the <see cref="ISchedulerListener" /> registered under the given name, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    ISchedulerListener? GetSchedulerListener(string name);
}
