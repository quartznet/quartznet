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
/// Adds and removes schedulers in a container that has already been built.
/// </summary>
/// <remarks>
/// <para>
/// A scheduler registered with <c>AddQuartz</c> is a set of registrations fixed when the container was
/// built, and a name the container never heard of has none. This is the other door: a scheduler added
/// here is built into a container of its own, holding its own thread pool, job store, connection
/// provider, plugins and listeners, and resolving everything else — the application's services, its
/// jobs, its options — from the application's container. It is bound into the same
/// <see cref="Extensibility.ISchedulerRepository" /> as every other scheduler, so the HTTP API, the
/// dashboard and <see cref="ISchedulerFactory.GetAllSchedulers" /> see it without knowing it arrived
/// late.
/// </para>
/// <para>
/// It extends <see cref="ISchedulerRegistry" />: <see cref="ISchedulerRegistry.QuerySchedulers" /> lists
/// what the container registered and what was added at runtime alike, the latter with
/// <see cref="SchedulerOrigin.Runtime" />. Resolving either interface answers with the same object, so
/// an application that only reads the listing need not know this one exists.
/// </para>
/// <para>
/// Registered by <c>AddQuartz</c>, so any container with Quartz in it has one. What it holds is the
/// container's for as long as the container lives: a scheduler still running when the host stops is
/// shut down with the rest, and one still running when the container is disposed is shut down then.
/// </para>
/// <para>
/// One thing it deliberately does not do is delete data. A removed tenant's rows under its
/// <c>SCHED_NAME</c> stay exactly where they are, and its <c>SCHEDULER_STATE</c> row expires the way a
/// stopped node's does. Deleting a tenant's data is the application's decision, not a side effect of
/// removing its scheduler.
/// </para>
/// </remarks>
public interface ISchedulerRuntime : ISchedulerRegistry
{
    /// <summary>
    /// Builds a scheduler under a name the container does not hold, binds it into the repository and —
    /// unless told otherwise — starts it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="configure" /> is the same <see cref="IQuartzBuilder" /> an
    /// <c>AddQuartz(name, …)</c> callback is given, applied in the same order to the same phases, so a
    /// recipe reads identically whichever door it came through. Its
    /// <see cref="IQuartzBuilder.Services" /> is <em>this scheduler's</em> collection rather than the
    /// application's: what it registers there belongs to this scheduler and goes away with it.
    /// </para>
    /// <para>
    /// Whatever <c>ConfigureAllQuartzSchedulers</c> said about every scheduler in the container is
    /// applied here too, after <paramref name="configure" />, exactly where a scheduler registered at
    /// startup receives it.
    /// </para>
    /// <para>
    /// This fails soft: a name that cannot be added is refused with a
    /// <see cref="SchedulerConfigException" /> saying which rule and what to do about it, and nothing is
    /// left behind — no half-built scheduler in the repository, and nothing in the listing. A name is
    /// refused when the container registered it, when a scheduler is already bound under it, when one
    /// has already been added under it, and when the host is stopping.
    /// </para>
    /// <para>
    /// The scheduler is reached afterwards through <see cref="ISchedulerFactory.LookupScheduler" /> or
    /// the repository. It is <em>not</em> reachable as
    /// <c>[FromKeyedServices("name")] IScheduler</c>: that is a container registration, and the point of
    /// this method is a scheduler the container was never told about.
    /// </para>
    /// </remarks>
    /// <param name="schedulerName">
    /// The scheduler's name, which is also its instance name and the name of its options.
    /// </param>
    /// <param name="configure">Configures the scheduler, as an <c>AddQuartz(name, …)</c> callback does.</param>
    /// <param name="options">
    /// Settings for the scheduler and whether to start it. The default is a scheduler configured
    /// entirely by <paramref name="configure" /> and started.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The scheduler, created and bound.</returns>
    /// <exception cref="SchedulerConfigException">
    /// The name cannot be added, or the scheduler could not be built from what it was configured with.
    /// </exception>
    ValueTask<IScheduler> Add(
        string schedulerName,
        Action<IQuartzBuilder>? configure = null,
        SchedulerAddOptions options = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Shuts down a scheduler this runtime added, unbinds it from the repository and releases everything
    /// built for it.
    /// </summary>
    /// <remarks>
    /// A name this runtime did not add is not an error: it answers <see langword="false" />, so removing
    /// twice, or removing a tenant that was shut down by hand, says what happened rather than throwing.
    /// A name the <em>container</em> registered is a different matter and is refused — the container owns
    /// those, and <see cref="IScheduler.Shutdown" /> is how one of them stops.
    /// <para>
    /// Nothing is deleted from the store. See the remarks on <see cref="ISchedulerRuntime" />.
    /// </para>
    /// </remarks>
    /// <param name="schedulerName">The scheduler's name.</param>
    /// <param name="waitForJobsToComplete">
    /// Whether to wait for the jobs that are running to finish before the scheduler shuts down.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// <see langword="true" /> when a scheduler was removed, <see langword="false" /> when this runtime
    /// held none under that name.
    /// </returns>
    /// <exception cref="SchedulerConfigException">The container registered this name.</exception>
    ValueTask<bool> Remove(
        string schedulerName,
        bool waitForJobsToComplete = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Shuts a scheduler down and builds another one from the recipe that built it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is restarted, and the name is the only thing the two schedulers share. A scheduler's
    /// thread pool, job store, connection provider, plugins and listeners are all one-way — every one of
    /// them refuses work once it has been shut down — so a second generation is a second set of
    /// instances, built by running the recipe again into a container of its own. That is what
    /// <see cref="Add" /> does for a name the container never heard of, and it is what this does for a
    /// name it did: <c>AddQuartz(name, …)</c> records what it was told, so a registered scheduler is
    /// restartable too.
    /// </para>
    /// <para>
    /// The order is build, drain, create — and each step is where it is for a reason. The new
    /// generation's container is built <em>first</em>, so a recipe that no longer works leaves the old
    /// scheduler running rather than nothing at all. The old scheduler is then shut down waiting for its
    /// jobs, because a persistent store's recovery sweep runs over the whole scheduler name unfiltered
    /// by instance id: a new generation that started while the old one's jobs ran would move their
    /// triggers back to waiting and delete their fired-trigger rows underneath them. Only once that
    /// drain has finished is the new scheduler created, which is when its store is initialized and its
    /// declared jobs and triggers are applied.
    /// </para>
    /// <para>
    /// A restart is observable from outside the process. With a fixed instance id the new generation
    /// checks in under the same <c>SCHEDULER_STATE</c> row; with <c>AUTO</c> it takes a new one and the
    /// old row expires the way a stopped node's does. Either way a clustered peer sees what it sees when
    /// a node is restarted, because that is what this is. A job that outlives the drain is at-least-once:
    /// the new generation's recovery re-fires it exactly as it would after a crash.
    /// </para>
    /// <para>
    /// A name this runtime does not hold and the container did not register is a
    /// <see cref="SchedulerNotFoundException" />. A name it holds but whose scheduler was shut down —
    /// by hand, by the host, or by a restart whose drain gave up — is not an error: there is simply
    /// nothing to shut down first, and this builds the next generation.
    /// </para>
    /// </remarks>
    /// <param name="schedulerName">The scheduler's name.</param>
    /// <param name="options">
    /// How long to wait for the outgoing scheduler's jobs, and whether to start the new one. The default
    /// waits thirty seconds and starts the new scheduler if the old one was running.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The new scheduler, created and bound.</returns>
    /// <exception cref="SchedulerNotFoundException">
    /// Neither this runtime nor the container holds a scheduler of that name.
    /// </exception>
    /// <exception cref="SchedulerConfigException">
    /// The scheduler has no recipe that can be replayed — the default scheduler, or one whose recipe
    /// supplies a part as an instance — or the recipe no longer builds. The old scheduler keeps running.
    /// </exception>
    /// <exception cref="SchedulerRestartException">
    /// The outgoing scheduler's jobs were still running when the drain gave up. The old scheduler is
    /// shut down and the new one was not built; ask again once the work has finished.
    /// </exception>
    ValueTask<IScheduler> Restart(
        string schedulerName,
        SchedulerRestartOptions options = default,
        CancellationToken cancellationToken = default);
}
