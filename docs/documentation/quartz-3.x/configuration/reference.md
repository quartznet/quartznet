---

title: Configuration Reference
---

# Quartz.NET Configuration Reference

[[toc]]

`StdSchedulerFactory` loads `quartz.config` from the current working directory. If that fails, it loads the `quartz.config` embedded in the Quartz dll. To use another file, set the system property `quartz.properties` to its path, or call one of the `Initialize(xx)` methods before `GetScheduler()`.

The factory creates `IJobStore`, `IThreadPool` and the other SPI types by type name, then sets any further properties through the matching property setter. For example, `quartz.jobStore.myProp = 10` calls the setter of `MyProp` on the job store. Values are converted to primitive types (int, long, float, double, boolean and string) first.

A value can reference another property with `$@other.property.name`, for example `$@quartz.scheduler.instanceName`.

::: tip
Code-based configuration builds these same keys.
:::

## Main Configuration

Scheduler identity and other top-level settings.

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

Any string. The scheduler gives it no meaning; client code uses it to tell schedulers apart in one program. In a cluster, every instance of the same logical scheduler must use the same name.

### `quartz.scheduler.instanceId`

Any string, unique among the instances of one logical scheduler in a cluster. `AUTO` generates the id. `SYS_PROP` reads it from the system property `quartz.scheduler.instanceId`.

### `quartz.scheduler.instanceIdGenerator.type`

Used only when `quartz.scheduler.instanceId` is `AUTO`. The default, `Quartz.Simpl.SimpleInstanceIdGenerator`, builds an id from the host name and a time stamp. Other implementations:

* `SystemPropertyInstanceIdGenerator`: reads the system property `quartz.scheduler.instanceId`.
* `HostnameInstanceIdGenerator`: uses the local host name (`Dns.GetHostEntry(Dns.GetHostName())`).

You can also implement the InstanceIdGenerator interface yourself.

### `quartz.scheduler.threadName`

Name of the main scheduler thread. Defaults to `quartz.scheduler.instanceName` plus `_QuartzSchedulerThread`.

### `quartz.scheduler.makeSchedulerThreadDaemon`

`true` or `false`: whether the main scheduler thread is a daemon thread.

### `quartz.scheduler.idleWaitTime`

Milliseconds the scheduler waits before re-querying for available triggers when it is idle. You rarely need to tune it, unless you use XA transactions and see delayed firings of triggers that should fire immediately. Values under 5000 ms cause excessive database querying and are not recommended. Values under 1000 are not legal.

### `quartz.scheduler.typeLoadHelper.type`

Defaults to `Quartz.Simpl.SimpleTypeLoadHelper`, the most robust choice, which loads types with `Type.GetType()`.

### `quartz.scheduler.jobFactory.type`

The `IJobFactory` that creates `IJob` instances. The default, `Quartz.Simpl.PropertySettingJobFactory`, calls `Activator.CreateInstance` for a new instance before every execution. It also sets the job's properties by reflection from the scheduler context and the job and trigger JobDataMaps.

### `quartz.context.key.SOME_KEY`

A name-value pair placed into the scheduler context as strings (see `IScheduler.Context`). `quartz.context.key.MyKey = MyValue` is the same as `scheduler.Context.Put("MyKey", "MyValue")`.

### `quartz.scheduler.batchTriggerAcquisitionMaxCount`

Maximum number of triggers a node may acquire for firing at once. Default 1. A larger value fires more efficiently when very many triggers fire at once, at the cost of possibly unbalanced load between cluster nodes.

With AdoJobStore and a value > 1, set `quartz.jobStore.acquireTriggersWithinLock` to `true` to avoid data corruption.

### `quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow`

Milliseconds a trigger may be acquired and fired ahead of its scheduled fire time. Default 0. A larger value lets batch acquisition select more than 1 trigger at a time, at the cost of precision: triggers may fire this much early. Use it for performance when very many triggers fire at or near the same time.

## ThreadPool

| Property Name                    | Required | Type   | Default Value                  |
|----------------------------------|----------|--------|--------------------------------|
| quartz.threadPool.type           | no       | string | Quartz.Simpl.DefaultThreadPool |
| quartz.threadPool.maxConcurrency | no       | int    | 10                             |

### `quartz.threadPool.type`

The thread pool implementation. `Quartz.Simpl.DefaultThreadPool` ships with Quartz and suits nearly every user. It dispatches tasks to the .NET task queue and enforces the configured maximum of concurrent tasks. To tune thread pools at CLR level, see [CLR's managed thread pool](https://docs.microsoft.com/en-us/dotnet/standard/threading/the-managed-thread-pool).

### `quartz.threadPool.maxConcurrency`

Number of concurrent tasks that can be dispatched to the CLR thread pool. A few jobs firing a few times a day need only 1. Tens of thousands of jobs, many firing every minute, may need 50 or 100, depending on the work your jobs do and your system's resources. The CLR thread pool is configured separately from Quartz.

### Custom ThreadPools

Properties of a custom thread pool are set by reflection when named like this:

```text
quartz.threadPool.type = MyLibrary.FooThreadPool, MyLibrary
quartz.threadPool.somePropOfFooThreadPool = someValue
```

## Listeners

`StdSchedulerFactory` can create and configure global listeners, or your application can register them at runtime. A global listener receives the events of every job or trigger, not only of those that reference it.

In the configuration file, give each listener a name, its type and any properties to set. The type needs a no-arg constructor. Properties are set by reflection, and only primitive values (including strings) are supported.

A global TriggerListener:

```text
quartz.triggerListener.NAME.type = MyLibrary.MyListenerType, MyLibrary
quartz.triggerListener.NAME.propName = propValue
quartz.triggerListener.NAME.prop2Name = prop2Value
```

A global JobListener:

```text
quartz.jobListener.NAME.type = MyLibrary.MyListenerType, MyLibrary
quartz.jobListener.NAME.propName = propValue
quartz.jobListener.NAME.prop2Name = prop2Value
```

## Plug-Ins

Plug-ins are configured like listeners: a name, the type and any properties. The type needs a no-arg constructor. Properties are set by reflection, and only primitive values (including strings) are supported.

```text
quartz.plugin.NAME.type = MyLibrary.MyPluginType, MyLibrary
quartz.plugin.NAME.propName = propValue
quartz.plugin.NAME.prop2Name = prop2Value
```

The [Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) package ships several plug-ins. Examples follow.

### Sample configuration of Logging Trigger History Plugin

A trigger listener that logs trigger events through the logging infrastructure.

```text
quartz.plugin.triggHistory.type = Quartz.Plugin.History.LoggingTriggerHistoryPlugin, Quartz.Plugins
quartz.plugin.triggHistory.triggerFiredMessage = Trigger {1}.{0} fired job {6}.{5} at: {4:HH:mm:ss MM/dd/yyyy}
quartz.plugin.triggHistory.triggerCompleteMessage = Trigger {1}.{0} completed firing job {6}.{5} at {4:HH:mm:ss MM/dd/yyyy} with resulting trigger instruction code: {9}
```

### Sample configuration of Structured Logging Plugins

Alternatives to the logging trigger/job history plugins. They use named message template parameters (e.g. `{JobName}`) instead of index-based placeholders (`{0}`), for structured logging sinks such as Serilog and NLog. Message templates can be customized through properties, with the same named parameters.

**StructuredLoggingJobHistoryPlugin**

```text
quartz.plugin.structuredJobLogging.type = Quartz.Plugin.History.StructuredLoggingJobHistoryPlugin, Quartz.Plugins
quartz.plugin.structuredJobLogging.jobToBeFiredMessage = Job {JobGroup}.{JobName} fired by trigger {TriggerGroup}.{TriggerName} at {FireTime} scheduled at {ScheduledFireTime} next fire at {NextFireTime} refire count {RefireCount}
quartz.plugin.structuredJobLogging.jobSuccessMessage = Job {JobGroup}.{JobName} execution complete at {FireTime} triggered by {TriggerGroup}.{TriggerName} with result {Result}
```

**StructuredLoggingTriggerHistoryPlugin**

```text
quartz.plugin.structuredTriggerLogging.type = Quartz.Plugin.History.StructuredLoggingTriggerHistoryPlugin, Quartz.Plugins
quartz.plugin.structuredTriggerLogging.triggerFiredMessage = Trigger {TriggerGroup}.{TriggerName} fired job {JobGroup}.{JobName} at {FireTime} scheduled at {ScheduledFireTime} next fire at {NextFireTime} refire count {RefireCount}
quartz.plugin.structuredTriggerLogging.triggerCompleteMessage = Trigger {TriggerGroup}.{TriggerName} completed firing job {JobGroup}.{JobName} at {CompletedTime} scheduled at {ScheduledFireTime} next fire at {NextFireTime} with instruction {TriggerInstructionCode}
```

### Sample configuration of XML Scheduling Data Processor Plugin

Reads jobs and triggers from an XML file and adds them to the scheduler during initialization. It can also delete existing data.

```text
quartz.plugin.jobInitializer.type = Quartz.Plugin.Xml.XMLSchedulingDataProcessorPlugin, Quartz.Plugins
quartz.plugin.jobInitializer.fileNames = data/my_job_data.xml
quartz.plugin.jobInitializer.failOnFileNotFound = true
```

The file's schema is [job_scheduling_data_2_0.xsd](https://github.com/quartznet/quartznet/blob/master/src/Quartz/Xml/job_scheduling_data_2_0.xsd).

### Sample configuration of Shutdown Hook Plugin

Calls shutdown on the scheduler when the CLR terminates.

```text
quartz.plugin.shutdownhook.type = Quartz.Plugin.Management.ShutdownHookPlugin, Quartz.Plugins
quartz.plugin.shutdownhook.cleanShutdown = true
```

### Sample configuration of Job Interrupt Monitor Plugin

If enabled, asks the scheduler to try interrupting a job that runs longer than the configured maximum. The default maximum is 5 minutes; the configured value is in milliseconds.

```text
quartz.plugin.jobAutoInterrupt.type = Quartz.Plugin.Interrupt.JobInterruptMonitorPlugin, Quartz.Plugins
quartz.plugin.jobAutoInterrupt.defaultMaxRunTime = 3000000
```

## Remoting Server and Client

::: warning
Remoting only works with .NET Full Framework. It is also considered unsafe.
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

To export the scheduler as a remoting server, set `quartz.scheduler.exporter.type` to `Quartz.Simpl.RemotingSchedulerExporter, Quartz`.

### `quartz.scheduler.exporter.type`

The `ISchedulerExporter` type. Only `Quartz.Simpl.RemotingSchedulerExporter, Quartz` is supported.

### `quartz.scheduler.exporter.port`

The port to listen on.

### `quartz.scheduler.exporter.bindName`

Name used when binding to the remoting infrastructure.

### `quartz.scheduler.exporter.channelType`

`tcp` or `http`. TCP is faster.

### `quartz.scheduler.exporter.channelName`

Channel name used when binding to the remoting infrastructure.

### `quartz.scheduler.exporter.typeFilterLevel`

The .NET Framework remoting deserialization level:

* **Low**: types associated with basic remoting functionality.
* **Full**: all types remoting supports, in all situations.

### `quartz.scheduler.exporter.rejectRemoteRequests`

`true` refuses requests from other computers, allowing only remoting calls from the local computer.

## RAMJobStore

Stores jobs, triggers and calendars in memory. Fast and lightweight, but all scheduling data is lost when the process ends.

Select it with `quartz.jobStore.type`:

```text
quartz.jobStore.type = Quartz.Simpl.RAMJobStore, Quartz
```

| Property Name                    | Required | Type | Default Value |
|----------------------------------|----------|------|---------------|
| quartz.jobStore.misfireThreshold | no       | int  | 60000         |

### `quartz.jobStore.misfireThreshold`

Milliseconds a trigger may pass its next-fire-time before it counts as misfired. Default 60000 (60 seconds).

## JobStoreTX (ADO.NET)

AdoJobStore stores jobs, triggers and calendars in a relational database. It has two implementations; pick by the transaction behaviour you need.

JobStoreTX calls `Commit()` (or `Rollback()`) on the connection after every action, such as adding a job. Use it unless you integrate with a transaction-aware framework. Select it with `quartz.jobStore.type`:

```text
quartz.jobStore.type = Quartz.Impl.AdoJobStore.JobStoreTX, Quartz
```

| Property Name                                | Required | Type    | Default Value                                                                |
|----------------------------------------------|----------|---------|------------------------------------------------------------------------------|
| quartz.jobStore.commandTimeout               | no       | long    | 0 (the provider's own default)                                               |
| quartz.jobStore.dbRetryInterval              | no       | long    | 15000 (15 seconds)                                                           |
| quartz.jobStore.driverDelegateType           | yes      | string  | null                                                                         |
| quartz.jobStore.dataSource                   | yes      | string  | null                                                                         |
| quartz.jobStore.tablePrefix                  | no       | string  | "QRTZ_"                                                                      |
| quartz.jobStore.useProperties                | no       | boolean | false                                                                        |
| quartz.jobStore.misfireThreshold             | no       | int     | 60000                                                                        |
| quartz.jobStore.clustered                    | no       | boolean | false                                                                        |
| quartz.jobStore.clusterCheckinInterval       | no       | long    | 7500 (7.5 seconds)                                                           |
| quartz.jobStore.clusterCheckinMisfireThreshold | no     | long    | 7500 (7.5 seconds)                                                           |
| quartz.jobStore.maxMisfiresToHandleAtATime   | no       | int     | 20                                                                           |
| quartz.jobStore.selectWithLockSQL            | no       | string  | "SELECT * FROM {0}LOCKS WHERE SCHED_NAME = {1} AND LOCK_NAME = ? FOR UPDATE" |
| quartz.jobStore.txIsolationLevelSerializable | no       | boolean | false                                                                        |
| quartz.jobStore.acceptEnlistedTransactions        | no       | boolean | false                                                                        |
| quartz.jobStore.acquireTriggersWithinLock    | no       | boolean | false (or true - see doc below)                                              |
| quartz.jobStore.lockHandler.type             | no       | string  | null                                                                         |
| quartz.jobStore.driverDelegateInitString     | no       | string  | null                                                                         |

### `quartz.jobStore.commandTimeout`

Since 3.22.0. Milliseconds a job store statement may run before the ADO.NET provider cancels it. Zero, the default, keeps the provider's default for a new command.

It applies to every statement the job store issues, including the lock handler's row lock. That is where it matters most: a node waiting on `QRTZ_LOCKS` behind a peer that stopped without releasing the row cannot schedule anything until the statement gives up.

`System.Data.Common.DbCommand.CommandTimeout` counts whole seconds, so the value is rounded *up*: 1500 is applied as 2 seconds. Rounding down would turn a sub-second value into 0, which every provider reads as "wait forever".

[A Lock Held by a Connection That Is Gone](../../troubleshooting.md#a-lock-held-by-a-connection-that-is-gone) describes the case this was added for and the server-side settings that go with it.

### `quartz.jobStore.dbRetryInterval`

Milliseconds the scheduler waits between retries after losing connectivity within the job store (e.g. to the database). Not meaningful for RAMJobStore.

Since 3.22.0, the cluster check-in loop backs off by this value only after a failed check-in has used up the window its peers give it (`clusterCheckinInterval` + `clusterCheckinMisfireThreshold`). Inside that window it retries sooner, and this value only caps the wait between those retries.

Before 3.22.0, one failed check-in slept the full `dbRetryInterval`. On the defaults the next row was written 22.5 seconds after the last one, 7.5 seconds after the peers had stopped trusting the node, so one database blip during a check-in got a live node recovered by its peers.

### `quartz.jobStore.driverDelegateType`

The delegate that understands a database's dialect. Built-in choices:

* Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz - default when no specific implementation available
* Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz - for Microsoft SQL Server
* Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz
* Quartz.Impl.AdoJobStore.OracleDelegate, Quartz
* Quartz.Impl.AdoJobStore.SQLiteDelegate, Quartz
* Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz

### `quartz.jobStore.dataSource`

Name of one of the DataSources defined in the configuration.

### `quartz.jobStore.tablePrefix`

Prefix of the Quartz tables in your database. Different prefixes let several sets of Quartz tables share one database.

**Including schema name in tablePrefix**

On databases with schemas (such as Microsoft SQL Server), the prefix can include the schema. For a schema named `foo`:

```sql
[foo].QRTZ_
```

**Note:** Table create scripts that were run with an explicit schema (such as `dbo`) must be changed to match.

### `quartz.jobStore.useProperties`

Tells AdoJobStore that all JobDataMap values are strings, so they are stored as name-value pairs instead of serialized objects in the BLOB column. This avoids the type versioning issues of serializing non-string types into a BLOB.

### `quartz.jobStore.clustered`

Set to `true` to enable clustering. Required when several Quartz instances use the same set of tables; otherwise you will experience havoc. See [Clustering](#clustering).

### `quartz.jobStore.clusterCheckinInterval`

How often, in milliseconds, this instance checks in with the other instances of the cluster. It sets how quickly failed instances are detected.

### `quartz.jobStore.clusterCheckinMisfireThreshold`

Since 3.1. Milliseconds a check-in may be late before the other instances consider this instance failed and recover its work. A peer writes an instance off once its last check-in is older than its check-in interval plus this threshold: 15 seconds on the defaults.

Raise it above your environment's worst *pause* (a long garbage collection, a paused virtual machine, a database failover). The cost: a failed instance's work waits that much longer to be taken over.

Since 3.22.0 it is also the window in which this instance retries a failed check-in, so a database blip shorter than the threshold does not get the instance written off.

### `quartz.jobStore.maxMisfiresToHandleAtATime`

Maximum number of misfired triggers the job store handles in one pass. Handling more than a couple dozen at once can lock the tables long enough to slow the firing of other, not yet misfired, triggers.

### `quartz.jobStore.selectWithLockSQL`

SQL that selects a row in the LOCKS table and locks it. The default, `SELECT * FROM {0}LOCKS WHERE SCHED_NAME = {1} AND LOCK_NAME = ? FOR UPDATE`, works for most databases. At run time `{0}` is replaced with the table prefix and `{1}` with the scheduler's name.

### `quartz.jobStore.txIsolationLevelSerializable`

`true` sets the transaction isolation level to serializable on ADO.NET connections (JobStoreTX or CMT). This can prevent lock timeouts on some databases under high load and with long-lasting transactions.

### `quartz.jobStore.acceptEnlistedTransactions`

`true` lets the job store take part in a transaction your application already owns, instead of always managing an ADO.NET transaction of its own. It uses the connection you enlisted with `IScheduler.EnlistTransaction` or `IScheduler.EnlistConnection` for operations on that asynchronous flow. Your application owns the commit, so scheduling commits or rolls back together with the rest of its work.

* Handing over a connection is the only way to take part. Operations with nothing enlisted use a connection of the job store's own, and while this setting is on, that connection is kept out of any ambient `System.Transactions.TransactionScope`.
* Locks are held until your transaction completes, so this setting also switches locking to database locks unless `quartz.jobStore.lockHandler.type` is set explicitly.

See [Joining an existing transaction](../tutorial/job-stores.md#joining-an-existing-transaction) for how this works in practice and what to watch out for.

### `quartz.jobStore.acquireTriggersWithinLock`

Whether acquiring the next triggers to fire happens inside an explicit database lock. Older Quartz versions needed it to avoid deadlocks on some databases. It is no longer needed, so the default is `false`.

With AdoJobStore and `quartz.scheduler.batchTriggerAcquisitionMaxCount` > 1, this must be `true` to avoid data corruption. Since Quartz 2, `true` is the default when `batchTriggerAcquisitionMaxCount` > 1.

### `quartz.jobStore.lockHandler.type`

Type name of the `Quartz.Impl.AdoJobStore.ISemaphore` used to lock job store data. An advanced setting that most users should not use. By default Quartz picks the most appropriate bundled semaphore.

### Customizing StdRowLockSemaphore

If you choose this database semaphore explicitly, you can set how often it polls for database locks:

```text
quartz.jobStore.lockHandler.type = Quartz.Impl.AdoJobStore.StdRowLockSemaphore
quartz.jobStore.lockHandler.maxRetry = 7     # Default is 3
quartz.jobStore.lockHandler.retryPeriod = 3000  # Default is 1000 millis
```

### `quartz.jobStore.driverDelegateInitString`

A pipe-delimited list of settings passed to the DriverDelegate at initialization:

`settingName=settingValue|otherSettingName=otherSettingValue|...`

`StdAdoDelegate` and all delegates that ship with Quartz support `triggerPersistenceDelegateTypes`: a comma-separated list of types implementing `ITriggerPersistenceDelegate`, for storing custom trigger types. `SimplePropertiesTriggerPersistenceDelegateSupport` is an example of such a persistence delegate.

## DataSources (ADO.NET JobStores)

AdoJobStore needs a DataSource (JobStoreCMT needs two). Give each DataSource a name and put that name in its property keys, as below. The name only identifies the DataSource when it is assigned to the AdoJobStore.

| Property Name                                  | Required | Type   | Default Value |
|------------------------------------------------|----------|--------|---------------|
| quartz.dataSource.NAME.provider                | yes      | string |               |
| quartz.dataSource.NAME.connectionString        |          | string |               |
| quartz.dataSource.NAME.connectionStringName    |          | string |               |
| quartz.dataSource.NAME.connectionProvider.type |          | string |               |

### `quartz.dataSource.NAME.provider`

Supported database providers:

* `SqlServer` - Microsoft SQL Server
* `OracleODP` - Oracle's Oracle Driver
* `OracleODPManaged` - Oracle's managed driver for Oracle 11
* `MySql` - MySQL Connector/.NET
* `SQLite` - SQLite ADO.NET Provider
* `SQLite-Microsoft` - Microsoft SQLite ADO.NET Provider
* `Firebird` - Firebird ADO.NET Provider
* `Npgsql` - PostgreSQL Npgsql

### `quartz.dataSource.NAME.connectionString`

ADO.NET connection string. Not needed if you set `connectionStringName`.

### `quartz.dataSource.NAME.connectionStringName`

Name of a connection string defined in app.config or appsettings.json.

### `quartz.dataSource.NAME.connectionProvider.type`

A custom connection provider implementing `IDbProvider`.

```text
quartz.dataSource.myDS.provider = SqlServer
quartz.dataSource.myDS.connectionString = Server=localhost;Database=quartznet;User Id=quartznet;Password=quartznet;
```

## Clustering

Clustering gives high availability and scalability through fail-over and load balancing. It works only with AdoJobStore (`JobStoreTX` or `JobStoreCMT`): every node shares the same database.

* **Load balancing** is automatic. When a trigger's fire time comes, the first node to lock it fires it. Each firing runs on exactly one node: a trigger firing every 10 seconds runs the job on one node at 12:00:00, on one node at 12:00:10, and so on. The node varies. Busy schedulers (many triggers) pick it near-randomly; non-busy ones favor the same node.
* **Fail-over** happens when a node fails while executing jobs. The other nodes detect it and find its in-progress jobs in the database. Jobs marked for recovery ("requests recovery" on the JobDetail) are re-executed by the remaining nodes. Other jobs are freed to run when a related trigger next fires.

Clustering suits long-running or CPU-intensive jobs. For thousands of short-running (e.g. 1 second) jobs, partition them across several distinct schedulers, clustered for HA if needed. The scheduler uses a cluster-wide lock, which degrades performance beyond about three nodes, depending on your database.

To enable clustering:

1. Set `quartz.jobStore.clustered` to `true`.
2. Use the same properties on every node. The allowed differences are the thread pool size and `quartz.scheduler.instanceId`.
3. Give each node a unique `instanceId`. `AUTO` does this without separate files.

::: danger
Never cluster separate machines unless their clocks are synchronized by a time-sync service that runs very regularly (clocks within a second of each other).
See [https://www.nist.gov/pml/time-and-frequency-division/services/internet-time-service-its](https://www.nist.gov/pml/time-and-frequency-division/services/internet-time-service-its) if you are unfamiliar with how to do this.
:::

::: danger
Never start (`scheduler.Start()`) a non-clustered instance against the same set of tables that any other started instance uses.
You may get serious data corruption, and will see erratic behavior.
:::

::: danger
Make sure your nodes have enough CPU to complete jobs.
A node at 100% CPU may be unable to update the job store, and other nodes can then consider its jobs lost and recover them by re-running.
:::

Example properties for a clustered scheduler:

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

## Execution Limits

Execution limits cap how many threads each execution group may use concurrently on one scheduler node. See the [Execution Groups tutorial](../tutorial/execution-groups.md) for details, and the [Node Affinity tutorial](../tutorial/node-affinity.md) for pinning a trigger to a specific cluster node.

| Property Name | Required | Type | Default Value |
|---|---|---|---|
| quartz.executionLimit.{groupName} | no | int or string | |

**Values:**
- A non-negative integer: maximum concurrent threads for the group (0 = forbidden on this node)
- `unlimited`, `none`, or `null`: no limit (same as not listing the group)

**Special group keys:**
- `_` (underscore) or `null`: limit for triggers with no execution group. The key `null` is a case-insensitive alias for `_`; the *value* `null` means unlimited.
- `*` (asterisk): default limit for named groups not listed. It does not apply to ungrouped triggers.

```
quartz.executionLimit.batch-jobs = 2
quartz.executionLimit.high-cpu = 3
quartz.executionLimit._ = 10
quartz.executionLimit.* = 5
```
