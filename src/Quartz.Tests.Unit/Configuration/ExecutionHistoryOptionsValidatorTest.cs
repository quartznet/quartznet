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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The bounds the execution history is kept under are checked when the host starts, not when the first
/// row is trimmed by them.
/// </summary>
public sealed class ExecutionHistoryOptionsValidatorTest
{
    [Test]
    public void TheDefaultsAreValid()
    {
        ExecutionHistoryOptions options = new();

        options.RetentionByResult.Should().BeEmpty();
        options.MisfireRetention.Should().BeNull("misfires are kept for Retention unless told otherwise");
        options.MaxEntriesPerJob.Should().Be(0, "a per-job cap is opt-in");
        options.RecordInput.Should().BeFalse("an input can hold secrets, so recording it is opt-in");
        options.MaxInputBytes.Should().Be(16 * 1024);

        Validate(options).Succeeded.Should().BeTrue();
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void TheInputCapMustBePositive(int maxInputBytes)
    {
        ValidateOptionsResult result = Validate(new ExecutionHistoryOptions { RecordInput = true, MaxInputBytes = maxInputBytes });

        result.Failed.Should().BeTrue("a cap that keeps nothing would flag every input as too large");
        result.FailureMessage.Should().Contain("MaxInputBytes").And.Contain("RecordInput off");
    }

    [Test]
    public void EveryBoundSetToASensibleValueIsValid()
    {
        ExecutionHistoryOptions options = new() { MisfireRetention = TimeSpan.FromHours(1), MaxEntriesPerJob = 50 };
        options.RetentionByResult[JobRunResult.Failed] = TimeSpan.FromDays(30);
        options.RetentionByResult[JobRunResult.Skipped] = TimeSpan.FromMinutes(10);

        Validate(options).Succeeded.Should().BeTrue();
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void AResultsAgeMustBePositive(int minutes)
    {
        ExecutionHistoryOptions options = new();
        options.RetentionByResult[JobRunResult.Skipped] = TimeSpan.FromMinutes(minutes);

        ValidateOptionsResult result = Validate(options);

        result.Failed.Should().BeTrue("an age of zero forgets a row the moment it is written");
        result.FailureMessage.Should().Contain("RetentionByResult[Skipped]");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void TheMisfireAgeMustBePositiveWhenSet(int minutes)
    {
        ValidateOptionsResult result = Validate(new ExecutionHistoryOptions { MisfireRetention = TimeSpan.FromMinutes(minutes) });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("MisfireRetention").And.Contain("null");
    }

    [Test]
    public void ThePerJobCapMustNotBeNegative()
    {
        ValidateOptionsResult result = Validate(new ExecutionHistoryOptions { MaxEntriesPerJob = -1 });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("MaxEntriesPerJob").And.Contain("0 for no per-job cap");
    }

    [Test]
    public void ABadBoundFailsTheContainerAtStartup()
    {
        ServiceCollection services = new();
        services.AddQuartzExecutionHistory(options => options.RetentionByResult[JobRunResult.Failed] = TimeSpan.Zero);
        using ServiceProvider provider = services.BuildServiceProvider();

        Action read = () => _ = provider.GetRequiredService<IOptions<ExecutionHistoryOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*RetentionByResult[Failed]*");
    }

    private static ValidateOptionsResult Validate(ExecutionHistoryOptions options)
    {
        using ServiceProvider provider = new ServiceCollection().AddQuartzExecutionHistory().BuildServiceProvider();

        IValidateOptions<ExecutionHistoryOptions> validator = provider.GetServices<IValidateOptions<ExecutionHistoryOptions>>().Single();
        return validator.Validate(Options.DefaultName, options);
    }
}
