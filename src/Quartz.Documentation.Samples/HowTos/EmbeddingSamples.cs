using System.Data.Common;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/embedding-quartz-in-a-library.md.
/// </summary>
public static class EmbeddingSamples
{
    public static void OwnScheduler(IServiceCollection services, string connectionString)
    {
        #region sample_embedding_named_scheduler

        // Everything this library registers lands under the scheduler's own service key: its thread
        // pool, its job store, its listeners. Nothing it does can starve the application's scheduler,
        // and nothing the application configures reaches this one.
        services.AddQuartz("acme.outbox", q =>
        {
            q.UsePersistentStore(store => store.UseSqlServer(connectionString));
            q.UseDefaultThreadPool(maxConcurrency: 4);
        });

        #endregion
    }

    public static void OwnSchedulerConsumer(IServiceProvider provider)
    {
        #region sample_embedding_named_scheduler_resolve

        IScheduler scheduler = provider.GetRequiredKeyedService<IScheduler>("acme.outbox");

        #endregion
    }

    #region sample_embedding_library_options

    public static IServiceCollection AddAcmeOutboxScheduler(
        this IServiceCollection services,
        Action<AcmeOutboxOptions>? configure = null)
    {
        services.AddQuartz("acme.outbox", q =>
        {
            // The library's own settings, named for this scheduler. A component the container
            // builds for "acme.outbox" and taking IOptions<AcmeOutboxOptions> is handed these.
            q.ConfigureOptions<AcmeOutboxOptions>(options =>
            {
                options.DrainInterval = TimeSpan.FromSeconds(30);
                configure?.Invoke(options);
            });

            // AddPlugin<T, TOptions> is the same thing said for a plugin
            q.AddPlugin<AcmeOutboxPlugin, AcmeOutboxOptions>(name: "acmeOutbox");
        });

        return services;
    }

    #endregion

    public static void SharedSchedulerExecutionGroup(IServiceCollection services)
    {
        #region sample_embedding_execution_group

        services.ConfigureAllQuartzSchedulers(q => q.UseExecutionLimits(limits => limits
            .ForGroup("acme.outbox", maxConcurrent: 4)
            .ForOtherGroups(int.MaxValue)));

        #endregion
    }

    #region sample_embedding_contributor

    public static IServiceCollection AddAcmeOutbox(this IServiceCollection services)
    {
        // Contributing twice is contributing twice: Quartz will not apply one delegate instance to one
        // scheduler more than once, but a second call here creates a second delegate. Guard the
        // extension method, not the delegate.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(AcmeOutboxMarker)))
        {
            return services;
        }

        services.AddSingleton<AcmeOutboxMarker>();

        // Applied to every scheduler in the container - those registered before this call, and those
        // registered after it. The application decides how many schedulers there are and what they are
        // called; this does not have to know.
        services.ConfigureAllQuartzSchedulers(q =>
        {
            q.AddJob<DrainOutboxJob>(j => j
                .WithIdentity(DrainOutboxJob.Key)
                .StoreDurably());

            q.AddTrigger(t => t
                .WithIdentity("drain", DrainOutboxJob.Key.Group)
                .ForJob(DrainOutboxJob.Key)
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(30)).RepeatForever()));
        });

        return services;
    }

    private sealed class AcmeOutboxMarker;

    #endregion

    #region sample_embedding_contributor_scheduler_name

    public static IServiceCollection AddAcmeOutboxToOneScheduler(this IServiceCollection services, string schedulerName)
    {
        services.ConfigureAllQuartzSchedulers(q =>
        {
            // "" is the default scheduler; anything else is the name it was registered under.
            if (!string.Equals(q.SchedulerName, schedulerName, StringComparison.Ordinal))
            {
                return;
            }

            q.AddJob<DrainOutboxJob>(j => j.WithIdentity(DrainOutboxJob.Key).StoreDurably());
        });

        return services;
    }

    #endregion

    public static void DeferredStart(IHostApplicationBuilder builder)
    {
        #region sample_embedding_deferred_start

        builder.Services.AddQuartz("acme.outbox", q => q.UseInMemoryStore());

        // Built, initialized and bound with the host, and then left in Created. The library starts it
        // when whatever it depends on - a bus connection, a leader lease, a migration - is ready.
        builder.Services.AddQuartzHostedService("acme.outbox", options => options.AutoStart = false);

        #endregion
    }

    public static void Middleware(IServiceCollection services)
    {
        #region sample_embedding_middleware_registration

        services.ConfigureAllQuartzSchedulers(q => q.AddJobMiddleware<OutboxScopeMiddleware>());

        #endregion
    }

    #region sample_embedding_middleware

    public sealed class OutboxScopeMiddleware(IOutboxContext outbox) : IJobExecutionMiddleware
    {
        public async ValueTask Invoke(
            IJobExecutionContext context,
            JobExecutionDelegate next,
            CancellationToken cancellationToken = default)
        {
            // Ambient state the library's own services read, established around the job rather than
            // inside a wrapper job that has to know how to construct the real one.
            using (outbox.Begin(context.FireInstanceId))
            {
                await next(context, cancellationToken);
            }
        }
    }

    #endregion

    #region sample_embedding_typed_job

    public sealed record SendReminder(string ConversationId, string MessageId, string Text);

    public sealed class SendReminderJob(IReminderSink sink) : IJob<SendReminder>
    {
        public ValueTask Execute(
            IJobExecutionContext context,
            SendReminder input,
            CancellationToken cancellationToken = default)
        {
            return sink.Send(input.ConversationId, input.Text, cancellationToken);
        }
    }

    #endregion

    #region sample_embedding_schedule_correlated

    public sealed class Conversations(IScheduler scheduler)
    {
        public async ValueTask<TriggerKey> Remind(SendReminder reminder, TimeSpan delay, CancellationToken cancellationToken)
        {
            ScheduledOneOffJob scheduled = await scheduler.ScheduleJob<SendReminderJob, SendReminder>(
                reminder,
                delay,
                new OneOffJobOptions
                {
                    // The name is this one firing; the group is what the firing is about. Both are the
                    // library's own identifiers, so nothing has to be looked up to cancel later.
                    Name = reminder.MessageId,
                    Group = reminder.ConversationId,
                    Replace = true
                },
                cancellationToken);

            // The call answers with the key it stored and the time the store will first fire it at.
            return scheduled.TriggerKey;
        }

        public ValueTask<bool> Cancel(TriggerKey firing, CancellationToken cancellationToken)
        {
            return scheduler.UnscheduleJob(firing, cancellationToken);
        }

        public ValueTask<List<TriggerKey>> CancelConversation(string conversationId, CancellationToken cancellationToken)
        {
            // Everything still scheduled for that conversation, in one store operation, answering with
            // the keys it removed.
            return scheduler.UnscheduleJobs(GroupMatcher<TriggerKey>.GroupEquals(conversationId), cancellationToken);
        }
    }

    #endregion

    public static async ValueTask Upsert(
        IScheduler scheduler,
        SendReminder reminder,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        #region sample_embedding_upsert

        ITrigger trigger = TriggerBuilder.Create<SendReminderJob>(scheduler.TimeProvider)
            .WithIdentity(reminder.MessageId, reminder.ConversationId)
            .ForJob(SchedulerConstants.ScheduledJobKey<SendReminderJob>())
            .StartAt(at)
            .UsingInput(reminder)
            .Build();

        await scheduler.ScheduleJob(trigger, ScheduleJobOptions.Replacing, cancellationToken);

        #endregion
    }

    public static void AcceptEnlistedTransactions(IServiceCollection services, string connectionString)
    {
        #region sample_embedding_accept_enlisted

        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(connectionString);
            store.ConfigureStore(options => options.AcceptEnlistedTransactions = true);
        }));

        #endregion
    }

    #region sample_embedding_enlist

    public sealed class Outbox(IScheduler scheduler)
    {
        /// <summary>
        /// Schedules inside a transaction the caller owns, so the scheduling and whatever else that
        /// transaction did commit together or not at all.
        /// </summary>
        public async ValueTask Enqueue(
            DbTransaction transaction,
            SendReminder reminder,
            DateTimeOffset at,
            CancellationToken cancellationToken)
        {
            // The enlistment flows with the asynchronous context, so it has to be established in the
            // same scope as the calls it covers - which is why this takes the transaction rather than
            // establishing one and handing it back.
            using (scheduler.EnlistTransaction(transaction))
            {
                await scheduler.ScheduleJob<SendReminderJob, SendReminder>(
                    reminder,
                    at,
                    new OneOffJobOptions { Name = reminder.MessageId, Group = reminder.ConversationId, Replace = true },
                    cancellationToken);
            }
        }
    }

    #endregion

    public static void TraceContextOff(IServiceCollection services)
    {
        #region sample_embedding_trace_context_off

        services.ConfigureAllQuartzSchedulers(q =>
            q.ConfigureScheduler(options => options.PropagateTraceContext = false));

        #endregion
    }

    public static void RetryPolicyOnATrigger(IServiceCollection services)
    {
        #region sample_embedding_retry_policy

        services.AddQuartz(q =>
        {
            q.AddJob<DrainOutboxJob>(j => j.WithIdentity(DrainOutboxJob.Key).StoreDurably());
            q.AddTrigger<DrainOutboxJob>(t => t
                .ForJob(DrainOutboxJob.Key)
                .WithIdentity("drain-outbox", "acme.outbox")
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever())
                // Four attempts, backing off 10s, 20s, 40s, 80s - but never past the next minute's
                // occurrence, which supersedes a retry that would collide with it.
                .WithRetryPolicy(RetryPolicy.Exponential(4, TimeSpan.FromSeconds(10))));
        });

        #endregion
    }
}

/// <summary>
/// The library-side services the embedding samples name in passing.
/// </summary>
public interface IOutboxContext
{
    IDisposable Begin(string fireInstanceId);
}

public interface IReminderSink
{
    ValueTask Send(string conversationId, string text, CancellationToken cancellationToken = default);
}

public sealed class DrainOutboxJob : IJob
{
    public static JobKey Key { get; } = new("drain-outbox", "acme.outbox");

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}
