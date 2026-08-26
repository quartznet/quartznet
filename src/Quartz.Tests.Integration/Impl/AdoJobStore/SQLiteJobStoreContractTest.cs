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

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The job store contract as the ADO.NET store implements it, against a real database. SQLite on a
/// file needs no container, so this runs wherever the in-memory fixture does — the point of the pair
/// is that both stores answer the same assertions, and that only holds if both actually run.
/// </summary>
/// <remarks>
/// No <c>db-*</c> category, so this runs in the <c>basic</c> leg: it is the one ADO dialect every
/// pull request pays for, and the five that need a container carry a category apiece.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class SQLiteJobStoreContractTest : SqliteFileJobStoreContractTest
{
}
