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

namespace Quartz.Analyzers;

/// <summary>
/// One <c>AddJob(name, handler, …)</c> or <c>ScheduleJob(name, handler, …)</c> call whose handler the
/// generated code can bind: a lambda or a method group, of a delegate type it can name, whose shape a
/// delegate job accepts.
/// </summary>
/// <remarks>
/// Values only, as <see cref="DeclaredJob" /> is, so that re-reading an unchanged file produces a call
/// that compares equal and nothing downstream runs again.
/// </remarks>
/// <param name="Member">Which of the four members the call binds to.</param>
/// <param name="DelegateType">
/// The handler's delegate type, fully qualified, as the generated code casts the handler to it.
/// </param>
/// <param name="Parameters">Where each of the handler's arguments comes from, in declaration order.</param>
/// <param name="Completion">How the handler says it has finished.</param>
/// <param name="Location">The call, as the interceptor names it.</param>
internal sealed record DelegateJobCall(
    DelegateJobMember Member,
    string DelegateType,
    EquatableArray<HandlerParameter> Parameters,
    HandlerCompletion Completion,
    InterceptedCall Location);

/// <summary>
/// One of the handler's parameters.
/// </summary>
/// <param name="Source">Where its argument comes from.</param>
/// <param name="TypeName">
/// Its type, fully qualified and without nullable annotations, which is what <c>typeof</c> and the cast
/// in the generated code need.
/// </param>
internal sealed record HandlerParameter(ArgumentSource Source, string TypeName);

/// <summary>
/// Where the generated binding reads the call from.
/// </summary>
/// <param name="Version">The interceptable location's encoding version.</param>
/// <param name="Data">The interceptable location's opaque data, which names the file by its checksum.</param>
/// <param name="FilePath">The file the call is in, for ordering the generated methods.</param>
/// <param name="Position">Where in the file the call's name starts, for ordering.</param>
/// <param name="DisplayLocation">
/// The file's name and the line and column of the call, for the comment above each attribute.
/// </param>
internal sealed record InterceptedCall(int Version, string Data, string FilePath, int Position, string DisplayLocation);

/// <summary>
/// The four members a delegate job is added through, each intercepted by a method of its own shape.
/// </summary>
internal enum DelegateJobMember
{
    /// <summary><c>AddJob(name, handler, Action&lt;IJobConfigurator&lt;IJob&gt;&gt;? configure = null)</c>.</summary>
    AddJob,

    /// <summary><c>AddJob(name, handler, Action&lt;IServiceProvider, IJobConfigurator&lt;IJob&gt;&gt; configure)</c>.</summary>
    AddJobWithServices,

    /// <summary><c>ScheduleJob(name, handler, Action&lt;ITriggerConfigurator&lt;IJob&gt;&gt; trigger)</c>.</summary>
    ScheduleJob,

    /// <summary><c>ScheduleJob(name, handler, Action&lt;IServiceProvider, ITriggerConfigurator&lt;IJob&gt;&gt; trigger)</c>.</summary>
    ScheduleJobWithServices,
}

/// <summary>
/// Where one of the handler's arguments comes from, the rule <c>DelegateJobBinding</c> applies at run time.
/// </summary>
internal enum ArgumentSource
{
    /// <summary>The firing's <c>IJobExecutionContext</c>.</summary>
    Context,

    /// <summary>The firing's token.</summary>
    CancellationToken,

    /// <summary>The firing's scope itself.</summary>
    Services,

    /// <summary>A required service, resolved from the firing's scope.</summary>
    Service,
}

/// <summary>
/// How the handler says it has finished.
/// </summary>
internal enum HandlerCompletion
{
    Void,
    Task,
    ValueTask,
}
