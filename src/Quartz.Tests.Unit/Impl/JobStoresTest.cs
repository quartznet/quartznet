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

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The seam every type test on a job store goes through, so that layering a decorator over one cannot
/// silently change what the store is taken to be.
/// </summary>
public sealed class JobStoresTest
{
    [Test]
    public void Unwrap_ReturnsAnUndecoratedStoreUnchanged()
    {
        RAMJobStore store = TestJobStores.Ram();

        JobStores.Unwrap(store).Should().BeSameAs(store,
            "a store that is nobody's decorator is its own answer, so the seam costs a type test and "
            + "nothing else on the common path");
    }

    [Test]
    public void Unwrap_WalksEveryDecoratorLayer()
    {
        RAMJobStore store = TestJobStores.Ram();
        IJobStore decorated = new DelegatingJobStore(new DelegatingJobStore(store));

        JobStores.Unwrap(decorated).Should().BeSameAs(store,
            "an application may wrap a store as many times as it likes — Quartz's own tracing layer is "
            + "already one of them — and 'is this a database store' has to survive all of them");
    }

    /// <summary>
    /// What the seam is guarding: the answers that used to come from a bare type test.
    /// </summary>
    [Test]
    public void Unwrap_FindsThePersistentStoreUnderADecorator()
    {
        LocalTransactionJobStore store = TestJobStores.Tx();
        IJobStore decorated = new DelegatingJobStore(store);

        (decorated is AdoJobStoreBase).Should().BeFalse(
            "this is the trap: a decorated ADO store fails a direct type test, and every caller that "
            + "asked one got 'not a database store' with no error to show for it");

        JobStores.Unwrap(decorated).Should().BeOfType<LocalTransactionJobStore>()
            .And.BeSameAs(store);
    }

    /// <summary>
    /// Behaviour is forwarded rather than unwrapped: a decorator that overrides one of these means to.
    /// </summary>
    [Test]
    public void DecoratedStore_StillReportsWhatItSupports()
    {
        IJobStore decorated = new DelegatingJobStore(TestJobStores.Tx());

        decorated.SupportsPersistence.Should().BeTrue(
            "SupportsPersistence and Clustered are questions about behaviour, and a decorator answers "
            + "them by forwarding — unwrapping them would discard an override an application wrote on "
            + "purpose");
    }
}
