; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------------------------------------------------------------------
QZ0001  | Quartz   | Error    | CronLiteralAnalyzer, [Documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/compile-time-checks.html#qz0001-invalidcronexpression)
QZ0002  | Quartz   | Error    | JobTimeoutLiteralAnalyzer, [Documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/compile-time-checks.html#qz0002-invalidjobtimeout)
QZ0003  | Quartz   | Warning  | JobAttributesAnalyzer, [Documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/compile-time-checks.html#qz0003-persistjobdatawithoutdisallowconcurrent)
QZ0004  | Quartz   | Info     | CancellationTokenAnalyzer, [Documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/compile-time-checks.html#qz0004-cancellationtokennotobserved)
QZ1001  | Quartz   | Error    | DeclaredJobsGenerator, [Documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/declaring-jobs-with-attributes.html#qz1001-declaredjobtypenotschedulable)
QZ1002  | Quartz   | Error    | DeclaredJobsGenerator, [Documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/declaring-jobs-with-attributes.html#qz1002-duplicatedeclaredidentity)
QZ1003  | Quartz   | Error    | DeclaredJobsGenerator, [Documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/declaring-jobs-with-attributes.html#qz1003-crontriggerwithoutquartzjob)
