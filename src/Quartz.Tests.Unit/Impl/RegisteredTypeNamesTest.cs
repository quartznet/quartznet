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

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The table a type name is answered from before the runtime is asked, and the APIs that feed it.
/// </summary>
/// <remarks>
/// The table is process-wide, like the <c>Type.GetType</c> it stands in front of, so every job type here
/// is private to this fixture: a type another fixture registers is registered for the whole run, and a
/// test about an unregistered name needs one nothing else has ever named.
/// </remarks>
public sealed class RegisteredTypeNamesTest
{
    [Test]
    public void ANameNothingRegisteredIsNotFound()
    {
        RegisteredTypeNames.Find(typeof(NeverRegisteredJob).AssemblyQualifiedNameWithoutVersion()).Should().BeNull(
            "the type exists and nothing has named it in code, which is the one case the table has no answer for");
        RegisteredTypeNames.Find("Nowhere.Job, Nowhere").Should().BeNull();
        RegisteredTypeNames.Find("").Should().BeNull();
    }

    [Test]
    public void JobBuilderCreateRegistersTheType()
    {
        _ = JobBuilder.Create<CreatedJob>();

        RegisteredTypeNames.Find(typeof(CreatedJob).AssemblyQualifiedNameWithoutVersion()).Should().Be<CreatedJob>(
            "every typed way of naming a job builds a JobType from the type, and that is where the name is learned");
    }

    [Test]
    public void AddJobTypeRegistersTheType()
    {
        new ServiceCollection().AddQuartz(q => q.AddJobType<AddedJob>());

        RegisteredTypeNames.Find(typeof(AddedJob).AssemblyQualifiedNameWithoutVersion()).Should().Be<AddedJob>(
            "AddJobType<T>() is what the how-to tells a trimmed host to call for every job a caller may name");
    }

    [Test]
    public void AddJobTypeWithAnImplementationRegistersBothHalves()
    {
        new ServiceCollection().AddQuartz(q => q.AddJobType<IContractJob, ContractJobImplementation>());

        RegisteredTypeNames.Find(typeof(IContractJob).AssemblyQualifiedNameWithoutVersion()).Should().Be<IContractJob>(
            "the job detail names the service type, which is the name a store or a request carries");
        RegisteredTypeNames.Find(typeof(ContractJobImplementation).AssemblyQualifiedNameWithoutVersion()).Should().Be<ContractJobImplementation>();
    }

    [Test]
    public void MapRegistersTheTarget()
    {
        new TypeLoaderOptions().Map("Acme.Jobs.Nightly, Acme.Jobs", typeof(MappedJob));

        RegisteredTypeNames.Find(typeof(MappedJob).AssemblyQualifiedNameWithoutVersion()).Should().Be<MappedJob>(
            "an alias from a stored name to a type has to hold in a publish that resolves nothing by reflection");
    }

    [Test]
    public void AFullyQualifiedSpellingFindsTheEntry()
    {
        _ = JobBuilder.Create<QualifiedJob>();

        RegisteredTypeNames.Find(typeof(QualifiedJob).AssemblyQualifiedName!).Should().Be<QualifiedJob>(
            "a configuration file spells the version, culture and public key token, and the stored name does not");
    }

    [Test]
    public void APre40SpellingOfAShippedJobFindsTheEntry()
    {
        _ = JobBuilder.Create<Quartz.Jobs.NoOpJob>();

        RegisteredTypeNames.Find("Quartz.Job.NoOpJob, Quartz", out string foundAs).Should().Be<Quartz.Jobs.NoOpJob>(
            "a JOB_CLASS_NAME written by 3.x carries the old namespace and the pre-split assembly, and the "
            + "same renames the type loader applies have to apply to the table");
        foundAs.Should().Be("Quartz.Jobs.NoOpJob, Quartz.Jobs", "the loader reports the spelling it found the name under");
    }

    [Test]
    public void RegisteringTheSameTypeAgainChangesNothing()
    {
        _ = JobBuilder.Create<CreatedJob>();
        _ = JobBuilder.Create<CreatedJob>();

        RegisteredTypeNames.Find(typeof(CreatedJob).AssemblyQualifiedNameWithoutVersion()).Should().Be<CreatedJob>(
            "the same type arrives through every builder that names it, and the last registration wins by holding the same entry");
    }

    [Test]
    public void DelegateJobIsRegisteredWithoutAnythingSchedulingOne()
    {
        RegisteredTypeNames.Find("Quartz.Impl.DelegateJob, Quartz").Should().Be<DelegateJob>(
            "a delegate job's row is read back on nodes that never scheduled one, and its class name is Quartz's own");
    }

    [Test]
    public void TheRefusalNamesTheRegistrationAndTheWayBack()
    {
        string message = RegisteredTypeNames.NotRegistered("Acme.Jobs.Nightly, Acme.Jobs");

        message.Should().Contain("Acme.Jobs.Nightly, Acme.Jobs");
        message.Should().Contain("AddJob<T>()").And.Contain("AddJobType<T>()").And.Contain("Map(\"Acme.Jobs.Nightly, Acme.Jobs\", typeof(T))");
        message.Should().Contain(FeatureSwitches.TypeNameResolutionSwitch);
        message.Should().Contain($"<{FeatureSwitches.TypeNameResolutionProperty}>true</{FeatureSwitches.TypeNameResolutionProperty}>",
            "the MSBuild property is how a publish opts back into reflection");
    }

    public sealed class NeverRegisteredJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class CreatedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class AddedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public interface IContractJob : IJob;

    public sealed class ContractJobImplementation : IContractJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class MappedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class QualifiedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
