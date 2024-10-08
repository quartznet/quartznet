---

title: 'Lesson 9: JobStores'
---

JobStore's are responsible for keeping track of all the "work data" that you give to the scheduler:
jobs, triggers, calendars, etc. Selecting the appropriate IJobStore implementation for your Quartz scheduler instance is an important step.
Luckily, the choice should be a very easy one once you understand the differences between them.
You declare which JobStore your scheduler should use (and it's configuration settings) in the properties file (or object) that
you provide to the SchedulerFactory that you use to produce your scheduler instance.

*Never use a JobStore instance directly in your code. For some reason many people attempt to do this.
The JobStore is for behind-the-scenes use of Quartz itself. You have to tell Quartz (through configuration) which JobStore to use,
but then you should only work with the Scheduler interface in your code.*

## RAMJobStore

RAMJobStore is the simplest JobStore to use, it is also the most performant (in terms of CPU time).
RAMJobStore gets its name in the obvious way: it keeps all of its data in RAM. This is why it's lightning-fast,
and also why it's so simple to configure. The drawback is that when your application ends (or crashes) all of
the scheduling information is lost - this means RAMJobStore cannot honor the setting of "non-volatility" on jobs and triggers.
For some applications this is acceptable - or even the desired behavior, but for other applications, this may be disastrous.

**Configuring Quartz to use RAMJobStore**

```text
 quartz.jobStore.type = Quartz.Simpl.RAMJobStore, Quartz
```

To use RAMJobStore (and assuming you're using StdSchedulerFactory) you don't need to do anything special. Default configuration
of Quartz.NET uses RAMJobStore as job store implementation.

## ADO.NET Job Store (AdoJobStore)

AdoJobStore is also aptly named - it keeps all of its data in a database via ADO.NET.
Because of this it is a bit more complicated to configure than RAMJobStore, and it also is not as fast.
However, the performance draw-back is not terribly bad, especially if you build the database tables with indexes on the primary keys.

To use AdoJobStore, you must first create a set of database tables for Quartz.NET to use.
You can find table-creation SQL scripts in the "database/dbtables" directory of the Quartz.NET distribution.
If there is not already a script for your database type, just look at one of the existing ones, and modify it in any way necessary for your DB.
One thing to note is that in these scripts, all the the tables start with the prefix "QRTZ_"
such as the tables "QRTZ_TRIGGERS", and "QRTZ_JOB_DETAIL". This prefix can actually be anything you'd like, as long as you inform AdoJobStore
what the prefix is (in your Quartz.NET properties). Using different prefixes may be useful for creating multiple sets of tables,
for multiple scheduler instances, within the same database.

Currently the only option for the internal implementation of job store is JobStoreTX which creates transactions by itself.
This is different from Java version of Quartz where there is also option to choose JobStoreCMT which uses J2EE container
managed transactions.

The last piece of the puzzle is setting up a data source from which AdoJobStore can get connections to your database.
Data sources are defined in your Quartz.NET properties. Data source information contains the connection string
and ADO.NET delegate information.

**Configuring Quartz to use JobStoreTx**

```text
    quartz.jobStore.type = Quartz.Impl.AdoJobStore.JobStoreTX, Quartz
```

Next, you need to select a IDriverDelegate implementation for the JobStore to use.
The DriverDelegate is responsible for doing any ADO.NET work that may be needed for your specific database.
StdAdoDelegate is a delegate that uses "vanilla" ADO.NET code (and SQL statements) to do its work.
If there isn't another delegate made specifically for your database, try using this delegate -
special delegates usually have better performance or workarounds for database specific issues.
Other delegates can be found in the "Quartz.Impl.AdoJobStore" namespace, or in its sub-namespaces.

**NOTE:** Quartz.NET will issue warning if you are using the default StdAdoDelegate as it has poor performance
when you have a lot of triggers to select from. Specific delegates have special SQL to limit result
set length (SQLServerDelegate uses TOP n, PostgreSQLDelegate LIMIT n, OracleDelegate ROWCOUNT() <= n etc.).

Once you've selected your delegate, set its class name as the delegate for AdoJobStore to use.

**Configuring AdoJobStore to use a DriverDelegate**

```text
    quartz.jobStore.driverDelegateType = Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz
```

Next, you need to inform the JobStore what table prefix (discussed above) you are using.

**Configuring AdoJobStore with the Table Prefix**

```text
    quartz.jobStore.tablePrefix = QRTZ_
```

And finally, you need to set which data source should be used by the JobStore. The named data source must also be defined in your Quartz properties.
In this case, we're specifying that Quartz should use the data source name "myDS" (that is defined elsewhere in the configuration properties).

**Configuring AdoJobStore with the name of the data source to use**

```text
    quartz.jobStore.dataSource = myDS
```

One last thing that is needed for the configuration is to set data source connection string information and database provider. Connection
string is the standard ADO.NET connection which is driver specific. Database provider is an abstraction of database drivers to create
loose coupling between database drivers and Quartz.

**Setting Data Source's Connection String And Database Provider**

```text
     quartz.dataSource.myDS.connectionString = Server=localhost;Database=quartz;Uid=quartznet;Pwd=quartznet
     quartz.dataSource.myDS.provider = MySql-50
```

Currently following database providers are supported:

* SqlServer-20 - SQL Server driver for .NET Framework 2.0
* OracleODP-20 - Oracle's Oracle Driver
* OracleODPManaged-1123-40 Oracle's managed driver for Oracle 11
* OracleODPManaged-1211-40 Oracle's managed driver for Oracle 12
* MySql-50 - MySQL Connector/.NET v. 5.0 (.NET 2.0)
* MySql-51 - MySQL Connector/:NET v. 5.1 (.NET 2.0)
* MySql-65 - MySQL Connector/:NET v. 6.5 (.NET 2.0)
* SQLite-10 - SQLite ADO.NET 2.0 Provider v. 1.0.56 (.NET 2.0)
* Firebird-201 - Firebird ADO.NET 2.0 Provider v. 2.0.1 (.NET 2.0)
* Firebird-210 - Firebird ADO.NET 2.0 Provider v. 2.1.0 (.NET 2.0)
* Npgsql-20 - PostgreSQL Npgsql

**You can and should use latest version of driver if newer is available, just create an assembly binding redirect**

If your Scheduler is very busy (i.e. nearly always executing the same number of jobs as the size of the thread pool, then you should
probably set the number of connections in the data source to be the about the size of the thread pool + 1.) This is commonly configured
in the ADO.NET connection string - see your driver implementation for details.

The "quartz.jobStore.useProperties" config parameter can be set to "true" (defaults to false) in order to instruct AdoJobStore that all values in JobDataMaps will be strings,
and therefore can be stored as name-value pairs, rather than storing more complex objects in their serialized form in the BLOB column. This is much safer in the long term,
as you avoid the class versioning issues that there are with serializing your non-String classes into a BLOB.

**Configuring AdoJobStore to use strings as JobDataMap values (recommended)**

```text
    quartz.jobStore.useProperties = true
```
