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

using AwesomeAssertions.Execution;

using Quartz.Impl;
using Quartz.Jobs;
using Quartz.Util;

namespace Quartz.Tests.Unit;

/// <author>Marko Lahma (.NET)</author>
public class JobDetailTest
{
    [Test]
    public void TestEquals()
    {
        JobDetailImpl jd1 = new JobDetailImpl("name", "group", typeof(NoOpJob));
        JobDetailImpl jd2 = new JobDetailImpl("name", "group", typeof(NoOpJob));
        JobDetailImpl jd3 = new JobDetailImpl("namediff", "groupdiff", typeof(NoOpJob));
        Assert.Multiple(() =>
        {
            Assert.That(jd2, Is.EqualTo(jd1));
            Assert.That(jd3, Is.Not.EqualTo(jd1));
            Assert.That(jd3, Is.Not.EqualTo(jd2));
            Assert.That(jd1, Is.Not.Null);
        });

    }

    [Test]
    public void TestClone()
    {
        JobDetailImpl jobDetail = new JobDetailImpl("test", typeof(NoOpJob));
        JobDetailImpl clonedJobDetail = (JobDetailImpl) jobDetail.Clone();

        Assert.That(clonedJobDetail, Is.EqualTo(jobDetail));
    }

    [Test]
    public void SettingKeyShouldAlsoSetNameAndGroup()
    {
        JobDetailImpl detail = new JobDetailImpl(nameof(SettingKeyShouldAlsoSetNameAndGroup), typeof(NoOpJob));
        detail.Key = new JobKey("name", "group");

        Assert.Multiple(() =>
        {
            Assert.That(detail.Name, Is.EqualTo("name"));
            Assert.That(detail.Group, Is.EqualTo("group"));
        });
    }

    [Test]
    public void GenericJobTypeShouldBeLoadable()
    {
        var type = typeof(GenericJob<IJobSubType>);
        var typeString = type.AssemblyQualifiedNameWithoutVersion();
        var loadedType = new SimpleTypeLoader().LoadType(typeString);

        Assert.Multiple(() =>
        {
            Assert.That(typeString, Is.Not.Contains(", Version="));
            Assert.That(loadedType, Is.Not.Null);
            Assert.That(loadedType, Is.EqualTo(type));
        });

    }

    [Test]
    public void CanConstructJobAndReadJobType()
    {
        var type = typeof(GenericJob<string>);
        var job = new JobDetailImpl("name", "group", type, true, true);
        using (new AssertionScope())
        {
            job.JobType.Type.Should().Be(type);
            job.JobType.FullName.Should().Be(type.AssemblyQualifiedNameWithoutVersion());
        }
    }

    /// <summary>
    /// A job store re-stores the data of a finished job by asking its detail for a copy of itself, so
    /// the copy has to carry everything else across and the detail the store already handed out has to
    /// be left as it was.
    /// </summary>
    [Test]
    public void WithJobDataCopiesTheDetailAndLeavesTheOriginalAlone()
    {
        IJobDetail original = JobBuilder.Create<NoOpJob>()
            .WithIdentity("job", "group")
            .WithDescription("description")
            .StoreDurably()
            .RequestRecovery()
            .UsingJobData("key", "original")
            .Build();

        JobDataMap replacement = new() { ["key"] = "replacement" };

        IJobDetail updated = original.WithJobData(replacement);

        using (new AssertionScope())
        {
            updated.Should().NotBeSameAs(original);
            updated.JobDataMap.Should().BeSameAs(replacement, "the caller hands over a map it does not keep");
            updated.Key.Should().Be(original.Key);
            updated.Description.Should().Be(original.Description);
            updated.JobType.Should().Be(original.JobType);
            updated.Durable.Should().Be(original.Durable);
            updated.RequestsRecovery.Should().Be(original.RequestsRecovery);
            updated.PersistJobDataAfterExecution.Should().Be(original.PersistJobDataAfterExecution);
            updated.ConcurrentExecutionDisallowed.Should().Be(original.ConcurrentExecutionDisallowed);
            original.JobDataMap.GetString("key").Should().Be("original",
                "a detail the store handed out earlier must not change under whoever holds it");
        }
    }

    public class GenericJob<T> : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public interface IJobSubType { }
}