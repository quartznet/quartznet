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
            var config = SchedulerBuilder.Create();
            config.UseInMemoryStore();
            config.UseDefaultThreadPool(x => x.SetThreadCount(100));

            Assert.That(config.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(RAMJobStore).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.threadPool.threadCount"], Is.EqualTo("100"));
        }

        [Test]
        public void TestSqlServerJobStore()
        {
            var config = SchedulerBuilder.Create();
            config.UsePersistentStore(js =>
            {
                js.UseJsonSerializer();
                js.Clustered(options => options
                    .SetCheckinInterval(TimeSpan.FromSeconds(10))
                    .SetCheckinMisfireThreshold(TimeSpan.FromSeconds(15))
                );

                js.UseSqlServer(db =>
                {
                    db
                        .SetConnectionString("Server=localhost;Database=quartznet;")
                        .SetTablePrefix("QRTZ2019_");
                });
            });
            Assert.That(config.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(config.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(SqlServerDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
            Assert.That(config.Properties["quartz.jobStore.tablePrefix"], Is.EqualTo("QRTZ2019_"));
            Assert.That(config.Properties["quartz.jobStore.clusterCheckinInterval"], Is.EqualTo("10000"));
            Assert.That(config.Properties["quartz.jobStore.clusterCheckinMisfireThreshold"], Is.EqualTo("15000"));
        }

        [Test]
        public void TestPostgresJobStore()
        {
            var config = SchedulerBuilder.Create();
            config
                .UsePersistentStore(options =>
                    options
                        .UsePostgres(db => db.SetConnectionString("Server=localhost;Database=quartznet;"))
                );
            Assert.That(config.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(config.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(PostgreSQLDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
        }

        [Test]
        public void TestMySqlJobStore()
        {
            var config = SchedulerBuilder.Create();
            config
                .UsePersistentStore(options =>
                    options.UseMySql(db => db.SetConnectionString("Server=localhost;Database=quartznet;"))
                );
            Assert.That(config.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(config.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(MySQLDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
        }

        [Test]
        public void TestFirebirdJobStore()
        {
            var config = SchedulerBuilder.Create();
            config
                .UsePersistentStore(persistence =>
                    persistence.UseFirebird(db => db
                        .SetConnectionString("Server=localhost;Database=quartznet;"))
                );
            Assert.That(config.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(config.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(FirebirdDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
        }

        [Test]
        public void TestOracleJsobStore()
        {
            var config = SchedulerBuilder.Create();
            config
                .UsePersistentStore(options => 
                    options.UseOracle(db => db.SetConnectionString("Server=localhost;Database=quartznet;"))
                      );
            Assert.That(config.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(config.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(OracleDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
            
        }
        [Test]
        public void TestSQLiteJsobStore()
        {
            var config = SchedulerBuilder.Create();
            config.UsePersistentStore(options =>
                options.UseSQLite(db => db.SetConnectionString("Server=localhost;Database=quartznet;"))
            );
            Assert.That(config.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(config.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(SQLiteDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
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
                    .SetFiles("jobs.xml", "jobs2.xml")
                    .SetScanInterval(TimeSpan.FromSeconds(2))
                    .SetFailOnFileNotFound()
                    .SetFailOnSchedulingError()
                );

            Assert.That(builder.Properties["quartz.plugin.xml.type"], Is.EqualTo(typeof(XMLSchedulingDataProcessorPlugin).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.plugin.xml.fileNames"], Is.EqualTo("jobs.xml,jobs2.xml"));
            Assert.That(builder.Properties["quartz.plugin.xml.failOnFileNotFound"], Is.EqualTo("true"));
            Assert.That(builder.Properties["quartz.plugin.xml.failOnSchedulingError"], Is.EqualTo("true"));
            Assert.That(builder.Properties["quartz.plugin.xml.scanInterval"], Is.EqualTo("2"));
        }
    }
}