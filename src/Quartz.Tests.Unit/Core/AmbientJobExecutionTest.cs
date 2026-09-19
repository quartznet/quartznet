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

using FakeItEasy;

using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The one ambient slot a firing writes: the identity it takes job-store locks under, and the execution
/// context it runs under, on one holder (#3802).
/// </summary>
[TestFixture]
public class AmbientJobExecutionTest
{
    [Test]
    public void AnOperationHasACallerIdBeforeItHasAContext()
    {
        Guid callerId = Guid.NewGuid();

        AmbientJobExecution.Holder holder = AmbientJobExecution.Begin(callerId);

        AmbientJobExecution.CurrentCallerId.Should().Be(callerId,
            "the job store takes its locks under this, and it has to be readable from the moment the operation starts");
        AmbientJobExecution.Current.Should().BeNull(
            "an operation that has not entered a firing is not inside one - the scheduler's own loop begins this way and must never look like a job execution");

        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        using (holder.Enter(context))
        {
            AmbientJobExecution.Current.Should().BeSameAs(context);
            AmbientJobExecution.CurrentCallerId.Should().Be(callerId, "entering a firing does not change whose locks they are");
        }

        AmbientJobExecution.Current.Should().BeNull("leaving the firing ends it for every flow that captured it");
        AmbientJobExecution.CurrentCallerId.Should().Be(callerId,
            "the operation is still this one - the run shell has its job to return and its store call to make after the context has gone");
    }

    [Test]
    public void EachOperationGetsAHolderOfItsOwn()
    {
        AmbientJobExecution.Holder first = AmbientJobExecution.Begin(Guid.NewGuid());
        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        first.Enter(context);

        AmbientJobExecution.Holder second = AmbientJobExecution.Begin(Guid.NewGuid());

        second.Should().NotBeSameAs(first);
        AmbientJobExecution.Current.Should().BeNull(
            "a firing must never be handed the holder of one that has ended, or it would start out reporting the previous firing's context");
        AmbientJobExecution.CurrentCallerId.Should().NotBe(first.CallerId);
    }

    [Test]
    public void EnteringAFiringCopiesNoExecutionContext()
    {
        IJobExecutionContext context = A.Fake<IJobExecutionContext>();

        // Warm: the first of each pays for JIT and for the AsyncLocal's own first write.
        AmbientJobExecution.Begin(Guid.NewGuid()).Enter(context).Dispose();

        AmbientJobExecution.Holder holder = AmbientJobExecution.Begin(Guid.NewGuid());
        long before = GC.GetAllocatedBytesForCurrentThread();
        holder.Enter(context).Dispose();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().BeLessThan(100,
            "publishing the context writes the holder the flow already has, not the AsyncLocal - a second AsyncLocal write would copy the execution context and its value map, which is what the firing used to pay twice");
    }
}
