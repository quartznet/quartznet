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

using Quartz.Configuration;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// Unit test for SystemPropertyInstanceIdGenerator.
/// </summary>
/// <remarks>
/// Not parallelizable: the setup writes environment variables, which belong to the process rather than
/// to this fixture, and one of the tests holds them across building a whole scheduler.
/// </remarks>
[NonParallelizable]
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

    /// <summary>
    /// The generator reached through configuration rather than constructed: the type, the variable it
    /// reads and the prefix and suffix around it all arrive as strings, and nothing else asserts that
    /// those four keys land on the object the container builds.
    /// </summary>
    /// <remarks>
    /// The store is in-memory and merely reports itself clustered — see
    /// <see cref="ClusteredNodeBuilder" /> — because that is the whole of what the id path needs. A
    /// scheduler whose store is not clustered never asks the generator anything; it takes
    /// <see cref="QuartzSchedulerOptions.DefaultInstanceId" />. The database the <c>TODO</c> that stood
    /// here was waiting for would have bought nothing beyond saying <c>true</c>.
    /// </remarks>
    [Test]
    public async Task TestGeneratorThroughSchedulerInstantiation()
    {
        NameValueCollection config = new NameValueCollection();
        config["quartz.scheduler.instanceName"] = "MeScheduler";
        config["quartz.scheduler.instanceId"] = "AUTO";
        config["quartz.scheduler.instanceIdGenerator.type"] = typeof(SystemPropertyInstanceIdGenerator).AssemblyQualifiedName;
        config["quartz.scheduler.instanceIdGenerator.prepend"] = "1";
        config["quartz.scheduler.instanceIdGenerator.postpend"] = "2";
        config["quartz.scheduler.instanceIdGenerator.systemPropertyName"] = "blah.blah";
        config["quartz.threadPool.maxConcurrency"] = "1";
        config["quartz.threadPool.type"] = typeof(DefaultThreadPool).AssemblyQualifiedName;

        await using StandaloneSchedulerFactory factory = ClusteredNodeBuilder.Build(config);
        IScheduler scheduler = await factory.GetScheduler();

        scheduler.SchedulerInstanceId.Should().Be("1goo2",
            "the id is the value of the variable systemPropertyName named, wrapped in the prepend and "
            + "postpend keys — all four settings arrive as strings and are applied to the generator after "
            + "the container has constructed it");
    }
}