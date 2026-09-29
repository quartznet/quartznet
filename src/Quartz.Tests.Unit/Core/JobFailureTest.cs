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

#nullable enable

using FakeItEasy;

using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Reading what a job threw back out of the run shell's wrappers: one shape is looked through, and
/// nothing else.
/// </summary>
public sealed class JobFailureTest
{
    [Test]
    public void TheRunShellsTwoWrappersAreLookedThrough()
    {
        InvalidOperationException thrown = new("the ledger is locked");
        JobExecutionException reported = new(new JobExecutionProcessException(A.Fake<IJobExecutionContext>(), thrown));

        reported.Message.Should().Be("Job threw an unhandled exception", "this is the text the wrappers carry, and the reason for the helper");
        JobFailure.Thrown(reported).Should().BeSameAs(thrown);
        JobFailure.MessageOf(reported).Should().Be("the ledger is locked",
            "the wrappers say only that Quartz caught something, which is true of every failure there is");
    }

    [Test]
    public void AJobExecutionExceptionTheJobThrewIsItsOwnMessage()
    {
        JobExecutionException own = new("quota exceeded", new InvalidOperationException("HTTP 429"));

        JobFailure.Thrown(own).Should().BeSameAs(own, "no JobExecutionProcessException is under it, so the run shell did not wrap it");
        JobFailure.MessageOf(own).Should().Be("quota exceeded", "a job that wrapped its own failure said what it meant in the wrapper");
    }

    [Test]
    public void AJobExecutionExceptionWithNoWordsOfItsOwnSaysItsCauses()
    {
        JobFailure.MessageOf(new JobExecutionException(new InvalidOperationException("the disk is full")))
            .Should().Be("the disk is full", "SchedulerException takes its cause's message when it is given none");
    }

    [Test]
    public void TheCauseIsNotUnwrappedFurther()
    {
        AggregateException thrown = new("two uploads failed", new IOException("a"), new IOException("b"));
        JobExecutionException reported = new(new JobExecutionProcessException(A.Fake<IJobExecutionContext>(), thrown));

        JobFailure.Thrown(reported).Should().BeSameAs(thrown, "an AggregateException is what failed, not its children");
    }

    [Test]
    public void AnExceptionThatIsNotAJobExecutionExceptionIsItself()
    {
        TimeoutException timeout = new("the lock was not granted");

        JobFailure.Thrown(timeout).Should().BeSameAs(timeout);
    }
}
