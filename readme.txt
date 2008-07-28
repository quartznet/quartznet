QUARTZ JOB SCHEDULER .NET, release 1.0 RC 1, July 28 2008
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

The .NET Framework 3.5 build with support for TimeZoneInfo isn't very thorougly
tested yet. Please perform testing and report any problems you may find.


3. RELEASE INFO
----------------

This is the release candidate 1 for Quartz.NET 1.0. This release contains
the new Quartz.NET server for running jobs so that end user doesn't need to 
create the server by hand for tasks. Besides the new server this release 
incorporates small bug fixes and minor feature enhancements.

This release also contains .NET Framwork 3.5 compiled build with preliminary
support for TimeZoneInfo for more useful time zone aware CrontTrigger scheduling.

For API documentation, please refer to Quartz.NET site: 

   http://quartznet.sourceforge.net/apidoc/
