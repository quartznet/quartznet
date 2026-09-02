#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Util;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Guards that a configuration mistake is reported when the host starts, by one mechanism, rather than
/// whenever the component that reads it happens to be built.
/// </summary>
/// <remarks>
/// <see cref="IStartupValidator"/> is what a host resolves and runs at the end of <c>Build()</c>, so
/// resolving it here is the same check the application gets — without needing a host.
/// </remarks>
public class OptionsValidationTest
{
    [Test]
    public void ABadSchedulerSettingFailsAtStartupRatherThanAtFirstUse()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.IdleWaitTime = TimeSpan.FromMilliseconds(5)));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*IdleWaitTime*",
            "nothing had to resolve QuartzSchedulerOptions for the mistake to be reported");
    }

    [Test]
    public void ABadThreadPoolSettingFailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UseDefaultThreadPool(options => options.MaxConcurrency = 0));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*MaxConcurrency*");
    }

    /// <summary>
    /// The batch size and the pool size are configured through different builder methods and different
    /// configuration sections, so the pair is only ever wrong by accident — which is what makes it worth
    /// checking.
    /// </summary>
    [Test]
    public void ABatchLargerThanTheThreadPoolFailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseDefaultThreadPool(maxConcurrency: 4);
            q.ConfigureScheduler(options => options.MaxBatchSize = 10);
        });

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*MaxBatchSize*MaxConcurrency*");
    }

    [Test]
    public void ABatchThatFitsTheThreadPoolIsFine()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseDefaultThreadPool(maxConcurrency: 10);
            q.ConfigureScheduler(options => options.MaxBatchSize = 10);
        });

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
    }

    /// <summary>
    /// The ADO.NET options are only checked for a scheduler that chose a persistent store. Validating
    /// them everywhere would make an unset <c>DataSource</c> a startup failure for every in-memory
    /// scheduler, which is a configuration nobody wrote.
    /// </summary>
    [Test]
    public void TheJobStoreOptionsOfAStoreNobodyChoseAreNotChecked()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow("AdoJobStoreOptions.DataSource is unset, and this scheduler never reads it");
    }

    [Test]
    public void AMissingDataSourceOnAPersistentStoreFailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.ConfigureStore(options => options.DataSource = "")));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*DataSource*");
    }

    /// <summary>
    /// Zero would reach ADO.NET as "wait forever", which is the opposite of what anyone setting a
    /// timeout means, and a negative value throws when it is assigned to the command. Both are caught
    /// at startup rather than at the first statement.
    /// </summary>
    [TestCase(0)]
    [TestCase(-1)]
    public void ACommandTimeoutThatIsNotPositiveIsAConfigurationError(int seconds)
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.ConfigureStore(options =>
        {
            options.DataSource = "test";
            options.CommandTimeout = TimeSpan.FromSeconds(seconds);
        })));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*CommandTimeout*");
    }

    [Test]
    public void ACommandTimeoutLeftUnsetIsFine()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.ConfigureStore(options => options.DataSource = "test")));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow("an unset timeout means the provider's own default, not a mistake");
    }

    /// <summary>
    /// Asking for clustering and then switching it off leaves database locking on, no cluster manager
    /// and no check-in row — a shape nobody means to configure. Not clustering is spelled by not calling
    /// <c>UseClustering</c>.
    /// </summary>
    [Test]
    public void ClusteringThatWasAskedForAndThenTurnedOffIsAConfigurationError()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.ConfigureStore(options => options.DataSource = "test");
            store.UseClustering(clustering => clustering.Enabled = false);
        }));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*UseClustering*");
    }

    [Test]
    public void ClusteringLeftEnabledIsFine()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.ConfigureStore(options => options.DataSource = "test");
            store.UseClustering(clustering => clustering.CheckinInterval = TimeSpan.FromSeconds(20));
        }));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
    }

    /// <summary>
    /// The rule is per scheduler: one clustered and one not is an ordinary deployment.
    /// </summary>
    [Test]
    public void ASchedulerWithoutClusteringIsUnaffectedByAnotherThatHasIt()
    {
        var services = new ServiceCollection();
        services.AddQuartz("clustered", q => q.UsePersistentStore(store =>
        {
            store.ConfigureStore(options => options.DataSource = "test");
            store.UseClustering();
        }));
        services.AddQuartz("solo", q => q.UsePersistentStore(store =>
            store.ConfigureStore(options => options.DataSource = "test")));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
        provider.GetRequiredService<IOptionsMonitor<ClusteringOptions>>().Get("solo").Enabled.Should().BeFalse();
    }

    /// <summary>
    /// A key in the flat bag without the <c>quartz.</c> prefix is read by nothing, so it has no symptom
    /// at all — the scheduler simply runs as if the application had said nothing. Startup is the only
    /// place it can be reported.
    /// </summary>
    [Test]
    public void APropertyKeyNothingReadsFailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddQuartz();
        services.Configure<QuartzOptions>(options => options.Properties["scheduler.instanceName"] = "svc");

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*scheduler.instanceName*quartz.scheduler.instanceName*",
                "the report has to name the key and the spelling that would have worked");
    }

    [Test]
    public void APropertyKeyNothingReadsIsAllowedWhenTheConfigurationCheckIsOff()
    {
        var services = new ServiceCollection();
        services.AddQuartz();
        services.Configure<QuartzOptions>(options =>
        {
            options.Properties["quartz.checkConfiguration"] = "false";
            options.Properties["myComponent.setting"] = "value";
        });

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow("checkConfiguration is the existing way to say the keys are yours");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Durations that end up in a timer
    //
    // Each of these is handed to a wait whose own ceiling is lower than a TimeSpan's, so a value past
    // it is refused by the BCL — in an ArgumentOutOfRangeException naming a parameter of whichever
    // method happened to be running, from wherever that method happened to be called. #3577's arrived
    // out of Shutdown. Startup is where a misconfigured duration can still be reported as one.
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// #3577: the store used to start happily on a 90-day frequency, and the failure arrived later,
    /// out of <c>Shutdown</c>, saying only that something called 'delay' was out of range.
    /// </summary>
    [Test]
    public void AMisfireHandlerFrequencyPastTheTimerCeilingFailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.ConfigureStore(options =>
        {
            options.DataSource = "test";
            options.MisfireHandlerFrequency = TimeSpan.FromDays(90);
        })));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*MisfireHandlerFrequency*4294967294ms (49.7 days)*",
                "the report has to name the option and the ceiling, and the one out of Task.Delay named neither");
    }

    [Test]
    public void AMisfireHandlerFrequencyAtTheTimerCeilingIsFine()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.ConfigureStore(options =>
        {
            options.DataSource = "test";
            options.MisfireHandlerFrequency = TimerLimits.MaxDelay;
        })));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow("the ceiling is the longest delay a timer accepts, not the first it refuses");
    }

    /// <summary>
    /// Unset, the frequency <em>is</em> the threshold, so the threshold is what the misfire handler
    /// sleeps for — and it is the setting the application wrote, so it is the one named.
    /// </summary>
    [Test]
    public void AMisfireThresholdPastTheTimerCeilingFailsWhenItIsAlsoTheHandlerFrequency()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.ConfigureStore(options =>
        {
            options.DataSource = "test";
            options.MisfireThreshold = TimeSpan.FromDays(90);
        })));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*MisfireThreshold*MisfireHandlerFrequency is unset*");
    }

    /// <summary>
    /// With a frequency of its own the threshold never reaches a timer: it is only ever subtracted
    /// from a clock reading, so it is bounded by nothing but the calendar.
    /// </summary>
    [Test]
    public void AMisfireThresholdPastTheTimerCeilingIsFineOnceTheHandlerHasItsOwnFrequency()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.ConfigureStore(options =>
        {
            options.DataSource = "test";
            options.MisfireThreshold = TimeSpan.FromDays(90);
            options.MisfireHandlerFrequency = TimeSpan.FromMinutes(1);
        })));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
    }

    [Test]
    public void ADbRetryIntervalPastTheTimerCeilingFailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.ConfigureStore(options =>
        {
            options.DataSource = "test";
            options.DbRetryInterval = TimeSpan.FromDays(90);
        })));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*DbRetryInterval*4294967294ms (49.7 days)*");
    }

    [Test]
    public void ATransientRetryIntervalPastTheTimerCeilingFailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.ConfigureStore(options =>
        {
            options.DataSource = "test";
            options.TransientRetryInterval = TimeSpan.FromDays(90);
        })));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*TransientRetryInterval*4294967294ms (49.7 days)*");
    }

    [Test]
    public void ACheckinIntervalPastTheTimerCeilingFailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.ConfigureStore(options => options.DataSource = "test");
            store.UseClustering(clustering => clustering.CheckinInterval = TimeSpan.FromDays(90));
        }));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*CheckinInterval*4294967294ms (49.7 days)*");
    }

    /// <summary>
    /// The idle wait is the one duration in the sweep that gets no ceiling. It is spent on a semaphore
    /// rather than a timer, so that a scheduling change can cut it short, and a semaphore takes a
    /// timeout of any length — so a wait past the timer ceiling is strange to configure but not a thing
    /// that breaks, and refusing it would be inventing a limit.
    /// </summary>
    [Test]
    public void AnIdleWaitTimePastTheTimerCeilingIsAllowedBecauseNoTimerWaitsItOut()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.IdleWaitTime = TimerLimits.MaxDelay + TimeSpan.FromDays(1)));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow("TimerLimitsTest holds the semaphore behaviour this rests on");
    }

    /// <summary>
    /// The start delay has the worst symptom of the lot: <c>StartDelayed</c> waits on a task nobody
    /// observes, so a delay the timer refuses faults that task, is collected without a word, and leaves
    /// a scheduler that was created, bound and reported healthy and simply never starts.
    /// </summary>
    /// <remarks>
    /// Read through the monitor rather than <see cref="IStartupValidator"/>, because these options are
    /// per scheduler name and the names are not known where the validator is registered. This is the
    /// read the hosted service makes for every scheduler while the host starts.
    /// </remarks>
    [Test]
    public void AStartDelayPastTheTimerCeilingFailsWhenTheHostedServiceReadsIt()
    {
        var services = new ServiceCollection();
        services.AddQuartz();
        services.AddQuartzHostedService(options => options.StartDelay = TimeSpan.FromDays(90));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptionsMonitor<QuartzHostedServiceOptions>>().Get(Options.DefaultName);

        act.Should().Throw<OptionsValidationException>().WithMessage("*StartDelay*4294967294ms (49.7 days)*");
    }

    [Test]
    public void AStartDelayShortEnoughToWaitOutIsFine()
    {
        var services = new ServiceCollection();
        services.AddQuartz();
        services.AddQuartzHostedService(options => options.StartDelay = TimeSpan.FromMinutes(5));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptionsMonitor<QuartzHostedServiceOptions>>().Get(Options.DefaultName);

        act.Should().NotThrow();
    }

    /// <summary>
    /// The two settings are two answers to the same question, <c>OverwriteExistingData</c> wins, and it
    /// defaults to <see langword="true" /> — so asking to pass over duplicates without also clearing it
    /// got the opposite, replacing them, in silence and at every start.
    /// </summary>
    [Test]
    public void AskingToIgnoreDuplicatesWhileStillOverwritingThemFailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddQuartz();
        services.Configure<QuartzOptions>(options => options.Scheduling.IgnoreDuplicates = true);

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*IgnoreDuplicates*", "the setting that would do nothing is named")
            .WithMessage("*OverwriteExistingData*", "and so is the one that made it do nothing")
            .WithMessage("*defaults to true*", "which a reader who never set it needs told");
    }

    [Test]
    public void IgnoringDuplicatesInsteadOfOverwritingThemPassesValidation()
    {
        var services = new ServiceCollection();
        services.AddQuartz();
        services.Configure<QuartzOptions>(options =>
        {
            options.Scheduling.OverwriteExistingData = false;
            options.Scheduling.IgnoreDuplicates = true;
        });

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow("this is the pair that says 'leave what is already stored alone'");
    }

    [Test]
    public void TheDefaultSchedulingSettingsPassValidation()
    {
        var services = new ServiceCollection();
        services.AddQuartz();

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow("OverwriteExistingData is on by default and the other two are off, which is a pair that agrees");
    }

    [Test]
    public void TheFlatKeysASchedulerIsConfiguredWithPassValidation()
    {
        var services = new ServiceCollection();
        services.AddQuartz(new Dictionary<string, string?>
        {
            ["quartz.scheduler.instanceName"] = "svc",
            ["quartz.threadPool.maxConcurrency"] = "4",
        });

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
    }
}
