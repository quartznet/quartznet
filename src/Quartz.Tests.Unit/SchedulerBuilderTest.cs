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
            var builder = SchedulerBuilder.Create()
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
            var builder = SchedulerBuilder.Create()
                .UsePersistentStore(persistence =>
                    persistence
                        .WithJsonSerializer()
                        .Clustered(cluster => cluster
                            .WithCheckinInterval(TimeSpan.FromSeconds(10))
                            .WithCheckinMisfireThreshold(TimeSpan.FromSeconds(15))
                        )
                        .UseSqlServer(db =>
                            db.WithConnectionString("Server=localhost;Database=quartznet;")
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
            var builder = SchedulerBuilder.Create()
                .UsePersistentStore(persistence =>
                    persistence
                        .UsePostgres(db =>
                            db.WithConnectionString("Server=localhost;Database=quartznet;")
                        )
                );
            Assert.That(builder.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(builder.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(PostgreSQLDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
        }

        [Test]
        public void TestMySqlJobStore()
        {
            var builder = SchedulerBuilder.Create()
                .UsePersistentStore(persistence =>
                    persistence
                        .UseMySql(db =>
                            db.WithConnectionString("Server=localhost;Database=quartznet;")
                        )
                );
            Assert.That(builder.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(builder.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(MySQLDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
        }

        [Test]
        public void TestFirebirdJobStore()
        {
            var builder = SchedulerBuilder.Create()
                .UsePersistentStore(persistence =>
                    persistence
                        .UseFirebird(db =>
                            db.WithConnectionString("Server=localhost;Database=quartznet;")
                        )
                );
            Assert.That(builder.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(builder.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(FirebirdDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
        }

        [Test]
        public void TestOracleJsobStore()
        {
            var builder = SchedulerBuilder.Create()
                .UsePersistentStore(persistence =>
                    persistence
                        .UseOracle(db =>
                            db.WithConnectionString("Server=localhost;Database=quartznet;")
                        )
                );
            Assert.That(builder.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(builder.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(OracleDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
            
        }
        [Test]
        public void TestSQLiteJsobStore()
        {
            var builder = SchedulerBuilder.Create()
                .UsePersistentStore(persistence =>
                    persistence
                        .UseSQLite(db =>
                            db.WithConnectionString("Server=localhost;Database=quartznet;")
                        )
                );
            Assert.That(builder.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(builder.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(SQLiteDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
        }

        [Test]
        public void TestTimeZonePlugin()
        {
            var builder = SchedulerBuilder.Create()
                .UseTimeZoneConverter();

            Assert.That(builder.Properties["quartz.plugin.timeZoneConverter.type"], Is.EqualTo(typeof(TimeZoneConverterPlugin).AssemblyQualifiedNameWithoutVersion()));
        }

        [Test]
        public void TestXmlSchedulingPlugin()
        {
            var builder = SchedulerBuilder.Create()
                .UseXmlSchedulingConfiguration(x => x
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