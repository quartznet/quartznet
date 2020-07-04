# QUARTZ.NET CHANGELOG

[http://www.quartz-scheduler.net](http://www.quartz-scheduler.net)

## Release 3.1.0, xx xx 2020

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
