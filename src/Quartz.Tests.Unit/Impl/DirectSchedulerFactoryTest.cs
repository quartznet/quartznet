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

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Impl
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
	public class DirectSchedulerFactoryTest
	{
		[Test]
		public async Task TestPlugins()
		{
			StringBuilder result = new StringBuilder();

			IDictionary<string, ISchedulerPlugin> data = new Dictionary<string, ISchedulerPlugin>();
			data["TestPlugin"] = new TestPlugin(result);

			IThreadPool threadPool = new DedicatedThreadPool
			{
			    ThreadCount = 1
			};
			threadPool.Initialize();
			DirectSchedulerFactory.Instance.CreateScheduler(
				"MyScheduler", "Instance1", threadPool,
				new RAMJobStore(), data,
				TimeSpan.Zero);


			IScheduler scheduler = await DirectSchedulerFactory.Instance.GetScheduler("MyScheduler");
			await scheduler.Start();
			await scheduler.Shutdown();

			Assert.AreEqual("TestPlugin|MyScheduler|Start|Shutdown", result.ToString());
		}

        [Test]
        public async Task FactoryCanCreateSchedulerWithSchedulerName()
        {
            string schedName = "TestSchd";
            DirectSchedulerFactory factory = DirectSchedulerFactory.Instance;
            factory.CreateScheduler(schedName);
            IScheduler scheduler = await factory.GetScheduler(schedName);
            Assert.IsNotNull(scheduler);
            Assert.AreEqual(scheduler.SchedulerName, schedName);
        }

		class TestPlugin : ISchedulerPlugin
		{
		    readonly StringBuilder result;

			public TestPlugin(StringBuilder result)
			{
				this.result = result;
			}

			public Task Initialize(string name, IScheduler scheduler, CancellationToken cancellationToken)
			{
				result.Append(name).Append("|").Append(scheduler.SchedulerName);
                return Task.FromResult(true);
            }

            Task ISchedulerPlugin.Start(CancellationToken cancellationToken)
		    {
		        result.Append("|Start");
                return Task.FromResult(true);
		    }

			public Task Shutdown(CancellationToken cancellationToken)
			{
				result.Append("|Shutdown");
                return Task.FromResult(true);
			}
		}
	}
}