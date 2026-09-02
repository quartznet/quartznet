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
using System.Text;

using Quartz.Diagnostics;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// The whole set of instruments a scheduler publishes, snapshotted the way the store span names are.
/// </summary>
/// <remarks>
/// <para>
/// A metric is read by a dashboard, an alert rule and a metrics view, none of which is in this
/// repository, so its name, its kind, its unit and what it says it measures are as much a contract as
/// a public signature. <c>InstrumentNameTest</c> holds the names against
/// <see cref="QuartzInstrumentation.Instruments" />, which catches a rename — but a tenth instrument
/// arriving with a constant of its own, a histogram quietly becoming a counter, or a unit changing
/// from <c>s</c> to <c>ms</c> would all pass it, and each of those breaks somebody's chart without
/// breaking their build. The snapshot is the review: none of them can land unread.
/// </para>
/// <para>
/// The counterpart of <c>TracingJobStoreTest_SpanNames</c>, which does the same for the thirty-three
/// store span names, and the table on the
/// <a href="https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/opentelemetry-integration.html">OpenTelemetry
/// integration</a> page is what a reader sees of it.
/// </para>
/// </remarks>
public sealed class MeterCatalogueTest
{
    [Test]
    public async Task EveryInstrumentKeepsItsName_Kind_UnitAndDescription()
    {
        // Its own meter rather than the shared one, and built before the listener starts: Start
        // publishes the instruments that already exist, so nothing is missed, and having the instance
        // in hand is what lets the filter below name one meter rather than every meter in the process
        // called "Quartz".
        using Meters meters = new(meterFactory: null);

        List<Instrument> published = [];

        using (MeterListener listener = new())
        {
            listener.InstrumentPublished = (instrument, _) =>
            {
                if (ReferenceEquals(instrument.Meter, meters.Meter))
                {
                    published.Add(instrument);
                }
            };

            listener.Start();
        }

        StringBuilder rendered = new();
        foreach (Instrument instrument in published.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            rendered.Append(instrument.Name)
                .Append("  ")
                .Append(Kind(instrument))
                .Append("  ")
                .Append(instrument.Unit ?? "-")
                .Append("  ")
                .AppendLine(instrument.Description ?? "-");
        }

        await Verify(rendered.ToString(), extension: "txt")
            .UseDirectory("../Verify")
            .UseFileName("MeterCatalogueTest_Instruments")
            .DisableRequireUniquePrefix();
    }

    /// <summary>
    /// The instrument's kind as a reader of the documentation would write it — <c>Histogram&lt;double&gt;</c>
    /// rather than the runtime's <c>Histogram`1[System.Double]</c>.
    /// </summary>
    private static string Kind(Instrument instrument)
    {
        Type type = instrument.GetType();
        string name = type.Name;

        int tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick < 0)
        {
            return name;
        }

        string arguments = string.Join(", ", type.GetGenericArguments().Select(FriendlyName));
        return $"{name[..tick]}<{arguments}>";
    }

    private static string FriendlyName(Type type) => type switch
    {
        _ when type == typeof(long) => "long",
        _ when type == typeof(int) => "int",
        _ when type == typeof(double) => "double",
        _ when type == typeof(float) => "float",
        _ when type == typeof(decimal) => "decimal",
        _ when type == typeof(short) => "short",
        _ when type == typeof(byte) => "byte",
        _ => type.Name,
    };
}
