QUARTZ JOB SCHEDULER .NET, release 1.0.1, Feb 2 2009
-----------------------------------------------------------------

http://quartznet.sourceforge.net/

1. INTRODUCTION
----------------

This is the README file for Quartz.NET, .NET port of Java Quartz.

Quartz.NET is an opensource project aimed at creating a
free-for-commercial use Job Scheduler, with 'enterprise' features.

Licensed under the Apache License, Version 2.0 (the "License"); you may not 
use this file except in compliance with the License. You may obtain a copy 
of the License at 
 
    http://www.apache.org/licenses/LICENSE-2.0 

Also, to keep the legal people happy:

    This product includes software developed by the
    Apache Software Foundation (http://www.apache.org/)


2. KNOWN ISSUES
---------------

None.


3. RELEASE INFO
----------------

This is the Quartz.NET release 1.0.1. This release corresponds
to Java Quartz version 1.6.4.

This release includes bug fixes to issues found in 1.0 release
and also includes performance optimization to AdoJobStore 
when Quartz.NET is handling a lot of triggers. Quarz.NET no 
longer loads all of them but is able limit the query results
by using a custom SQL delegate. Quartz.NET issues a warning
on startup if custom delegate is not used. Available delegates
include ones for SQL Server, Oracle, SQLite, MySQL, PostgreSQL
and Firebird.

For API documentation, please refer to Quartz.NET site: 

   http://quartznet.sourceforge.net/apidoc/
