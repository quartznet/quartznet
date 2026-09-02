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

using System.Diagnostics.Metrics;
using System.Reflection;

using Quartz.Diagnostics;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// <see cref="QuartzInstrumentation.Instruments" /> and the instruments a scheduler's meter actually
/// creates are the same set of names.
/// </summary>
/// <remarks>
/// The names are what a dashboard, an alert rule and a metrics view match on, so they are as much a
/// contract as a public signature — and until they were constants an integrator could only copy them
/// out of a documentation page and hope. Publishing them is worth nothing if they can drift from what
/// the meter emits, which is what this holds in both directions: an instrument the meter creates whose
/// name no constant carries, and a constant naming an instrument nothing creates, both fail.
/// </remarks>
public sealed class InstrumentNameTest
{
    /// <summary>
    /// The instrument names the meter publishes, collected by listening while one is constructed.
    /// </summary>
    /// <remarks>
    /// <see cref="MeterListener.InstrumentPublished" /> fires as each instrument is created, so a
    /// listener started first sees the whole set without the meter having to describe itself. Reading
    /// the constants back off the meter is the point: a name typed inline would show up here and in no
    /// constant.
    /// </remarks>
    private static List<string> PublishedInstruments()
    {
        List<string> published = [];

        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name == QuartzInstrumentation.MeterName)
            {
                published.Add(instrument.Name);
            }
        };

        listener.Start();

        // Its own meter rather than the shared one, so a listener that arrives after some other test has
        // already built the shared instance still sees every instrument being created.
        _ = new Meters(meterFactory: null);

        return published;
    }

    private static List<string> DeclaredInstruments() => typeof(QuartzInstrumentation.Instruments)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(x => x.IsLiteral && x.FieldType == typeof(string))
        .Select(x => (string) x.GetRawConstantValue()!)
        .ToList();

    [Test]
    public void EveryInstrumentTheMeterCreatesIsNamedByAConstant()
    {
        PublishedInstruments().Should().BeSubsetOf(DeclaredInstruments(),
            "an instrument whose name is a literal in Meters cannot be nameof-proofed by anybody watching "
            + "it, which is the whole reason QuartzInstrumentation.Instruments exists - build the meter's "
            + "instrument from the constant");
    }

    [Test]
    public void EveryConstantNamesAnInstrumentTheMeterCreates()
    {
        DeclaredInstruments().Should().BeSubsetOf(PublishedInstruments(),
            "a published constant is a promise that a series by that name exists, so one the meter no "
            + "longer creates is a dashboard that silently reads nothing");
    }

    [Test]
    public void TheInstrumentNamesAreTheOnesAlreadyDocumented()
    {
        DeclaredInstruments().Should().BeEquivalentTo(
            [
                "quartz.job.execution.active",
                "quartz.job.execution.duration",
                "quartz.trigger.misfire",
                "quartz.trigger.retry",
                "quartz.trigger.acquisition.duration",
                "quartz.trigger.acquired",
                "quartz.cluster.checkin.duration",
                "quartz.cluster.recovery.trigger",
                "quartz.jobstore.operation.duration",
            ],
            "these are the strings the OpenTelemetry integration page tabulates and the Aspire dashboard "
            + "charts, so publishing them as constants must not be an occasion to rename one");
    }
}
