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

#nullable enable

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What a trimmed or native publish does with a type name: <c>Quartz.TypeNameResolution.IsSupported</c>
/// off, a name resolves only to a type something registered, and anything else is refused naming the
/// registration to add.
/// </summary>
/// <remarks>
/// The switch is process-wide, so the fixture is non-parallelizable and puts it back after each test.
/// The job types are private to the fixture for the reason <see cref="RegisteredTypeNamesTest" /> gives:
/// a name this fixture calls unregistered has to be one nothing in the run has ever named in code.
/// </remarks>
[NonParallelizable]
public sealed class TypeNameResolutionSwitchTest
{
    private const string Switch = "Quartz.TypeNameResolution.IsSupported";

    private FeatureSwitchScope reflectionOff = null!;

    [SetUp]
    public void TurnReflectionOff()
    {
        reflectionOff = new FeatureSwitchScope(Switch, enabled: false);
    }

    [TearDown]
    public void TurnReflectionBackOn()
    {
        reflectionOff.Dispose();
    }

    [Test]
    public void ARegisteredNameResolvesThroughTheLoader()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.AddJobType<RegisteredJob>());
        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<ITypeLoader>().LoadType(Name<RegisteredJob>()).Should().Be<RegisteredJob>(
            "the registered types are the whole of what a publish without reflection resolves, and this one was registered");
    }

    [Test]
    public void AnUnregisteredNameIsRefusedNamingTheRegistration()
    {
        ServiceCollection services = new();
        services.AddQuartz();
        using ServiceProvider container = services.BuildServiceProvider();

        var act = () => container.GetRequiredService<ITypeLoader>().LoadType(Name<UnregisteredJob>());

        act.Should().Throw<TypeLoadException>()
            .WithMessage($"*{Name<UnregisteredJob>()}*")
            .WithMessage("*AddJob<T>()*")
            .WithMessage($"*{Switch}*",
                "the type exists in this process and the runtime would find it, so refusing it is the proof that "
                + "nothing reflected - and the message has to say what to register rather than that the type is missing");
    }

    [Test]
    public void AShippedJobs3xSpellingResolvesOnceItIsRegistered()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.AddJobType<Quartz.Jobs.NoOpJob>());
        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<ITypeLoader>().LoadType("Quartz.Job.NoOpJob, Quartz").Should().Be<Quartz.Jobs.NoOpJob>(
            "a JOB_CLASS_NAME a 3.x scheduler wrote names the old namespace and assembly, and the rename walk applies to the registered types");
    }

    [Test]
    public void ANameConstructedJobTypeOfARegisteredTypeResolves()
    {
        _ = JobBuilder.Create<RegisteredJob>();

        new global::Quartz.JobType(Name<RegisteredJob>()).Type.Should().Be<RegisteredJob>(
            "a name-constructed JobType is what a stored row becomes, and the store's fallback is the same table");
    }

    [Test]
    public void ANameConstructedJobTypeNobodyRegisteredFailsWithTheHint()
    {
        global::Quartz.JobType jobType = new("Nowhere.Type, Nowhere");

        jobType.TryResolve(out Type? resolved).Should().BeFalse("a probe has no answer and throws nothing");
        resolved.Should().BeNull();

        var act = () => jobType.Type;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Nowhere.Type, Nowhere*")
            .WithMessage("*AddJob<T>()*")
            .WithMessage($"*{Switch}*",
                "the first fire of a job nothing registered is where a trimmed deployment finds out, and the message has to say what to do");
    }

    [Test]
    public void AMappedNameResolvesThroughTheTable()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.UseTypeLoader(loader => loader.Map("Acme.Jobs.NightlyReport, Acme.Jobs", typeof(MappedJob))));
        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<ITypeLoader>().LoadType("Acme.Jobs.NightlyReport, Acme.Jobs").Should().Be<MappedJob>(
            "Map registers its target, so the alias holds where the target's own name would otherwise be refused");
    }

    [Test]
    public void AnAliasFromConfigurationToAnUnregisteredTargetFailsValidationNamingTheRegistration()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Quartz:TypeLoader:Aliases:Acme.Jobs.Invoicing, Acme.Jobs"] = Name<UnregisteredJob>(),
            })
            .Build();

        ServiceCollection services = new();
        services.AddQuartz(configuration.GetSection("Quartz"));
        using ServiceProvider container = services.BuildServiceProvider();

        var act = () => container.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{Name<UnregisteredJob>()}*")
            .WithMessage("*AddJobType<T>()*")
            .WithMessage($"*{Switch}*",
                "a target the runtime would find is still no answer in a publish without reflection, and the failure has to say so at startup");
    }

    [Test]
    public void UseSqliteByConnectionStringFailsNamingTheFactoryOverload()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseSqlite("Data Source=:memory:")));
        using ServiceProvider container = services.BuildServiceProvider();

        var act = () => container.GetRequiredService<IDbProvider>();

        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*SQLite*")
            .WithMessage("*DbProviderFactory*")
            .WithMessage($"*{Switch}*",
                "a driver chosen by name has its types resolved by name, and the way out is the overload that takes the factory");
    }

    [Test]
    public void UseSqliteWithTheFactoryResolves()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseSqlite(SqliteFactory.Instance, "Data Source=:memory:")));
        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<IDbProvider>().Should().NotBeNull(
            "the factory path reads the half of the description that names no type, so the switch never enters into it");
    }

    [Test]
    public void AScheduleSectionNamingAnUnregisteredJobTypeFailsWithTheHint()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Schedule:Jobs:0:Name"] = "nightly",
                ["Schedule:Jobs:0:JobType"] = Name<UnregisteredJob>(),
                ["Schedule:Jobs:0:Durable"] = "true",
            })
            .Build();

        ServiceCollection services = new();
        services.AddQuartz(configuration);
        using ServiceProvider container = services.BuildServiceProvider();

        var act = () => container.ScheduledJobs();

        act.Should().Throw<TypeLoadException>()
            .WithMessage($"*{Name<UnregisteredJob>()}*")
            .WithMessage("*AddJob<T>()*",
                "the Quartz:Schedule reader asks the scheduler's loader, and its refusal is the one the application sees");
    }

    private static string Name<TJob>() => typeof(TJob).AssemblyQualifiedNameWithoutVersion();

    public sealed class RegisteredJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Exists, and is never passed to a typed API: no <c>AddJobType</c>, no <c>JobBuilder.Create&lt;T&gt;()</c>,
    /// no <c>JobType(Type)</c>. Its name is produced from <c>typeof</c>, which registers nothing.
    /// </summary>
    public sealed class UnregisteredJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class MappedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
