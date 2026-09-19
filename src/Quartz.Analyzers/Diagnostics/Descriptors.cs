#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using Microsoft.CodeAnalysis;

namespace Quartz.Analyzers;

/// <summary>
/// Every diagnostic this analyzer reports, in one place so that the ids, the severities and the
/// sentences a reader sees are read together rather than one file at a time.
/// </summary>
internal static class Descriptors
{
    private const string Category = "Quartz";

    private const string HelpLink = "https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/compile-time-checks.html";

    /// <summary>
    /// A cron literal that the parser refuses.
    /// </summary>
    /// <remarks>
    /// An error rather than a warning, because there is no reading of the program in which this
    /// literal works: the same parser runs at build time and at run time, and it has already said no.
    /// </remarks>
    internal static readonly DiagnosticDescriptor InvalidCronExpression = new DiagnosticDescriptor(
        id: "QZ0001",
        title: "Cron expression cannot be parsed",
        messageFormat: "'{0}' is not a valid cron expression: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A cron expression written as a literal or a constant is read at build time with the same parser that would read it at run time. This one does not parse, so the call it was written for would have thrown.",
        helpLinkUri: HelpLink + "#qz0001-invalidcronexpression");

    /// <summary>
    /// A <c>[JobTimeout]</c> literal that is not a <see cref="TimeSpan" />, or is negative.
    /// </summary>
    internal static readonly DiagnosticDescriptor InvalidJobTimeout = new DiagnosticDescriptor(
        id: "QZ0002",
        title: "Job timeout cannot be parsed",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The attribute's constructor parses its argument, and until the timeout middleware reflects over the job for the first time nothing runs that constructor. A literal that cannot parse is therefore a deployment-time failure unless it is caught here.",
        helpLinkUri: HelpLink + "#qz0002-invalidjobtimeout");

    /// <summary>
    /// A job that writes its data map back but lets two firings run at once.
    /// </summary>
    /// <remarks>
    /// A warning rather than an error: it is a race, not a contradiction, and a job whose map is
    /// written by one firing alone is a legitimate if unusual shape.
    /// </remarks>
    internal static readonly DiagnosticDescriptor PersistJobDataWithoutDisallowConcurrent = new DiagnosticDescriptor(
        id: "QZ0003",
        title: "Job persists its data map without disallowing concurrent execution",
        messageFormat: "'{0}' carries [PersistJobDataAfterExecution] without [DisallowConcurrentExecution], so two firings can write the same job data map and the later one wins",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "PersistJobDataAfterExecution re-stores the job data map when a firing completes. With concurrent firings allowed, two of them read the same map, change it independently and store it one after the other, so whatever the first wrote is lost. Add [DisallowConcurrentExecution] to the same type, a base type or an interface it implements.",
        helpLinkUri: HelpLink + "#qz0003-persistjobdatawithoutdisallowconcurrent");

    /// <summary>
    /// A job that can be interrupted and never looks at the token that would interrupt it.
    /// </summary>
    /// <remarks>
    /// Information, not a warning: whether work is interruptible is a judgement the analyzer cannot
    /// make, and a job that finishes in a millisecond is right to ignore the token.
    /// </remarks>
    internal static readonly DiagnosticDescriptor CancellationTokenNotObserved = new DiagnosticDescriptor(
        id: "QZ0004",
        title: "Job execution does not observe its cancellation token",
        messageFormat: "'{0}' awaits or loops without reading its cancellation token, so shutdown and Interrupt cannot stop it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The token this method is handed is the one the scheduler cancels on shutdown and on IScheduler.Interrupt. A body that awaits or loops without ever reading it - either the parameter or the identical IJobExecutionContext.CancellationToken - runs to completion whatever the scheduler asks of it.",
        helpLinkUri: HelpLink + "#qz0004-cancellationtokennotobserved");
}
