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

using System.Reflection;

namespace Quartz.Tests.Unit;

/// <summary>
/// What <c>[QuartzJob]</c> and <c>[CronTrigger]</c> carry, and what they default to.
/// </summary>
/// <remarks>
/// The attributes are read by the source generator in <c>Quartz.Analyzers</c>, which the tests in
/// <c>Quartz.Analyzers.Tests</c> drive — nothing here runs a generator. What this holds is the half
/// that is a shipped contract whatever reads it: the defaults an unwritten property means, and the
/// usage that lets a job declare several schedules.
/// </remarks>
public class DeclaredJobAttributesTest
{
    [Test]
    public void JobDefaultsToNothingSaid()
    {
        QuartzJobAttribute attribute = new QuartzJobAttribute();

        attribute.Name.Should().BeNull("a job that names nothing is named after its class, and only the generator knows what that class is called");
        attribute.Group.Should().BeNull();
        attribute.Description.Should().BeNull();
        attribute.Durable.Should().BeFalse("durability is forced on for a job with no schedule, which is a decision the generator makes rather than a different default here");
        attribute.RequestRecovery.Should().BeFalse();
        attribute.Scheduler.Should().BeNull("a job naming no scheduler belongs to every scheduler AddDeclaredJobs is called on");
    }

    [Test]
    public void ScheduleDefaultsToTheTriggerDefaults()
    {
        CronTriggerAttribute attribute = new CronTriggerAttribute("0 0 0/6 * * ?");

        attribute.CronExpression.Should().Be("0 0 0/6 * * ?");
        attribute.Priority.Should().Be(TriggerConstants.DefaultPriority, "an unwritten priority has to mean what an unwritten WithPriority means");
        attribute.MisfireInstruction.Should().Be(CronTriggerMisfireInstruction.SmartPolicy);
        attribute.Name.Should().BeNull();
        attribute.Group.Should().BeNull();
        attribute.TimeZone.Should().BeNull();
        attribute.Description.Should().BeNull();
        attribute.ExecutionGroup.Should().BeNull();
    }

    [Test]
    public void EveryPropertyIsReadBackFromTheDeclaration()
    {
        QuartzJobAttribute job = typeof(DeclaredCleanupJob).GetCustomAttribute<QuartzJobAttribute>()!;

        job.Should().NotBeNull();
        job.Name.Should().Be("cleanup");
        job.Group.Should().Be("maintenance");
        job.Description.Should().Be("removes rows nobody reads");
        job.Durable.Should().BeTrue();
        job.RequestRecovery.Should().BeTrue();
        job.Scheduler.Should().Be("housekeeping");

        CronTriggerAttribute[] schedules = [.. typeof(DeclaredCleanupJob).GetCustomAttributes<CronTriggerAttribute>()];

        schedules.Should().HaveCount(2, "AllowMultiple is what lets one job declare several schedules");

        CronTriggerAttribute noon = schedules.Single(x => x.Name == "cleanup-weekday-noon");
        noon.CronExpression.Should().Be("0 0 12 ? * MON-FRI");
        noon.Group.Should().Be("housekeeping");
        noon.TimeZone.Should().Be("Europe/Helsinki");
        noon.MisfireInstruction.Should().Be(CronTriggerMisfireInstruction.DoNothing);
        noon.Priority.Should().Be(9);
        noon.Description.Should().Be("every weekday at noon, Helsinki time");
        noon.ExecutionGroup.Should().Be("maintenance");
    }

    /// <summary>
    /// The usage the generator's reading of these attributes rests on.
    /// </summary>
    [Test]
    public void UsageIsClassesOnly()
    {
        AttributeUsageAttribute job = typeof(QuartzJobAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        job.ValidOn.Should().Be(AttributeTargets.Class);
        job.AllowMultiple.Should().BeFalse("a class is one job");
        job.Inherited.Should().BeFalse("a base class declaring a job would declare it again for every class deriving from it, under one key");

        AttributeUsageAttribute schedule = typeof(CronTriggerAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        schedule.ValidOn.Should().Be(AttributeTargets.Class);
        schedule.AllowMultiple.Should().BeTrue("a job may run on more than one schedule");
        schedule.Inherited.Should().BeFalse();
    }

    [QuartzJob(
        Name = "cleanup",
        Group = "maintenance",
        Description = "removes rows nobody reads",
        Durable = true,
        RequestRecovery = true,
        Scheduler = "housekeeping")]
    [CronTrigger("0 0 0/6 * * ?")]
    [CronTrigger(
        "0 0 12 ? * MON-FRI",
        Name = "cleanup-weekday-noon",
        Group = "housekeeping",
        TimeZone = "Europe/Helsinki",
        MisfireInstruction = CronTriggerMisfireInstruction.DoNothing,
        Priority = 9,
        Description = "every weekday at noon, Helsinki time",
        ExecutionGroup = "maintenance")]
    private sealed class DeclaredCleanupJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
