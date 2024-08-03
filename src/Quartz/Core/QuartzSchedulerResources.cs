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

using Quartz.Spi;

namespace Quartz.Core;

/// <summary>
/// Contains all the resources (<see cref="IJobStore" />,<see cref="IThreadPool" />,
/// etc.) necessary to create a <see cref="QuartzScheduler" /> instance.
/// </summary>
/// <seealso cref="QuartzScheduler" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public sealed class QuartzSchedulerResources
{
    internal static readonly TimeSpan DefaultIdleWaitTime = TimeSpan.FromSeconds(30);
    internal const int DefaultMaxBatchSize = 1;
    internal static readonly TimeSpan DefaultBatchTimeWindow = TimeSpan.Zero;

    private string name = null!;
    private string instanceId = null!;
    private string threadName = null!;
    private IThreadPool threadPool = null!;
    private IJobStore jobStore = null!;
    private IJobRunShellFactory jobRunShellFactory = null!;
    private int _maxBatchSize;
    private TimeSpan _idleWaitTime;
    private TimeSpan _batchTimeWindow;

    public QuartzSchedulerResources()
    {
        _maxBatchSize = DefaultMaxBatchSize;
        _idleWaitTime = DefaultIdleWaitTime;
        _batchTimeWindow = DefaultBatchTimeWindow;
        TimeProvider = TimeProvider.System;
        SchedulerRepository = Impl.SchedulerRepository.Instance;
    }

    /// <summary>
    /// Get or set the name for the <see cref="QuartzScheduler" />.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// if name is null or empty.
    /// </exception>
    public string Name
    {
        get => name;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                ThrowHelper.ThrowArgumentException("Scheduler name cannot be empty.");
            }

            name = value;

            if (threadName is null)
            {
                // thread name not already set, use default thread name
                ThreadName = $"{value}_QuartzSchedulerThread";
            }
        }
    }

    /// <summary>
    /// Get or set the instance Id for the <see cref="QuartzScheduler" />.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// if name is null or empty.
    /// </exception>
    public string InstanceId
    {
        get => instanceId;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                ThrowHelper.ThrowArgumentException("Scheduler instanceId cannot be empty.");
            }

            instanceId = value;
        }
    }


    /// <summary>
    /// Get or set the name for the <see cref="QuartzSchedulerThread" />.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// if name is null or empty.
    /// </exception>
    public string ThreadName
    {
        get => threadName;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                ThrowHelper.ThrowArgumentException("Scheduler thread name cannot be empty.");
            }

            threadName = value;
        }
    }

    /// <summary>
    /// Get or set the <see cref="ThreadPool" /> for the <see cref="QuartzScheduler" />
    /// to use.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// if threadPool is null.
    /// </exception>
    public IThreadPool ThreadPool
    {
        get => threadPool;
        set
        {
            if (value is null)
            {
                ThrowHelper.ThrowArgumentException("ThreadPool cannot be null.");
            }
            threadPool = value;
        }
    }

    /// <summary>
    /// Get or set the <see cref="IJobStore" /> for the <see cref="QuartzScheduler" />
    /// to use.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// if jobStore is null.
    /// </exception>
    public IJobStore JobStore
    {
        get => jobStore;
        set
        {
            if (value is null)
            {
                ThrowHelper.ThrowArgumentException("JobStore cannot be null.");
            }
            jobStore = value;
        }
    }

    /// <summary>
    /// Get or set the <see cref="JobRunShellFactory" /> for the <see cref="QuartzScheduler" />
    /// to use.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// if jobRunShellFactory is null.
    /// </exception>
    public IJobRunShellFactory JobRunShellFactory
    {
        get => jobRunShellFactory;
        set
        {
            if (value is null)
            {
                ThrowHelper.ThrowArgumentException("JobRunShellFactory cannot be null.");
            }
            jobRunShellFactory = value;
        }
    }

    /// <summary>
    /// Gets the unique identifier.
    /// </summary>
    /// <param name="schedName">Name of the scheduler.</param>
    /// <param name="schedInstId">The scheduler instance id.</param>
    /// <returns></returns>
    public static string GetUniqueIdentifier(string schedName, string schedInstId)
    {
        return $"{schedName}_$_{schedInstId}";
    }

    /// <summary>
    /// Gets the unique identifier.
    /// </summary>
    /// <returns></returns>
    public string GetUniqueIdentifier()
    {
        return GetUniqueIdentifier(name, instanceId);
    }

    /// <summary>
    /// Add the given <see cref="ISchedulerPlugin" /> for the
    /// <see cref="QuartzScheduler" /> to use. This method expects the plugin's
    /// "initialize" method to be invoked externally (either before or after
    /// this method is called).
    /// </summary>
    /// <param name="plugin"></param>
    public void AddSchedulerPlugin(ISchedulerPlugin plugin)
    {
        SchedulerPlugins.Add(plugin);
    }

    /// <summary>
    /// Get the <see cref="IList&lt;ISchedulerPlugin&gt;" /> of all  <see cref="ISchedulerPlugin" />s for the
    /// <see cref="QuartzScheduler" /> to use.
    /// </summary>
    /// <returns></returns>
    public IList<ISchedulerPlugin> SchedulerPlugins { get; } = new List<ISchedulerPlugin>(10);

    /// <summary>
    /// Gets or sets a value indicating whether to make scheduler thread daemon.
    /// </summary>
    /// <value>
    /// 	<c>true</c> if scheduler should be thread daemon; otherwise, <c>false</c>.
    /// </value>
    public bool MakeSchedulerThreadDaemon { get; set; }

    /// <summary>
    /// Gets or sets a value that determines how long the scheduler should wait before checking again
    /// when there is no current trigger to fire.
    /// </summary>
    /// <value>
    /// A value that determines how long the scheduler should wait before checking again when there
    /// is no current trigger to fire. The default value is <c>30</c> seconds.
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than <see cref="TimeSpan.Zero"/>.</exception>
    public TimeSpan IdleWaitTime
    {
        get => _idleWaitTime;
        set
        {
            if (value < TimeSpan.Zero)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(value), $"Cannot be less than {nameof(TimeSpan)}.{nameof(TimeSpan.Zero)}.");
            }

            _idleWaitTime = value;
        }
    }

    /// <summary>
    /// Gets or sets the time window for which it is allowed to "pre-acquire" triggers to fire.
    /// </summary>
    /// <value>
    /// The time window for which it is allowed to "pre-acquire" triggers to fire. The default value is
    /// <see cref="TimeSpan.Zero"/>.
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than <see cref="TimeSpan.Zero"/>.</exception>
    public TimeSpan BatchTimeWindow
    {
        get => _batchTimeWindow;
        set
        {
            if (value < TimeSpan.Zero)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(value), $"Cannot be less than {nameof(TimeSpan)}.{nameof(TimeSpan.Zero)}.");
            }

            _batchTimeWindow = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of triggers to acquire in an iteration of <see cref="QuartzSchedulerThread"/>.
    /// </summary>
    /// <value>
    /// The maximum number of triggers to acquire in an iteration of <see cref="QuartzSchedulerThread"/>. The default
    /// value is <c>1</c>.
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than <c>1</c>.</exception>
    public int MaxBatchSize
    {
        get => _maxBatchSize;
        set
        {
            if (value < 1)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(value), $"Cannot be less than 1.");
            }

            _maxBatchSize = value;
        }
    }

    public bool InterruptJobsOnShutdown { get; set; }

    public bool InterruptJobsOnShutdownWithWait { get; set; }

    public TimeProvider TimeProvider { get; set; }

    internal ISchedulerRepository SchedulerRepository { get; set; }
}