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

using Quartz.Configuration;

namespace Quartz;

/// <summary>
/// How the dashboard is served and what it is allowed to do.
/// </summary>
/// <remarks>
/// The dashboard renders the schedulers in its own process, through the <c>IQuartzApiClient</c>
/// registered in the container. <see cref="AttachStore" /> is the one thing here that adds to that set:
/// it points the dashboard at a database and every scheduler in it becomes a window.
/// </remarks>
public sealed class QuartzDashboardOptions
{
    internal const string DefaultDashboardPath = "/quartz";

    private readonly List<AttachedStoreDescriptor> attachedStores = [];

    /// <summary>
    /// The databases this dashboard has been pointed at, in the order they were attached.
    /// </summary>
    internal IReadOnlyList<AttachedStoreDescriptor> AttachedStores => attachedStores;

    /// <summary>
    /// Points the dashboard at a database, so that every scheduler in it is shown as a window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is asked of the processes that run those schedulers: no port towards them, no plugin in
    /// them, no change to them at all. The dashboard discovers the <c>SCHED_NAME</c> values the database
    /// holds and builds one never-started scheduler over the same store for each — a <em>window</em>,
    /// <see cref="SchedulerOrigin.Window" /> in the listing, spelled <c>prod/reporting</c> wherever a
    /// scheduler name is shown. This is the right answer for a cluster behind a load balancer, where
    /// dialing one node reaches an arbitrary one of them.
    /// </para>
    /// <para>
    /// <paramref name="store" /> is the same <c>IPersistentStoreBuilder</c> callback
    /// <c>UsePersistentStore</c> takes, and it has to be <em>the cluster's own</em>: the dialect, the
    /// table prefix, the serializer, and any <c>UseTriggerPersistenceDelegate</c> the nodes were given.
    /// A window reads the blobs the nodes wrote, so a serializer that does not match is a page that
    /// fails on the first trigger it cannot rebuild. Add <c>UseExecutionHistory()</c> when the cluster
    /// keeps its history in the database, which is what puts executions on the window's History page;
    /// without it a window has no history to show, because nothing it can read was written by a node.
    /// </para>
    /// <para>
    /// What a window can do is what the store is: jobs, triggers and calendars are read, added, paused,
    /// resumed, rescheduled, triggered and deleted, and whichever node picks the work up honours it.
    /// What it cannot do is anything that belongs to one process — starting, standing down, shutting
    /// down, interrupting a running job — and the pages hide those rather than offering a button that
    /// would act on a scheduler this process built and never runs.
    /// </para>
    /// <para>
    /// A scheduler name the database holds that this process already has a scheduler under is refused,
    /// naming both, and the rest of the database is shown as usual.
    /// </para>
    /// </remarks>
    /// <param name="target">
    /// The name this database is known by, which is the first half of every window's identity. It may
    /// not contain <c>/</c>, which is what separates it from the scheduler's name.
    /// </param>
    /// <param name="store">How to reach the database — the cluster's own store configuration.</param>
    /// <param name="configure">How often the database is asked again which schedulers are in it.</param>
    /// <returns>The same options, so calls can be chained.</returns>
    /// <exception cref="ArgumentException"><paramref name="target" /> is empty or contains <c>/</c>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="store" /> is null.</exception>
    /// <exception cref="InvalidOperationException">A store is already attached under that name.</exception>
    public QuartzDashboardOptions AttachStore(
        string target,
        Action<IPersistentStoreBuilder> store,
        Action<AttachStoreOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(store);

        if (target.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{target}' cannot be a target name: '/' is what separates a target from the scheduler name in "
                + "a window's identity, so a target containing one would be unreadable wherever a window is shown.",
                nameof(target));
        }

        foreach (AttachedStoreDescriptor existing in attachedStores)
        {
            if (string.Equals(existing.Target, target, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"A store is already attached as '{target}'. The name is half of every window's identity, so "
                    + "two databases under one name would give two schedulers one spelling; attach the second one "
                    + "under a name of its own.");
            }
        }

        AttachStoreOptions options = new();
        configure?.Invoke(options);

        attachedStores.Add(new AttachedStoreDescriptor(target, store, options.RediscoveryInterval));
        return this;
    }

    /// <summary>
    /// The base path the dashboard UI is served from. Defaults to "/quartz".
    /// A custom value is honored when the dashboard hosts its own Blazor root
    /// (the parameterless <c>MapQuartzDashboard()</c> overload). When integrating into an
    /// existing Blazor application the dashboard page routes are fixed at "/quartz".
    /// </summary>
    /// <remarks>
    /// <c>MapQuartzDashboard(pattern)</c> says the same thing where the endpoints are mapped, which is
    /// where the rest of an application's routes are written, and a pattern given there wins over this.
    /// </remarks>
    public string DashboardPath { get; set; } = DefaultDashboardPath;

    /// <summary>
    /// The authorization policy the dashboard's pages, hub, circuit and assets are held to. Null — the
    /// default — applies none of its own, so what guards the dashboard is whatever the application's
    /// pipeline already does.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// The authorization policy each scheduler is held to, evaluated against a
    /// <see cref="SchedulerResource" /> carrying that scheduler's name. Null — the default — leaves the
    /// dashboard as it was: whoever passes <see cref="AuthorizationPolicy" /> sees every scheduler in the
    /// process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set it and the scheduler picker offers only the schedulers the visitor passes for, a page opened on
    /// one they do not says so without reading anything, and the live-events hub refuses to subscribe them
    /// to it. The two policies compose: <see cref="AuthorizationPolicy" /> decides who reaches the
    /// dashboard at all, this one decides which schedulers they see once they are in, and
    /// <see cref="ReadOnly" /> still decides what anyone may change.
    /// </para>
    /// <para>
    /// It is the same policy and the same resource the HTTP API's
    /// <c>QuartzHttpApiOptions.SchedulerAuthorizationPolicy</c> evaluates, so one
    /// <c>AuthorizationHandler&lt;TRequirement, SchedulerResource&gt;</c> answers for both surfaces.
    /// </para>
    /// </remarks>
    public string? SchedulerAuthorizationPolicy { get; set; }

    /// <summary>
    /// Hides every mutating action — pause, resume, trigger now, reschedule, unschedule, delete — so the
    /// dashboard is a view of the schedulers rather than a way to drive them.
    /// </summary>
    /// <remarks>
    /// Enforced by the client rather than by the pages alone: a mutation refused here is refused
    /// wherever it is called from, so hiding a button is not what makes it safe.
    /// </remarks>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Which job types a call through the dashboard's <c>IQuartzApiClient</c> may name. Null — the
    /// default — allows every one, which is what every earlier release did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the narrowing for the thing <c>SECURITY.md</c> says is not a vulnerability: a visitor who
    /// passes authorization can add or schedule a job of any type that implements <see cref="IJob" />,
    /// and <c>Quartz.Jobs</c>' <c>NativeJob</c> implements it and starts the executable its job data
    /// names. Where <see cref="ReadOnly" /> refuses every mutation, this refuses one kind of them.
    /// </para>
    /// <para>
    /// The predicate is given the job type <em>name</em>, as it was written, and nothing resolves it
    /// first — the dashboard stores a job type name unresolved for the same reason the HTTP API does, so
    /// that a name arriving from outside never makes this process probe its assemblies. Match on the
    /// string, and remember that one type has more than one spelling: <c>Acme.Jobs.Nightly, Acme.Jobs</c>
    /// and the same name carrying <c>Version</c>, <c>Culture</c> and <c>PublicKeyToken</c> are both it.
    /// </para>
    /// <para>
    /// A refused name raises <see cref="UnauthorizedAccessException" />, the way a scheduler the visitor
    /// fails <see cref="SchedulerAuthorizationPolicy" /> for does. Enforced by the client rather than by
    /// the pages, so it holds wherever the call is made from. It says nothing about the HTTP API, which
    /// is mapped and configured separately: <c>QuartzHttpApiOptions.IsJobTypeAllowed</c> is the same
    /// setting for that surface.
    /// </para>
    /// </remarks>
    public Func<string, bool>? IsJobTypeAllowed { get; set; }

    /// <summary>
    /// How far back the dashboard's own history store keeps executions and misfires. Defaults to 24 hours.
    /// </summary>
    /// <remarks>
    /// The count bound below cannot answer for a scheduler that has gone quiet: it keeps whatever it last
    /// recorded, so a page shows executions from an arbitrary distance in the past with nothing to say how
    /// old they are. Both bounds apply, and whichever bites first wins.
    /// </remarks>
    public TimeSpan HistoryRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How many executions and how many misfires the dashboard's own history store keeps per scheduler,
    /// oldest dropped first. Defaults to 2000 of each.
    /// </summary>
    public int HistoryMaxEntriesPerScheduler { get; set; } = 2000;

    /// <summary>
    /// <see cref="DashboardPath"/> normalized to a rooted path without a trailing slash,
    /// falling back to <see cref="DefaultDashboardPath"/> when unset or empty.
    /// </summary>
    internal string TrimmedDashboardPath => DashboardPathCache.Trimmed;

    /// <summary>
    /// Whether <see cref="DashboardPath"/> differs from the compile-time default "/quartz".
    /// A custom path implies the standalone hosting mode because it is rejected when
    /// integrating with an existing Blazor application.
    /// </summary>
    internal bool HasCustomDashboardPath => DashboardPathCache.HasCustom;

    /// <summary>
    /// <see cref="TrimmedDashboardPath"/> in its percent-encoded form, as browsers emit it in
    /// request URIs and the &lt;base href&gt;. Server-side route patterns keep the raw form
    /// (route matching compares decoded values); client-side URI comparisons need this one.
    /// </summary>
    internal string EscapedDashboardPath => DashboardPathCache.Escaped;

    private DerivedDashboardPath? dashboardPathCache;

    /// <summary>
    /// Values derived from <see cref="DashboardPath"/>, computed once and reused — they are read
    /// on Blazor render hot paths (links, route matching) while the option itself only changes
    /// during startup configuration. Held behind a single reference so a concurrent reader always
    /// observes a fully-populated instance (reference reads/writes are atomic) even if the option
    /// is mutated mid-render.
    /// </summary>
    private DerivedDashboardPath DashboardPathCache
    {
        get
        {
            string source = DashboardPath;
            DerivedDashboardPath? cache = dashboardPathCache;
            if (cache is null || !string.Equals(cache.Source, source, StringComparison.Ordinal))
            {
                string trimmed = DefaultDashboardPath;
                if (!string.IsNullOrWhiteSpace(source))
                {
                    string candidate = source.Trim().Trim('/');
                    if (candidate.Length > 0)
                    {
                        trimmed = "/" + candidate;
                    }
                }

                string escaped = new Uri("http://localhost" + trimmed).AbsolutePath;
                bool hasCustom = !string.Equals(trimmed, DefaultDashboardPath, StringComparison.OrdinalIgnoreCase);
                cache = new DerivedDashboardPath(source, trimmed, escaped, hasCustom);
                dashboardPathCache = cache;
            }

            return cache;
        }
    }

    private sealed record DerivedDashboardPath(string Source, string Trimmed, string Escaped, bool HasCustom);
}
