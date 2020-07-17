---
layout: default
title: Quartz.NET Configuration Reference
---

# Quartz.NET Configuration Reference

[[toc]]

By default, `StdSchedulerFactory` loads a properties file named `quartz.config` from the "current working directory". If that fails, then the `quartz.config` file located (as an embedded resource) in the Quartz dll is loaded. If you wish to use a file other than these defaults, you must define the system property `quartz.properties` to point to the file you want.

Alternatively, you can explicitly initialize the factory by calling one of the `Initialize(xx)` methods before calling `GetScheduler()` on the `StdSchedulerFactory`.

Instances of the specified `JobStore`, `ThreadPool`, and other SPI classes will be created by name, and then any additional properties specified for them in the config file will be set on the instance by calling an equivalent property set method. For example if the properties file contains the property `quartz.jobStore.myProp = 10` then after the JobStore class has been instantiated, the setter of property `MyProp` will be called on it. Type conversion to primitive types (int, long, float, double, boolean, and string) are performed before calling the property’s setter method.

One property can reference another property’s value by specifying a value following the convention of `$@other.property.name`, for example, to reference the scheduler’s instance name as the value for some other property, you would use `$@quartz.scheduler.instanceName`.

## Main Configuration

These properties configure the identification of the scheduler, and various other "top level" settings.

|Property Name |	Required |	Type |	Default Value |
|--------------|----------|------|----------------|
|quartz.scheduler.instanceName|	no|	string	|'QuartzScheduler'|
|quartz.scheduler.instanceId	|no	string	|'NON_CLUSTERED'|
|quartz.scheduler.instanceIdGenerator.type	|no|	string (type name)	| Quartz.Simpl.SimpleInstanceIdGenerator, Quartz |
|quartz.scheduler.threadName	|no|	string	|instanceName + '_QuartzSchedulerThread'|
|quartz.scheduler.makeSchedulerThreadDaemon	|no|	boolean	|false|
|quartz.scheduler.threadsInheritContextClassLoaderOfInitializer	|no|	boolean	|false|
|quartz.scheduler.idleWaitTime |	no	|long	|30000|
|quartz.scheduler.dbFailureRetryInterval|	no	|long|	15000|
|quartz.scheduler.typeLoadHelper.type	|no|	string (type name)	| Quartz.Simpl.SimpleTypeLoadHelper|
|quartz.scheduler.jobFactory.type	|no|	string (type name)	| Quartz.Simpl.PropertySettingJobFactory |
|quartz.context.key.SOME_KEY	|no	|string|	none|
|quartz.scheduler.wrapJobExecutionInUserTransaction	|no|	boolean	|false|
|quartz.scheduler.batchTriggerAcquisitionMaxCount|	no|	int|	1|
|quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow	|no|	long	|0|

**`quartz.scheduler.instanceName`**

Can be any string, and the value has no meaning to the scheduler itself - but rather serves as a mechanism for client code to distinguish schedulers when multiple instances are used within the same program. If you are using the clustering features, you must use the same name for every instance in the cluster that is 'logically' the same Scheduler.

**`quartz.scheduler.instanceId`**

Can be any string, but must be unique for all schedulers working as if they are the same 'logical' Scheduler within a cluster. You may use the value "AUTO" as the instanceId if you wish the Id to be generated for you. Or the value "SYS_PROP" if you want the value to come from the system property "quartz.scheduler.instanceId".

**`quartz.scheduler.instanceIdGenerator.type`**

Only used if quartz.scheduler.instanceId is set to "AUTO". Defaults to "quartz.simpl.SimpleInstanceIdGenerator", which generates an instance id based upon host name and time stamp. Other IntanceIdGenerator implementations include SystemPropertyInstanceIdGenerator (which gets the instance id from the system property "quartz.scheduler.instanceId", and HostnameInstanceIdGenerator which uses the local host name (InetAddress.getLocalHost().getHostName()). You can also implement the InstanceIdGenerator interface your self.

**`quartz.scheduler.threadName`**

Can be any String that is a valid name for a java thread. If this property is not specified, the thread will receive the scheduler’s name ("quartz.scheduler.instanceName") plus an the appended string '_QuartzSchedulerThread'.

**`quartz.scheduler.makeSchedulerThreadDaemon`**

A boolean value ('true' or 'false') that specifies whether the main thread of the scheduler should be a daemon thread or not. See also the quartz.scheduler.makeSchedulerThreadDaemon property for tuning the SimpleThreadPool if that is the thread pool implementation you are using (which is most likely the case).

**`quartz.scheduler.idleWaitTime`**

Is the amount of time in milliseconds that the scheduler will wait before re-queries for available triggers when the scheduler is otherwise idle. Normally you should not have to 'tune' this parameter, unless you’re using XA transactions, and are having problems with delayed firings of triggers that should fire immediately. Values less than 5000 ms are not recommended as it will cause excessive database querying. Values less than 1000 are not legal.

**`quartz.scheduler.dbFailureRetryInterval`**

Is the amount of time in milliseconds that the scheduler will wait between re-tries when it has detected a loss of connectivity within the JobStore (e.g. to the database). This parameter is obviously not very meaningful when using RamJobStore.

**`quartz.scheduler.typeLoadHelper.type`**

Defaults to the most robust approach, which is to use the "quartz.simpl.CascadingClassLoadHelper" class - which in turn uses every other ClassLoadHelper class until one works. You should probably not find the need to specify any other class for this property, though strange things seem to happen within application servers. All of the current possible ClassLoadHelper implementation can be found in the quartz.simpl package.

**`quartz.scheduler.jobFactory.type`**

The class name of the JobFactory to use. A JobFatcory is responsible for producing instances of JobClasses. The default is 'quartz.simpl.PropertySettingJobFactory', which simply calls newInstance() on the class to produce a new instance each time execution is about to occur. PropertySettingJobFactory also reflectively sets the job’s bean properties using the contents of the SchedulerContext and Job and Trigger JobDataMaps.

**`quartz.context.key.SOME_KEY`**

Represent a name-value pair that will be placed into the "scheduler context" as strings. (see Scheduler.getContext()). So for example, the setting "quartz.context.key.MyKey = MyValue" would perform the equivalent of scheduler.getContext().put("MyKey", "MyValue").

NOTE: The Transaction-Related properties should be left out of the config file unless you are using JTA transactions.

**`quartz.scheduler.batchTriggerAcquisitionMaxCount`**

The maximum number of triggers that a scheduler node is allowed to acquire (for firing) at once. Default value is 1. The larger the number, the more efficient firing is (in situations where there are very many triggers needing to be fired all at once) - but at the cost of possible imbalanced load between cluster nodes. If the value of this property is set to > 1, and JDBC JobStore is used, then the property "quartz.jobStore.acquireTriggersWithinLock" must be set to "true" to avoid data corruption.

**`quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow`**

The amount of time in milliseconds that a trigger is allowed to be acquired and fired ahead of its scheduled fire time. Defaults to 0. The larger the number, the more likely batch acquisition of triggers to fire will be able to select and fire more than 1 trigger at a time - at the cost of trigger schedule not being honored precisely (triggers may fire this amount early). This may be useful (for performance’s sake) in situations where the scheduler has very large numbers of triggers that need to be fired at or near the same time.

## ThreadPool

Property Name	Required	Type	Default Value
quartz.threadPool.class	yes	string (class name)	null
quartz.threadPool.threadCount	yes	int	-1
quartz.threadPool.threadPriority	no	int	Thread.NORM_PRIORITY (5)
quartz.threadPool.class

Is the name of the ThreadPool implementation you wish to use. The threadpool that ships with Quartz is "quartz.simpl.SimpleThreadPool", and should meet the needs of nearly every user. It has very simple behavior and is very well tested. It provides a fixed-size pool of threads that 'live' the lifetime of the Scheduler.

**`quartz.threadPool.threadCount`**

Can be any positive integer, although you should realize that only numbers between 1 and 100 are very practical. This is the number of threads that are available for concurrent execution of jobs. If you only have a few jobs that fire a few times a day, then 1 thread is plenty! If you have tens of thousands of jobs, with many firing every minute, then you probably want a thread count more like 50 or 100 (this highly depends on the nature of the work that your jobs perform, and your systems resources!).

**`quartz.threadPool.threadPriority`**

Can be any int between Thread.MIN_PRIORITY (which is 1) and Thread.MAX_PRIORITY (which is 10). The default is Thread.NORM_PRIORITY (5).

### SimpleThreadPool-Specific Properties

Property Name	Required	Type	Default Value
quartz.threadPool.makeThreadsDaemons	no	boolean	false
quartz.threadPool.threadsInheritGroupOfInitializingThread	no	boolean	true
quartz.threadPool.threadsInheritContextClassLoaderOfInitializingThread	no	boolean	false
quartz.threadPool.threadNamePrefix	no	string	[Scheduler Name]_Worker
quartz.threadPool.makeThreadsDaemons

Can be set to "true" to have the threads in the pool created as daemon threads. Default is "false". See also the ConfigMain quartz.scheduler.makeSchedulerThreadDaemon property.

**`quartz.threadPool.threadNamePrefix`**

The prefix for thread names in the worker pool - will be postpended with a number.

### Custom ThreadPools

If you use your own implementation of a thread pool, you can have properties set on it reflectively simply by naming the property as thus:

Setting Properties on a Custom ThreadPool

```
quartz.threadPool.type = MyLibrary.FooThreadPool, MyLibrary
quartz.threadPool.somePropOfFooThreadPool = someValue
```

## Listeners

Global listeners can be instantiated and configured by StdSchedulerFactory, or your application can do it itself at runtime, and then register the listeners with the scheduler. "Global" listeners listen to the events of every job/trigger rather than just the jobs/triggers that directly reference them.

Configuring listeners through the configuration file consists of giving then a name, and then specifying the class name, and any other properties to be set on the instance. The class must have a no-arg constructor, and the properties are set reflectively. Only primitive data type values (including Strings) are supported.

Thus, the general pattern for defining a "global" TriggerListener is:

Configuring a Global TriggerListener

```
quartz.triggerListener.NAME.type = MyLibrary.MyListenerClass, MyLibrary
quartz.triggerListener.NAME.propName = propValue
quartz.triggerListener.NAME.prop2Name = prop2Value
```

And the general pattern for defining a "global" JobListener is:

Configuring a Global JobListener

```
quartz.jobListener.NAME.type = MyLibrary.MyListenerClass, MyLibrary
quartz.jobListener.NAME.propName = propValue
quartz.jobListener.NAME.prop2Name = prop2Value
```

## Plug-Ins

Like listeners configuring plugins through the configuration file consists of giving then a name, and then specifying the class name, and any other properties to be set on the instance. The class must have a no-arg constructor, and the properties are set reflectively. Only primitive data type values (including Strings) are supported.

Thus, the general pattern for defining a plug-in is:

Configuring a Plugin

```
quartz.plugin.NAME.class = MyLibrary.MyPluginClass, MyLibrary
quartz.plugin.NAME.propName = propValue
quartz.plugin.NAME.prop2Name = prop2Value
```

There are several Plugins that come with Quartz, that can be found in the quartz.plugins package (and subpackages). Example of configuring a few of them are as follows:

### Sample configuration of Logging Trigger History Plugin

The logging trigger history plugin catches trigger events (it is also a trigger listener) and logs then with Jakarta Commons-Logging. See the class’s JavaDoc for a list of all the possible parameters.

Sample configuration of Logging Trigger History Plugin

```
quartz.plugin.triggHistory.type = Quartz.Plugins.History.LoggingTriggerHistoryPlugin, TODO
quartz.plugin.triggHistory.triggerFiredMessage = Trigger \{1\}.\{0\} fired job \{6\}.\{5\} at: \{4, date, HH:mm:ss MM/dd/yyyy}
quartz.plugin.triggHistory.triggerCompleteMessage = Trigger \{1\}.\{0\} completed firing job \{6\}.\{5\} at \{4, date, HH:mm:ss MM/dd/yyyy\}.
```

### Sample configuration of XML Scheduling Data Processor Plugin

Job initialization plugin reads a set of jobs and triggers from an XML file, and adds them to the scheduler during initialization. It can also delete exiting data. See the class’s JavaDoc for more details.

Sample configuration of JobInitializationPlugin

```
quartz.plugin.jobInitializer.type = Quartz.Plugins.Xml.XMLSchedulingDataProcessorPlugin, TODO
quartz.plugin.jobInitializer.fileNames = data/my_job_data.xml
quartz.plugin.jobInitializer.failOnFileNotFound = true
```

The XML schema definition for the file can be found here:

/xml/job_scheduling_data_1_8.xsd

### Sample configuration of Shutdown Hook Plugin

The shutdown-hook plugin catches the event of the JVM terminating, and calls shutdown on the scheduler.

Sample configuration of ShutdownHookPlugin

```
quartz.plugin.shutdownhook.type = Quartz.Plugins.Management.ShutdownHookPlugin, TODO
quartz.plugin.shutdownhook.cleanShutdown = true
```

## Remoting Server and Client

::: warning
Remoting only works with .NET Full Framework. It's also considered unsafe.
:::

None of the primary properties are required, and all have 'reasonable' defaults. When using Quartz via RMI, you need to start an instance of Quartz with it configured to "export" its services via RMI. You then create clients to the server by configuring a Quartz scheduler to "proxy" its work to the server.

NOTE: Some users experience problems with class availability (namely Job classes) between the client and server. To work through these problems you’ll need an understanding of RMI’s "codebase" and RMI security managers. You may find these resources to be useful:

An excellent description of RMI and codebase: http://www.kedwards.com/jini/codebase.html. One of the important points is to realize that "codebase" is used by the client!

Quick info about security managers: http://gethelp.devx.com/techtips/java_pro/10MinuteSolutions/10min0500.asp

And finally from the Java API docs, read the docs for the RMISecurityManager.

Property Name	Required	Default Value
quartz.scheduler.rmi.export	no	false
quartz.scheduler.rmi.registryHost	no	'localhost'
quartz.scheduler.rmi.registryPort	no	1099
quartz.scheduler.rmi.createRegistry	no	'never'
quartz.scheduler.rmi.serverPort	no	random
quartz.scheduler.rmi.proxy	no	false
quartz.scheduler.rmi.export

If you want the Quartz Scheduler to export itself via RMI as a server then set the 'rmi.export' flag to true.

quartz.scheduler.rmi.registryHost

The host at which the RMI Registry can be found (often 'localhost').

quartz.scheduler.rmi.registryPort

The port on which the RMI Registry is listening (usually 1099).

quartz.scheduler.rmi.createRegistry

Set the 'rmi.createRegistry' flag according to how you want Quartz to cause the creation of an RMI Registry. Use "false" or "never" if you don’t want Quartz to create a registry (e.g. if you already have an external registry running). Use "true" or "as_needed" if you want Quartz to first attempt to use an existing registry, and then fall back to creating one. Use "always" if you want Quartz to attempt creating a Registry, and then fall back to using an existing one. If a registry is created, it will be bound to port number in the given 'quartz.scheduler.rmi.registryPort' property, and 'quartz.rmi.registryHost' should be "localhost".

quartz.scheduler.rmi.serverPort

The port on which the the Quartz Scheduler service will bind and listen for connections. By default, the RMI service will 'randomly' select a port as the scheduler is bound to the RMI Registry.

quartz.scheduler.rmi.proxy

If you want to connect to (use) a remotely served scheduler, then set the 'quartz.scheduler.rmi.proxy' flag to true. You must also then specify a host and port for the RMI Registry process - which is typically 'localhost' port 1099.

NOTE: It does not make sense to specify a 'true' value for both 'quartz.scheduler.rmi.export' and 'quartz.scheduler.rmi.proxy' in the same config file - if you do, the 'export' option will be ignored. A value of 'false' for both 'export' and 'proxy' properties is of course valid, if you’re not using Quartz via RMI.

## RAMJobStore

RAMJobStore is used to store scheduling information (job, triggers and calendars) within memory. RAMJobStore is fast and lightweight, but all scheduling information is lost when the process terminates.

RAMJobStore is selected by setting the `quartz.jobStore.type` property as such:

Setting The Scheduler’s JobStore to RAMJobStore

```
quartz.jobStore.type = Quartz.Simpl.RAMJobStore, Quartz
```

RAMJobStore can be tuned with the following properties:

|Property Name	| Required|	Type	| Default Value|
|---------------|---------|---------|--------------|
|quartz.jobStore.misfireThreshold	|no|	int|	60000|

**`quartz.jobStore.misfireThreshold`**

The the number of milliseconds the scheduler will 'tolerate' a trigger to pass its next-fire-time by, before being considered "misfired". The default value (if you don’t make an entry of this property in your configuration) is 60000 (60 seconds).

## JobStoreTX (ADO.NET)

JDBCJobStore is used to store scheduling information (job, triggers and calendars) within a relational database. There are actually two seperate JDBCJobStore classes that you can select between, depending on the transactional behaviour you need.

JobStoreTX manages all transactions itself by calling commit() (or rollback()) on the database connection after every action (such as the addition of a job). JDBCJobStore is appropriate if you are using Quartz in a stand-alone application, or within a servlet container if the application is not using JTA transactions.

The JobStoreTX is selected by setting the quartz.jobStore.class property as such:

Setting The Scheduler’s JobStore to JobStoreTX

quartz.jobStore.type = Quartz.Impl.AdoJobStore.JobStoreTX, Quartz

JobStoreTX can be tuned with the following properties:

|Property Name	| Required|	Type	| Default Value|
|---------------|---------|---------|--------------|
|quartz.jobStore.driverDelegateType|	yes|	string|	null|
|quartz.jobStore.dataSource	|yes|	string	|null|
|quartz.jobStore.tablePrefix	|no|	string	|"QRTZ_"|
|quartz.jobStore.useProperties|	no	|boolean|	false|
|quartz.jobStore.misfireThreshold|	no|	int	|60000|
|quartz.jobStore.isClustered|	no	|boolean	|false|
|quartz.jobStore.clusterCheckinInterval	|no	|long|	15000|
|quartz.jobStore.maxMisfiresToHandleAtATime	|no|	int|	20|
|quartz.jobStore.dontSetAutoCommitFalse|	no	|boolean|	false|
|quartz.jobStore.selectWithLockSQL|	no|	string|	"SELECT * FROM {0}LOCKS WHERE SCHED_NAME = {1} AND LOCK_NAME = ? FOR UPDATE"|
|quartz.jobStore.txIsolationLevelSerializable|	no	|boolean|	false|
|quartz.jobStore.acquireTriggersWithinLock	|no|	boolean	|false (or true - see doc below)|
|quartz.jobStore.lockHandler.type |	no|	string|	null|
|quartz.jobStore.driverDelegateInitString|	no	|string|	null|

**`quartz.jobStore.driverDelegateType`**

Driver delegates understand the particular 'dialects' of varies database systems. Possible choices include:

quartz.impl.jdbcjobstore.StdJDBCDelegate (for fully JDBC-compliant drivers)

quartz.impl.jdbcjobstore.MSSQLDelegate (for Microsoft SQL Server, and Sybase)

quartz.impl.jdbcjobstore.PostgreSQLDelegate

quartz.impl.jdbcjobstore.WebLogicDelegate (for WebLogic drivers)

quartz.impl.jdbcjobstore.oracle.OracleDelegate

quartz.impl.jdbcjobstore.oracle.WebLogicOracleDelegate (for Oracle drivers used within Weblogic)

quartz.impl.jdbcjobstore.oracle.weblogic.WebLogicOracleDelegate (for Oracle drivers used within Weblogic)

quartz.impl.jdbcjobstore.CloudscapeDelegate

quartz.impl.jdbcjobstore.DB2v6Delegate

quartz.impl.jdbcjobstore.DB2v7Delegate

quartz.impl.jdbcjobstore.DB2v8Delegate

quartz.impl.jdbcjobstore.HSQLDBDelegate

quartz.impl.jdbcjobstore.PointbaseDelegate

quartz.impl.jdbcjobstore.SybaseDelegate

Note that many databases are known to work with the StdJDBCDelegate, while others are known to work with delegates for other databases, for example Derby works well with the Cloudscape delegate (no surprise there).

**`quartz.jobStore.dataSource`**

The value of this property must be the name of one the DataSources defined in the configuration properties file. See the ConfigDataSources configuration docs for DataSources for more information.

**`quartz.jobStore.tablePrefix`**

JDBCJobStore’s "table prefix" property is a string equal to the prefix given to Quartz’s tables that were created in your database. You can have multiple sets of Quartz’s tables within the same database if they use different table prefixes.

**`quartz.jobStore.useProperties`**

The "use properties" flag instructs JDBCJobStore that all values in JobDataMaps will be Strings, and therefore can be stored as name-value pairs, rather than storing more complex objects in their serialized form in the BLOB column. This is can be handy, as you avoid the class versioning issues that can arise from serializing your non-String classes into a BLOB.

**`quartz.jobStore.misfireThreshold`**

The the number of milliseconds the scheduler will 'tolerate' a trigger to pass its next-fire-time by, before being considered "misfired". The default value (if you don’t make an entry of this property in your configuration) is 60000 (60 seconds).

**`quartz.jobStore.isClustered`**

Set to "true" in order to turn on clustering features. This property must be set to "true" if you are having multiple instances of Quartz use the same set of database tables…​ otherwise you will experience havoc. See the configuration docs for clustering for more information.

**`quartz.jobStore.clusterCheckinInterval`**

Set the frequency (in milliseconds) at which this instance "checks-in"* with the other instances of the cluster. Affects the quickness of detecting failed instances.

**`quartz.jobStore.maxMisfiresToHandleAtATime`**

The maximum number of misfired triggers the jobstore will handle in a given pass. Handling many (more than a couple dozen) at once can cause the database tables to be locked long enough that the performance of firing other (not yet misfired) triggers may be hampered.

**`quartz.jobStore.dontSetAutoCommitFalse`**

Setting this parameter to "true" tells Quartz not to call setAutoCommit(false) on connections obtained from the DataSource(s). This can be helpful in a few situations, such as if you have a driver that complains if it is called when it is already off. This property defaults to false, because most drivers require that setAutoCommit(false) is called.

**`quartz.jobStore.selectWithLockSQL`**

Must be a SQL string that selects a row in the "LOCKS" table and places a lock on the row. If not set, the default is "SELECT * FROM {0}LOCKS WHERE SCHED_NAME = {1} AND LOCK_NAME = ? FOR UPDATE", which works for most databases. The "{0}" is replaced during run-time with the TABLE_PREFIX that you configured above. The "{1}" is replaced with the scheduler’s name.

**`quartz.jobStore.txIsolationLevelSerializable`**

A value of "true" tells Quartz (when using JobStoreTX or CMT) to call setTransactionIsolation(Connection.TRANSACTION_SERIALIZABLE) on JDBC connections. This can be helpful to prevent lock timeouts with some databases under high load, and "long-lasting" transactions.

**`quartz.jobStore.acquireTriggersWithinLock`**

Whether or not the acquisition of next triggers to fire should occur within an explicit database lock. This was once necessary (in previous versions of Quartz) to avoid dead-locks with particular databases, but is no longer considered necessary, hence the default value is "false".

If "quartz.scheduler.batchTriggerAcquisitionMaxCount" is set to > 1, and JDBC JobStore is used, then this property must be set to "true" to avoid data corruption (as of Quartz 2.1.1 "true" is now the default if batchTriggerAcquisitionMaxCount is set > 1).

**`quartz.jobStore.lockHandler.type`**

The class name to be used to produce an instance of a quartz.impl.jdbcjobstore.Semaphore to be used for locking control on the job store data. This is an advanced configuration feature, which should not be used by most users. By default, Quartz will select the most appropriate (pre-bundled) Semaphore implementation to use. quartz.impl.jdbcjobstore.UpdateLockRowSemaphore QUARTZ-497 may be of interest to MS SQL Server users. See QUARTZ-441.

### Customizing StdRowLockSemaphore

If you explicitly choose to use this DB Semaphore, you can customize it further on how frequent to poll for DB locks.

Example of Using a Custom StdRowLockSemaphore Implementation

```
quartz.jobStore.lockHandler.class = quartz.impl.jdbcjobstore.StdRowLockSemaphore
quartz.jobStore.lockHandler.maxRetry = 7     # Default is 3
quartz.jobStore.lockHandler.maxRetry = 3000  # Default is 1000 millis
```

**`quartz.jobStore.driverDelegateInitString`**

A pipe-delimited list of properties (and their values) that can be passed to the DriverDelegate during initialization time.

The format of the string is as such:

`settingName=settingValue|otherSettingName=otherSettingValue|...`

The StdJDBCDelegate and all of its descendants (all delegates that ship with Quartz) support a property called 'triggerPersistenceDelegateClasses' which can be set to a comma-separated list of classes that implement the TriggerPersistenceDelegate interface for storing custom trigger types. See the Java classes SimplePropertiesTriggerPersistenceDelegateSupport and SimplePropertiesTriggerPersistenceDelegateSupport for examples of writing a persistence delegate for a custom trigger.


## DataSources (ADO.NET JobStores)

If you’re using JDBC-Jobstore, you’ll be needing a DataSource for its use (or two DataSources, if you’re using JobStoreCMT).

DataSources can be configured in three ways:

All pool properties specified in the quartz.properties file, so that Quartz can create the DataSource itself.

The JNDI location of an application server managed Datasource can be specified, so that Quartz can use it.

Custom defined quartz.utils.ConnectionProvider implementations.

It is recommended that your Datasource max connection size be configured to be at least the number of worker threads in the thread pool plus three. You may need additional connections if your application is also making frequent calls to the scheduler API. If you are using JobStoreCMT, the "non managed" datasource should have a max connection size of at least four.

Each DataSource you define (typically one or two) must be given a name, and the properties you define for each must contain that name, as shown below. The DataSource’s "NAME" can be anything you want, and has no meaning other than being able to identify it when it is assigned to the JDBCJobStore.

Quartz-created DataSources are defined with the following properties:
Property Name	Required	Type	Default Value
quartz.dataSource.NAME.driver	yes	String	null
quartz.dataSource.NAME.URL	yes	String	null
quartz.dataSource.NAME.user	no	String	""
quartz.dataSource.NAME.password	no	String	""
quartz.dataSource.NAME.maxConnections	no	int	10
quartz.dataSource.NAME.validationQuery	no	String	null
quartz.dataSource.NAME.idleConnectionValidationSeconds	no	int	50
quartz.dataSource.NAME.validateOnCheckout	no	boolean	false
quartz.dataSource.NAME.discardIdleConnectionsSeconds	no	int	0 (disabled)
quartz.dataSource.NAME.driver

Must be the java class name of the JDBC driver for your database.

quartz.dataSource.NAME.URL

The connection URL (host, port, etc.) for connection to your database.

quartz.dataSource.NAME.user

The user name to use when connecting to your database.

quartz.dataSource.NAME.password

The password to use when connecting to your database.

quartz.dataSource.NAME.maxConnections

The maximum number of connections that the DataSource can create in it’s pool of connections.

quartz.dataSource.NAME.validationQuery

Is an optional SQL query string that the DataSource can use to detect and replace failed/corrupt connections. For example an oracle user might choose "select table_name from user_tables" - which is a query that should never fail - unless the connection is actually bad.

quartz.dataSource.NAME.idleConnectionValidationSeconds

The number of seconds between tests of idle connections - only enabled if the validation query property is set. Default is 50 seconds.

quartz.dataSource.NAME.validateOnCheckout

Whether the database sql query to validate connections should be executed every time a connection is retrieved from the pool to ensure that it is still valid. If false, then validation will occur on check-in. Default is false.

quartz.dataSource.NAME.discardIdleConnectionsSeconds

Discard connections after they have been idle this many seconds. 0 disables the feature. Default is 0.

Example of a Quartz-defined DataSource

quartz.dataSource.myDS.driver = oracle.jdbc.driver.OracleDriver
quartz.dataSource.myDS.URL = jdbc:oracle:thin:@10.0.1.23:1521:demodb
quartz.dataSource.myDS.user = myUser
quartz.dataSource.myDS.password = myPassword
quartz.dataSource.myDS.maxConnections = 30
References to Application Server DataSources are defined with the following properties:
Property Name	Required	Type	Default Value
quartz.dataSource.NAME.jndiURL	yes	String	null
quartz.dataSource.NAME.java.naming.factory.initial	no	String	null
quartz.dataSource.NAME.java.naming.provider.url	no	String	null
quartz.dataSource.NAME.java.naming.security.principal	no	String	null
quartz.dataSource.NAME.java.naming.security.credentials	no	String	null
quartz.dataSource.NAME.jndiURL

The JNDI URL for a DataSource that is managed by your application server.

quartz.dataSource.NAME.java.naming.factory.initial

The (optional) class name of the JNDI InitialContextFactory that you wish to use.

quartz.dataSource.NAME.java.naming.provider.url

The (optional) URL for connecting to the JNDI context.

quartz.dataSource.NAME.java.naming.security.principal

The (optional) user principal for connecting to the JNDI context.

quartz.dataSource.NAME.java.naming.security.credentials

The (optional) user credentials for connecting to the JNDI context.

Example of a Datasource referenced from an Application Server

quartz.dataSource.myOtherDS.jndiURL=jdbc/myDataSource
quartz.dataSource.myOtherDS.java.naming.factory.initial=com.evermind.server.rmi.RMIInitialContextFactory
quartz.dataSource.myOtherDS.java.naming.provider.url=ormi:<span class="code-comment">//localhost
</span>quartz.dataSource.myOtherDS.java.naming.security.principal=admin
quartz.dataSource.myOtherDS.java.naming.security.credentials=123
Custom ConnectionProvider Implementations
Property Name	Required	Type	Default Value
quartz.dataSource.NAME.connectionProvider.class	yes	String (class name)	null
quartz.dataSource.NAME.connectionProvider.class

The class name of the ConnectionProvider to use. After instantiating the class, Quartz can automatically set configuration properties on the instance, bean-style.

Example of Using a Custom ConnectionProvider Implementation

quartz.dataSource.myCustomDS.connectionProvider.class = com.foo.FooConnectionProvider
quartz.dataSource.myCustomDS.someStringProperty = someValue
quartz.dataSource.myCustomDS.someIntProperty = 5


## Clustering

Quartz’s clustering features bring both high availability and scalability to your scheduler via fail-over and load balancing functionality.

Clustering currently only works with the JDBC-Jobstore (JobStoreTX or JobStoreCMT), and essentially works by having each node of the cluster share the same database.

Load-balancing occurs automatically, with each node of the cluster firing jobs as quickly as it can. When a trigger’s firing time occurs, the first node to acquire it (by placing a lock on it) is the node that will fire it.

Only one node will fire the job for each firing. What I mean by that is, if the job has a repeating trigger that tells it to fire every 10 seconds, then at 12:00:00 exactly one node will run the job, and at 12:00:10 exactly one node will run the job, etc. It won’t necessarily be the same node each time - it will more or less be random which node runs it. The load balancing mechanism is near-random for busy schedulers (lots of triggers) but favors the same node for non-busy (e.g. few triggers) schedulers.

Fail-over occurs when one of the nodes fails while in the midst of executing one or more jobs. When a node fails, the other nodes detect the condition and identify the jobs in the database that were in progress within the failed node. Any jobs marked for recovery (with the "requests recovery" property on the JobDetail) will be re-executed by the remaining nodes. Jobs not marked for recovery will simply be freed up for execution at the next time a related trigger fires.

The clustering feature works best for scaling out long-running and/or cpu-intensive jobs (distributing the work-load over multiple nodes). If you need to scale out to support thousands of short-running (e.g 1 second) jobs, consider partitioning the set of jobs by using multiple distinct schedulers (including multiple clustered schedulers for HA). The scheduler makes use of a cluster-wide lock, a pattern that degrades performance as you add more nodes (when going beyond about three nodes - depending upon your database’s capabilities, etc.).

Enable clustering by setting the "quartz.jobStore.isClustered" property to "true". Each instance in the cluster should use the same copy of the quartz.properties file. Exceptions of this would be to use properties files that are identical, with the following allowable exceptions: Different thread pool size, and different value for the "quartz.scheduler.instanceId" property. Each node in the cluster MUST have a unique instanceId, which is easily done (without needing different properties files) by placing "AUTO" as the value of this property. See the info about the configuration properties of JDBC-JobStore for more information.

NOTE: Never run clustering on separate machines, unless their clocks are synchronized using some form of time-sync service (daemon) that runs very regularly (the clocks must be within a second of each other). See https://www.nist.gov/pml/time-and-frequency-division/services/internet-time-service-its if you are unfamiliar with how to do this.

NOTE: Never start (scheduler.start()) a non-clustered instance against the same set of database tables that any other instance is running (start()ed) against. You may get serious data corruption, and will definitely experience erratic behavior.

Example Properties For A Clustered Scheduler

```
#============================================================================
# Configure Main Scheduler Properties
#============================================================================

quartz.scheduler.instanceName = MyClusteredScheduler
quartz.scheduler.instanceId = AUTO

#============================================================================
# Configure ThreadPool
#============================================================================

quartz.threadPool.type = Quartz.Simpl.SimpleThreadPool, Quartz
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

quartz.jobStore.isClustered = true
quartz.jobStore.clusterCheckinInterval = 20000

#============================================================================
# Configure Datasources
#============================================================================

quartz.dataSource.myDS.driver = oracle.jdbc.driver.OracleDriver
quartz.dataSource.myDS.URL = jdbc:oracle:thin:@polarbear:1521:dev
quartz.dataSource.myDS.user = quartz
quartz.dataSource.myDS.password = quartz
quartz.dataSource.myDS.maxConnections = 5
quartz.dataSource.myDS.validationQuery=select 0 from dual
```
