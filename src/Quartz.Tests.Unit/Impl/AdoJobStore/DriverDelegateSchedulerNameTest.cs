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

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The scheduler name a dialect delegate scopes its own statements by.
/// </summary>
/// <remarks>
/// Nearly every statement in <see cref="StdAdoDelegate" /> is scoped by SCHED_NAME, so a delegate that
/// writes one of its own needs the same value. It used to have to override
/// <see cref="StdAdoDelegate.Initialize" /> and keep a second copy of it from
/// <see cref="DriverDelegateContext" />; the base class holds the only copy now.
/// </remarks>
public class DriverDelegateSchedulerNameTest
{
    [Test]
    public void ADelegateOfItsOwnScopesByTheSchedulerNameItWasInitializedWith()
    {
        ScopedDelegate scoped = new();

        scoped.Initialize(Context("first"));

        scoped.Scope().Should().Be(
            "SCHED_NAME = 'first'",
            "the delegate serves one scheduler, and the name it scopes by is that scheduler's");
    }

    [Test]
    public void ItArrivesWithoutOverridingInitialize()
    {
        typeof(ScopedDelegate).GetMethod(nameof(StdAdoDelegate.Initialize), BindingFlags.Public | BindingFlags.Instance)!
            .DeclaringType.Should().Be(typeof(StdAdoDelegate),
                "capturing the name in an override of Initialize is the workaround this replaces, so a "
                + "delegate that only wants to read it must not need one");
    }

    private static DriverDelegateContext Context(string schedulerName)
    {
        return new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            SchedulerName = schedulerName,
            InstanceId = "node",
            DbProvider = A.Fake<IDbProvider>(),
            TypeLoader = new SimpleTypeLoader(),
        };
    }

    /// <summary>
    /// Stands in for a dialect delegate that writes a whole statement rather than adjusting one of the
    /// base class's. It overrides nothing.
    /// </summary>
    private sealed class ScopedDelegate : StdAdoDelegate
    {
        public string Scope() => $"{AdoConstants.ColumnSchedulerName} = '{SchedulerName}'";
    }
}
