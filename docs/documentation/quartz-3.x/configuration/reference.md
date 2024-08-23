---

title: Configuration Reference
---

# Quartz.NET Configuration Reference

[[toc]]

By default, `StdSchedulerFactory` loads a properties file named `quartz.config` from the "current working directory".
If that fails, then the `quartz.config` file located (as an embedded resource) in the Quartz dll is loaded.
If you wish to use a file other than these defaults, you must define the system property `quartz.properties` to point to the file you want.

Alternatively, you can explicitly initialize the factory by calling one of the `Initialize(xx)` methods before calling `GetScheduler()` on the `StdSchedulerFactory`.

Instances of the specified `IJobStore`, `IThreadPool`, and other SPI types will be created by name, and then any additional properties specified for them in the conFig file will be set on the instance by calling an equivalent property set method.
For example if the properties file contains the property `quartz.jobStore.myProp = 10` then after the JobStore type has been instantiated, the setter of property `MyProp` will be called on it.
Type conversion to primitive types (int, long, float, double, boolean, and string) are performed before calling the property’s setter method.

One property can reference another property’s value by specifying a value following the convention of `$@other.property.name`, for example, to reference the scheduler’s instance name as the value for some other property, you would use `$@quartz.scheduler.instanceName`.

::: tip
You can also use code-based configuration which essentially builds these keys.
:::

## Main Configuration

These properties configure the identification of the scheduler, and various other "top level" settings.

| Property Name                                               | Required | Type    | Default Value                                  |
|-------------------------------------------------------------|----------|---------|------------------------------------------------|
| quartz.scheduler.instanceName                               | no       | string  | 'QuartzScheduler'                              |
| quartz.scheduler.instanceId                                 | no       | string  | 'NON_CLUSTERED'                                |
| quartz.scheduler.instanceIdGenerator.type                   | no       | string  | Quartz.Simpl.SimpleInstanceIdGenerator, Quartz |
| quartz.scheduler.threadName                                 | no       | string  | instanceName + '_QuartzSchedulerThread'        |
| quartz.scheduler.makeSchedulerThreadDaemon                  | no       | boolean | false                                          |
| quartz.scheduler.idleWaitTime                               | no       | long    | 30000                                          |
| quartz.scheduler.typeLoadHelper.type                        | no       | string  | Quartz.Simpl.SimpleTypeLoadHelper              |
| quartz.scheduler.jobFactory.type                            | no       | string  | Quartz.Simpl.PropertySettingJobFactory         |
| quartz.context.key.SOME_KEY                                 | no       | string  | none                                           |
| quartz.scheduler.wrapJobExecutionInUserTransaction          | no       | boolean | false                                          |
| quartz.scheduler.batchTriggerAcquisitionMaxCount            | no       | int     | 1                                              |
| quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow | no       | long    | 0                                              |

### `quartz.scheduler.instanceName`

Can be any string, and the value has no meaning to the scheduler itself - but rather serves as a mechanism for client code to distinguish schedulers when multiple instances are used within the same program.
If you are using the clustering features, you must use the same name for every instance in the cluster that is 'logically' the same scheduler.

### `quartz.scheduler.instanceId`

Can be any string, but must be unique for all schedulers working as if they are the same 'logical' Scheduler within a cluster.
You may use the value "AUTO" as the instanceId if you wish the Id to be generated for you.
Or the value "SYS_PROP" if you want the value to come from the system property "quartz.scheduler.instanceId".

### `quartz.scheduler.instanceIdGenerator.type`

Only used if quartz.scheduler.instanceId is set to "AUTO". Defaults to "Quartz.Simpl.SimpleInstanceIdGenerator",
which generates an instance id based upon host name and time stamp. Other `InstanceIdGenerator` implementations include `SystemPropertyInstanceIdGenerator`
(which gets the instance id from the system property "quartz.scheduler.instanceId", and `HostnameInstanceIdGenerator` which uses the local host name (`Dns.GetHostEntry(Dns.GetHostName())`).
You can also implement the InstanceIdGenerator interface your self.

### `quartz.scheduler.threadName`

Can be any string that is a valid name for the main scheduler thread.
If this property is not specified,
the thread will receive the scheduler’s name ("quartz.scheduler.instanceName") plus an the appended string '_QuartzSchedulerThread'.

### `quartz.scheduler.makeSchedulerThreadDaemon`

A boolean value ('true' or 'false') that specifies whether the main thread of the scheduler should be a daemon thread or not.
See also the `quartz.scheduler.makeSchedulerThreadDaemon` property for tuning the `DefaultThreadPool` if that is the thread pool implementation you are using.
(which is most likely the case).

### `quartz.scheduler.idleWaitTime`

Is the amount of time in milliseconds that the scheduler will wait before re-queries for available triggers when the scheduler is otherwise idle.
Normally you should not have to 'tune' this parameter, unless you’re using XA transactions, and are having problems with delayed firings of triggers that should fire immediately.
Values less than 5000ms are not recommended as it will cause excessive database querying. Values less than 1000 are not legal.

### `quartz.scheduler.typeLoadHelper.type`

Defaults to the most robust approach, which is to use the "Quartz.Simpl.SimpleTypeLoadHelper" type - which just loads by using `Type.GetType()`.

### `quartz.scheduler.jobFactory.type`

The type name of the IJobFactory to use. A job factory is responsible for producing instances of `IJob` implementations.
The default is 'Quartz.Simpl.PropertySettingJobFactory', which simply calls `Activator.CreateInstance` with given type to produce a new instance each time execution is about to occur.
`PropertySettingJobFactory` also reflectively sets the job’s properties using the contents of the scheduler context and job and trigger JobDataMaps.

### `quartz.context.key.SOME_KEY`

Represent a name-value pair that will be placed into the "scheduler context" as strings (see IScheduler.Context).
So for example, the setting "quartz.context.key.MyKey = MyValue" would perform the equivalent of `scheduler.Context.Put("MyKey", "MyValue")`.

### `quartz.scheduler.batchTriggerAcquisitionMaxCount`

The maximum number of triggers that a scheduler node is allowed to acquire (for firing) at once. Default value is 1.
The larger the number, the more efficient firing is (in situations where there are very many triggers needing to be fired all at once) -
but at the cost of possible imbalanced load between cluster nodes.

If the value of this property is set to > 1, and AdoJobStore is used, then the property "quartz.jobStore.acquireTriggersWithinLock" must be set to "true" to avoid data corruption.

### `quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow`

The amount of time in milliseconds that a trigger is allowed to be acquired and fired ahead of its scheduled fire time.
Defaults to 0. The larger the number, the more likely batch acquisition of triggers to fire will be able to select and fire more than 1 trigger at a time -
at the cost of trigger schedule not being honored precisely (triggers may fire this amount early).

This may be useful (for performance’s sake) in situations where the scheduler has very large numbers of triggers that need to be fired at or near the same time.

## ThreadPool

| Property Name                    | Required | Type   | Default Value                  |
|----------------------------------|----------|--------|--------------------------------|
| quartz.threadPool.type           | no       | string | Quartz.Simpl.DefaultThreadPool |
| quartz.threadPool.maxConcurrency | no       | int    | 10                             |

### `quartz.threadPool.type`

Is the name of the ThreadPool implementation you wish to use.
The thread pool that ships with Quartz is "Quartz.Simpl.DefaultThreadPool", and should meet the needs of nearly every user.

It has very simple behavior and is very well tested. It dispatches tasks to .NET task queue and ensures that configured max amount of concurrent tasks limit is obeyed.
You should study [CLR's managed thread pool](https://docs.microsoft.com/en-us/dotnet/standard/threading/the-managed-thread-pool) if you want to fine-tune thread pools on CLR level.

### `quartz.threadPool.maxConcurrency`

This is the number of concurrent tasks that can be dispatched to CLR thread pool.
If you only have a few jobs that fire a few times a day, then 1 tasks is plenty!
If you have tens of thousands of jobs, with many firing every minute, then you probably want a max concurrency count more like 50 or 100 (this highly depends on the nature of the work that your jobs perform, and your systems resources!).
Also note CLR thread pool configuration separate from Quartz itself.

### Custom ThreadPools

If you use your own implementation of a thread pool, you can have properties set on it reflectively simply by naming the property as thus:

**Setting Properties on a Custom ThreadPool**

```text
quartz.threadPool.type = MyLibrary.FooThreadPool, MyLibrary
quartz.threadPool.somePropOfFooThreadPool = someValue
```

## Listeners

Global listeners can be instantiated and configured by `StdSchedulerFactory`, or your application can do it itself at runtime, and then register the listeners with the scheduler.
"Global" listeners listen to the events of every job/trigger rather than just the jobs/triggers that directly reference them.

Configuring listeners through the configuration file consists of giving then a name, and then specifying the type name, and any other properties to be set on the instance.
The type must have a no-arg constructor, and the properties are set reflectively. Only primitive data type values (including strings) are supported.

Thus, the general pattern for defining a "global" TriggerListener is:

**Configuring a Global TriggerListener**

```text
quartz.triggerListener.NAME.type = MyLibrary.MyListenerType, MyLibrary
quartz.triggerListener.NAME.propName = propValue
quartz.triggerListener.NAME.prop2Name = prop2Value
```

And the general pattern for defining a "global" JobListener is:

**Configuring a Global JobListener**

```text
quartz.jobListener.NAME.type = MyLibrary.MyListenerType, MyLibrary
quartz.jobListener.NAME.propName = propValue
quartz.jobListener.NAME.prop2Name = prop2Value
```

## Plug-Ins

Like listeners configuring plugins through the configuration file consists of giving then a name, and then specifying the type name, and any other properties to be set on the instance.
The type must have a no-arg constructor, and the properties are set reflectively.
Only primitive data type values (including Strings) are supported.

Thus, the general pattern for defining a plug-in is:

**Configuring a Plugin**

```text
quartz.plugin.NAME.type = MyLibrary.MyPluginType, MyLibrary
quartz.plugin.NAME.propName = propValue
quartz.plugin.NAME.prop2Name = prop2Value
```

There are several plugins that come with Quartz, that can be found in the [Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) package.
Example of configuring a few of them are as follows:

### Sample configuration of Logging Trigger History Plugin

The logging trigger history plugin catches trigger events (it is also a trigger listener) and logs then with logging infrastructure.

**Sample configuration of Logging Trigger History Plugin**

```text
quartz.plugin.triggHistory.type = Quartz.Plugin.History.LoggingTriggerHistoryPlugin, Quartz.Plugins
quartz.plugin.triggHistory.triggerFiredMessage = Trigger {1}.{0} fired job {6}.{5} at: {4:HH:mm:ss MM/dd/yyyy}
quartz.plugin.triggHistory.triggerCompleteMessage = Trigger {1}.{0} completed firing job {6}.{5} at {4:HH:mm:ss MM/dd/yyyy} with resulting trigger instruction code: {9}
```

### Sample configuration of XML Scheduling Data Processor Plugin

Job initialization plugin reads a set of jobs and triggers from an XML file, and adds them to the scheduler during initialization. It can also delete exiting data.

**Sample configuration of JobInitializationPlugin**

```text
quartz.plugin.jobInitializer.type = Quartz.Plugin.Xml.XMLSchedulingDataProcessorPlugin, Quartz.Plugins
quartz.plugin.jobInitializer.fileNames = data/my_job_data.xml
quartz.plugin.jobInitializer.failOnFileNotFound = true
```

The XML schema definition for the file can be found [here](https://github.com/quartznet/quartznet/blob/master/src/Quartz/Xml/job_scheduling_data_2_0.xsd).

### Sample configuration of Shutdown Hook Plugin

The shutdown-hook plugin catches the event of the CLR terminating, and calls shutdown on the scheduler.

**Sample configuration of ShutdownHookPlugin**

```text
quartz.plugin.shutdownhook.type = Quartz.Plugin.Management.ShutdownHookPlugin, Quartz.Plugins
quartz.plugin.shutdownhook.cleanShutdown = true
```

### Sample configuration of Job Interrupt Monitor Plugin

This plugin catches the event of job running for a long time (more than the configured max time) and tells the scheduler to "try" interrupting it if enabled.
Plugin defaults to signaling interrupt after 5 minutes, but the default van be configured to something different, value is in milliseconds in configuration.

**Sample configuration of JobInterruptMonitorPlugin**

```text
quartz.plugin.jobAutoInterrupt.type = Quartz.Plugin.Interrupt.JobInterruptMonitorPlugin, Quartz.Plugins
quartz.plugin.jobAutoInterrupt.defaultMaxRunTime = 3000000
```

## Remoting Server and Client

::: warning
Remoting only works with .NET Full Framework. It's also considered unsafe.
:::

| Property Name                                  | Required | Type    | Default Value     |
|------------------------------------------------|----------|---------|-------------------|
| quartz.scheduler.exporter.type                 | yes      | string  |                   |
| quartz.scheduler.exporter.port                 | yes      | int     |                   |
| quartz.scheduler.exporter.bindName             | no       | string  | 'QuartzScheduler' |
| quartz.scheduler.exporter.channelType          | no       | string  | 'tcp'             |
| quartz.scheduler.exporter.channelName          | no       | string  | 'http'            |
| quartz.scheduler.exporter.typeFilterLevel      | no       | string  | 'Full'            |
| quartz.scheduler.exporter.rejectRemoteRequests | no       | boolean | false             |

If you want the Quartz Scheduler to export itself via remoting as a server then set the 'quartz.scheduler.exporter.type' to "Quartz.Simpl.RemotingSchedulerExporter, Quartz".

### `quartz.scheduler.exporter.type`

The type of `ISchedulerExporter`, currently only "Quartz.Simpl.RemotingSchedulerExporter, Quartz" is supported.

### `quartz.scheduler.exporter.port`

The port to listen to.

### `quartz.scheduler.exporter.bindName`

Name to use when binding to remoting infrastructure.

### `quartz.scheduler.exporter.channelType`

Either 'tcp' or 'http', TCP is more performant.

### `quartz.scheduler.exporter.channelName`

Channel name to use when binding to remoting infrastructure.

### `quartz.scheduler.exporter.typeFilterLevel`

**Low**

The low deserialization level for .NET Framework remoting. It supports types associated with basic remoting functionality

**Full**

The full deserialization level for .NET Framework remoting. It supports all types that remoting supports in all situations

### `quartz.scheduler.exporter.rejectRemoteRequests`

A boolean value (true or false) that specifies whether to refuse requests from other computers. Specifying true allows only remoting calls from the local computer.

## RAMJobStore

RAMJobStore is used to store scheduling information (job, triggers and calendars) within memory. RAMJobStore is fast and lightweight, but all scheduling information is lost when the process terminates.

RAMJobStore is selected by setting the `quartz.jobStore.type` property as such:

**Setting The Scheduler’s JobStore to RAMJobStore**

```text
quartz.jobStore.type = Quartz.Simpl.RAMJobStore, Quartz
```

RAMJobStore can be tuned with the following properties:

| Property Name                    | Required | Type | Default Value |
|----------------------------------|----------|------|---------------|
| quartz.jobStore.misfireThreshold | no       | int  | 60000         |

### `quartz.jobStore.misfireThreshold`

The number of milliseconds the scheduler will 'tolerate' a trigger to pass its next-fire-time by, before being considered "misfired". The default value (if you don’t make an entry of this property in your configuration) is 60000 (60 seconds).

## JobStoreTX (ADO.NET)

AdoJobStore is used to store scheduling information (job, triggers and calendars) within a relational database.
There are actually two separate AdoJobStore implementations that you can select between, depending on the transactional behaviour you need.

JobStoreTX manages all transactions itself by calling `Commit()` (or `Rollback()`) on the database connection after every action (such as the addition of a job).
This is the job store you should normally be using unless you want to integrate to some transaction-aware framework.

The JobStoreTX is selected by setting the `quartz.jobStore.type` property as such:

**Setting The Scheduler’s JobStore to JobStoreTX**

```text
quartz.jobStore.type = Quartz.Impl.AdoJobStore.JobStoreTX, Quartz
```

JobStoreTX can be tuned with the following properties:

| Property Name                                | Required | Type    | Default Value                                                                |
|----------------------------------------------|----------|---------|------------------------------------------------------------------------------|
| quartz.jobStore.dbRetryInterval              | no       | long    | 15000   (15 seconds)                                                         |
| quartz.jobStore.driverDelegateType           | yes      | string  | null                                                                         |
| quartz.jobStore.dataSource                   | yes      | string  | null                                                                         |
| quartz.jobStore.tablePrefix                  | no       | string  | "QRTZ_"                                                                      |
| quartz.jobStore.useProperties                | no       | boolean | false                                                                        |
| quartz.jobStore.misfireThreshold             | no       | int     | 60000                                                                        |
| quartz.jobStore.clustered                    | no       | boolean | false                                                                        |
| quartz.jobStore.clusterCheckinInterval       | no       | long    | 15000                                                                        |
| quartz.jobStore.maxMisfiresToHandleAtATime   | no       | int     | 20                                                                           |
| quartz.jobStore.selectWithLockSQL            | no       | string  | "SELECT * FROM {0}LOCKS WHERE SCHED_NAME = {1} AND LOCK_NAME = ? FOR UPDATE" |
| quartz.jobStore.txIsolationLevelSerializable | no       | boolean | false                                                                        |
| quartz.jobStore.acquireTriggersWithinLock    | no       | boolean | false (or true - see doc below)                                              |
| quartz.jobStore.lockHandler.type             | no       | string  | null                                                                         |
| quartz.jobStore.driverDelegateInitString     | no       | string  | null                                                                         |

### `quartz.jobStore.dbRetryInterval`

Is the amount of time in milliseconds that the scheduler will wait between re-tries when it has detected a loss of connectivity within the JobStore (e.g. to the database).
This parameter is obviously not very meaningful when using RamJobStore.

### `quartz.jobStore.driverDelegateType`

Driver delegates understand the particular 'dialects' of varies database systems. Possible built-in choices include:

* Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz - default when no specific implementation available
* Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz - for Microsoft SQL Server
* Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz
* Quartz.Impl.AdoJobStore.OracleDelegate, Quartz
* Quartz.Impl.AdoJobStore.SQLiteDelegate, Quartz
* Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz

### `quartz.jobStore.dataSource`

The value of this property must be the name of one the DataSources defined in the configuration properties file.

### `quartz.jobStore.tablePrefix`

AdoJobStore’s "table prefix" property is a string equal to the prefix given to Quartz’s tables that were created in your database.
You can have multiple sets of Quartz’s tables within the same database if they use different table prefixes.

**Including schema name in tablePrefix**

For backing databases that support schemas (such as Microsoft SQL Server), you may use the tablePrefix to include the schema name.  i.e. for a schema named `foo` the prefix could be set as:

```sql
[foo].QRTZ_
```

**Note:** Any database table create scripts that were run with an explicit schema (such as `dbo`), will need to be modified to reflect this configuration.

### `quartz.jobStore.useProperties`

The "use properties" flag instructs AdoJobStore that all values in JobDataMaps will be strings, and therefore can be stored as name-value pairs, rather than storing more complex objects in their serialized form in the BLOB column.
This is can be handy, as you avoid the type versioning issues that can arise from serializing your non-string types into a BLOB.

### `quartz.jobStore.clustered`

Set to "true" in order to turn on clustering features.
This property must be set to "true" if you are having multiple instances of Quartz use the same set of database tables…​ otherwise you will experience havoc.
See the configuration docs for clustering for more information.

### `quartz.jobStore.clusterCheckinInterval`

Set the frequency (in milliseconds) at which this instance "checks-in"* with the other instances of the cluster. Affects the quickness of detecting failed instances.

### `quartz.jobStore.maxMisfiresToHandleAtATime`

The maximum number of misfired triggers the job store will handle in a given pass.
Handling many (more than a couple dozen) at once can cause the database tables to be locked long enough that the performance of firing other (not yet misfired) triggers may be hampered.

### `quartz.jobStore.selectWithLockSQL`

Must be a SQL string that selects a row in the "LOCKS" table and places a lock on the row. If not set, the default is "SELECT * FROM {0}LOCKS WHERE SCHED_NAME = {1} AND LOCK_NAME = ? FOR UPDATE", which works for most databases.
The "{0}" is replaced during run-time with the TABLE_PREFIX that you configured above.
The "{1}" is replaced with the scheduler’s name.

### `quartz.jobStore.txIsolationLevelSerializable`

A value of "true" tells Quartz (when using JobStoreTX or CMT) to set transaction level to serialize on ADO.NET connections.
This can be helpful to prevent lock timeouts with some databases under high load, and "long-lasting" transactions.

### `quartz.jobStore.acquireTriggersWithinLock`

Whether or not the acquisition of next triggers to fire should occur within an explicit database lock.
This was once necessary (in previous versions of Quartz) to avoid dead-locks with particular databases, but is no longer considered necessary, hence the default value is "false".

If "quartz.scheduler.batchTriggerAcquisitionMaxCount" is set to > 1, and AdoJobStore is used, then this property must be set to "true" to avoid data corruption
(as of Quartz 2 "true" is now the default if batchTriggerAcquisitionMaxCount is set > 1).

### `quartz.jobStore.lockHandler.type`

The type name to be used to produce an instance of a `Quartz.Impl.AdoJobStore.ISemaphore` to be used for locking control on the job store data.
This is an advanced configuration feature, which should not be used by most users.

By default, Quartz will select the most appropriate (pre-bundled) Semaphore implementation to use.

### Customizing StdRowLockSemaphore

If you explicitly choose to use this DB Semaphore, you can customize it further on how frequent to poll for DB locks.

**Example of Using a Custom StdRowLockSemaphore Implementation**

```text
quartz.jobStore.lockHandler.type = Quartz.Impl.AdoJobStore.StdRowLockSemaphore
quartz.jobStore.lockHandler.maxRetry = 7     # Default is 3
quartz.jobStore.lockHandler.retryPeriod = 3000  # Default is 1000 millis
```

### `quartz.jobStore.driverDelegateInitString`

A pipe-delimited list of properties (and their values) that can be passed to the DriverDelegate during initialization time.

The format of the string is as such:

`settingName=settingValue|otherSettingName=otherSettingValue|...`

The StdAdoDelegate and all of its descendants (all delegates that ship with Quartz) support a property called 'triggerPersistenceDelegateTypes' which can be set to a comma-separated list of types that implement the `ITriggerPersistenceDelegate` interface for storing custom trigger types.
See the implementations `SimplePropertiesTriggerPersistenceDelegateSupport` and `SimplePropertiesTriggerPersistenceDelegateSupport` for examples of writing a persistence delegate for a custom trigger.

## DataSources (ADO.NET JobStores)

If you’re using AdoJobstore, you’ll be needing a DataSource for its use (or two DataSources, if you’re using JobStoreCMT).

Each DataSource you define (typically one or two) must be given a name, and the properties you define for each must contain that name, as shown below. The DataSource’s "NAME" can be anything you want, and has no meaning other than being able to identify it when it is assigned to the AdoJobStore.

Quartz-created DataSources are defined with the following properties:

| Property Name                                  | Required | Type   | Default Value |
|------------------------------------------------|----------|--------|---------------|
| quartz.dataSource.NAME.provider                | yes      | string |               |
| quartz.dataSource.NAME.connectionString        |          | string |               |
| quartz.dataSource.NAME.connectionStringName    |          | string |               |
| quartz.dataSource.NAME.connectionProvider.type |          | string |               |

### `quartz.dataSource.NAME.provider`

Currently following database providers are supported:

* `SqlServer` - Microsoft SQL Server
* `OracleODP` - Oracle's Oracle Driver
* `OracleODPManaged` - Oracle's managed driver for Oracle 11
* `MySql` - MySQL Connector/.NET
* `SQLite` - SQLite ADO.NET Provider
* `SQLite-Microsoft` - Microsoft SQLite ADO.NET Provider
* `Firebird` - Firebird ADO.NET Provider
* `Npgsql` - PostgreSQL Npgsql

### `quartz.dataSource.NAME.connectionString`

ADO.NET connection string to use. You can skip this if you are using connectionStringName below.

### `quartz.dataSource.NAME.connectionStringName`

Connection string name to use. Defined either in app.config or appsettings.json.

### `quartz.dataSource.NAME.connectionProvider.type`

Allows you to define a custom connection provider implementing IDbProvider interface.

**Example of a Quartz-defined DataSource**

```text
quartz.dataSource.myDS.provider = SqlServer
quartz.dataSource.myDS.connectionString = Server=localhost;Database=quartznet;User Id=quartznet;Password=quartznet;
```

## Clustering

Quartz’s clustering features bring both high availability and scalability to your scheduler via fail-over and load balancing functionality.

Clustering currently only works with the AdoJobstore (`JobStoreTX` or `JobStoreCMT`), and essentially works by having each node of the cluster share the same database.

Load-balancing occurs automatically, with each node of the cluster firing jobs as quickly as it can. When a trigger’s firing time occurs, the first node to acquire it (by placing a lock on it) is the node that will fire it.

Only one node will fire the job for each firing.
What I mean by that is, if the job has a repeating trigger that tells it to fire every 10 seconds, then at 12:00:00 exactly one node will run the job, and at 12:00:10 exactly one node will run the job, etc.
It won’t necessarily be the same node each time - it will more or less be random which node runs it.
The load balancing mechanism is near-random for busy schedulers (lots of triggers) but favors the same node for non-busy (e.g. few triggers) schedulers.

Fail-over occurs when one of the nodes fails while in the midst of executing one or more jobs. When a node fails, the other nodes detect the condition and identify the jobs in the database that were in progress within the failed node.
Any jobs marked for recovery (with the "requests recovery" property on the JobDetail) will be re-executed by the remaining nodes. Jobs not marked for recovery will simply be freed up for execution at the next time a related trigger fires.

The clustering feature works best for scaling out long-running and/or cpu-intensive jobs (distributing the work-load over multiple nodes).
If you need to scale out to support thousands of short-running (e.g 1 second) jobs, consider partitioning the set of jobs by using multiple distinct schedulers (including multiple clustered schedulers for HA).
The scheduler makes use of a cluster-wide lock, a pattern that degrades performance as you add more nodes (when going beyond about three nodes - depending upon your database’s capabilities, etc.).

Enable clustering by setting the `quartz.jobStore.clustered` property to "true". Each instance in the cluster should use the same copy of the quartz.properties file.
Exceptions of this would be to use properties files that are identical, with the following allowable exceptions: Different thread pool size, and different value for the `quartz.scheduler.instanceId` property.
Each node in the cluster MUST have a unique instanceId, which is easily done (without needing different properties files) by placing "AUTO" as the value of this property.
See the info about the configuration properties of AdoJobStore for more information.

::: danger
Never run clustering on separate machines, unless their clocks are synchronized using some form of time-sync service (daemon) that runs very regularly (the clocks must be within a second of each other).
See [https://www.nist.gov/pml/time-and-frequency-division/services/internet-time-service-its](https://www.nist.gov/pml/time-and-frequency-division/services/internet-time-service-its) if you are unfamiliar with how to do this.
:::

::: danger
Never start (`scheduler.Start()`) a non-clustered instance against the same set of database tables that any other instance is running (`Start()`ed) against.
You may get serious data corruption, and will definitely experience erratic behavior.
:::

::: danger
Monitor and ensure that your nodes have enough CPU resources to complete jobs.
When some nodes are in 100% CPU, they may be unable to update the job store and other nodes can consider these jobs lost and recover them by re-running.  
:::

**Example Properties For A Clustered Scheduler**

```text
#============================================================================
# Configure Main Scheduler Properties
#============================================================================

quartz.scheduler.instanceName = MyClusteredScheduler
quartz.scheduler.instanceId = AUTO

#============================================================================
# Configure ThreadPool
#============================================================================

quartz.threadPool.type = Quartz.Simpl.DefaultThreadPool, Quartz
quartz.threadPool.threadCount = 25
quartz.threadPool.threadPriority = 5

#============================================================================
# Configure JobStore
#============================================================================

quartz.jobStore.misfireThreshold = 60000

quartz.jobStore.type = Quartz.Impl.AdoJobStore.JobStoreTX
quartz.jobStore.driverDelegateType = Quartz.Impl.AdoJobStore.SqlServerDelegate
quartz.jobStore.useProperties = true
quartz.jobStore.dataSource = myDS
quartz.jobStore.tablePrefix = QRTZ_

quartz.jobStore.clustered = true
quartz.jobStore.clusterCheckinInterval = 20000

#============================================================================
# Configure Datasources
#============================================================================

quartz.dataSource.myDS.provider = SqlServer
quartz.dataSource.myDS.connectionString = Server=localhost;Database=quartznet;User Id=quartznet;Password=quartznet;
```
