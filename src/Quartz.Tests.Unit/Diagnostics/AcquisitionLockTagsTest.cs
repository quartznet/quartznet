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

using System.Diagnostics.Metrics;

using Quartz.Diagnostics;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// The lock attributes on the two acquisition instruments (#4067): the lock a firing round ran under and,
/// on a locked round, why. Present where the store says, absent where it does not, so that the in-memory
/// store's series keep the shape they had.
/// </summary>
public sealed class AcquisitionLockTagsTest
{
    private const string LockTag = "quartz.jobstore.lock";
    private const string LockReasonTag = "quartz.jobstore.lock.reason";

    private readonly List<(string Instrument, KeyValuePair<string, object>[] Tags)> measurements = [];

    private RecordingMeterFactory meterFactory;
    private MeterListener listener;
    private Meters meters;

    [SetUp]
    public void SetUp()
    {
        measurements.Clear();
        meterFactory = new RecordingMeterFactory();
        listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (ReferenceEquals(instrument.Meter, meterFactory.Created))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => measurements.Add((instrument.Name, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => measurements.Add((instrument.Name, tags.ToArray())));
        listener.Start();
        meters = new Meters(meterFactory);
    }

    [TearDown]
    public void TearDown()
    {
        listener.Dispose();
        meters.Dispose();
        meterFactory.Dispose();
    }

    [Test]
    public void ALockedRoundIsTaggedWithTheLockAndTheReason()
    {
        meters.TriggersAcquired("sched", "node-a", 2, TimeSpan.FromMilliseconds(5), "TRIGGER_ACCESS", "serial_job");

        measurements.Select(x => x.Instrument).Should().BeEquivalentTo(["quartz.trigger.acquisition.duration", "quartz.trigger.acquired"]);
        measurements.Should().OnlyContain(x => x.Tags.Contains(new KeyValuePair<string, object>(LockTag, "TRIGGER_ACCESS")));
        measurements.Should().OnlyContain(x => x.Tags.Contains(new KeyValuePair<string, object>(LockReasonTag, "serial_job")),
            "the reason is what tells an operator which schedule keeps the lock in play");
    }

    [Test]
    public void ARoundThatTookNoLockIsTaggedNoneWithNoReason()
    {
        meters.TriggersAcquired("sched", "node-a", 1, TimeSpan.FromMilliseconds(5), "none", lockReason: null);

        measurements.Should().HaveCount(2).And.OnlyContain(x => x.Tags.Contains(new KeyValuePair<string, object>(LockTag, "none")));
        measurements.Should().OnlyContain(x => x.Tags.All(tag => tag.Key != LockReasonTag),
            "a round that took no lock has no reason to give, and an empty attribute would be a third series");
    }

    [Test]
    public void AStoreThatSaysNothingAboutItsLockAddsNoAttribute()
    {
        meters.TriggersAcquired("sched", "node-a", 0, TimeSpan.FromMilliseconds(5));

        measurements.Should().ContainSingle(x => x.Instrument == "quartz.trigger.acquisition.duration")
            .Which.Tags.Select(tag => tag.Key).Should().BeEquivalentTo(["quartz.scheduler.name", "quartz.scheduler.id"],
                "the in-memory store says nothing, so its series keep the two attributes they had");
    }

    /// <summary>
    /// Hands out one meter and remembers it, so that the listener can tell this test's instruments from
    /// every other test's.
    /// </summary>
    private sealed class RecordingMeterFactory : IMeterFactory
    {
        public Meter Created { get; private set; }

        public Meter Create(MeterOptions options)
        {
            Created = new Meter(options);
            return Created;
        }

        public void Dispose() => Created?.Dispose();
    }
}
