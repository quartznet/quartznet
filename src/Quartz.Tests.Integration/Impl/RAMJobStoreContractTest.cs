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

namespace Quartz.Tests.Integration.Impl;

/// <summary>
/// The job store contract as the in-memory store implements it.
/// </summary>
[TestFixture]
public sealed class RAMJobStoreContractTest : JobStoreContractTest
{
    /// <summary>
    /// The in-memory store keeps a set of paused job groups, so it can answer the question.
    /// </summary>
    protected override bool ReportsJobGroupPauseState => true;

    /// <summary>
    /// Pausing walks over any state but complete, the error state included.
    /// </summary>
    protected override bool PauseOverwritesTheErrorState => true;

    protected override async ValueTask<IJobStore> CreateStore()
    {
        IJobStore store = TestJobStores.Ram();
        await store.Initialize();
        return store;
    }
}
