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

using Quartz.Diagnostics;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// How <c>OTEL_SEMCONV_EXCEPTION_SIGNAL_OPT_IN</c> and <see cref="QuartzSchedulerOptions.RecordExceptionSpanEvents" />
/// decide whether a failed span records its exception as an event.
/// </summary>
/// <remarks>
/// The values are read here as strings, so nothing in this fixture touches the process environment;
/// <c>JobExecutionObservabilityTest</c> sets the variable for real, in a fixture that runs alone.
/// </remarks>
public sealed class ExceptionSignalTest
{
    [Test]
    public void TheVariableIsTheOneOpenTelemetryDefines()
    {
        ExceptionSignal.OptInVariable.Should().Be("OTEL_SEMCONV_EXCEPTION_SIGNAL_OPT_IN",
            "the name is OpenTelemetry's, shared by every instrumentation that honours it");
    }

    [TestCase(null, true)]
    [TestCase("", true)]
    [TestCase("logs/dup", true)]
    [TestCase("spans", true)]
    [TestCase("logs", false)]
    [TestCase("LOGS", false)]
    [TestCase(" logs ", false)]
    public void OnlyLogsStopsTheSpanEvents(string optIn, bool recordsSpanEvents)
    {
        ExceptionSignal.RecordsSpanEventsUnder(optIn).Should().Be(recordsSpanEvents,
            "logs asks for exceptions as logs only; logs/dup and every other value, unset included, keep "
            + "today's span events, and OpenTelemetry reads an enumerated value without regard to case");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ATypedOptionIsTakenAsItIs(bool configured)
    {
        ExceptionSignal.RecordsSpanEvents(configured).Should().Be(configured,
            "an application that set the option has said what it wants, whatever the environment says");
    }

    [Test]
    public void AnUnsetOptionFollowsTheEnvironment()
    {
        bool expected = ExceptionSignal.RecordsSpanEventsUnder(Environment.GetEnvironmentVariable(ExceptionSignal.OptInVariable));

        ExceptionSignal.RecordsSpanEvents(configured: null).Should().Be(expected,
            "with nothing set in code, the environment variable is what decides");
    }
}
