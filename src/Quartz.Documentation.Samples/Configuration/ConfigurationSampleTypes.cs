using System.Data.Common;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Documentation.Samples.Configuration;

/// <summary>
/// The types the configuration reference names as stand-ins for your own.
/// </summary>
/// <remarks>
/// They exist only so the registrations that name them compile; none of them appears on a page.
/// </remarks>
public sealed class MyJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public sealed class MyThreadPoolOptions
{
    public int Slots { get; set; }
}

public sealed class MyThreadPool : IThreadPool
{
    public int PoolSize => 0;

    public ValueTask<bool> Drain(CancellationToken cancellationToken = default) => default;

    public ValueTask Initialize(CancellationToken cancellationToken = default) => default;

    public ValueTask Shutdown(bool waitForJobsToComplete = true, CancellationToken cancellationToken = default) => default;

    public ValueTask<bool> TryRun(Func<ValueTask> action, CancellationToken cancellationToken = default) => default;

    public ValueTask<int> WaitForAvailableThreads(CancellationToken cancellationToken = default) => default;
}

public sealed class MyJobFactory : IJobFactory
{
    public ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default) => default;

    public ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default) => default;
}

public sealed class MySchedulerListener : ISchedulerListener;

public sealed class MyJobListener : IJobListener;

public sealed class MyTriggerListener : ITriggerListener;

public sealed class MyDbProvider : IDbProvider
{
    public string ConnectionString => "";

    public DbMetadata Metadata => new();

    public DbCommand CreateCommand() => null!;

    public DbConnection CreateConnection() => null!;

    public void Shutdown()
    {
    }
}

/// <summary>Stands in for an ADO.NET provider's own types in the <c>DbMetadata</c> sample.</summary>
public sealed class MyConnection;

public sealed class MyCommand;

public enum MyDbType
{
    Text = 0,
}

public sealed class MyParameter
{
    public MyDbType MyDbType { get; set; }
}

public sealed class MyException : Exception
{
    public MyException()
    {
    }

    public MyException(string message) : base(message)
    {
    }

    public MyException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
