using System;

using NUnit.Framework;

using Quartz.Impl.AdoJobStore;
using Quartz.Plugin.TimeZoneConverter;
using Quartz.Plugin.Xml;
using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Tests.Unit
{
    public class SchedulerBuilderTest
    {
        [Test]
        public void TestRamJobStore()
        {
            var builder = new SchedulerBuilder()
                .UseMemoryStore()
                .WithDefaultThreadPool(x =>
                    x.WithThreadCount(100)
                );

            Assert.That(builder.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(RAMJobStore).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.threadPool.threadCount"], Is.EqualTo("100"));
        }

        [Test]
        public void TestSqlServerJobStore()
        {
            var builder = new SchedulerBuilder()
                .UsePersistentStore(persistence =>
                    persistence
                        .WithJsonSerializer()
                        .Clustered(cluster => cluster
                            .WithCheckinInterval(TimeSpan.FromSeconds(10))
                            .WithCheckinMisfireThreshold(TimeSpan.FromSeconds(15))
                        )
                        .UseSqlServer(mssql =>
                            mssql.WithConnectionString("Server=localhost;Database=quartznet;")
                        )
                        .WithTablePrefix("QRTZ2019_")

                );
            Assert.That(builder.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(builder.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(SqlServerDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
            Assert.That(builder.Properties["quartz.jobStore.tablePrefix"], Is.EqualTo("QRTZ2019_"));
            Assert.That(builder.Properties["quartz.jobStore.clusterCheckinInterval"], Is.EqualTo("10000"));
            Assert.That(builder.Properties["quartz.jobStore.clusterCheckinMisfireThreshold"], Is.EqualTo("15000"));
        }

        [Test]
        public void TestPostgresJobStore()
        {
            var builder = new SchedulerBuilder()
                .UsePersistentStore(persistence =>
                    persistence
                        .UsePostgres(postgres =>
                            postgres.WithConnectionString("Server=localhost;Database=quartznet;")
                        )
                );
            Assert.That(builder.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(builder.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(PostgreSQLDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
        }

        [Test]
        public void TestTimeZonePlugin()
        {
            var builder = new SchedulerBuilder().UseTimeZoneConverter();
            Assert.That(builder.Properties["quartz.plugin.timeZoneConverter.type"], Is.EqualTo(typeof(TimeZoneConverterPlugin).AssemblyQualifiedNameWithoutVersion()));
        }

        [Test]
        public void TestXmlSchedulingPlugin()
        {
            var builder = new SchedulerBuilder().UseXmlSchedulingConfiguration(x => x
                .WithFiles("jobs.xml", "jobs2.xml")
                .WithChangeDetection(TimeSpan.FromSeconds(2))
                .FailOnFileNotFound()
                .FailOnSchedulingError()
            );

            Assert.That(builder.Properties["quartz.plugin.xml.type"], Is.EqualTo(typeof(XMLSchedulingDataProcessorPlugin).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.plugin.xml.fileNames"], Is.EqualTo("jobs.xml,jobs2.xml"));
            Assert.That(builder.Properties["quartz.plugin.xml.failOnFileNotFound"], Is.EqualTo("true"));
            Assert.That(builder.Properties["quartz.plugin.xml.failOnSchedulingError"], Is.EqualTo("true"));
            Assert.That(builder.Properties["quartz.plugin.xml.scanInterval"], Is.EqualTo("2"));
        }
    }
}