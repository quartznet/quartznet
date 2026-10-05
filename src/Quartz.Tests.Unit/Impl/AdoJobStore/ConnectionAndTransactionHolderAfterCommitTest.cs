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
using System.Data.Common;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The store's bookkeeping after a commit (#4006): done once, in order, and never failing the committed
/// operation.
/// </summary>
public class ConnectionAndTransactionHolderAfterCommitTest
{
    [Test]
    public void WhatWasRecordedIsDoneInOrderAndOnce()
    {
        ConnectionAndTransactionHolder conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        List<int> done = new List<int>();
        conn.AfterCommit(() => done.Add(1));
        conn.AfterCommit(() => done.Add(2));

        conn.RunAfterCommit();
        conn.RunAfterCommit();

        done.Should().Equal(new[] { 1, 2 }, "the second run finds nothing left to do");
    }

    /// <summary>
    /// One that throws is logged, and the rest are done. Thrown out of the transaction wrapper, it would
    /// fail an operation that has committed, and a completion would be retried for it for as long as it
    /// kept throwing.
    /// </summary>
    [Test]
    public void OneThatThrowsDoesNotStopTheRestOrFailTheOperation()
    {
        ConnectionAndTransactionHolder conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        List<int> done = new List<int>();
        conn.AfterCommit(() => done.Add(1));
        conn.AfterCommit(() => throw new InvalidOperationException("The log sink is down."));
        conn.AfterCommit(() => done.Add(3));

        Action run = () => conn.RunAfterCommit();

        run.Should().NotThrow();
        done.Should().Equal(new[] { 1, 3 });
    }
}
