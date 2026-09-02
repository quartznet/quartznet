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

using Microsoft.Extensions.DependencyInjection;

using Quartz.Diagnostics;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// Which of the two meters a scheduler can end up with is the scheduler's to close.
/// </summary>
/// <remarks>
/// <para>
/// <c>Meters</c> takes the container's <see cref="IMeterFactory" /> where there is one and creates a
/// meter of its own where there is not. A factory's meter belongs to the factory — that is what lets
/// two hosts in one process collect each other's measurements apart — and closing it here would take
/// it out from under whatever else shares the factory's entry for the same name. The other one had no
/// owner at all until <c>Meters</c> became <see cref="IDisposable" />: a host that never called
/// <c>AddMetrics()</c> left a live <see cref="Meter" /> behind for every scheduler it ever built, and
/// <c>CA1001</c> is suppressed repository-wide, so no analyzer said so.
/// </para>
/// <para>
/// Disposal is observed through <see cref="MeterListener.MeasurementsCompleted" />, which is what a
/// listener is told when an instrument it was collecting goes away. The listener is narrowed to one
/// instance's own meter by reference, because every <c>Meters</c> in the process publishes on the same
/// meter <em>name</em>.
/// </para>
/// </remarks>
public sealed class MeterOwnershipTest
{
    /// <summary>
    /// Records the names of <paramref name="meters" />' instruments as they are closed, for as long as
    /// the returned listener is alive.
    /// </summary>
    private static (MeterListener Listener, List<string> Closed) WatchForClosure(Meters meters)
    {
        List<string> closed = [];

        MeterListener listener = new();
        listener.InstrumentPublished = (instrument, subscription) =>
        {
            if (ReferenceEquals(instrument.Meter, meters.Meter))
            {
                subscription.EnableMeasurementEvents(instrument);
            }
        };
        listener.MeasurementsCompleted = (instrument, _) => closed.Add(instrument.Name);
        listener.Start();

        return (listener, closed);
    }

    /// <summary>
    /// Every instrument name the meter is meant to publish, read off the published constants so that a
    /// tenth instrument does not have to be counted here as well.
    /// </summary>
    private static List<string> DeclaredInstruments() => typeof(QuartzInstrumentation.Instruments)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(x => x.IsLiteral && x.FieldType == typeof(string))
        .Select(x => (string) x.GetRawConstantValue()!)
        .ToList();

    [Test]
    public void AMeterItMadeItselfIsClosedOnDispose()
    {
        Meters meters = new(meterFactory: null);
        (MeterListener listener, List<string> closed) = WatchForClosure(meters);
        using (listener)
        {
            meters.Dispose();

            closed.Should().BeEquivalentTo(DeclaredInstruments(),
                "nothing else can close a meter Quartz created for itself, so a host with no "
                + "IMeterFactory leaked one — with every one of its instruments — per scheduler it built");
        }
    }

    [Test]
    public void AMeterTheFactoryMadeIsLeftToTheFactory()
    {
        ServiceCollection services = new();
        services.AddMetrics();
        using ServiceProvider provider = services.BuildServiceProvider();

        Meters meters = new(provider.GetRequiredService<IMeterFactory>());
        (MeterListener listener, List<string> closed) = WatchForClosure(meters);
        using (listener)
        {
            meters.Dispose();

            closed.Should().BeEmpty(
                "the factory created the meter and disposes it with the container; closing it here "
                + "would take it away from anything else holding the factory's entry for the same name");
        }
    }

    /// <summary>
    /// The container builds <c>Meters</c> through a factory registration, so the instance is one it
    /// owns and disposes with everything else it built. That wiring is what makes the
    /// <see cref="IDisposable" /> worth anything.
    /// </summary>
    [Test]
    public void TheContainerClosesTheMeterItBuilt()
    {
        ServiceCollection services = new();
        services.AddQuartz();

        ServiceProvider provider = services.BuildServiceProvider();
        Meters meters = provider.GetRequiredService<Meters>();

        (MeterListener listener, List<string> closed) = WatchForClosure(meters);
        using (listener)
        {
            provider.Dispose();

            closed.Should().BeEquivalentTo(DeclaredInstruments(),
                "AddQuartz on its own registers no IMeterFactory, so this meter is the scheduler's own "
                + "and goes down with the container that asked for it");
        }
    }
}
