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

using System.Collections.Specialized;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// Unit test for SystemPropertyInstanceIdGenerator.
/// </summary>
[TestFixture]
public class SystemPropertyInstanceIdGeneratorTest
{
    [SetUp]
    public void SetUp()
    {
        Environment.SetEnvironmentVariable(SystemPropertyInstanceIdGenerator.SystemProperty, "foo");
        Environment.SetEnvironmentVariable("blah.blah", "goo");
    }

    [Test]
    public async Task TestGetInstanceId()
    {
        SystemPropertyInstanceIdGenerator gen = new SystemPropertyInstanceIdGenerator();

        string instId = await gen.GenerateInstanceId();

        Assert.That(instId, Is.EqualTo("foo"));
    }

    [Test]
    public async Task TestGetInstanceIdWithPrepend()
    {
        SystemPropertyInstanceIdGenerator gen = new SystemPropertyInstanceIdGenerator();
        gen.Prepend = "1";

        string instId = await gen.GenerateInstanceId();

        Assert.That(instId, Is.EqualTo("1foo"));
    }

    [Test]
    public async Task TestGetInstanceIdWithPostpend()
    {
        SystemPropertyInstanceIdGenerator gen = new SystemPropertyInstanceIdGenerator();
        gen.Postpend = "2";

        string instId = await gen.GenerateInstanceId();

        Assert.That(instId, Is.EqualTo("foo2"));
    }

    [Test]
    public async Task TestGetInstanceIdWithPrependAndPostpend()
    {
        SystemPropertyInstanceIdGenerator gen = new SystemPropertyInstanceIdGenerator();
        gen.Prepend = "1";
        gen.Postpend = "2";

        string instId = await gen.GenerateInstanceId();

        Assert.That(instId, Is.EqualTo("1foo2"));
    }

    [Test]
    public async Task TestGetInstanceIdFromCustomSystemProperty()
    {
        SystemPropertyInstanceIdGenerator gen = new SystemPropertyInstanceIdGenerator();
        gen.SystemPropertyName = "blah.blah";

        string instId = await gen.GenerateInstanceId();

        Assert.That(instId, Is.EqualTo("goo"));
    }

    [Test]
    [Ignore("Work in progress")]
    public async Task TestGeneratorThroughSchedulerInstantiation()
    {
        // TODO
        //JdbcQuartzTestUtilities.createDatabase("MeSchedulerDatabase");

        NameValueCollection config = new NameValueCollection();
        config["quartz.scheduler.instanceName"] = "MeScheduler";
        config["quartz.scheduler.instanceId"] = "AUTO";
        config["quartz.scheduler.instanceIdGenerator.type"] = typeof(SystemPropertyInstanceIdGenerator).AssemblyQualifiedName;
        config["quartz.scheduler.instanceIdGenerator.prepend"] = "1";
        config["quartz.scheduler.instanceIdGenerator.postpend"] = "2";
        config["quartz.scheduler.instanceIdGenerator.systemPropertyName"] = "blah.blah";
        config["quartz.threadPool.threadCount"] = "1";
        config["quartz.threadPool.type"] = typeof(DefaultThreadPool).AssemblyQualifiedName;
        config["quartz.jobStore.type"] = typeof(JobStoreTX).AssemblyQualifiedName;
        config["quartz.jobStore.clustered"] = "true";
        config["quartz.jobStore.dataSource"] = "MeSchedulerDatabase";

        IScheduler sched = await new StdSchedulerFactory(config).GetScheduler();

        Assert.That(sched.SchedulerInstanceId, Is.EqualTo("1goo2"));
    }
}