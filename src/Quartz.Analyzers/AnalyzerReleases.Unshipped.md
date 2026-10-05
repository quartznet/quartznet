; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------------------------------------------------------------------
QZ0005  | Quartz   | Error    | SimpleTriggerLiteralAnalyzer, [Documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/compile-time-checks.html#qz0005-invalidsimpletriggerschedule)
QZ0006  | Quartz   | Error    | RetryPolicyLiteralAnalyzer, [Documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/compile-time-checks.html#qz0006-invalidretrypolicydelay)
QZ1005  | Quartz   | Error    | DeclaredJobsGenerator, [Documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/declaring-jobs-with-attributes.html#qz1005-configurationkeywithoutconfiguration)

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------------------------------------------------------------------
QZ1004  | Quartz       | Info         | Quartz       | Warning      | DeclaredJobsGenerator, [Documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/declaring-jobs-with-attributes.html#qz1004-declaredjobsregistrationrenamed)
