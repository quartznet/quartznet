# QUARTZ.NET CHANGELOG

[http://www.quartz-scheduler.net](http://www.quartz-scheduler.net)

## Release 4.0.0, T.B.D

### BREAKING CHANGES

  * A lot of types were sealed and/or internalized, these can be opened later on if needed. We are just trying to minimize API surface that needs to be maintained
  * netstandard2.0 build no longer reference System.Configuration.ConfigurationManager and thus there's no support for Full Framework style .config files
  * **JobKey** and **TriggerKey** now throw an **ArgumentNullException** when you specify **null** for _name_ or _group_ (#1359)
  * The following properties have been removed from **AbstractTrigger** as they represent information that is already available through the **Key** and **JobKey** properties:
    * Name
    * GroupName
    * JobName
    * JobGroup
    * FullName
  * Triggers can no longer be constructed with a **null** group name (#1359)
  * The *endUtc* argument of **SimpleTriggerImpl** is no longer nullable.
  * If a value is explicitly specified for "IdleWaitTime", we will no longer silently ignore the value (and use
    a default value of 30 seconds instead) if it's less than or equal to **zero**.
  * If you use **StdSchedulerFactory** to create a scheduler, we will no longer reject an **IdleWaitTime** that
    is greater than **zero** but less than **1000 milliseconds**.
  * An negative value for **IdleWaitTime** or **BatchTimeWindow** will no longer be accepted.
  * For **MaxBatchSize**, a value less than or equal to **zero** will be rejected.
  * The ctor for **QuartzScheduler** no longer takes an **idleWaitTime** argument. This value
    is now obtained from a newly introduced **IdleWaitTime** property on **QuartzSchedulerResources**.

  * `SystemTime` was removed as way to provide "now", you can inject `TimeProvider` via configuration or `SchedulerBuilder.UseTimeProvider<T>()`

  * The `Equals(StringOperator? other)` method of **StringOperator** is now also virtual to allow it to be
    overridden in pair with `Equals(object? obj)` and `GetHashCode()`.

  * The **Quartz.Util.DictionaryExtensions** type was removed.

  * The 'Get(TKey key)' method of **DirtyFlagMap<TKey,TValue>** has been removed. You can instead use the
    this[TKey key] indexer or `TryGetValue(TKey key, out TValue value)` to obtain the value for a given key.

  * (Logging): `LibLog` has been removed and replaced with `Microsoft.Logging.Abstractions` (#1480).

  * The following properties of **DirtyFlagMap<TKey,TValue>** are now explicit interface implementations:
    * IsReadOnly
    * IsFixedSize
    * SyncRoot
    * IsSynchronized

  * A **IJobStore** that implements **IJobListener** no longer automatically receives all events. You should
    instead register it as job listener using the **ListenerManager** of the **QuartzScheduler**.

  * **QuartzScheduler** no longer defines a default protected ctor. You should use ctor((QuartzSchedulerResources resources)
    to initialize the **QuartzScheduler**.

  * To improve performance and reduce allocations, `IListenerManager.GetJobListeners()` now returns (a shallow copy of)
    the registered **IJobListener** instances as an array instead of an **IReadOnlyCollection<IJobListener>**.

  * **QuartzScheduler** no longer defines properties and methods for accessing or manipulating internal job listeners.
    **ListenerManager** on **QuartzScheduler** allows more control over the events that a **IJobListener** will
    receive.

  * To improve performance and reduce allocations, `IListenerManager.GetTriggerListeners()` now returns (a shallow copy of)
    the registered **ITriggerListener** instances as an array instead of an **IReadOnlyCollection<ITriggerListener>**.

  * **QuartzScheduler** no longer defines properties and methods for accessing or manipulating internal trigger listeners.
    **ListenerManager** on **QuartzScheduler** allows more control over the events that a **ITriggerListener** will
    receive.

  * Introduce JobType to allow storing job's type information without actual Type instance (#1610)

  * IJobExecutionContext.RecoveringTriggerKey now returns null if IJobExecutionContext.Recovering is false instead of throwing exception.

  * `Task` return types and parameters have been changed to `ValueTask`.  Any consumers of Quartz expecting a `Task` will require to update the signatures to `ValueTask`,
     or use the `AsTask()` Method on ValueTask to Return the `ValueTask` as a `Task`  (#988)

  * To configure JSON serialization to be used in job store instead of old `UseJsonSerializer` you should now use either `UseSystemTextJsonSerializer` or `UseNewtonsoftJsonSerializer`
    and remove the old package reference `Quartz.Serialization.Json` (and if Newtonsoft used, reference `Quartz.Serialization.Newtonsoft`). Change was made to distinguish the two common
    serializers that are being used (System.Text.Json and JSON.NET).

  * `Quartz.Extensions.DependencyInjection`, `Quartz.Extensions.Hosting` and `Quartz.Serialization.SystemTextJson`  were merged to be part of main Quartz package, you can now remove those package references

  * `JobStoreSupport`'s `GetNonManagedTXConnection` and `GetConnection` return signatures changed from `ConnectionAndTransactionHolder` to `ValueTask<ConnectionAndTransactionHolder>`

  * `DirectSchedulerFactory.CreateScheduler` must now be `await`ed

#### Cron Parser

  * Add cron parser support for 'L' and 'LW' in expression combinations for daysOfMonth (#1939) (#1288)
  * Add cron parser support for `LW-<OFFSET>`. i.e. `LW-2` (calculate the Last weekday then offset by -2) (#1287)
    If the calculated day would cross a month boundary it will be reset to the 1st of the month. (e.g. LW-30 in Feb will be 1st Feb)
  * Add cron parser support for day-of-month and day-of-week together. (#1543)

### FIXES

  * Fix for deserializing CronExpression using Json Serializer throwing error calling `GetNextValidTimeAfter`.  (#1996)
    `IDeserializationCallback` interface was removed from class `CronExpression` and the deserialization logic
    added to the constructor `CronExpression(SerializationInfo info, StreamingContext context)`.


## Release 3.8.1, Feb xx 2024

* Fix handling of env var quartz.config (#2212) (#2213)
* Use configured type loader in scheduler factory init (#2268)


## Release 3.8.0, Nov 18 2023

* CHANGES
    * `TryGetString` method added to JobDataMap (#2125)
    * Add NET 8.0 targeting for examples, tests and integration projects (#2192)
    * Upgrade TimeZoneConverter to version 6.1.0 (#2194)
    * Improve trimming compatibility (#2195, #2131, #2197)

* FIXES
    * JobDataMap `TryGetXXX` methods will now correctly return true/false if a key value can be retrieved (or not) (#2125)
    * JobDataMap `GetXXX` methods throw KeyNotFoundException if the key does not exist on the JobDataMap (#2125)
    * JobDataMap `GetXXX` methods throw InvalidCastException if null value for non nullable types is found. (#2125)
    * DailyCalendar should use same time zone offset for all checks (#2113)
    * SendMailJob will now throw JobExecutionException on BuildMessage construction failure due to missing mandatory params. (#2126)
    * JobInterruptMonitorPlugin should tolerate missing JobDataMapKeyAutoInterruptable (#2191)
    * XMLSchedulingDataProcessorPlugin not using custom TypeLoader #2131

## Release 3.7.0, xxx xx 2023

* CHANGES

    * Mark UseJsonSerializer as obsolete, one should use UseNewtonsoftJsonSerializer (#2077)
    * Removed obsolete UseMicrosoftDependencyInjectionScopedJobFactory(), mark UseMicrosoftDependencyInjectionJobFactory() obsolete (#2085)

* FIXES

    * Now omitting UseMicrosoftDependencyInjectionJobFactory() should actually work as it will be the default (#2085)


## Release 3.6.3, Jun 25 2023

To celebrate my daughter's 8th birthday, let's have a maintenance release. This release bring important fix to scoped
job dependency disposal which had regressed in 3.6.1 release.

* FIXES

    * Performance issues reading large job objects from AdoJobStore on SQL Server (#2054)
    * ScopedJob is no longer disposed when using MS DI (#1954)
    * Persistence of extended properties not working when the trigger type is changed (#2040)
    * PersistJobDataAfterExecution not set when loading job detail data from database (#2014)
    * JobInterruptMonitor Plugin should read MaxRunTime from MergedJobDataMap (#2004)
    * Fix unable to get any job related information when using IObserver (#1966)
    * ServiceCollectionSchedulerFactory.GetNamedConnectionString passes wrong value to base (#1960)
    * CronExpression.BuildExpression() fails to catch this invalid expression: 0 0 * * * ?h (#1953)
    * QuartzServiceCollectionExtensions is ambiguous between Quartz.AspNetCore and Quartz.Extensions.Hosting (#1948)

## Release 3.6.2, Feb 25 2023

This is fix to a fix release, 3.6.1 introduced a regression to job selection logic when using persistent job store.

* FIXES

    * Fix SqlSelectJobDetail to include IS_NONCONCURRENT #1927


## Release 3.6.1, Feb 25 2023

This bug fix release contains an important fix to anyone configuring jobs using job builder's `DisallowConcurrentExecution()`
without having the attribute `DisallowConcurrentExecutionAttribute` on type itself.

* FIXES

    * Add missing "disallow concurrency" materialization for jobs (#1923)
    * Allow accessing the wrapped scoped job instance from job execution context (#1917)
    * JobDiagnosticsWriter can throw error when writing context data (#1191)

## Release 3.6.0, Jan 29 2023

This release contains new API to reset errored trigger state in job store, some bug fixes and refinement of package dependencies/targets.

* NEW FEATURES

    * Add explicit netcoreapp3.1 and net6.0 targets to MS integration projects (#1879)
    * Use IHostApplicationLifetime instead of IApplicationLifetime in >= netcoreapp3.1 Hosting targets (#1593)
    * Add ResetTriggerFromErrorState functionality (#1904)

* FIXES

    * Fix named connection string resolution when using MS DI and its configuration system (#1839)
    * Upgrade to System.Configuration.ConfigurationManager 6.0.1 to avoid vulnerable dependency chain (#1792)
    * Fix configuration handling for custom DB provider (#1795)
    * Add extra overloads for registering listeners (#1852)
    * JobDataMap.TryGetGuidValue should return Guid instead of int (#1856)
    * Upgrade to Newtonsoft.Json 13.0.1 (#1859)


## Release 3.5.0, Sep 18 2022

* NEW FEATURES

    * Allow PersistJobDataAfterExecution and ConcurrentExecutionDisallowed to be explicitly set in JobBuilder and pulled up to IJobConfigurator (#1575)
    * Add TryGet functions to JobDataMap and StringKeyDirtyFlagMap (#1592)
    * Add UseMySqlConnector overload for DB configuration (#1621)
    * Validate database schema during scheduler initialization (#1716)
    * Support DataSource name configuration (#1710)
    * Add "UsePersistentStore<T> where T : IJobStore" in DI Extension (#1715)

* FIXES

    * Make RAMJobStore.RemoveJobInternal return true even if job has no triggers (#1580)
    * Configuration property `quartz.jobStore.dbRetryInterval` will be correctly set when constructing the Scheduler JobStore.
        * If you previously had configuration with the key `quartz.scheduler.dbFailureRetryInterval` please change to the above mentioned key.
    * DailyCalendar doesn't include first and last millisecond of day in checks (#1665)
    * StdSchedulerFactory and derived factories are not thread-safe (#1587)
    * Change QuartzOptions to inherit from Dictionary<string, string?> instead of NameValueCollection to fix Microsoft.Extensions.Configuration 7 RC integration (#1748)

* IMPROVEMENTS

    * Reduce scheduler initialization logging noise (#1752)


## Release 3.4.0, Mar 27 2022

This release has Quartz jobs start executing only after application startup completes successfully, unless QuartzHostedServiceOptions are used to specify otherwise.
By default, this prevents jobs from running while the application is still starting, and it alleviates the need to use arbitrary start delays to achieve the effect manually.
Quartz.OpenTelemetry.Instrumentation has been marked obsolete as there's official contrib project on OpenTelemetry project side.

* FIXES

  * Fix for job type loading after version change (#1286)
  * Fix StartDelayed delaying the start of other hosted services (#1314)
  * Set NextFireTime of the replaced trigger relative to the old trigger's StartTime if the old trigger's PreviousFireTime is null (#1519)
  * Include InvertTimeRange property in DailyCalendar.Clone (#1522)
  * QuartzHealthCheck never recovers after detecting failure (#1496)
  * Microsoft DI integration does not working with Microsoft.Extensions.Hosting v7 preview (#1544)


* IMPROVEMENTS

  * Jobs now start executing after application startup completes successfully (#1432)
  * Support strongly-typed configuration of IDbProvider (#1312)
  * Add MSSQL Script compatible with SQL 2014 and 2012 (#1337)
  * Added usage of DisallowConcurrentExecutionAttribute for interfaces (#1345)
  * Multiple performance improvements (#1351, #1355, #1354, #1353, #1356, #1358)
  * Increase precision of SimpleTriggerImpl to ticks. (#1360)
  * Switch from FAKE to NUKE (#1413)
  * QuartzHostedService now has jobs start after application startup (#1449)
  * QuartzHostedServiceOptions can let jobs be started as part of application startup, as before this version  (#1432)
  * Add helper methods to setup Microsoft.Data.Sqlite (#1275)
  * Quartz will scan job and trigger listeners from MS DI container automatically (#1561)


* BREAKING CHANGES

  * Quartz.OpenTelemetry.Instrumentation is now obsolete as there is contrib package on OT side: https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.Quartz
  * .NET Framework minimum version is 4.6.2 (previously 4.6.1) (#1549)


    * The `Equals(StringOperator? other)` method of **StringOperator** is now also virtual to allow it to be
      overridden in pair with `Equals(object? obj)` and `GetHashCode()`.

    * The **Quartz.Util.DictionaryExtensions** type was removed.

    * The 'Get(TKey key)' method of **DirtyFlagMap<TKey,TValue>** has been removed. You can instead use the
      this[TKey key] indexer or `TryGetValue(TKey key, out TValue value)` to obtain the value for a given key.

    * (Logging): `LibLog` has been removed and replaced with `Microsoft.Logging.Abstractions` (#1480).

    * The following properties of **DirtyFlagMap<TKey,TValue>** are now explicit interface implementations:
      * IsReadOnly
      * IsFixedSize
      * SyncRoot
      * IsSynchronized

## Release 3.3.3, Aug 1 2021

This is a maintenance release mostly fixing some smaller bugs and improving DI API story.

* FIXES

    * Lock 'TRIGGER_ACCESS' attempt to return by: de9325af-3e1c-4ae9-a99b-24be994b75f4 -- but not owner! (#1236)
    * ScheduleJob shorthand: Job name should match trigger name by default (#1211)
    * CronTriggerImpl.WillFireOn returns wrong result when TimeZone is specified (#1187)
    * Race condition in DI scheduler listener initialization (#1117)
    * JobRunShell handle Job CancellationToken (#1183)
    * Restore System.Data.SqlClient support on .NET Core (#1181)

* IMPROVEMENTS

    * Replace static loggers with instance-based (#1264)
    * Expose more configuration options via programmatic APIs (#1263)
    * Add ConfigureScope extension point to MicrosoftDependencyInjectionJobFactory (#1189)
    * Update StdAdoConstants.cs (#1186)
    * Use custom InstantiateType for all instantiations in StdSchedulerFactory (#1185)
    * Add support for the ISchedulerFactory.StartDelayed in the QuartzHostedService (#1166)
    * Remove SimpleThreadPool from examples? (#1230)


## Release 3.3.2, Apr 9 2021

This release returns the possibility to resolve jobs from Microsoft DI container. Now container is checked first and if not found then
ActivatorUtilities is used to construct the type with constructor injection support. Now both AllowDefaultConstructor and CreateScope have
been obsoleted as behavior is now either via DI construction or ActivatorUtilities and scope is always created to prevent resource leaks / double disposal.

Also a problem with host name resolution under WSL2 scenario was fixed.

* FIXES

  * Try resolving jobs from service provider before resorting to ActivatorUtilities (#1159)
  * Can't get hostname on WSL2+docker host network (#1158)


## Release 3.3.1, Apr 8 2021

This release fixes assembly signing problem introduced in 3.3.

* FIXES

  * Remove PublicSign property from csproj (#1155)


## Release 3.3, Apr 7 2021

This release addresses problems with using Quartz with .NET Full Framework lower than 4.7.2. ValueTask loading
could fail due the dependencies brought with activity source support. Now activity sources are only supported when
using .NET Framework >= 4.7.2 and netstandard >= 2.0. This also raises requirement the same way for package
Quartz.OpenTelemetry.Instrumentation.

This release also improves trigger acquisition performance when using persistent job store, mostly by reducing network round-trips.
The semaphore implementations were also re-written to gain more performance.

Also some bug fixes included, thanks to all contributors!

* BREAKING CHANGES

  * Activity source listener is not longer part of net461 build, only net472
  * Quartz.AspNetCore integration package minimum .NET Core version is now 3.1 for HealthChecks support

* NEW FEATURES

  * Separate build configuration for .NET Framework 4.7.2
  * OpenTelemetry integration upgraded to target OpenTelemetry 1.0.0-rc1.1
  * Ported JobInterruptMonitorPlugin from Java version which allows automatic interrupt calls for registered jobs (#1110)
  * Rewrite semaphore implementations (#1115)
  * UsingJobData now has Guid and char overloads (#1141)
  * Add a regular AddJob Type (#1090)

* FIXES

  * Jobs not firing after upgrade to 3.2.x (from 3.0.7) on Microsoft Server 2008 R2 (#1083)
  * Jobs are not fired (#1072)
  * MicrosoftDependencyInjectionJobFactory does not inject job properties for scoped jobs (#1106)
  * XSD schema no longer requires defining durable element if you just want to define recover (#1128)
  * Stack trace logging fixed in case of reporting invalid lock acquire (#1133)
  * Disposable job is disposed twice when using UseMicrosoftDependencyInjectionScopedJobFactory (#1120)
  * QuartzHostedService.StopAsync throws NullReferenceException if StartAsync hasn't been run (#1123)


## Release 3.2.4, Jan 19 2021

This release is a maintenance release with couple of bug fixes. The most important fix for this release is that
now Quartz distinguishes between external code task cancellation (say HttpClient) and job cancellation triggered by using
the Quartz API's Interrupt method. Earlier Quartz incorrectly considered also other TaskCanceledExceptions as clean instead of being errors.

* FIXES

  * JobRunShell silently handles OperationCanceledException which is not correct in terms of job retry handling (#1064)
  * Handled exceptions thrown while retrieving the misfired trigger (#1040)
  * FileScanJob is faling after upgrading from 3.0.7 to 3.2.3 (#1027)
  * JobBuilder.UsingJobData(string key, string value) should be JobBuilder.UsingJobData(string key, string? value) (#1025)


## Release 3.2.3, Oct 31 2020

This release addresses issue with Autofac integration and adds new integration package Quartz.OpenTracing to allow
integration with OpenTracing.

* NEW FEATURE

  * Add Quartz.OpenTracing support (#1006)

* FIXES

  * Xamarin Android can't get scheduler (#1008)
  * Autofac job factory registration fails (#1011)


## Release 3.2.2, Oct 19 2020

This release addresses regression in scoped job resolution which was introduced by job factory refactoring done in 3.2.1.

* FIXES

  * Fix scoped job resolution (#998)

## Release 3.2.1, Oct 18 2020

This is a maintenance release containing mostly bug fixes.

MS dependency injection job factory configuration was unified and you can now configure relevant options
like whether to create a separate scope with using just the UseMicrosoftDependencyInjectionJobFactory and its callback.
Now scoped jobs also get their properties set from job data map.

Pre-configuring Quartz options from appsettings.json with services.Configure<QuartzOptions>(Configuration.GetSection("Quartz"));
now also works as expected.

* FIXES

  * Make QuartzOptions Triggers and JobDetails public (#981)
  * Fix configuration system injection for dictionary/quartz.jobStore.misfireThreshold in DI (#983)
  * XMLSchedulingDataProcessor can cause IOException due to file locking (#993)

* IMPROVEMENTS

  * Unify MS dependency injection job factory logic and configuration (#995)
  * Improve job dispatch performance to reduce latency before hitting Execute (RAMJobStore) (#996)

## Release 3.2.0, Oct 1 2020

This is a release that focuses on restructuring some packages that warrants for a minor version number increment.

Now Quartz no longer has hard dependency on Microsoft.Data.SqlClient, you need to add that dependency to your project
if you are using Microsoft SQL Server as backing store for your project. Now requirement is in line with other providers/drivers.

There's also important fix for SQL Server where varying text parameter sizes caused query plan pollution.

* BREAKING CHANGES

    * Remove dependency on Microsoft.Data.SqlClient (#912)
    * LogContext moved from Quartz namespace to Quartz.Logging namespace (#915)
    * For Full Framework, System.Data.SqlClient is again the default provider, Microsoft.Data can be used via provider MicrosoftDataSqlClient (#916)
    * `QuartzSchedlingOptions` was renamed to `SchedulingOptions`

* FIXES

    * Revert change in 3.1: CronExpression/cron trigger throwing NotImplementedException when calculating final fire time (#905)
    * Use 2.1 as the minimum version for the .NET Platform Extensions (#923)
    * ServiceCollection.AddQuartz() should register default ITypeLoadHelper if none supplied (#924)
    * SqlServer AdoJobStore SqlParameter without text size generates pressure on server (#939)
    * DbProvider initialization logic should also read quartz.config (#951)
    * LoggingJobHistoryPlugin and LoggingTriggerHistoryPlugin names are null with IoC configuration (#926)
    * Improve options pattern to allow better custom configuration story (#955)

* NEW FEATURE

    * Introduce separate Quartz.Extensions.Hosting (#911)
    * You can now schedule job and trigger in MS DI integration with single .ScheduleJob call (#943)
    * Support adding calendars to MS DI via AddCalendar<T> (#945)

## Release 3.1.0, Jul 24 2020

This release concentrates on performance and bringing support to de facto Microsoft libraries like dependency injection and ASP.NET Core hosting.
A big change is that now SQL queries use parametrized scheduler name, which allows database server to reuse query plans and use indexes more optimally.
This will help especially clusters which have large number of nodes. The SQL server indexes were also revisited and their amount reduced by using smarter covering indexes.

There is also a very important bug fix present for lock handling on retries. There was a possibility for a deadlock in database lock handling in some situations.

* BREAKING CHANGES

    * minimum supported .NET Full Framework is now 4.6.1
    * changed SQL commands format in `Quartz.Impl.AdoJobStore.JobStoreSupport` (see also [#818](https://github.com/quartznet/quartznet/pull/818)). Affected are only schedulers that use customized configurations of SQL commands in `Quartz.Impl.AdoJobStore.JobStoreSupport`, e.g. `SelectWithLockSQL`. Migration example:
```xml
<!-- Quartz <=3.0.7 -->
<item key="quartz.jobStore.selectWithLockSQL">SELECT * FROM {0}LOCKS WITH (UPDLOCK,ROWLOCK) WHERE SCHED_NAME = {1} AND LOCK_NAME = @lockName</item>
<!-- Quartz >=3.1.0 -->
<item key="quartz.jobStore.selectWithLockSQL">SELECT * FROM {0}LOCKS WITH (UPDLOCK,ROWLOCK) WHERE SCHED_NAME = @schedulerName AND LOCK_NAME = @lockName</item>
```

* NEW FEATURE

    * Microsoft DI integration via package Quartz.Extensions.DependencyInjection (also allows bridging to Microsoft Logging)
    * DI configuration now supports adding scheduler, job and trigger listeners (#877)
    * DI configuration now processes appsettings.json section "Quartz" looking for key value pairs (#877)
    * Add diagnostics source and OpenTelemetry support (#901)
    * Use Microsoft.Data.SqlClient as SQL Server connection library (#839)
    * ASP.NET Core / Hosting integration and health checks via revisited NuGet package Quartz.AspNetCore (thank you zlzforever for contributing the work)
    * Introduced a config parameter `ClusterCheckinMisfireThreshold` (#692)
    * Giving meaningful names to examples folders (#701)
    * Added search patterns/sub directory search to directory scanner job (#411, #708)
    * Fluent interface for scheduler configuration (#791)
    * Support every nth week in cron expression (#790)
    * Enable SQLite job store provider for NetStandard (#802)
    * Add configurable params for StdRowLockSemaphore for Failure obtaining db row lock
    * SchedName added to queries as sql parameter (#818)
    * Server, example and test projects upgraded to user .NET Core 3.1
    * Nullable reference type annotations have been enabled
    * Symbols are now provided as a separate NuGet symbol package (snupkg)
    * SQL Server indexes have been fine-tuned, redundancies were removed and you can follow the current scripts to update to latest version of them
    * Upgrade MySqlConnector to 1.0 (namespace has changed) (#890)
    * Support Microsoft.Extensions.Logging.Abstractions (#756)
    * Support Microsoft.Data.SQLite with full framework (#893)
    * Support custom calendar JSON serialization (#697)
    * DI configuration now supports adding scheduler, job and trigger listeners (#877)
    * DI configuration now processes appsettings.json section "Quartz" looking for key value pairs (#877)
    * Use Microsoft.Data.SqlClient as SQL Server connection library (#839)

* FIXES

    * Allow binary serialization for DirectoryScanJob data (#658)
    * LibLog - Fixed NLog + Log4net callsite. Added support for NLog structured logging. Optimized Log4net-logger (#705)
    * Upgrade LibLog to latest version (#749)
    * RAMJobStore performance improvements (#718, #719, #720)
    * General performance improvements (#725, #723, #727)
    * GetTimeBefore() and GetFinalFireTime() should throw NotImplementedException instead of returning null (#731)
    * Switch to official TimeZoneConverter NuGet package (#739)
    * Remove invalid TimeSpanParseRule.Days (#782)
    * Update tables_sqlServer.sql to follow current SQL syntax and structures (#787)
    * Fix China Standard Time mapping in TimeZoneUtil.cs (#765)
    * Release BLOCKED triggers in ReleaseAcquiredTrigger (#741 #800)
    * DailyTimeIntervalTrigger failed to set endingDailyAfterCount = 1
    * CronTrigger: cover all valid misfire policies, and provide a sensible default and logging when seeing an invalid one
    * Remove internal dependencies from examples (#742)
    * Properly assign MaxConcurrency in CreateVolatileScheduler (#726)
    * Fix potential scheduler deadlock caused by changed lock request id inside ExecuteInNonManagedTXLock (#794)
    * Ensure NuGet.exe is part of produced zip to ensure build works (#881)
    * JobDataMap with enum values persisted as JSON can now be set back to job members via PropertySettingJobFactory (#770)
    * Ensure GetScheduleBuilder for triggers respects IgnoreMisfirePolicy (#750)
    * Remove cron expression validation from XML schema and rely on CronExpression itself (#729)


## Release 3.1.0 beta 3, Jul 21 2020

* NEW FEATURES

    * Upgrade MySqlConnector to 1.0 (namespace has changed) (#890)
    * Support Microsoft.Extensions.Logging.Abstractions (#756)
    * Support Microsoft.Data.SQLite with full framework (#893)
    * Support custom calendar JSON serialization (#697)

* FIXES

    * Remove internal dependencies from examples (#742)
    * Properly assign MaxConcurrency in CreateVolatileScheduler (#726)


## Release 3.1.0 beta 2, Jul 14 2020

On the road for 3.1 release, also note beta 1 remarks.

* NEW FEATURES

    * DI configuration now supports adding scheduler, job and trigger listeners (#877)
    * DI configuration now processes appsettings.json section "Quartz" looking for key value pairs (#877)
    * Use Microsoft.Data.SqlClient as SQL Server connection library (#839)

* FIXES

    * Fix potential scheduler deadlock caused by changed lock request id inside ExecuteInNonManagedTXLock (#794)
    * Ensure NuGet.exe is part of produced zip to ensure build works (#881)
    * JobDataMap with enum values persisted as JSON can now be set back to job members via PropertySettingJobFactory (#770)
    * Ensure GetScheduleBuilder for triggers respects IgnoreMisfirePolicy (#750)
    * Remove cron expression validation from XML schema and rely on CronExpression itself (#729)

## Release 3.1.0 beta 1, Jul 8 2020

This release concentrates on performance and bringing support to de facto Microsoft libraries like dependency injection and ASP.NET Core hosting.
A big change is that now SQL queries use parametrized scheduler name, which allows database server to reuse query plans and use indexes more optimally.
This will help especially clusters which have large number of nodes. The SQL server indexes were also revisited and their amount reduced by using smarter covering indexes.

There are also some minor bug fixes present.

* BREAKING CHANGES

     * minimum supported .NET Full Framework is now 4.6.1

* NEW FEATURE

    * Microsoft DI integration via package Quartz.Extensions.DependencyInjection (also allows briding to Microsoft Logging)
    * ASP.NET Core / Hosting integration and health checks via revisited NuGet package Quartz.AspNetCore (thank you zlzforever for contributing the work)
    * Introduced a config parameter `ClusterCheckinMisfireThreshold` (#692)
    * Giving meaningful names to examples folders (#701)
    * Added search patterns/sub directory search to directoty scanner job (#411, #708)
    * Fluent interface for scheduler configuration (#791)
    * Support every nth week in cron expression (#790)
    * Enable SQLite job store provider for NetStandard (#802)
    * Add configurable params for StdRowLockSemaphore for Failure obtaining db row lock
    * SchedName added to queries as sql paramteter (#818)
    * Server, example and test projects upgraded to user .NET Core 3.1
    * Nullable reference type annotations have been enabled
    * Symbols are now provided as a separate NuGet symbol package (snupkg)
    * SQL Server indexes have been fine-tuned, redudancies were removed and you can follow the current scripts to update to latest version of them

* FIXES

    * Allow binary serialization for DirectoryScanJob data (#658)
    * LibLog - Fixed NLog + Log4net callsite. Added support for NLog structured logging. Optimized Log4net-logger (#705)
    * Upgrade LibLog to latest version (#749)
    * RAMJobStore performance improvements (#718, #719, #720)
    * General performance improvements (#725, #723, #727)
    * GetTimeBefore() and GetFinalFireTime() should throw NotImplementedException instead of returning null (#731)
    * Switch to official TimeZoneConverter NuGet package (#739)
    * Remove invalid TimeSpanParseRule.Days (#782)
    * Update tables_sqlServer.sql to follow current SQL syntax and structures (#787)
    * Fix China Standard Time mapping in TimeZoneUtil.cs (#765)
    * Release BLOCKED triggers in ReleaseAcquiredTrigger (#741 #800)
    * DailyTimeIntervalTrigger failed to set endingDailyAfterCount = 1
    * CronTrigger: cover all valid misfire policies, and provide a sensible default and logging when seeing an invalid one


## Release 3.0.7, Oct 7 2018

This release brings .NET Core 2.1 version of example server and adds new plugin
Quartz.Plugins.TimeZoneConverter which allows usage of TimeZoneConverter library
(https://github.com/mj1856/TimeZoneConverter) to get consistent time zone id parsing between
Linux and Windows.

There are also some bug fixes related to AdoJobStore.

* NEW FEATURE

    * Add .NET Core 2.1 version of example server (#682)
    * New plugin Quartz.Plugins.TimeZoneConverter which allows usage of TimeZoneConverter library (#647)

* FIXES

    * Added transient codes from EF into new JobStore (#681)
    * Parametrized queries produced by ReplaceTablePrefix should be cached (#651)
    * Use TypeNameHandling.Auto for JsonObjectSerializer (#621)
    * Fix a race condition that could cause duplicate trigger firings (#690)
    * ISchedulerListener.JobScheduled not called when scheduling multiple jobs (ScheduleJobs) (#678)


## Release 3.0.6, Jul 6 2018

This release fixes a nasty bug with JSON calendar database serialization and .NET Core SQL Server client libraries
have been updated to mitigiate possible hangs when connection drops occur.

Also some other minor bugs have been also addressed.

You should now be able to debug into Quartz.NET sources with added SourceLink support.

* NEW FEATURE

    * Add SourceLink support (#642)
    * Make JobInterrupted method virtual in class SchedulerListenerSupport (#631)

* FIXES

    * Trigger group can be left as paused when all triggers have been removed (#641)
    * PlatformNotSupportedException on RaspberryPi (Windows IoT) (#630)
    * JSON serialisation returning defaults for derived calendar settings (#634)
    * .NET Core version not able to recover from DB connection drops (#637)


## Release 3.0.5, May 27 2018

This release fixes couple bugs and adds support for .NET Core version of Oracle's managed data access library.

* NEW FEATURE

    * Support Oracle.ManagedDataAccess.Core (#609)

* FIXES

    * trigger loop encountered using DailyTimeIntervalTrigger across DST start boundary (#610)
    * Missing ConfigureAwait(false) in some parts of code (#618)


## Release 3.0.4, Mar 4 2018

This release fixes a nasty memory leak caused by QuartzSchedulerThread sharing
its CancellationTokenSource with calls it makes. Everyone using 3.x is advised to upgrade.

* FIXES

    * Memory leak caused by CancellationTokenSource sharing (#600)
    * tables_oracle.sql should use NUMBER(19) instead of NUMBER(13) for long properties (#598)


## Release 3.0.3, Feb 24 2018

* FIXES

    * XML scheduling no longer requires write access to source XML file (#591)
    * Improve listener error handling (#589)
    * SQL command parameters are not defined in 'IsTriggerStillPresent' method (#579)
    * Source distribution couldn't be built with build.cmd/.sh when no .git directory present (#596)
    * Currently executing jobs cannot be retrieved via remoting (#580)


## Release 3.0.2, Jan 25 2018

This is a minor fix release that fixes single issue that still prevented full usage of remoting.

* FIXES

    * Mark HashSet as serializable (#576)


## Release 3.0.1, Jan 21 2018

This is a bug fix release that fixes cron expression parsing bug and reverts IRemotableQuartzScheduler
interface back to its original form without Tasks and CancellationTokens - so that's it's actually usable
through .NET Remoting infrastructure. Now zip packing is also back and includes Quartz.Server.

* FIXES

    * Create zip package as part of release, including Quartz.Server (#572)
    * A specific CronExpression fails with "Input string was not in a correct format." (#568)
    * Cannot use remoting due to Task and CancellationToken signatures (#571)


## Release 3.0, Dec 30 2017

See 3.x releases for full list.

* NEW FEATURE

    * Random number generation now uses RNGCryptoServiceProvider to silence some code analysis warnings (#551)

## Release 3.0 beta 1, Oct 8 2017

* NEW FEATURE

    * returned .NET Framework 4.5.2 compatibility to better support library consumers like NServiceBus and MassTransit
    * netstandard 2.0 is now minimum for .NET Core
    * support for Microsoft.Data.Sqlite via provider name SQLite-Microsoft, the old provider SQLite also still works
    * Firebird is supported in .NET Core
    * Added preliminary support for SQL Server Memory-Optimized tables and Quartz.Impl.AdoJobStore.UpdateLockRowSemaphoreMOT

* BREAKING CHANGES

    * Jobs and plugins are now in a separate assemblies/NuGet packages Quartz.Jobs and Quartz.Plugins
    * ADO.NET provider names have been simplified, the provider names are without version, e.g. SqlServer-20 => SqlServer


## Release 3.0 alpha 3, Jul 30 2017

* NEW FEATURE

    * support for .NET Standard 2.0 preview (#486)
    * support for MySQL on .NET Standard via provider 'MySql' (#493)
    * change SQL database IMAGE types to VARBINARY - requires migration schema_26_to_30.sql
    * add ISchedulerListener.JobInterrupted(JobKey jobKey, CancellationToken cancellationToken) (#467)

* FIXES

    * fix PosgreSQL db provider configuration for .NET Core (#449)
    * CancellationToken is now supported in async methods (#444)
    * fix regression with XML schema validation

* BREAKING CHANGES

    * possibly breaking, cron expression validation is now stricter (#315 #485)
    * .NET 4.6 required instead of old 4.5
    * API methods have been revisited to mainly use IReadOnlyCollection<T>, this hides both HashSet<T>s and List<T>s
    * LibLog has been hidden as internal (ILog etc), like it was originally intended to be

## Release 3.0 alpha 2, Aug 24 2016

* FIXES

    * fix scheduler signaling not working with AdoJobStore due to thread local storage
    * thread local state removed altogether
    * quartz.serializer.type was required even though non-serializing RAMJobStore was in use
    * JSON serialization incorrectly called serialization callbacks

* BREAKING CHANGES

    * IStatefulJob was removed, been obsolete since 2.x
    * ISemaphore interface now passes Guid requestorId that defines lock owner instead of implicit thread name


## Release 3.0 alpha 1, Aug 16 2016

* NEW FEATURE

    * Task based jobs with async/await support
    * Support .NET Core

* BREAKING CHANGES

    * .NET 4.5/netstandard1.3 required
    * Scheduler methods have been changed to be Task based, remember to await them
    * IJob interface now returns a task
    * Some IList properties have been changed to IReadOnlyList to properly reflect intent
    * SQL Server CE support has been dropped
    * DailyCalendar uses now datetimes for excluded dates and has ISet interface to access them
    * IObjectSerializer has new method, void Initialize(), that has to be implemented

* KNOWN ISSUES

    * Issues with time zone ids between Windows and Linux, they use different ids for the same zone
    * No remoting support


## Release 2.6, Jul 30, 2017

**Addition of column required to database**

* This release fixes a long standing issue, DailyTimeIntervalTrigger's time zone is now finally persisted to database
* This requires running schema_25_to_26_upgrade.sql to add new column to QRTZ_SIMPROP_TRIGGERS table
* https://github.com/quartznet/quartznet/blob/2.x/database/schema_25_to_26_upgrade.sql

A slight performance boost can also be unlocked when using PostgreSQL by switching PostgreSqlDelegate.

* NEW FEATURE

    * Add support for eager validation of job scheduling XML file on plugin start (#492)
    * Add support for extra custom time zone resolver function in TimeZoneUtil (#290)

* FIXES

    * CalendarIntervalTrigger's first fire time doesn't consider time zone (#505)
    * QRTZ_FIRED_TRIGGERS.ENTRY_ID column length too small (#474)
    * Decrease log level for message when host name is too long (#471)
    * Quartz should not log transient faults related to azure db connection as errors (#470)
    * RemotingSchedulerExporter can't create remoting channel on Mono (#464)
    * Regression in 2.5, TriggerBuilder.UsingJobData(JobDataMap newJobDataMap) should ovewrite existing (#460)
    * QuartzScheduler.Clear does not clear QRTZ_FIRED_TRIGGERS table (#437)
    * No wait time between db connection failures with AcquireNextTriggers (#428)
    * DailyTimeIntervalTriggerImpl prevents altering EndTimeOfDay to a later time of day (#382)
    * Quartz.CronExpression.IsSatisfiedBy claims to ignore milliseconds but doesn't (#349)
    * Add back PostgreSqlDelegate to support database LIMIT in trigger acquiring (#318)
    * Bug in XSD schema: cannot set <misfire-instruction>IgnoreMisfirePolicy</misfire-instruction> (#280)
    * Quartz.Xml.XMLSchedulingDataProcessor uses GetType() instead of typeof(Quartz.Xml.XMLSchedulingDataProcessor) (#277)
    * With SQLite default isolation level should be set to serializable (#242)
    * DailyTimeIntervalTrigger's time zone is not persisted into database (#136)
    * XMLSchedulingDataProcessorPlugin incompatible with StdAdoDelegate when useProperties=true (#44)
    * Trigger loop encountered using DailyTimeIntervalTrigger across DST start boundary (#332)


## Release 2.5, Feb 18, 2017

This release contains mainly bug fixes but because there's a behavioural change in
DST handling (for the better) that warrants for a minor version number increment.

See https://github.com/quartznet/quartznet/pull/317 for details.

* FIXES

    * Jobs get stuck in the Blocked state after the DB connection is lost in NotifyJobListenersComplete (#282)
    * Oracle rownum based queries can work wrong after Oracle SQL tuning task has ran (#413)
    * Handle DST better (#317)
    * RAMJobStore fails to resume when paused without triggers (#433)
    * CronExpression doesn't properly check the range when an "/interval" is specified (#432)
    * Fix JobDataMap dirty flag when constructing from existing map (#431)
    * Store triggers by job in RAMJobStore for better performance (#430)
    * Create WorkerThread in virtual method (#426)
    * SqlSelectJobForTrigger is not using primary key join and causes index scan (#407)


## Release 2.4.1, Aug 24, 2016

* FIXES

    * Fix Common.Logging version 3.3.1 to be a true binary reference instead of just NuGet dependency


## Release 2.4, Aug 18, 2016

Only minor changes and fixes but dependency updates which merits for a minor version upgrade.
This version changes HolidayCalendar serialization not to depend on C5 library.
Quartz v3 can only handle HolidayCalendars serialized in this 2.4 binary format.

* NEW FEATURE

    * Add SQL limit support for MySQLDelegate
    * Removed dbFailureRetryInterval since it is no longer used
    * Update Common Logging to v3.3.1

* FIXES

    * Batch acquisition can cause early firing of triggers
    * Should not rely on C5.TreeSet<T> on HolidayCalendar field serialization

## Release 2.3.3, Jul 9, 2015


This is a minor release containing mostly bug fixes.

* NEW FEATURE

    * Support generic job types with AdoJobStore

* FIXES

    * AdoJobStore doesn't notify about removing orphaned job
    * Store null JobData in JobDetails if it's empty
    * Documentation error in SimpleTriggerImpl UpdateAfterMisfire
    * Ensure IDriverDelegate members in StdAdoDelegate are virtual

## Release 2.3.2, Mar 30, 2015

This is a minor release containing mostly bug fixes.

* NEW FEATURE

    * Add mysql 6.9.5 provider support

* FIXES

    * Avoid unnecessary object allocations in CronExpression
    * CalendarIntervalTrigger and DailyTimeIntervalTrigger produce incorrect schedule builders
    * Incorrect multiplication factor in DailyTimeIntervalScheduleBuilder.EndingDailyAfterCount()
    * AnnualCalendar SetDayExcluded does not update internal data structures if base calendar excludes date
    * Ensure IDriverDelegate members in StdAdoDelegate are virtual
    * Several XML documentation spelling error fixes

## Release 2.3.1, Jan 15, 2015

This is a bug fix release with upgraded Common.Logging dependency, also problems running
under .NET 4.0 should now be finally fixed.

* NEW FEATURE

    * Upgrade to Common.Logging 3.0.0

* FIXES

    * JobDetailImpl members should be virtual
    * Triggers do not transition to error state in AdoJobStore when job retrieval fails during trigger acquisition
    * Quartz.Server.exe.config refers to wrong Common.Logging.Log4Net assembly
    * Incorrect NextFireTime when 'schedule-trigger-relative-to-replaced-trigger' = 'true'
    * Could not load type 'System.Runtime.CompilerServices.ExtensionAttribute' from assembly mscorlib
    * TriggerBuilder.UsingJobData(JobDataMap newJobDataMap) should ovewrite existing data

## Release 2.3, Nov 8, 2014

This is a bug fix release with some changes that warrant a minor version increment.

* NEW FEATURE

    * Upgrade to Common.Logging 2.3.1
    * Add ability to check if calendar exists in job store
    * Add FirebirdDelegate and update Firebird driver

* FIXES

    * DailyTimeIntervalTriggerImpl fires twice during daylight saving time day
    * No wait time between db connection failures with AcquireNextTriggers causes excessive logging
    * Configure the quartz server in the `<quartz>` section fails
    * CronExpression ctor incorrectly uses the non-uppercased string
    * Triggers fired milliseconds too early
    * Loading of Quartz 4.0 DLL fails on systems with no .NET 4.5 installed

## Release 2.2.4, Jul 27, 2014

This is a bug fix release addressing some minor issues.

* FIXES

    * Cannot register trigger persistence delegates with assembly qualified names
    * Set example server's current directory to the one where server.exe is
    * Fix TimeZoneInfo.GetUtcOffset(DateTimeOffset dateTimeOffset) not implemented in Mono
    * Gracefully handle mixed useProperties usage when reading from DB when useproperties value has changed
    * FindSystemTimeZoneById should work with both 'Coordinated Universal Time' and 'UTC'
    * Latest release (2.3) didn't include Dbprovider constant string in StdSchedulerFactory - running examples fails


## Release 2.2.3, Mar 30, 2014

This is a bug fix release which has some critical fixes, especially for CalendarIntevalTrigger
future date calculation and trigger's next fires not being processed in a timely fashion when AdoJobStore is used
with DisallowConcurrentExecutionAttribute and trigger has short repeat interval.

* FIXES

    * StdAdoConstants.SqlSelectSchedulerStates does not filter on the SCHED_NAME column
    * CalendarIntervalTrigger produces incorrect schedule
    * Trigger completion signaling from AdoJobStore does not work properly when DisallowConcurrentExecution is set

* NEW FEATURE

    * IDisposable jobs should be disposed after execution
    * Support for defining DbMetadata via App.config's quartz section


## Release 2.2.2, Feb 9, 2014

This is a minor release fixing couple of minor bugs

* FIXES

    * long properties incorrectly read as int in SimplePropertiesTriggerPersistenceDelegateSupport
    * RecoveringTriggerKey in JobExecutionContext has group and name wrong way around
    * Make SQL Server table create script Azure SQL compliant
    * Add missing clustered index for QRTZ_BLOB_TRIGGERS table

**You need to manually add this to existing installation (tables created with old script),   see ALTER TABLE [dbo].QRTZ_BLOB_TRIGGERS WITH NOCHECK ADD... in script**


## Release 2.2.1, Nov 24, 2013

This is a minor release containing mostly bug fixes.

* NEW FEATURES
    * GroupMatcher<T>.AnyGroup() support
    * Add network credential and SMTP port definition support to SendMailJob

* FIXES
    * SchedulerException constructor unnecessarily uses Exception.ToString as message
    * Thread name prefix for thread pool is not set
    * Triggers should not be excluded based on the fire time of the first selected trigger
    * Quarts server does not properly log possible exception when starting the service
    * DailyTimeIntervalTrigger GetFireTimeAfter produces incorrect result when date is in the past
    * batchTriggerAcquisitionMaxCount acquires one trigger unless batchTriggerAcquisitionFireAheadTimeWindow is also set
    * Oracle ODP Managed provider should set BindByName to true for OracleCommands

## Release 2.2, Sept 9, 2013

This release contains important bug fixes, new functionality and minor breaking changes.

* UPGRADING

    * this script adds a new column SCHED_TIME to table QRTZ_FIRED_TRIGGERS
    * file contains the alter command for SQL Server and other database samples in comments

**Please examine and run the database\schema_20_to_22_upgrade.sql script if you are using AdoJobStore**

* BREAKING CHANGES
    * database schema needs upgrade
    * add SchedulerStarting() method to ISchedulerListener interface
    * make the scheduler's TypeLoadHelper available to plugins when they are initialized
    * dbFailureRetryInterval parameter was removed from DirectSchedulerFactory APIs

* NEW FEATURES
    * ability to override worker thread names (when using SimpleThreadPool)
    * add new IScheduler method: ScheduleJob(IJobDetail job, ISet trigger) to schedule multiple triggers for a job all at once
    * allow 'triggerless' initial storing of non-durable jobs.
    * improvements for job recovery information
    * package job_scheduling_data_2_0.xsd to nuget package's content folder
    * allow scheduler exported with remoting to be used from local machine only
    * support for Oracle managed ODP driver

* FIXES
    * job ending with exception and trigger not going to fire again, trigger is incorrectly not removed from job store
    * XML schema supports multiple schedule elements but processor does not
    * DailyTimeIntervalTriggerPersistenceDelegate does not handle empty time interval properly
    * DailyTimeIntervalScheduleBuilder.EndingDailyAfterCount(...) doesn't pass validation
    * trace throwing exception
    * bug in QuartzSchedulerThread.GetRandomizedIdleWaitTime()
    * can't delete or replace job without the referenced class

* MISC

    * Performance improvements, including improvements to some select statements in AdoJobStore


Thanks to our vibrant community actively helping on mailing list and reportings issues and creating pull requests.


## Release 2.1.2, Jan 13, 2013

This is a maintenance release that fixes .NET 4.5 requirement for 4.0 DLLs caused by ilmerge process

## Release 2.1.1, Jan 5, 2013

This is a maintenance release that adds strong naming back to Quartz.NET assemblies.


## Release 2.1, Dec 31, 2012

This release contains important bug fixes, new functionality and minor breaking changes.
Custom IJobFactory implementations now need to implement new method void ReturnJob(IJob job) for container managed cleanup.
NthIncludedDayTrigger was removed as it was accidentally left behind even though being legacy and replaced by DailyTimeIntervalTrigger.

* NEW FEATURES

    * TimeZone support for calendars / Andrew Smith
    * Allow scheduling relative to replaced trigger with XML configuration
    * Add method to IJobFactory to destroy a job instance created by the factory breaking / minor breaking, added new required method
    * Internalize C5 dependency
    * Support for Oracle ODP 11.2 Release 4
    * Upgrade SQLite dependency to version 1.0.83
    * Upgrade to Common.Logging 2.1.2

* FIXES

    * Scheduled Shutdown blocked even if waitForJobsToComplete is false
    * DailyTimeIntervalTriggerImpl should be serializable
    * InstanceID = "AUTO" may cause "String or binary data would be truncated" error on qrtz_fired_triggers.entry_id
    * PlugInExample doesn't execute any jobs
    * Recovering triggers have empty/incorrect JobDataMap
    * Make Quartz.NET work under medium trust when running .NET 3.5
    * tables_oracle.sql uses deprecated VARCHAR type
    * Improve error reporting for database connection failure
    * Scheduler Shutdown Freezes when There are Jobs Still Running
    * Use System.Version instead of FileVersionInfo to retive current Quartz version
    * DailyTimeIntervalTriggerImpl Validate broken

* BREAKING CHANGES

    * Remove NthIncludedDayTrigger that was supposed to be removed in 2.0 breaking
    * Remove Visual Studio 2008 solutions and projects breaking
    * Add support for DateTimeOffset and TimeSpan to JobDataMap / minor breaking - cleanup of API


Special thanks to Andrew Smith for working hard on TimeZone support.
Credits go also to our vibrant community actively helping on mailing list and reportings issues and creating pull requests.

# Release 2.0.1, Apr 22, 2012

This release contains some bug fixes.

* FIXES

    * Oracle database support broken
    * Incorrect .NET 4.5 requirement in 4.0 build (only NuGet affected)
    * XML validation fails as schema not embedded (only NuGet affected)
    * ObjectUtils.SetPropertyValue fails with explicitly implemented interface members

## Release 2.0, Apr 9, 2012

This release contains some bug fixes.

* POSSIBLE BREAKING CHANGES (since 2.0 beta 2)

    * DateBuilder now uses the UTC offset that is active for the date constructed (earlier it was always offset of DateOffset.Now)

* FIXES

    * Possible bug with triggers left/stuck in ACQUIRED state (QTZ-179)
    * More checks to CalendarIntevalTrigger for daylight savings


## Release 2.0 beta 2, Dec 31, 2011

This release contains some bug fixes and some compile time breaking changes.

* BREAKING CHANGES (since 2.0 beta 1)

    * DateBuilder TranslateTime method was removed as it's better done with TimeZoneInfo's ConvertTime
    * DateBuilder.IntervalUnit enumeration was replaced by usage of generic IntervalUnit enumeration in root namespace
    * DateBuilder now creates all dates in local time zone by default
    * ICommandAccessor was renamed IDbAccessor, this interface should normally not be used from client code
    * DailyTimeIntervalTrigger was changed to correctly assume start and end times are UTC times (calculation is based on local time)
    * DailyTimeIntervalTrigger properties StartTimeOfDayUtc and EndTimeOfDayUtc were renamed to StartTimeOfDay and EndTimeOfDay as they are local times without UTC notion

* FIXES

    * DailyTimeIntervalTriggerPersistenceDelegate does not store weekdays in correct format
    * DisallowConcurrentExecution decorated triggers not being updated after TriggerCompleted
    * DailyTimeIntervalTrigger does not work as expected

* Improvement
    * ADO.NET job store: force UTC ticks when storing datetimes

# Release 2.0 beta 1, October 2, 2011

* BREAKING CHANGES

    * .NET 1.1 and 2.0 support is dropped
    * Quartz.NET now needs .NET version 3.5 SP1 or later to run due to use of new language features and classes
    * Many public interface methods have changed from returning arrays to generic IList or ISet interfaces
    * TriggerBuilder implementations and JobBuilder should now be used to create different job and trigger definitions
    * Introduced IJobDetail, IContrigger, ISimpleTrigger, ICalendarIntervalTrigger have far less members and especially mutators
    * When C5 collections were introduced as set-based implementation provider, ISet and ISortedSet interfaces were narrowed (IList inheritance removed)
    * string triggerName, string triggerGroup are now encapsulated in TriggerKey (has the same fields)
    * string jobName, string jobGroup are now encapsulated in JobKey (has the same fields)
    * JobInitializationPlugin is now deprecated in favor of XMLSchedulingDataProcessorPlugin, JobInitializationPlugin no longer included
    * Microsoft's Oracle drivers are no longer supported, use 10g or 11g ODP.NET drivers
    * Database schema has changed, you need to convert your 1.x schema to new version, sample migration script available in database folder

* OTHER NOTABLE CHANGES

    * XMLSchedulingDataProcessorPlugin uses new XML format that allows more control over triggers but no support for calendars
    * There are extension methods for the new trigger builder that allow you to set trigger specifics
    * Client Profile is now supported and there are separate DLLs for client profile
    * PropertySettingJobFactory is now the default JobFactory

# Release 1.0.3, Aug 22, 2010

* Bug

    * [QRTZNET-190] - Most outstanding misfired trigger should be first to be updated
    * [QRTZNET-192] - Trigger Listeners Having Misfire Handler Called Twice
    * [QRTZNET-194] - Select Trigger method for Cron Triggers does not set the Priority property
    * [QRTZNET-217] - Triggers fail to obey millisecond precision when setting start time

* Improvement

    * [QRTZNET-219] - PostgreSQL database scripts should create database indexes
    * [QRTZNET-220] - CronExpression should check that the 'L' field's value is between 1 and 7



## Release 1.0.2, Dec 12, 2009

* Bug
    * [QRTZNET-163] - quartz.jobStore.clustered mentioned wrongly as quartz.jobStore.isClustered
    * [QRTZNET-169] - OracleDelegate uses invalid keyword 'rowcount' instead of correct 'rownum'
    * [QRTZNET-174] - Rollback on a closed connection in JobStoreSupport.DoCheckin
    * [QRTZNET-175] - Race condition in SimpleThreadPool
    * [QRTZNET-176] - LW in cron-expression (last weekday of month) flagged as invalid by job_scheduling_data.xsd
    * [QRTZNET-180] - Possible hang in SimpleThreadPool.Shutdown(true)
    * [QRTZNET-181] - Quartz Fills up log file when database connection goes down
    * [QRTZNET-188] - AdoJobStore cannot delete jobs if job type cannot be loaded

* Improvement
    * [QRTZNET-165] - in QuartzServer the 'scheduler' field is defined as private - that makes inheriting the QuartzServer class problematic
    * [QRTZNET-173] - Convenience Constructors for JobDetail and Trigger Classes

* Task
    * [QRTZNET-178] - Please keep the generic job.xml in the download package for all tags that we can use



## Release 1.0.1, May 16, 2009

* Bug
    * [QRTZNET-145] - NthIncludedDayTrigger.ComputeFirstFireTimeUtc fails if no start time given
    * [QRTZNET-149] - CronExpression.GetTimeAfter(DateTime afterTimeUtc) does not account for day increment over days in month
    * [QRTZNET-150] - LoggingTriggerHistoryPlugin.TriggerMisfired writes incorrect message
    * [QRTZNET-151] - XML configuration fails with jobs that have no triggers
    * [QRTZNET-152] - Nearest weekday 'W' expression does not work correctly in CronTrigger
    * [QRTZNET-153] - JobInitializationPlugin overwrite-existing-jobs parameter ignored
    * [QRTZNET-155] - JobSchedulingDataProcessor does not set Trigger description
    * [QRTZNET-156] - JobDetail.RemoveJobListener throws InvalidCastException
    * [QRTZNET-157] - JobDetail.Equals(object) is implemented wrong
    * [QRTZNET-159] - Records in QRTZ_TRIGGER_LISTENERS table are deleted when trigger is paused and resumed
    * [QRTZNET-160] - AcquireNextTrigger executing on managed Tx Connection when using JobStoreCMT and configured not to acquire triggers within lock
    * [QRTZNET-161] - TimeZoneInfo's StandardName is incorrectly saved to database instead of Id

* Improvement
    * [QRTZNET-142] - Xml Configuration support for Trigger Listeners
    * [QRTZNET-144] - Locking around AcquireNextTrigger no longer necessary for AdoJobStore
    * [QRTZNET-146] - Introduce result limiting ADO.NET delegates for better performance
    * [QRTZNET-154] - Support for setting the working directory in NativeJob

* New Feature
    * [QRTZNET-148] - Add SQL Server Compact Edition support for AdoJobStore

## Release 1.0, Nov 6, 2008

* Bug
    * [QRTZNET-125] - TimeZones are not handled correctly when reading XML job configuration
    * [QRTZNET-127] - CronExpression does not handle custom TimeZone correctly in GetTimeAfter in 2.0 build
    * [QRTZNET-128] - RemoteScheduler does not delegate IsJobGroupPaused and IsTriggerGroupPaused to remote scheduler
    * [QRTZNET-131] - NthIncludeDayTrigger doesn't utilize custom TimeZone correctly
    * [QRTZNET-132] - NullReferenceException when computing next fire time for misfired triggers
    * [QRTZNET-133] - SimpleThreadPool.CreateWorkerThreads does not respect threadCount parameter value
    * [QRTZNET-136] - NativeJob is broken

* Improvement
    * [QRTZNET-126] - Apply AllowPartiallyTrustedCallersAttribute to DLL

* New Feature
    * [QRTZNET-129] - Support for connectionStrings section in App.config
    * [QRTZNET-134] - New pool implementation -- ZeroSizeThreadPool
    * [QRTZNET-135] - Support job-data-map for triggers in XML


## Release 1.0 RC 3, Sep 6, 2008

* Bug
    * [QRTZNET-91] - JobSchedulingDataProcessor does not handle job listeners from XML correctly
    * [QRTZNET-115] - AnnualCalendar isDayExcluded doesn't take the basecalendar into account
    * [QRTZNET-116] - Error saving recovery trigger during cluster recovery for volatile jobs
    * [QRTZNET-117] - CronTrigger may return a firing time not included in the calender
    * [QRTZNET-118] - TimeZone setting lost when CronExpressionString is set
    * [QRTZNET-119] - Port Java Quartz's threading fixes that help with multi-core machines
    * [QRTZNET-121] - Remoting Scheduler - re-start scheduler throws remoting binding error.
    * [QRTZNET-122] - Triggering a job remotely often does not fire the job
    * [QRTZNET-123] - QuartzSchedulerThread Log property is null.
    * [QRTZNET-124] - SendMailJob throws a NullReferenceException

* Task
    * [QRTZNET-113] - Include server source in distribution

## Release 1.0 RC 2, Aug 6, 2008

* Improvement
    * [QRTZNET-114] - Express intervals and durations using TimeSpan instead of ints and longs

* Breaking changes:
    * Public API has changed with the introduction of TimeSpan usage,
      changes should show only as compile time errors and should be easily
      fixable.

## Release 1.0 RC 1, July 28, 2008

* Bug
    * [QRTZNET-91] - JobSchedulingDataProcessor does not handle job listeners from XML correctly
    * [QRTZNET-93] - AdoJobStore calendar update fails because of an already open DataReader
    * [QRTZNET-94] - Schema does not properly represent itself
    * [QRTZNET-96] - Relative path names for xml configuration are not working under ASP.NET
    * [QRTZNET-97] - QuartzSchedulerThread stops processing jobs if computer clock is advanced more than 248 days
    * [QRTZNET-99] - The query SelectNextTriggerToAcquire is incorrect for certain SQL servers
    * [QRTZNET-101] - RAMJobStore.TriggerFired() fails to return null in some cases
    * [QRTZNET-102] - NthIncludedDayTrigger shouldn't use fixed start day of week Sunday
    * [QRTZNET-103] - Deadlock in RAMJobStore
    * [QRTZNET-105] - CronExpression fails if nth weekday of month is used and expression passes year
    * [QRTZNET-107] - SimpleTrigger.ComputeFirstFireTime() method can get into infinite loop
    * [QRTZNET-110] - Scheduling change causes a paused/blocked scheduler to do unnecessary trigger release

* Improvement
    * [QRTZNET-90] - Allow XML configuration to inherit trigger's job name and job group from containing job definition
    * [QRTZNET-92] - PreviousFireTime should be change to PreviousFireTimeUtc in JobExecutionContext
    * [QRTZNET-106] - Add complete common logging libraries to distribution, offer log4net as server example
    * [QRTZNET-109] - Update database scripts default column sizes to be reasonable for more applications

* New Feature

    * [QRTZNET-40] - Quartz server for running jobs
    * [QRTZNET-98] - Introduce mechanism for delaying the start-up of the scheduler
    * [QRTZNET-108] - Support for reading configuration from properties file

* Task
    * [QRTZNET-74] - Add SQLite database script to distribution
    * [QRTZNET-76] - Update assembly version numbers to current builds in dbproviders.properties
    * [QRTZNET-100] - Make SetTimeRange() methods public in DailyCalendar
    * [QRTZNET-111] - Change quartz.properties to quartz.config for safer usage in ASP.NET applications

## Release 0.9.1, January 20, 2008

* Bug
    * [QRTZNET-68] - XML configuration example's XML does not conform to schema
    * [QRTZNET-73] - ComputeFirstFireTimeUtc fails when GetFireTimeAfter returns null
    * [QRTZNET-80] - MonthlyCalendar GetNextIncludedTime: infinite loop if included date > 7
    * [QRTZNET-81] - TriggerListenerSupport's methods should be virtual
    * [QRTZNET-83] - CronExample outputs next scheduled fire times misleadingly in UTC format
    * [QRTZNET-84] - Quartz's exceptions are not properly serializable when using remoting
    * [QRTZNET-85] - Cron expression fails when month is incorrectly incremented to 13
    * [QRTZNET-86] - StdAdoDelegate works incorrectly when Trigger is not found from database

* Improvement
    * [QRTZNET-72] - DailyCalendar's parameter names should contain Utc in them
    * [QRTZNET-75] - Remoting should initialize life time service to forever by default
    * [QRTZNET-77] - Allow TypeFilterLevel configuration for RemotingExporter and default to Full
    * [QRTZNET-79] - JobDataMap GetIntValue returns long
    * [QRTZNET-82] - Xml Job plugin doesn't need write access to schedule file



## Release 0.9, December 1, 2007

* Bug
    * [QRTZNET-45] - TriggerUtils.GetEvenMinuteDate Bug
    * [QRTZNET-48] - JobExecutionContext.ToString() Bug
    * [QRTZNET-50] - CronExpression and CronTrigger fail when date passes year boundary
    * [QRTZNET-53] - log format message strings in logging history plugin listeners
    * [QRTZNET-58] - Scheduler thread daemon information is not passed to scheduler correctly
    * [QRTZNET-65] - Bug in XmlDataProcessor end time and start time handling & filescanjob last modified date storing

* Improvement
    * [QRTZNET-36] - Add set accessor to Trigger.TriggerListenerNames
    * [QRTZNET-38] - Replace NullableDateTime with DateTime? in 2.0 build
    * [QRTZNET-41] - Add extra configuration examples as part of the distribution
    * [QRTZNET-43] - Refacftory misfire instructions outside implementation classes
    * [QRTZNET-46] - Change time handling to work internally on UTC times only
    * [QRTZNET-52] - Allow default SimpleThreadPool initialization if StdSchedulerFactory isn't fed thread pool properties
    * [QRTZNET-54] - RemoteScheduler
    * [QRTZNET-55] - Add strong naming to Quartz.NET assembly
    * [QRTZNET-57] - Better error reporting when problems with database initialization
    * [QRTZNET-62] - Better exception reporting when Quartz is unable to load database driver
    * [QRTZNET-64] - Make all Trigger members virtual
    * [QRTZNET-66] - Clean up NAnt build script

* New Feature
    * [QRTZNET-2] - XML configuration support
    * [QRTZNET-32] - Allow inherited trigger implementations to be saved as non-blobs when applicable
    * [QRTZNET-39] - Remotable scheduler support
    * [QRTZNET-40] - Quartz server console for running jobs
    * [QRTZNET-42] - Add support for building with Mono
    * [QRTZNET-67] - Support for Firebird database

* Task
    * [QRTZNET-49] - useProperties configuration does not work as expected
    * [QRTZNET-56] - Upgrade Common.Logging to 1.2.0
    * [QRTZNET-60] - Go through web documentation and check for errors

* Other:
    * Breaking changes:
      * Quartz.NET now uses internally UTC times only
      * Misfire instructions are now encapsulated in MisfirePolicy
      * Constants are CamelCase instead of ALL_UPPER_CASE

## Release 0.6, August 4, 2007

* Bug
    * [QRTZNET-18] - JobDataMap cannot convert Int32 values correctly from string representation
    * [QRTZNET-20] - CronExpression fails when minute is incremented to 60
    * [QRTZNET-21] - DailyCalendar.GetTimeRangeEndingTime reports starting time instead of ending time
    * [QRTZNET-24] - AnnualCalendar.SetDayExcluded(d, false) does not work
    * [QRTZNET-25] - StringKeyDirtyFlagMap lacks Put(string, int)
    * [QRTZNET-27] - RamJobStore.StoreTrigger incorrectly removes JobDetail when replacing trigger
    * [QRTZNET-28] - CronTrigger: Hour, Minute, and Second parameters describe an un-representable DateTime
    * [QRTZNET-29] - RAMJobStore.RemoveCalendar fails when triggers are present
    * [QRTZNET-31] - CronExpression GetTimeAfter() fails with System.ArgumentOutOfRangeException

* Improvement
    * [QRTZNET-7] - Load balanced database support
    * [QRTZNET-22] - CronExpression should all extra white space from expression
    * [QRTZNET-23] - IScheduler.SchedulerJob should throw always exception if trigger's calendar cannot be found
    * [QRTZNET-26] - Change trigger instructions and states to enum values
    * [QRTZNET-33] - Remove confusing Durability and Volatility properties from JobDetail

* New Feature
    * [QRTZNET-3] - AdoJobStore support
    * [QRTZNET-16] - Quartz default properties should be read from embedded resource inside the assembly

* Task
    * [QRTZNET-17] - CronTrigger misfire instruction constants should be made public
    * [QRTZNET-30] - Upgrade Common.Logging libraries to 1.1
    * [QRTZNET-34] - Rename Schedulder_Fields class to more describing SchedulerConstants
    * [QRTZNET-35] - Change Quartz initialization properties to use "type" instead of "class" to be more .NET like

* Other:

    * Breaking changes:

        * Quartz.NET initialization property keys now use "*.type" instead of "*.class", you need to update configuration or if you are fine with defaults you can also ditch configuration from app.config (see QRTZNET-16)
        * Trigger instructions and states are now enum values instead of old class constants
        * There's no longer properties named Durablity or Volatility, only Durable and Volatile (JobDetail & Trigger)
        * Class Scheduler_Fields was renamed to SchedulerConstants

Special thanks for Drew Burlingame for string concatenation and TreeSet performance patches
and Anton Dvinskiy for hunting down bugs, finding good places to refactor and patches
for Quartz and its tests.

## Release 0.5, June 17, 2007

* Bug
    * [QRTZNET-13] - CronExpressions don't handle time shift from 24th hour correctly
    * [QRTZNET-14] - CronExpression does not handle weekdays correctly

* Improvement
    * [QRTZNET-10] - Bring unit tests from Java side

* Task
    * [QRTZNET-1] - Merge changes between 1.5.1 and 1.5.2
    * [QRTZNET-12] - Fix API documentation, wrong usage of `<code>` tags makes MSDN doc look terrible
    * [QRTZNET-15] - Bring changes from Quartz 1.6 to .NET side

* Other:
    * Work around the code base to make it cleaner and more .NET like.

Special thanks to Radoslav Radivojevic for hunting CronExpression bugs.

## Release 0.4.1, March 24, 2007
Summary: Bug fix release

* Bugs:
    * [QRTZNET-8] - CronExpression problems

* Other:
    * Tutorial created, available on the web page.

Special thanks to Sebastian Fialka for sending Quartz.NET first patch! (QRTZNET-8)

## Release 0.4, March 4, 2007

Summary: Initial release
