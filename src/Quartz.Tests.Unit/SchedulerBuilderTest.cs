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
            config.UseDefaultThreadPool(x => x.ThreadCount = 100);

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
                js.RetryInterval = TimeSpan.FromSeconds(20);
                js.UseClustering(c =>
                {
                    c.CheckinInterval = TimeSpan.FromSeconds(10);
                    c.CheckinMisfireThreshold = TimeSpan.FromSeconds(15);
                });

                js.UseSqlServer(db =>
                {
                    db.ConnectionString = "Server=localhost;Database=quartznet;";
                    db.TablePrefix = "QRTZ2019_";
                });
            });
            Assert.That(config.Properties["quartz.dataSource.default.connectionString"], Is.EqualTo("Server=localhost;Database=quartznet;"));

            Assert.That(config.Properties["quartz.jobStore.type"], Is.EqualTo(typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.driverDelegateType"], Is.EqualTo(typeof(SqlServerDelegate).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(config.Properties["quartz.jobStore.dataSource"], Is.EqualTo("default"));
            Assert.That(config.Properties["quartz.jobStore.tablePrefix"], Is.EqualTo("QRTZ2019_"));
            Assert.That(config.Properties["quartz.jobStore.clusterCheckinInterval"], Is.EqualTo("10000"));
            Assert.That(config.Properties["quartz.jobStore.clusterCheckinMisfireThreshold"], Is.EqualTo("15000"));
            Assert.That(config.Properties["quartz.jobStore.dbRetryInterval"], Is.EqualTo("20000"));
        }

        [Test]
        public void TestPostgresJobStore()
        {
            var config = SchedulerBuilder.Create();
            config
                .UsePersistentStore(s =>
                    s.UsePostgres(("Server=localhost;Database=quartznet;"))
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
                .UsePersistentStore(s =>
                    s.UseMySql("Server=localhost;Database=quartznet;")
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
                .UsePersistentStore(p =>
                    p.UseFirebird("Server=localhost;Database=quartznet;")
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
            config.UsePersistentStore(s =>
                s.UseOracle("Server=localhost;Database=quartznet;")
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
                options.UseSQLite("Server=localhost;Database=quartznet;")
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
                .UseXmlSchedulingConfiguration(x =>
                {
                    x.Files = new[] {"jobs.xml", "jobs2.xml"};
                    x.ScanInterval = TimeSpan.FromSeconds(2);
                    x.FailOnFileNotFound = true;
                    x.FailOnSchedulingError = true;
                });

            Assert.That(builder.Properties["quartz.plugin.xml.type"], Is.EqualTo(typeof(XMLSchedulingDataProcessorPlugin).AssemblyQualifiedNameWithoutVersion()));
            Assert.That(builder.Properties["quartz.plugin.xml.fileNames"], Is.EqualTo("jobs.xml,jobs2.xml"));
            Assert.That(builder.Properties["quartz.plugin.xml.failOnFileNotFound"], Is.EqualTo("true"));
            Assert.That(builder.Properties["quartz.plugin.xml.failOnSchedulingError"], Is.EqualTo("true"));
            Assert.That(builder.Properties["quartz.plugin.xml.scanInterval"], Is.EqualTo("2"));
        }
    }
}