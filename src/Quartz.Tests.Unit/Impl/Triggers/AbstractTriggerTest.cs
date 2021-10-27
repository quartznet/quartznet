using System;
using System.IO;
using System.Runtime.Serialization.Formatters;
using System.Runtime.Serialization.Formatters.Binary;
using Newtonsoft.Json;

using NUnit.Framework;

using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Impl.Triggers
{
    [TestFixture]
    public class AbstractTriggerTest
    {
        [Test]
        public void Serialization_CanBeDeserialized_OnlyName()
        {
            var deserializedObject = Deserialize("TestTrigger_OnlyName");
            Assert.That(deserializedObject, Is.Not.Null);
            Assert.That(deserializedObject, Is.TypeOf<TestTrigger>());

            var trigger = (TestTrigger)deserializedObject;
            Assert.That(trigger.JobKey, Is.Null);
            Assert.That(trigger.Key, Is.Not.Null);
            Assert.That(trigger.Key.Name, Is.EqualTo("TriggerName"));
            Assert.That(trigger.Key.Group, Is.EqualTo(SchedulerConstants.DefaultGroup));
            Assert.That(trigger.StartTimeUtc, Is.EqualTo(DateTimeOffset.MinValue));
            Assert.That(trigger.EndTimeUtc, Is.Null);
            Assert.That(trigger.MisfireInstruction, Is.EqualTo(MisfireInstruction.InstructionNotSet));
            Assert.That(trigger.JobDataMap, Is.Not.Null);
            Assert.That(trigger.JobDataMap, Is.Empty);
        }

        [Test]
        public void Serialization_CanBeDeserialized_NameAndGroup()
        {
            var deserializedObject = Deserialize("TestTrigger_NameAndGroup");
            Assert.That(deserializedObject, Is.Not.Null);
            Assert.That(deserializedObject, Is.TypeOf<TestTrigger>());

            var trigger = (TestTrigger)deserializedObject;
            Assert.That(trigger.JobKey, Is.Null);
            Assert.That(trigger.Key, Is.Not.Null);
            Assert.That(trigger.Key.Name, Is.EqualTo("TriggerName"));
            Assert.That(trigger.Key.Group, Is.EqualTo("TriggerGroup"));
            Assert.That(trigger.StartTimeUtc, Is.EqualTo(DateTimeOffset.MinValue));
            Assert.That(trigger.EndTimeUtc, Is.Null);
            Assert.That(trigger.MisfireInstruction, Is.EqualTo(MisfireInstruction.InstructionNotSet));
            Assert.That(trigger.JobDataMap, Is.Not.Null);
            Assert.That(trigger.JobDataMap, Is.Empty);
        }


        [Test]
        public void Serialization_CanBeDeserialized_NameAndGroupAndJobNameAndJobGroup()
        {
            var deserializedObject = Deserialize("TestTrigger_NameAndGroupAndJobNameAndJobGroup");
            Assert.That(deserializedObject, Is.Not.Null);
            Assert.That(deserializedObject, Is.TypeOf<TestTrigger>());

            var trigger = (TestTrigger)deserializedObject;
            Assert.That(trigger.JobKey, Is.Not.Null);
            Assert.That(trigger.JobKey.Name, Is.EqualTo("JobName"));
            Assert.That(trigger.JobKey.Group, Is.EqualTo("JobGroup"));
            Assert.That(trigger.Key, Is.Not.Null);
            Assert.That(trigger.Key.Name, Is.EqualTo("TriggerName"));
            Assert.That(trigger.Key.Group, Is.EqualTo("TriggerGroup"));
            Assert.That(trigger.StartTimeUtc, Is.EqualTo(DateTimeOffset.MinValue));
            Assert.That(trigger.EndTimeUtc, Is.Null);
            Assert.That(trigger.MisfireInstruction, Is.EqualTo(MisfireInstruction.InstructionNotSet));
            Assert.That(trigger.JobDataMap, Is.Not.Null);
            Assert.That(trigger.JobDataMap, Is.Empty);
        }

        [Test]
        public void Serialization_CanBeDeserialized_Complete()
        {
            var deserializedObject = Deserialize("TestTrigger_Complete");
            Assert.That(deserializedObject, Is.Not.Null);
            Assert.That(deserializedObject, Is.TypeOf<TestTrigger>());

            var trigger = (TestTrigger)deserializedObject;
            Assert.That(trigger.JobKey, Is.Not.Null);
            Assert.That(trigger.JobKey.Name, Is.EqualTo("JobName"));
            Assert.That(trigger.JobKey.Group, Is.EqualTo("JobGroup"));
            Assert.That(trigger.Key, Is.Not.Null);
            Assert.That(trigger.Key.Name, Is.EqualTo("TriggerName"));
            Assert.That(trigger.Key.Group, Is.EqualTo("TriggerGroup"));
            Assert.That(trigger.StartTimeUtc, Is.EqualTo(new DateTimeOffset(1969, 5, 9, 7, 43, 21, TimeSpan.FromHours(1))));
            Assert.That(trigger.EndTimeUtc, Is.EqualTo(new DateTimeOffset(1973, 8, 13, 16, 3, 45, TimeSpan.FromHours(2))));
            Assert.That(trigger.MisfireInstruction, Is.EqualTo(MisfireInstruction.IgnoreMisfirePolicy));
            Assert.That(trigger.JobDataMap, Is.Not.Null);
            Assert.That(trigger.JobDataMap.Count, Is.EqualTo(2));
            Assert.That(trigger.JobDataMap["X"], Is.EqualTo(7));
            Assert.That(trigger.JobDataMap["Y"], Is.EqualTo(5));
        }

        [Test]
        public void Serialization_CanBeSerializedAndDeserialized_OnlyName()
        {
            BinaryFormatter binaryFormatter = new BinaryFormatter();

            using (var ms = new MemoryStream())
            {
                var trigger = new TestTrigger("TriggerName");
                binaryFormatter.Serialize(ms, trigger);

                ms.Position = 0;

                var deserializedTrigger = binaryFormatter.Deserialize(ms) as TestTrigger;
                Assert.That(deserializedTrigger, Is.Not.Null);
                Assert.That(deserializedTrigger, Is.TypeOf<TestTrigger>());
                Assert.That(deserializedTrigger.JobKey, Is.Null);
                Assert.That(deserializedTrigger.Key, Is.Not.Null);
                Assert.That(deserializedTrigger.Key.Name, Is.EqualTo("TriggerName"));
                Assert.That(deserializedTrigger.Key.Group, Is.EqualTo(SchedulerConstants.DefaultGroup));
            }

            using (var ms = new MemoryStream())
            {
                var trigger = new TestTrigger
                    {
                        Key = new TriggerKey("TriggerName")
                    };
                binaryFormatter.Serialize(ms, trigger);

                ms.Position = 0;

                var deserializedTrigger = binaryFormatter.Deserialize(ms) as TestTrigger;
                Assert.That(deserializedTrigger, Is.Not.Null);
                Assert.That(deserializedTrigger, Is.TypeOf<TestTrigger>());
                Assert.That(deserializedTrigger.JobKey, Is.Null);
                Assert.That(deserializedTrigger.Key, Is.Not.Null);
                Assert.That(deserializedTrigger.Key.Name, Is.EqualTo("TriggerName"));
                Assert.That(deserializedTrigger.Key.Group, Is.EqualTo(SchedulerConstants.DefaultGroup));
            }
        }

        [Test]
        public void Serialization_CanBeSerializedAndDeserialized_NameAndGroup()
        {
            BinaryFormatter binaryFormatter = new BinaryFormatter();

            using (var ms = new MemoryStream())
            {
                var trigger = new TestTrigger("TriggerName", "TriggerGroup");
                binaryFormatter.Serialize(ms, trigger);

                ms.Position = 0;

                var deserializedTrigger = binaryFormatter.Deserialize(ms) as TestTrigger;
                Assert.That(deserializedTrigger, Is.Not.Null);
                Assert.That(deserializedTrigger, Is.TypeOf<TestTrigger>());
                Assert.That(deserializedTrigger.JobKey, Is.Null);
                Assert.That(deserializedTrigger.Key, Is.Not.Null);
                Assert.That(deserializedTrigger.Key.Name, Is.EqualTo("TriggerName"));
                Assert.That(deserializedTrigger.Key.Group, Is.EqualTo("TriggerGroup"));
            }

            using (var ms = new MemoryStream())
            {
                var trigger = new TestTrigger
                    {
                        Key = new TriggerKey("TriggerName", "TriggerGroup")
                    };
                binaryFormatter.Serialize(ms, trigger);

                ms.Position = 0;

                var deserializedTrigger = binaryFormatter.Deserialize(ms) as TestTrigger;
                Assert.That(deserializedTrigger, Is.Not.Null);
                Assert.That(deserializedTrigger, Is.TypeOf<TestTrigger>());
                Assert.That(deserializedTrigger.JobKey, Is.Null);
                Assert.That(deserializedTrigger.Key, Is.Not.Null);
                Assert.That(deserializedTrigger.Key.Name, Is.EqualTo("TriggerName"));
                Assert.That(deserializedTrigger.Key.Group, Is.EqualTo("TriggerGroup"));
            }
        }

        [Test]
        public void Serialization_CanBeSerializedAndDeserialized_NameAndGroupAndJobNameAndJobGroup()
        {
            BinaryFormatter binaryFormatter = new BinaryFormatter();

            using (var ms = new MemoryStream())
            {
                var trigger = new TestTrigger("TriggerName", "TriggerGroup", "JobName", "JobGroup");
                binaryFormatter.Serialize(ms, trigger);

                ms.Position = 0;

                var deserializedTrigger = binaryFormatter.Deserialize(ms) as TestTrigger;
                Assert.That(deserializedTrigger, Is.Not.Null);
                Assert.That(deserializedTrigger, Is.TypeOf<TestTrigger>());
                Assert.That(deserializedTrigger.JobKey, Is.Not.Null);
                Assert.That(deserializedTrigger.JobKey.Name, Is.EqualTo("JobName"));
                Assert.That(deserializedTrigger.JobKey.Group, Is.EqualTo("JobGroup"));
                Assert.That(deserializedTrigger.Key, Is.Not.Null);
                Assert.That(deserializedTrigger.Key.Name, Is.EqualTo("TriggerName"));
                Assert.That(deserializedTrigger.Key.Group, Is.EqualTo("TriggerGroup"));
            }

            using (var ms = new MemoryStream())
            {
                var trigger = new TestTrigger
                    {
                        JobKey = new JobKey("JobName", "JobGroup"),
                        Key = new TriggerKey("TriggerName", "TriggerGroup")
                    };
                binaryFormatter.Serialize(ms, trigger);

                ms.Position = 0;

                var deserializedTrigger = binaryFormatter.Deserialize(ms) as TestTrigger;
                Assert.That(deserializedTrigger, Is.Not.Null);
                Assert.That(deserializedTrigger, Is.TypeOf<TestTrigger>());
                Assert.That(deserializedTrigger.JobKey, Is.Not.Null);
                Assert.That(deserializedTrigger.JobKey.Name, Is.EqualTo("JobName"));
                Assert.That(deserializedTrigger.JobKey.Group, Is.EqualTo("JobGroup"));
                Assert.That(deserializedTrigger.Key, Is.Not.Null);
                Assert.That(deserializedTrigger.Key.Name, Is.EqualTo("TriggerName"));
                Assert.That(deserializedTrigger.Key.Group, Is.EqualTo("TriggerGroup"));
            }
        }

        [Test]
        public void Serialization_CanBeSerializedAndDeserialized_OnlyJobName()
        {
            BinaryFormatter binaryFormatter = new BinaryFormatter();

            using (var ms = new MemoryStream())
            {
                var trigger = new TestTrigger
                    {
                        JobKey = new JobKey("JobName")
                    };
                binaryFormatter.Serialize(ms, trigger);

                ms.Position = 0;

                var deserializedTrigger = binaryFormatter.Deserialize(ms) as TestTrigger;
                Assert.That(deserializedTrigger, Is.Not.Null);
                Assert.That(deserializedTrigger, Is.TypeOf<TestTrigger>());
                Assert.That(deserializedTrigger.JobKey, Is.Not.Null);
                Assert.That(deserializedTrigger.JobKey.Name, Is.EqualTo("JobName"));
                Assert.That(deserializedTrigger.JobKey.Group, Is.EqualTo(SchedulerConstants.DefaultGroup));
                //Assert.That(deserializedTrigger6.Key, Is.Null);
            }
        }

        [Test]
        public void Serialization_CanBeSerializedAndDeserialized_JobNameAndJobGroup()
        {
            BinaryFormatter binaryFormatter = new BinaryFormatter();

            using (var ms = new MemoryStream())
            {
                var trigger = new TestTrigger
                    {
                        JobKey = new JobKey("JobName", "JobGroup")
                    };
                binaryFormatter.Serialize(ms, trigger);

                ms.Position = 0;

                var deserializedTrigger = binaryFormatter.Deserialize(ms) as TestTrigger;
                Assert.That(deserializedTrigger, Is.Not.Null);
                Assert.That(deserializedTrigger, Is.TypeOf<TestTrigger>());
                Assert.That(deserializedTrigger.JobKey, Is.Not.Null);
                Assert.That(deserializedTrigger.JobKey.Name, Is.EqualTo("JobName"));
                Assert.That(deserializedTrigger.JobKey.Group, Is.EqualTo("JobGroup"));
                //Assert.That(deserializedTrigger7.Key, Is.Null);
            }
        }

        [Test]
        public void Serialization_CanBeSerializedAndDeserialized_Complete()
        {
            BinaryFormatter binaryFormatter = new BinaryFormatter();

            using (var ms = new MemoryStream())
            {
                var trigger = new TestTrigger("TriggerName", "TriggerGroup", "JobName", "JobGroup");
                trigger.StartTimeUtc = new DateTimeOffset(1969, 5, 9, 7, 43, 21, TimeSpan.FromHours(1));
                trigger.EndTimeUtc = new DateTimeOffset(1973, 8, 13, 16, 3, 45, TimeSpan.FromHours(2));
                trigger.MisfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
                trigger.JobDataMap = new JobDataMap
                    {
                        {"X", 7 },
                        {"Y", 5 }
                    };

                binaryFormatter.Serialize(ms, trigger);

                ms.Position = 0;

                var deserializedTrigger = binaryFormatter.Deserialize(ms) as TestTrigger;
                Assert.That(deserializedTrigger, Is.Not.Null);
                Assert.That(deserializedTrigger, Is.TypeOf<TestTrigger>());
                Assert.That(deserializedTrigger.JobKey, Is.Not.Null);
                Assert.That(deserializedTrigger.JobKey.Name, Is.EqualTo("JobName"));
                Assert.That(deserializedTrigger.JobKey.Group, Is.EqualTo("JobGroup"));
                Assert.That(deserializedTrigger.Key, Is.Not.Null);
                Assert.That(deserializedTrigger.Key.Name, Is.EqualTo("TriggerName"));
                Assert.That(deserializedTrigger.Key.Group, Is.EqualTo("TriggerGroup"));
                Assert.That(deserializedTrigger.StartTimeUtc, Is.EqualTo(new DateTimeOffset(1969, 5, 9, 7, 43, 21, TimeSpan.FromHours(1))));
                Assert.That(deserializedTrigger.EndTimeUtc, Is.EqualTo(new DateTimeOffset(1973, 8, 13, 16, 3, 45, TimeSpan.FromHours(2))));
                Assert.That(deserializedTrigger.MisfireInstruction, Is.EqualTo(MisfireInstruction.IgnoreMisfirePolicy));
                Assert.That(deserializedTrigger.JobDataMap, Is.Not.Null);
                Assert.That(deserializedTrigger.JobDataMap.Count, Is.EqualTo(2));
                Assert.That(deserializedTrigger.JobDataMap["X"], Is.EqualTo(7));
                Assert.That(deserializedTrigger.JobDataMap["Y"], Is.EqualTo(5));
            }
        }

        private static object Deserialize(string name)
        {
            using (var fs = File.OpenRead(Path.Combine("Serialized", name + ".ser")))
            {
                BinaryFormatter binaryFormatter = new BinaryFormatter();
                binaryFormatter.AssemblyFormat = FormatterAssemblyStyle.Simple;
                return binaryFormatter.Deserialize(fs);
            }
        }

        [Serializable]
        private class TestTrigger : AbstractTrigger
        {
            public TestTrigger()
            {
            }

            public TestTrigger(string name)
                : base(name)
            {
            }

            public TestTrigger(string name, string group)
                : base(name, group)
            {
            }

            public TestTrigger(string name, string group, string jobName, string jobGroup)
                : base(name, group, jobName, jobGroup)
            {
            }

            public override IScheduleBuilder GetScheduleBuilder()
            {
                throw new NotImplementedException();
            }

            [JsonIgnore]
            public override DateTimeOffset? FinalFireTimeUtc => throw new NotImplementedException();

            public override void Triggered(ICalendar cal)
            {
                throw new NotImplementedException();
            }

            public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar cal)
            {
                throw new NotImplementedException();
            }

            public override bool GetMayFireAgain()
            {
                throw new NotImplementedException();
            }

            public override DateTimeOffset? GetNextFireTimeUtc()
            {
                throw new NotImplementedException();
            }

            public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime)
            {
                throw new NotImplementedException();
            }

            protected override bool ValidateMisfireInstruction(int misfireInstruction)
            {
                // This method must be implemented because it's used in AbstractTrigger.MisfireInstruction's setter
                // and JSON serialization serializes at the property level (as opposed to the binary formatter which
                // serialized at the field level and, therefore, did not need this implemented).
                return true;
            }

            public override void UpdateAfterMisfire(ICalendar cal)
            {
                throw new NotImplementedException();
            }

            public override void UpdateWithNewCalendar(ICalendar cal, TimeSpan misfireThreshold)
            {
                throw new NotImplementedException();
            }

            public override void SetNextFireTimeUtc(DateTimeOffset? nextFireTime)
            {
                throw new NotImplementedException();
            }

            public override void SetPreviousFireTimeUtc(DateTimeOffset? previousFireTime)
            {
                throw new NotImplementedException();
            }

            public override DateTimeOffset? GetPreviousFireTimeUtc()
            {
                throw new NotImplementedException();
            }

            public override bool HasMillisecondPrecision => false;
        }
    }
}