#nullable enable

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A job type registration can belong to one scheduler, so two schedulers in one container can build
/// the same job type differently.
/// </summary>
/// <remarks>
/// The registration and the resolution have to agree: <c>AddJob&lt;T&gt;</c> registers the type
/// unkeyed and the job factory reads the unkeyed registration, so keying only one half would leave a
/// registration nobody reads. What is keyed here is what <c>AddJobType</c> declares; the unkeyed
/// registration remains the fallback, and the default scheduler — which has no service key — resolves
/// in exactly one lookup, as it always has.
/// </remarks>
public sealed class JobTypeKeyingTest
{
    [Test]
    public async Task TwoSchedulersCanBuildOneJobTypeDifferently()
    {
        JobRunLog log = new();

        ServiceCollection services = new();
        services.AddSingleton(log);

        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(o => o.InstanceName = "shared");
            Schedule(q, "shared", triggers: 1);
        });

        services.AddQuartz("acme", q =>
        {
            q.AddJobType<TenantJob, AcmeJob>();
            Schedule(q, "acme", triggers: 1);
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        Task complete = log.Expect(2);
        await RunSchedulers(provider, complete, "acme");

        log.Runs.Select(x => (x.Scheduler, x.Marker)).Should().BeEquivalentTo(
            [("shared", "plain"), ("acme", "acme")],
            "the implementation registered for one scheduler is that scheduler's, and the other keeps "
            + "what the container holds unkeyed");
    }

    [Test]
    public async Task ASchedulerWithNoRegistrationOfItsOwnStillResolvesTheContainers()
    {
        JobRunLog log = new();

        ServiceCollection services = new();
        services.AddSingleton(log);

        // An application registering the job type itself, which is where a job type most naturally
        // goes when there is nothing per-scheduler about it.
        services.AddScoped<TenantJob, AcmeJob>();

        services.AddQuartz("initech", q => Schedule(q, "initech", triggers: 1));

        await using ServiceProvider provider = services.BuildServiceProvider();

        Task complete = log.Expect(1);
        await RunSchedulers(provider, complete, "initech");

        log.Runs.Select(x => x.Marker).Should().BeEquivalentTo(["acme"],
            "keying the lookup adds a place to look first, it does not stop the container's own "
            + "registration from being read");
    }

    [Test]
    public async Task OneSchedulerCanHoldAJobTypeAsASingletonWhileAnotherKeepsItPerFire()
    {
        JobRunLog log = new();

        ServiceCollection services = new();
        services.AddSingleton(log);

        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(o => o.InstanceName = "shared");
            Schedule(q, "shared", triggers: 2);
        });

        services.AddQuartz("acme", q =>
        {
            q.AddJobType<TenantJob>(ServiceLifetime.Singleton);
            Schedule(q, "acme", triggers: 2);
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        Task complete = log.Expect(4);
        await RunSchedulers(provider, complete, "acme");

        log.Runs.Where(x => x.Scheduler == "acme").Select(x => x.Instance).Distinct()
            .Should().ContainSingle("a job type this scheduler holds as a singleton serves every fire from one instance");

        log.Runs.Where(x => x.Scheduler == "shared").Select(x => x.Instance).Distinct()
            .Should().HaveCount(2, "the other scheduler keeps the per-fire lifetime AddJob registers");
    }

    private static void Schedule(IQuartzBuilder builder, string tenant, int triggers)
    {
        builder.AddJob<TenantJob>(job => job.WithIdentity("job", tenant));

        for (int i = 0; i < triggers; i++)
        {
            int index = i;
            builder.AddTrigger<TenantJob>(trigger => trigger
                .WithIdentity($"trigger-{index}", tenant)
                .ForJob("job", tenant)
                .StartNow());
        }
    }

    private static async Task RunSchedulers(IServiceProvider provider, Task complete, params string[] named)
    {
        List<IScheduler> schedulers = [];

        ISchedulerFactory? shared = provider.GetService<ISchedulerFactory>();
        if (shared is not null)
        {
            schedulers.Add(await shared.GetScheduler());
        }

        foreach (string name in named)
        {
            schedulers.Add(await provider.GetRequiredKeyedService<ISchedulerFactory>(name).GetScheduler());
        }

        try
        {
            foreach (IScheduler scheduler in schedulers)
            {
                await scheduler.Start();
            }

            Task finished = await Task.WhenAny(complete, Task.Delay(TimeSpan.FromSeconds(30)));
            finished.Should().BeSameAs(complete, "every scheduled job should have run");
        }
        finally
        {
            foreach (IScheduler scheduler in schedulers)
            {
                await scheduler.Shutdown(waitForJobsToComplete: true);
            }
        }
    }

    /// <summary>
    /// What ran, on which scheduler, and which instance did it.
    /// </summary>
    public sealed class JobRunLog
    {
        private readonly Lock gate = new();
        private readonly List<(string Scheduler, string Marker, object Instance)> runs = [];
        private readonly TaskCompletionSource complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int expected = int.MaxValue;

        public IReadOnlyList<(string Scheduler, string Marker, object Instance)> Runs
        {
            get
            {
                lock (gate)
                {
                    return [.. runs];
                }
            }
        }

        public Task Expect(int count)
        {
            lock (gate)
            {
                expected = count;
                if (runs.Count >= expected)
                {
                    complete.TrySetResult();
                }
            }

            return complete.Task;
        }

        public void Record(string scheduler, string marker, object instance)
        {
            lock (gate)
            {
                runs.Add((scheduler, marker, instance));
                if (runs.Count >= expected)
                {
                    complete.TrySetResult();
                }
            }
        }
    }

    public class TenantJob : IJob
    {
        private readonly JobRunLog log;

        public TenantJob(JobRunLog log)
        {
            this.log = log;
        }

        protected virtual string Marker => "plain";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            log.Record(context.Scheduler.SchedulerName, Marker, this);
            return default;
        }
    }

    public sealed class AcmeJob : TenantJob
    {
        public AcmeJob(JobRunLog log) : base(log)
        {
        }

        protected override string Marker => "acme";
    }
}
