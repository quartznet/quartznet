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

using System.Diagnostics;

using Quartz.Diagnostics;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// A store source of its own, a second source standing in for the database driver, and a listener
/// on both whose sampler is parent-based, as OpenTelemetry's default is.
/// </summary>
/// <remarks>
/// <para>
/// Its own sources rather than the Quartz one, so that a fixture's listener, which records every
/// Quartz activity whatever its parent, has no say in what is sampled here.
/// </para>
/// <para>
/// With <c>listenToQuartz</c> the listener subscribes to the Quartz source as well, so that it has a
/// listener, which is the condition for the unrecorded stand-in. Only this instance's own sources'
/// activities are kept in <see cref="Stopped" />.
/// </para>
/// </remarks>
internal sealed class ParentBasedTracing : IDisposable
{
    internal const string ChildName = "db.query";

    private readonly string id = Guid.NewGuid().ToString("N");
    private readonly ActivitySource driverSource;
    private readonly ActivitySource callerSource;
    private readonly ActivityListener listener;
    private readonly List<Activity> stopped = [];
    private int childrenStarted;

    internal ParentBasedTracing(bool listenToQuartz = false)
    {
        StoreSource = new ActivitySource($"Quartz.Tests.Store.{id}");
        driverSource = new ActivitySource($"Quartz.Tests.Driver.{id}");
        callerSource = new ActivitySource($"Quartz.Tests.Caller.{id}");

        listener = new ActivityListener
        {
            ShouldListenTo = source => IsOurs(source)
                || (listenToQuartz && source.Name == QuartzInstrumentation.ActivitySourceName),
            Sample = static (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Parent == default || options.Parent.TraceFlags.HasFlag(ActivityTraceFlags.Recorded)
                    ? ActivitySamplingResult.AllDataAndRecorded
                    : ActivitySamplingResult.None,
            ActivityStopped = activity =>
            {
                if (!IsOurs(activity.Source))
                {
                    return;
                }

                lock (stopped)
                {
                    stopped.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
    }

    internal ActivitySource StoreSource { get; }

    internal List<Activity> Stopped
    {
        get
        {
            lock (stopped)
            {
                return [.. stopped];
            }
        }
    }

    /// <summary>The driver spans asked for, whether or not the sampler recorded them.</summary>
    internal int ChildrenStarted => Volatile.Read(ref childrenStarted);

    internal List<Activity> StoppedChildren => Stopped.Where(a => a.OperationName == ChildName).ToList();

    internal Activity StartCaller() => callerSource.StartActivity("caller")!;

    /// <summary>
    /// Opens and closes a driver span under whatever is current, as a database command does.
    /// </summary>
    internal void OpenChild()
    {
        Interlocked.Increment(ref childrenStarted);
        using Activity query = driverSource.StartActivity(ChildName, ActivityKind.Client);
    }

    /// <summary>
    /// A database call that suspends inside the span the driver opens for it.
    /// </summary>
    internal async ValueTask Query()
    {
        Interlocked.Increment(ref childrenStarted);
        using Activity query = driverSource.StartActivity(ChildName, ActivityKind.Client);
        await Task.Yield();
    }

    public void Dispose()
    {
        listener.Dispose();
        StoreSource.Dispose();
        driverSource.Dispose();
        callerSource.Dispose();
    }

    private bool IsOurs(ActivitySource source) => source.Name.EndsWith(id, StringComparison.Ordinal);
}
